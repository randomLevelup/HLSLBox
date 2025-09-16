using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TODO
/// </summary>
public class Particles2D : MonoBehaviour
{
    [Header("Spawn")]
    [SerializeField, Range(1, 20000)] int particleCount = 200;
    [SerializeField] Vector2 spawnArea = new Vector2(10f, 10f);

    [Header("Dynamics")]
    [SerializeField] float particleMass = 1f;
    [SerializeField, Tooltip("Coulomb-ish constant. Larger -> stronger repulsion")] float repulsionStrength = 100f;
    [SerializeField, Tooltip("Softening min distance to avoid singularities")] float minDistance = 0.1f;
    [SerializeField, Tooltip("Linear drag per second (0 = no drag)")] float dragPerSecond = 0.5f;
    [SerializeField, Tooltip("Clamp force magnitude to prevent explosions")] float maxForce = 1000f;
    [SerializeField] float maxVelocity = 10f;

    [Header("Attraction")]
    [SerializeField, Tooltip("Acceleration towards center (units/s^2). 0 = off")] float gravity = 30f;

    [Header("Bounds & display")]
    [SerializeField] Vector2 bounds = new Vector2(10f, 10f);
    [SerializeField] bool wrapEdges = false;
    [SerializeField] bool showGizmos = true;
    [SerializeField] float gizmoRadius = 0.05f;

    [Header("Render Settings")]
    [SerializeField] float particleRadius = 0.15f;
    [SerializeField, Tooltip("Soft edge as fraction of radius [0..1]")] [Range(0f, 1f)] float edgeSoftness = 0.35f;

    // Simulation state (local XY space centered at transform)
    Vector2[] positions;
    Vector2[] velocities;
    Vector2[] positionsUV; // cached normalized 0..1 for shader

    // Rendering
    ComputeBuffer positionsBuffer; // float2 per particle
    MaterialPropertyBlock mpb;
    MeshRenderer meshRenderer;

    const int STRIDE_FLOAT2 = sizeof(float) * 2;

    void Awake()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null)
        {
            Debug.LogWarning("Particles2D requires a MeshRenderer on the same GameObject.");
        }
        mpb = new MaterialPropertyBlock();
        InitializeSimulation();
        EnsureBuffer();
        UploadPositionsToGPU();
        UpdateMaterialProperties();
    }

    void OnEnable()
    {
        if (positions == null || positions.Length != particleCount)
        {
            InitializeSimulation();
        }
        EnsureBuffer();
        UploadPositionsToGPU();
        UpdateMaterialProperties();
    }

    void OnDisable()
    {
        ReleaseBuffer();
    }

    void OnDestroy()
    {
        ReleaseBuffer();
    }

    void OnValidate()
    {
        particleCount = Mathf.Max(1, particleCount);
        bounds.x = Mathf.Max(0.001f, bounds.x);
        bounds.y = Mathf.Max(0.001f, bounds.y);
        spawnArea.x = Mathf.Max(0.001f, spawnArea.x);
        spawnArea.y = Mathf.Max(0.001f, spawnArea.y);
        particleMass = Mathf.Max(0.0001f, particleMass);
        minDistance = Mathf.Max(0.0001f, minDistance);
        maxForce = Mathf.Max(0.0001f, maxForce);
        maxVelocity = Mathf.Max(0.0001f, maxVelocity);

        // Reinitialize when values change in editor for immediate feedback
        if (Application.isPlaying)
        {
            if (positions == null || positions.Length != particleCount)
            {
                InitializeSimulation();
                EnsureBuffer();
            }
            UpdateMaterialProperties();
            UploadPositionsToGPU();
        }
    }

    void InitializeSimulation()
    {
        positions = new Vector2[particleCount];
        velocities = new Vector2[particleCount];
        positionsUV = new Vector2[particleCount];

        var halfSpawn = spawnArea * 0.5f;
        for (int i = 0; i < particleCount; i++)
        {
            // random position within spawn rect centered at origin (local space)
            float x = Random.Range(-halfSpawn.x, halfSpawn.x);
            float y = Random.Range(-halfSpawn.y, halfSpawn.y);
            positions[i] = new Vector2(x, y);
            velocities[i] = Vector2.zero;
        }
        NormalizePositionsToUV();
    }

    void EnsureBuffer()
    {
        if (positionsBuffer != null && positionsBuffer.count != particleCount)
        {
            ReleaseBuffer();
        }
        if (positionsBuffer == null)
        {
            positionsBuffer = new ComputeBuffer(particleCount, STRIDE_FLOAT2);
        }
    }

    void ReleaseBuffer()
    {
        if (positionsBuffer != null)
        {
            positionsBuffer.Release();
            positionsBuffer = null;
        }
    }

    void Update()
    {
        float dt = Mathf.Max(Time.deltaTime, 1e-6f);
        Simulate(dt);
        NormalizePositionsToUV();
        UploadPositionsToGPU();
        UpdateMaterialProperties();
    }

    // Fast inverse square root approximation
    static float FISQRT(float x)
    {
        unsafe
        {
            float xhalf = 0.5f * x;
            int i = *(int*)&x;           // get bits for floating value
            i = 0x5f3759df - (i >> 1);   // gives initial guess y0
            x = *(float*)&i;             // convert bits back to float
            x = x * (1.5f - xhalf * x * x); // Newton step, repeating increases accuracy
            return x;
        }
    }

    void Simulate(float dt)
    {
        if (particleCount <= 0) return;

        // Accumulated forces per particle (avoid realloc by reusing velocities as temp? Keep separate for clarity)
        // We'll reuse a static buffer pattern by clearing per step using an array
        // But to avoid GC, allocate once and reuse a field-level array
        // Using local stack array isn't possible with dynamic size, so allocate temp once
        // We'll allocate on first use
        EnsureTempForces();
        for (int i = 0; i < particleCount; i++) tmpForces[i] = Vector2.zero;

        // Pairwise repulsion (naive O(n^2))
        for (int i = 0; i < particleCount; i++)
        {
            var pi = positions[i];
            for (int j = i + 1; j < particleCount; j++)
            {
                var pj = positions[j];
                Vector2 d = pj - pi;
                float dist2 = d.sqrMagnitude + minDistance * minDistance;
                float invDist = FISQRT(dist2);
                float invDist2 = invDist * invDist; // 1/dist^2
                // direction
                Vector2 dir = d * invDist; // normalized
                // Coulomb-like repulsion: k / r^2
                float fmag = repulsionStrength * invDist2; // per unit mass, becoming acceleration when divided by mass
                Vector2 f = dir * fmag;
                tmpForces[i] -= f;
                tmpForces[j] += f;
            }
        }

        // Add gravity-like attraction towards center (origin)
        if (gravity != 0f)
        {
            for (int i = 0; i < particleCount; i++)
            {
                Vector2 toCenter = -positions[i];
                float toCenterMag = Mathf.Max(toCenter.magnitude, minDistance);
                tmpForces[i] += toCenter / toCenterMag * gravity;
            }
        }

        // Apply mass, clamp, drag, integrate
        float invMass = 1f / particleMass;
        float drag = dragPerSecond;
        float dragFactor = drag > 0f ? Mathf.Exp(-drag * dt) : 1f;
        float maxForceSq = maxForce * maxForce;
        float maxVel = maxVelocity;
        Vector2 halfBounds = bounds * 0.5f;

        for (int i = 0; i < particleCount; i++)
        {
            // clamp force
            Vector2 f = tmpForces[i];
            if (f.sqrMagnitude > maxForceSq)
            {
                f = f.normalized * maxForce;
            }
            // a = F/m
            Vector2 a = f * invMass;
            // integrate velocity with drag
            Vector2 v = velocities[i];
            v += a * dt;
            // clamp velocity
            float vmag = v.magnitude;
            if (vmag > maxVel)
            {
                v = v / vmag * maxVel;
            }
            // apply drag
            v *= dragFactor;
            velocities[i] = v;

            // integrate position
            Vector2 p = positions[i] + v * dt;

            // bounds handling
            if (wrapEdges)
            {
                if (p.x < -halfBounds.x) p.x += bounds.x; else if (p.x > halfBounds.x) p.x -= bounds.x;
                if (p.y < -halfBounds.y) p.y += bounds.y; else if (p.y > halfBounds.y) p.y -= bounds.y;
            }
            else
            {
                if (p.x < -halfBounds.x)
                {
                    p.x = -halfBounds.x; v.x *= -1f;
                }
                else if (p.x > halfBounds.x)
                {
                    p.x = halfBounds.x; v.x *= -1f;
                }
                if (p.y < -halfBounds.y)
                {
                    p.y = -halfBounds.y; v.y *= -1f;
                }
                else if (p.y > halfBounds.y)
                {
                    p.y = halfBounds.y; v.y *= -1f;
                }
                velocities[i] = v;
            }

            positions[i] = p;
        }
    }

    Vector2[] tmpForces;
    void EnsureTempForces()
    {
        if (tmpForces == null || tmpForces.Length != particleCount)
        {
            tmpForces = new Vector2[particleCount];
        }
    }

    void NormalizePositionsToUV()
    {
        // Map local positions in [-bounds/2, +bounds/2] to UV [0,1]
        float invW = 1f / bounds.x;
        float invH = 1f / bounds.y;
        for (int i = 0; i < particleCount; i++)
        {
            Vector2 p = positions[i];
            positionsUV[i] = new Vector2(p.x * invW + 0.5f, p.y * invH + 0.5f);
        }
    }

    void UploadPositionsToGPU()
    {
        if (positionsBuffer == null) return;
        positionsBuffer.SetData(positionsUV);
    }

    void UpdateMaterialProperties()
    {
        if (meshRenderer == null) return;
        meshRenderer.GetPropertyBlock(mpb);
        // Provide buffer and uniforms to shader
        mpb.SetBuffer("_Positions", positionsBuffer);
        mpb.SetInt("_ParticleCount", particleCount);
        // Radius in UV units per axis
        Vector2 radiusUV = new Vector2(particleRadius / bounds.x, particleRadius / bounds.y);
        mpb.SetVector("_RadiusUV", radiusUV);
        mpb.SetFloat("_SoftEdge", Mathf.Clamp01(edgeSoftness));
        mpb.SetColor("_Color", Color.white);
        meshRenderer.SetPropertyBlock(mpb);
    }

    void OnDrawGizmos()
    {
        if (!showGizmos) return;
        Gizmos.color = Color.yellow;
        // Draw bounds rectangle in world space
        Vector3 c = transform.position;
        Vector3 right = transform.right * (bounds.x * 0.5f);
        Vector3 up = transform.up * (bounds.y * 0.5f);
        Vector3 p0 = c - right - up;
        Vector3 p1 = c + right - up;
        Vector3 p2 = c + right + up;
        Vector3 p3 = c - right + up;
        Gizmos.DrawLine(p0, p1);
        Gizmos.DrawLine(p1, p2);
        Gizmos.DrawLine(p2, p3);
        Gizmos.DrawLine(p3, p0);

        if (positions != null)
        {
            Gizmos.color = Color.cyan;
            float r = gizmoRadius;
            for (int i = 0; i < Mathf.Min(positions.Length, 2000); i++)
            {
                Vector3 wp = transform.TransformPoint(new Vector3(positions[i].x, positions[i].y, 0f));
                Gizmos.DrawSphere(wp, r);
            }
        }
    }


}

