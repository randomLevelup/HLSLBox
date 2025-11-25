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
        //========================================================================
        // CORE UTILITIES
        //========================================================================
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

        public static int Select<T>(IList<T> list, int k, IComparer<T> cmp)
        {
            int n = list.Count;
            if (n == 0 || k < 0 || k >= n)
                throw new ArgumentOutOfRangeException(nameof(k), "k out of range");

            // Quickselect on auxiliary index array.
            int[] idx = new int[n];
            for (int i = 0; i < n; i++) idx[i] = i;

            int left = 0;
            int right = n - 1;
            while (true)
            {
                if (left >= right)
                    return idx[k];

                // Median of Three
                int a  = left,    b  = (left + right) >> 1,  c  = right;
                int ia = idx[a],  ib = idx[b],               ic = idx[c];
                
                T A = list[ia];
                T B = list[ib];
                T C = list[ic];

                int med;

                if (cmp.Compare(A, B) < 0)
                {
                    if (cmp.Compare(B, C) < 0) med = b;      // A < B < C
                    else if (cmp.Compare(A, C) < 0) med = c; // A < C <= B
                    else med = a;                             // C <= A < B
                }
                else
                {
                    if (cmp.Compare(A, C) < 0) med = a;      // B <= A < C
                    else if (cmp.Compare(B, C) < 0) med = c; // B < C <= A
                    else med = b;                             // C <= B <= A
                }

                // Partition around median
                int pivotId = idx[med];
                T pivotValue = list[pivotId];

                Swap(idx, med, right);
                int store = left;
                for (int i = left; i < right; i++)
                {
                    if (cmp.Compare(list[idx[i]], pivotValue) < 0)
                    {
                        Swap(idx, store, i);
                        store++;
                    }
                }
                Swap(idx, right, store);
                int pivotNewIndex = store;

                if (k == pivotNewIndex)
                    return idx[k];
                else if (k < pivotNewIndex)
                    right = pivotNewIndex - 1;
                else
                    left = pivotNewIndex + 1;
            }
        }

        private static void Swap(int[] a, int i, int j)
        {
            if (i == j) return;
            int t = a[i];
            a[i] = a[j];
            a[j] = t;
        }

        //========================================================================
        // VECTOR MATH
        //========================================================================

        // fast inverse square root (1/sqrt(x)) approximation.
        // classic bit-level trick w/ Newton-Raphson step.
        // returns 0 for non-positive inputs.
        [MethodImplFast]
        public static float InSqrt(float x)
        {
            if (x <= 0f) return 0f;
            float xhalf = 0.5f * x;
            int i = BitConverter.SingleToInt32Bits(x);
            i = 0x5f3759df - (i >> 1);
            x = BitConverter.Int32BitsToSingle(i);
            x *= (1.5f - xhalf * x * x);
            return x;
        }

        [MethodImplFast]
        public static float Cross(in Vector2 a, in Vector2 b) => a.x * b.y - a.y * b.x;

        [MethodImplFast]
        public static float Cross(in Vector2 a, in Vector2 b, in Vector2 c) => Cross(b - a, c - a);

        //========================================================================
        // GEOMETRY SHIT
        //========================================================================

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

        //========================================================================
        // COMPARERS
        //========================================================================

        // Compare vertices by X then Y
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

        // Compare vertices by Y then X
        private sealed class IndexByYXComparer : IComparer<int>
        {
            private readonly Vector2[] _p;
            public IndexByYXComparer(Vector2[] p) { _p = p; }
            public int Compare(int a, int b)
            {
                var pa = _p[a];
                var pb = _p[b];
                int cmp = pa.y.CompareTo(pb.y);
                return cmp != 0 ? cmp : pa.x.CompareTo(pb.x);
            }
        }

        //========================================================================
        // DCEL CONSTRUCTOR (for Monotone Triangulation)
        //========================================================================
        // Constructor from a simple polygon touring all vertices in some order
        // This constructor remains in Algo2D for use by MonotoneTriangulateIndices
        private static DCEL CreateDCELFromPolygon(List<int> vertices, int count)
        {
            var dcel = new DCEL();
            if (vertices == null || vertices.Count < 3) return dcel;

            // Create inner (polygon) face and the unbounded outer face
            var innerFace = new DCEL.Face();
            dcel.Faces.Add(innerFace);

            int n = vertices.Count;
            var inner = new DCEL.HalfEdge[n];
            var outer = new DCEL.HalfEdge[n];

            // Create paired half-edges for each polygon side
            for (int i = 0; i < n; i++)
            {
                int a = vertices[i];
                int b = vertices[(i + 1) % n];

                // inner a->b
                inner[i] = new DCEL.HalfEdge
                {
                    Origin = a,
                    IncidentFace = innerFace
                };
                // outer b->a (reverse direction, belongs to OuterFace)
                outer[i] = new DCEL.HalfEdge
                {
                    Origin = b,
                    IncidentFace = dcel.OuterFace
                };

                // set twins immediately
                inner[i].Twin = outer[i];
                outer[i].Twin = inner[i];

                dcel.HalfEdges.Add(inner[i]);
                dcel.HalfEdges.Add(outer[i]);
            }

            // Link inner cycle (assume input vertices wound CCW)
            for (int i = 0; i < n; i++)
            {
                inner[i].Next = inner[(i + 1) % n];
                inner[i].Prev = inner[(i - 1 + n) % n];
            }

            // Link outer cycle in reverse order (CW)
            for (int i = 0; i < n; i++)
            {
                // If inner is a->b with index i, the corresponding outer is b->a with same i
                // The outer boundary should traverse b->a then a->prev, which is outer[(i - 1 + n)%n]
                outer[i].Next = outer[(i - 1 + n) % n];
                outer[i].Prev = outer[(i + 1) % n];
            }

            // Assign representative boundary edges
            innerFace.OuterComponent = inner[0];
            dcel.OuterFace.OuterComponent = outer[0];
            
            return dcel;
        }

        //========================================================================
        // MONOTONE TRIGNAGULATION ALG
        //========================================================================
        public static List<Vector2Int> MonotoneTriangulateIndices(Vector2[] positions, int count)
        {
            if (positions == null || count <= 0) return new List<Vector2Int>();
            count = Mathf.Clamp(count, 0, positions.Length);
            if (count < 3) return new List<Vector2Int>();

            // Build initial index list [0..count-1]
            List<int> indices = new List<int>(count);
            for (int i = 0; i < count; i++) indices.Add(i);

            // find vertex with median X coordinate
            int medX_index = Select<int>(indices, count / 2, new IndexByXYComparer(positions));
            float medX = positions[medX_index].x;

            // build y-monotone polygon
            List<int> left = new List<int>();
            List<int> right = new List<int>();
            indices.Sort(new IndexByYXComparer(positions));
            // deep copy sorted list
            List<int> chain = new List<int>(indices);
            for (int i = 0; i < indices.Count; i++)
            {
                int vi = indices[i];
                Vector2 v = positions[vi];
                if (v.x < medX || (v.x == medX && v.y <= positions[medX_index].y))
                    right.Add(vi);
                else
                    left.Add(vi);
            }
            left.Reverse();
            indices = right;
            indices.AddRange(left);

            // build doubly-connected edge list
            DCEL dcel = CreateDCELFromPolygon(indices, count);
            Stack<int> stack = new Stack<int>();
            stack.Push(chain[0]);
            stack.Push(chain[1]);
            for (int j = 2; j < chain.Count; j++)
            {
                // if u[j] and stack top are on different chains
                bool ujOnLeft = left.Contains(chain[j]);
                bool topOnLeft = left.Contains(stack.Peek());
                if (ujOnLeft != topOnLeft)
                {
                    // pop all stack vertices and form triangles
                    int top = stack.Pop();
                    while (stack.Count > 1)
                    {
                        int vi = stack.Pop();
                        // add triangle (u[j], top, vi)
                        dcel.AddTriangle(chain[j], top, vi);
                        top = vi;
                    }
                    stack.Pop(); // remove last
                    stack.Push(chain[j - 1]);
                    stack.Push(chain[j]);
                }
                else // same chain
                {
                    int top = stack.Pop();
                    // create triangles until the new diagonal is not internal
                    while (stack.Count > 0)
                    {
                        int vi = stack.Peek();
                        Vector2 pj = positions[chain[j]];
                        Vector2 pt = positions[top];
                        Vector2 pi = positions[vi];
                        bool isLeftOfLine = Cross(pi, pt, pj) > 0f;
                        if ((ujOnLeft && isLeftOfLine) || (!ujOnLeft && !isLeftOfLine))
                        {
                            // add triangle (u[j], top, vi)
                            dcel.AddTriangle(chain[j], top, vi);
                            top = stack.Pop();
                        }
                        else
                        {
                            break;
                        }
                    }
                    stack.Push(top);
                    stack.Push(chain[j]);
                }
            }
            // add remaining diagonals from u[-1] to stack vertices except first and last
            int last = chain[chain.Count - 1];
            stack.Pop(); // remove top
            while (stack.Count > 1)
            {
                int vi = stack.Pop();
                dcel.AddTriangle(last, vi, stack.Peek());
            }
            return dcel.ToEdgeList();
        }



        //========================================================================
        // CONVEX HULL ALG
        //========================================================================
        // Returns the indices of the vertices that form the convex hull of the
        // given set of 2D points, using the Monotone Chain algorithm.
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

        //========================================================================
        // UNITY / GRAPHICS INTEGRATION
        //========================================================================
        public static void EnsureComputeBuffer(ref ComputeBuffer buffer, int count, int stride)
        {
            count = Mathf.Max(1, count);
            // Release if parameters changed or buffer is invalid
            if (buffer != null && (!buffer.IsValid() || buffer.count != count || buffer.stride != stride))
            {
                buffer.Release();
                buffer = null;
            }
            // Create if we don't have one
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

        //========================================================================
        // ATTRIBUTE MACRO
        //========================================================================
        [AttributeUsage(AttributeTargets.Method)]
        private sealed class MethodImplFastAttribute : Attribute { }
    }
}
