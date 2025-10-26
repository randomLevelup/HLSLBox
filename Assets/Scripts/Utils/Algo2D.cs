using System;
using System.Collections.Generic;
using UnityEngine;

namespace HLSLBox.Algorithms
{
    /// <summary>
    /// Lightweight, allocation-conscious 2D algorithms commonly used across renderers and behaviors.
    /// Focuses on clarity and good asymptotic complexity.
    /// </summary>
    public static class Algo2D
    {
        // ------------------------------------------------------------
        // General helpers
        // ------------------------------------------------------------
        public static void EnsureArraySize<T>(ref T[] array, int size)
        {
            if (size < 0) size = 0;
            if (array == null || array.Length != size)
                array = new T[size];
        }

        public static bool SequenceEqual<T>(IList<T> a, IList<T> b) where T : IEquatable<T>
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (!a[i].Equals(b[i])) return false;
            return true;
        }

        // ------------------------------------------------------------
        // Vector math
        // ------------------------------------------------------------
        // Fast inverse square root (1/sqrt(x)) approximation.
        // Uses the classic bit-level trick and one Newton-Raphson step.
        // Valid for x > 0. Returns 0 for non-positive inputs.
        [MethodImplFast]
        public static float InSqrt(float x)
        {
            if (x <= 0f) return 0f;
            float xhalf = 0.5f * x;
            int i = BitConverter.SingleToInt32Bits(x);
            i = 0x5f3759df - (i >> 1);
            x = BitConverter.Int32BitsToSingle(i);
            x = x * (1.5f - xhalf * x * x);
            return x;
        }

        [MethodImplFast]
        public static float Cross(in Vector2 a, in Vector2 b) => a.x * b.y - a.y * b.x;

        [MethodImplFast]
        public static float Cross(in Vector2 a, in Vector2 b, in Vector2 c) => Cross(b - a, c - a);

        [MethodImplFast]
        public static bool IsLeftOf(in Vector2 a, in Vector2 b, in Vector2 c) => Cross(a, b, c) > 0f;

        // ------------------------------------------------------------
        // Unity helpers to reduce boilerplate in render components
        // ------------------------------------------------------------
        public static void EnsureComputeBuffer(ref ComputeBuffer buffer, int count, int stride)
        {
            count = Mathf.Max(1, count);
            if (buffer != null && (buffer.count != count || buffer.stride != stride))
            {
                buffer.Release();
                buffer = null;
            }
            if (buffer == null)
            {
                buffer = new ComputeBuffer(count, stride);
            }
        }

        public static void ReleaseComputeBuffer(ref ComputeBuffer buffer)
        {
            if (buffer != null)
            {
                buffer.Release();
                buffer = null;
            }
        }

        public static void EnsureRendererAndBlock(MonoBehaviour owner, ref MeshRenderer renderer, ref MaterialPropertyBlock block)
        {
            if (owner == null) return;
            if (renderer == null)
            {
                renderer = owner.GetComponent<MeshRenderer>();
            }
            if (block == null)
            {
                block = new MaterialPropertyBlock();
            }
        }

        public static void ApplyParticlesToMaterial(MaterialPropertyBlock block, Particles2D particles)
        {
            if (block == null) return;
            if (particles != null && particles.PositionsBuffer != null)
            {
                block.SetBuffer("_Positions", particles.PositionsBuffer);
                block.SetInt("_ParticleCount", particles.ParticleCount);
            }
            else
            {
                block.SetInt("_ParticleCount", 0);
            }
        }

        public static Particles2D FindParticles(Particles2D current)
        {
            if (current != null) return current;
            return UnityEngine.Object.FindObjectOfType<Particles2D>();
        }

        public static void ClampIndices(List<int> indices, int maxInclusive)
        {
            if (indices == null) return;
            maxInclusive = Mathf.Max(0, maxInclusive);
            for (int i = 0; i < indices.Count; i++)
            {
                int v = indices[i];
                if (v < 0) v = 0;
                if (v > maxInclusive) v = maxInclusive;
                indices[i] = v;
            }
        }

        public static void ClampEdges(List<Vector2Int> edges, int maxInclusive)
        {
            if (edges == null) return;
            maxInclusive = Mathf.Max(0, maxInclusive);
            for (int i = 0; i < edges.Count; i++)
            {
                Vector2Int edge = edges[i];
                if (edge.x < 0) edge.x = 0;
                if (edge.x > maxInclusive) edge.x = maxInclusive;
                if (edge.y < 0) edge.y = 0;
                if (edge.y > maxInclusive) edge.y = maxInclusive;
                edges[i] = edge;
            }
        }

        // ------------------------------------------------------------
        // Distance to segment / closest point
        // ------------------------------------------------------------
        [MethodImplFast]
        public static float ProjectPointOnSegment(in Vector2 p, in Vector2 a, in Vector2 b)
        {
            Vector2 ab = b - a;
            float denom = Mathf.Max(1e-12f, ab.sqrMagnitude);
            float t = Vector2.Dot(p - a, ab) / denom;
            return Mathf.Clamp01(t);
        }

        [MethodImplFast]
        public static Vector2 ClosestPointOnSegment(in Vector2 p, in Vector2 a, in Vector2 b, out float t)
        {
            t = ProjectPointOnSegment(p, a, b);
            return a + (b - a) * t;
        }

        [MethodImplFast]
        public static float DistancePointSegmentSq(in Vector2 p, in Vector2 a, in Vector2 b, out Vector2 closest, out float t)
        {
            t = ProjectPointOnSegment(p, a, b);
            Vector2 ab = b - a;
            closest = a + ab * t;
            Vector2 d = p - closest;
            return d.sqrMagnitude;
        }

        [MethodImplFast]
        public static float DistancePointSegment(in Vector2 p, in Vector2 a, in Vector2 b, out Vector2 closest, out float t)
        {
            float d2 = DistancePointSegmentSq(p, a, b, out closest, out t);
            return Mathf.Sqrt(d2);
        }

        // ------------------------------------------------------------
        // Convex hull (Monotone Chain, O(n log n))
        // Returns indices into the source array in CCW order without repeating the first point.
        // ------------------------------------------------------------
        public static List<int> ConvexHullIndices(Vector2[] positions, int count)
        {
            var hull = new List<int>();
            if (positions == null) return hull;
            if (count <= 0) return hull;
            if (count == 1)
            {
                hull.Add(0);
                return hull;
            }

            // Build index array 0..count-1
            int[] point_ids = new int[count];
            for (int i = 0; i < count; i++) point_ids[i] = i;

            // Sort by x then y (stable order for deterministic hull)
            Array.Sort(point_ids, new IndexByXYComparer(positions));

            // Lower hull
            for (int k = 0; k < point_ids.Length; k++)
            {
                int i = point_ids[k];
                Vector2 pi = positions[i];
                while (hull.Count >= 2)
                {
                    int j = hull[hull.Count - 1];
                    int h = hull[hull.Count - 2];
                    // Remove last if it would cause a clockwise turn or colinear (<= 0)
                    if (Cross(positions[h], positions[j], pi) <= 0f) hull.RemoveAt(hull.Count - 1);
                    else break;
                }
                hull.Add(i);
            }

            // Upper hull
            int lowerCount = hull.Count;
            for (int k = point_ids.Length - 2; k >= 0; k--) // skip last because it's already included
            {
                int i = point_ids[k];
                Vector2 pi = positions[i];
                while (hull.Count > lowerCount)
                {
                    int j = hull[hull.Count - 1];
                    int h = hull[hull.Count - 2];
                    if (Cross(positions[h], positions[j], pi) <= 0f) hull.RemoveAt(hull.Count - 1);
                    else break;
                }
                hull.Add(i);
            }

            // Remove the duplicated first index at the end
            if (hull.Count > 1)
                hull.RemoveAt(hull.Count - 1);

            return hull;
        }

        // Comparer used for sorting indices by coordinates without capturing lambdas
        private sealed class IndexByXYComparer : IComparer<int>
        {
            private readonly Vector2[] _p;
            public IndexByXYComparer(Vector2[] p) { _p = p; }
            public int Compare(int a, int b)
            {
                var pa = _p[a];
                var pb = _p[b];
                int cmp = pa.x.CompareTo(pb.x);
                return cmp != 0 ? cmp : pa.y.CompareTo(pb.y);
            }
        }

        // Attribute to hint inlining
        [AttributeUsage(AttributeTargets.Method)]
        private sealed class MethodImplFastAttribute : Attribute { }
    }
}
