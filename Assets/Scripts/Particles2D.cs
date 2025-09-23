using System.Collections.Generic;
using UnityEngine;

public class Particles2D : MonoBehaviour
{
    [Header("Spawn")]
    [SerializeField, Range(1, 200)] int particleCount = 10;
    [SerializeField] Vector2 spawnArea = new Vector2(10f, 10f);

    [Header("Dynamics")]
    [SerializeField] float particleMass = 1f;
    [SerializeField, Tooltip("Coulomb-ish constant. Larger -> stronger repulsion")] float repulsionStrength = 5f;
    [SerializeField, Tooltip("Softening min distance to avoid singularities")] float minDistance = 0.1f;
    [SerializeField, Tooltip("Linear drag per second (0 = no drag)")] float dragPerSecond = 2.5f;
    [SerializeField, Tooltip("Clamp force magnitude to prevent explosions")] float maxForce = 1000f;
    [SerializeField] float maxVelocity = 10f;

    [Header("Attraction")]
    [SerializeField, Tooltip("Acceleration towards center (units/s^2). 0 = off")] float gravity = 30f;

    [Header("Noise Repulsion (matches shader)")]
    [SerializeField, Tooltip("When enabled, each particle's repulsion strength is modulated by 2D value noise at its position.")] bool noiseModulatesRepulsion = true;
    [SerializeField, Tooltip("Noise tiling scale (like _Scale in shader). Higher = smaller features."), Min(0.0001f)] float noiseScale = 20f;
    [SerializeField, Tooltip("Noise speed over time (like _Speed in shader)." )] float noiseSpeed = 40f;
    [SerializeField, Tooltip("Blend between no modulation (0) and fully driven by noise (1)."), Range(0f, 1f)] float noiseInfluence = 0.175f;

    [Header("Bounds & display")]
    [SerializeField] Vector2 bounds = new Vector2(10f, 10f);
    [SerializeField] bool wrapEdges = false;
    [SerializeField] bool showGizmos = false;
    [SerializeField] float gizmoRadius = 0.05f;

    [Header("Render Settings")]
    [SerializeField] float particleRadius = 0.04f;
    [SerializeField, Tooltip("Soft edge as fraction of radius [0..1]")] [Range(0f, 1f)] float edgeSoftness = 0.75f;

    // Simulation state (local XY space centered at transform)
    Vector2[] positions;
    Vector2[] velocities;
    Vector2[] positionsUV; // cached normalized 0..1 for shader
    float[] noiseFactors;   // per-particle multiplier from CPU noise [~0..1], blended by influence

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
        noiseScale = Mathf.Max(0.0001f, noiseScale);
        noiseInfluence = Mathf.Clamp01(noiseInfluence);

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
        noiseFactors = new float[particleCount];

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

        // Precompute per-particle noise modulation factors
        EnsureNoiseFactors();
        if (noiseModulatesRepulsion)
        {
            float w = Time.time * noiseSpeed;         // shader uses _Time.y * _Speed
            float wScaled = w / 100.0f;               // match valueNoise2D usage (w/100)
            for (int i = 0; i < particleCount; i++)
            {
                // positionsUV already in 0..1, like mesh UV used in shader
                Vector2 st = positionsUV[i] * noiseScale;
                float n = ValueNoise2D(st, wScaled);  // ~[0..1]
                // Blend between 1 (no modulation) and n (full modulation)
                noiseFactors[i] = Mathf.Lerp(1f, n, noiseInfluence);
            }
        }
        else
        {
            for (int i = 0; i < particleCount; i++) noiseFactors[i] = 1f;
        }

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
                // Modulate strength by per-particle noise at both ends (symmetric interaction)
                float pairMod = noiseFactors[i] * noiseFactors[j];
                float fmag = repulsionStrength * pairMod * invDist2; // per unit mass, becoming acceleration when divided by mass
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

    void EnsureNoiseFactors()
    {
        if (noiseFactors == null || noiseFactors.Length != particleCount)
        {
            noiseFactors = new float[particleCount];
        }
    }

    // --- CPU-side noise to mirror Assets/Shaders/PoorlinBG.shader ---
    // frac(x) helper
    static float Frac(float x) => x - Mathf.Floor(x);

    // Hash for integer lattice points: frac(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123 + w)
    static float Hash21(Vector2 p, float w)
    {
        // use integer lattice by flooring p
        p = new Vector2(Mathf.Floor(p.x), Mathf.Floor(p.y));
        float dotv = p.x * 127.1f + p.y * 311.7f;
        float s = Mathf.Sin(dotv);
        float v = s * 43758.5453123f + w;
        return Frac(v);
    }

    // 2D value noise with smooth bilinear interpolation matching shader
    static float ValueNoise2D(Vector2 st, float w)
    {
        Vector2 i = new Vector2(Mathf.Floor(st.x), Mathf.Floor(st.y));
        Vector2 f = new Vector2(Frac(st.x), Frac(st.y));

        float a = Hash21(i, w);
        float b = Hash21(i + new Vector2(1f, 0f), w);
        float c = Hash21(i + new Vector2(0f, 1f), w);
        float d = Hash21(i + new Vector2(1f, 1f), w);

        // Smoothstep-like curve u = f*f*(3-2f)
        Vector2 u = new Vector2(f.x * f.x * (3f - 2f * f.x), f.y * f.y * (3f - 2f * f.y));

        float nx0 = Mathf.Lerp(a, b, u.x);
        float nx1 = Mathf.Lerp(c, d, u.x);
        return Mathf.Lerp(nx0, nx1, u.y);
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

    // Public read-only accessors for other renderers (e.g., Poly2D)
    public ComputeBuffer PositionsBuffer => positionsBuffer;
    public int ParticleCount => particleCount;

    // Apply a small velocity impulse specified in UV units; internally converted to local XY.
    public void AddVelocityImpulseUV(int index, Vector2 impulseUV)
    {
        if (index < 0 || index >= particleCount) return;
        // Convert UV delta to local space delta using bounds scaling (see NormalizePositionsToUV)
        Vector2 dv = new Vector2(impulseUV.x * bounds.x, impulseUV.y * bounds.y);
        velocities[index] += dv;
        // Optional: clamp to max velocity to avoid spikes
        float vmag = velocities[index].magnitude;
        if (vmag > maxVelocity)
        {
            velocities[index] = velocities[index] / vmag * maxVelocity;
        }
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

