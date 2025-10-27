using System;
using System.Collections.Generic;
using UnityEngine;
using HLSLBox.Algorithms;

[ExecuteAlways]
[RequireComponent(typeof(MeshRenderer))]
public class Simple : Poly2D
{
    [Header("Simple Mode")]
    [SerializeField, Tooltip("Seconds to keep convex hull active before freezing indices")] float hullWarmupSeconds = 0.5f;
    [SerializeField, Tooltip("Strength of collision/containment impulses (UV units per frame)")] float barrierStrengthUV = 0.002f;
    [SerializeField, Tooltip("Minimum separation from any segment in UV units (acts like thickness)")] float barrierThicknessUV = 0.0025f;

    float startTime;
    bool frozen;

    protected override void OnEnable()
    {
        startTime = Time.time;
        frozen = false;
        base.OnEnable();
    }

    protected override void UpdateShape()
    {
        if (particles == null) return;
        var posBuffer = particles.PositionsBuffer;
        int count = Mathf.Max(0, particles.ParticleCount);
        if (posBuffer == null || count <= 0) return;

        if (!frozen && Time.time - startTime < hullWarmupSeconds)
        {
            // Compute convex hull for a brief warm-up
            Algo2D.EnsureArraySize(ref vtexPositionsUV, count);
            try { posBuffer.GetData(vtexPositionsUV); } catch (Exception) { return; }
            var newIdx = Algo2D.ConvexHullIndices(vtexPositionsUV, count);
            if (!Algo2D.SequenceEqual(indices, newIdx))
            {
                indices = newIdx;
                EnsureBuffer();
                Upload();
                UpdateMaterial();
            }
            return;
        }

        if (!frozen)
        {
            // Freeze current indices
            frozen = true;
        }

        // Apply barrier impulses so vertices can't cross polygon segments
        // We only need the particle positions in UV to check crossings and compute impulses.
        Algo2D.EnsureArraySize(ref vtexPositionsUV, count);
        try { posBuffer.GetData(vtexPositionsUV); } catch (Exception) { return; }

        // For each vertex in indices, push it away from any segment if it gets too close
        int m = indices.Count;
        for (int a = 0; a < m; a++)
        {
            int i = indices[a];
            Vector2 pi = vtexPositionsUV[i];

            // Determine whether polygon is closed; if open, skip the last segment
            int segCount = closed ? m : m - 1;
            for (int s = 0; s < segCount; s++)
            {
                int ia = indices[s];
                int ib = indices[(s + 1) % m];
                if (ia == i || ib == i) continue; // ignore segments that use the vertex itself

                Vector2 aPos = vtexPositionsUV[ia];
                Vector2 bPos = vtexPositionsUV[ib];
                // Compute closest point on segment to pi (projection-based)
                Vector2 closest;
                float t;
                float dist2 = Algo2D.DistancePointSegmentSq(pi, aPos, bPos, out closest, out t);
                float thresh2 = barrierThicknessUV * barrierThicknessUV;
                if (dist2 < thresh2)
                {
                    // Direction: away from the segment (orientation independent)
                    Vector2 d = pi - closest;
                    float dist = Mathf.Sqrt(Mathf.Max(0f, dist2));
                    Vector2 ab = bPos - aPos;
                    Vector2 n = dist > 1e-6f ? (d / Mathf.Max(1e-6f, dist)) : new Vector2(-ab.y, ab.x).normalized;
                    // Scale impulse by penetration depth for smoother resolution
                    float depth = barrierThicknessUV - dist;
                    Vector2 impulse = n * (barrierStrengthUV * (depth / Mathf.Max(1e-6f, barrierThicknessUV)));
                    particles.AddVelocityImpulseUV(i, impulse);
                    // Avoid stacking multiple corrections in one frame for this vertex
                    break;
                }
            }
        }
    }

    // Called by the custom editor button
    public void ReConvexify()
    {
        if (particles == null) return;
        var posBuffer = particles.PositionsBuffer;
        int count = Mathf.Max(0, particles.ParticleCount);
        if (posBuffer == null || count <= 0) return;
        Algo2D.EnsureArraySize(ref vtexPositionsUV, count);
        try { posBuffer.GetData(vtexPositionsUV); } catch (Exception) { return; }
        var newIdx = Algo2D.ConvexHullIndices(vtexPositionsUV, count);
        indices = newIdx;
        EnsureBuffer();
        Upload();
        UpdateMaterial();
        // Restart warmup if desired:
        startTime = Time.time;
        frozen = false;
    }
}
