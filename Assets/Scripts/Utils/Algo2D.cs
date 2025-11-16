using System;
using System.Collections.Generic;
using UnityEngine;

namespace HLSLBox.Algorithms
{
    /// Lightweight, allocation-conscious 2D algorithms commonly used across renderers and behaviors.
    /// Focuses on clarity and good asymptotic complexity.
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
        // DCEL STRUCTURE
        //========================================================================
        public class DCEL
        {
            // Represents a directed edge key (origin -> dest)
            private readonly struct EdgeKey : IEquatable<EdgeKey>
            {
                public readonly int A;
                public readonly int B;
                public EdgeKey(int a, int b) { A = a; B = b; }
                public bool Equals(EdgeKey other) => A == other.A && B == other.B;
                public override bool Equals(object obj) => obj is EdgeKey ek && Equals(ek);
                public override int GetHashCode() => HashCode.Combine(A, B);
            }

            public class HalfEdge
            {
                public int Origin; // index of origin vertex
                public HalfEdge Twin;
                public HalfEdge Next;
                public HalfEdge Prev;
                public Face IncidentFace;
            }

            public class Face
            {
                public HalfEdge OuterComponent;
                public List<HalfEdge> InnerComponents = new List<HalfEdge>();
            }

            public List<HalfEdge> HalfEdges = new List<HalfEdge>();

            public List<Face> Faces = new List<Face>();

            // Optional explicit reference to the unbounded (outside) face
            public Face OuterFace { get; private set; }

            // Directed edge lookup
            private readonly Dictionary<EdgeKey, HalfEdge> _edgeMap = new Dictionary<EdgeKey, HalfEdge>();

            private void RegisterEdge(int origin, int dest, HalfEdge e)
                => _edgeMap[new EdgeKey(origin, dest)] = e;

            public bool TryGetEdge(int origin, int dest, out HalfEdge edge)
                => _edgeMap.TryGetValue(new EdgeKey(origin, dest), out edge);

            // Constructor for an empty DCEL
            public DCEL()
            {
                HalfEdges = new List<HalfEdge>();
                Faces = new List<Face>();
                OuterFace = new Face();
                Faces.Add(OuterFace);
            }

            // Constructor from a simple polygon touring all vertices in some order
            public DCEL(List<int> vertices)
            {
                if (vertices == null || vertices.Count < 3) return;

                // Create inner (polygon) face and the unbounded outer face
                OuterFace = new Face();
                Faces.Add(OuterFace);
                var innerFace = new Face();
                Faces.Add(innerFace);

                int n = vertices.Count;
                var inner = new HalfEdge[n];
                var outer = new HalfEdge[n];

                // Create paired half-edges for each polygon side
                for (int i = 0; i < n; i++)
                {
                    int a = vertices[i];
                    int b = vertices[(i + 1) % n];

                    // inner a->b
                    inner[i] = new HalfEdge
                    {
                        Origin = a,
                        IncidentFace = innerFace
                    };
                    // outer b->a (reverse direction, belongs to OuterFace)
                    outer[i] = new HalfEdge
                    {
                        Origin = b,
                        IncidentFace = OuterFace
                    };

                    // set twins immediately
                    inner[i].Twin = outer[i];
                    outer[i].Twin = inner[i];

                    HalfEdges.Add(inner[i]);
                    HalfEdges.Add(outer[i]);
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

                // Register directed edges in the map
                for (int i = 0; i < n; i++)
                {
                    int a = vertices[i];
                    int b = vertices[(i + 1) % n];
                    RegisterEdge(a, b, inner[i]);
                    RegisterEdge(b, a, outer[i]);
                }

                // Assign representative boundary edges
                innerFace.OuterComponent = inner[0];
                OuterFace.OuterComponent = outer[0];
            }

            public HalfEdge GetOrCreateDirectedEdge(int origin, int dest, Face leftFace)
            {
                if (TryGetEdge(origin, dest, out var e))
                {
                    // Assign face if it's currently the outer/unset side
                    if (e.IncidentFace == null || e.IncidentFace == OuterFace)
                    {
                        e.IncidentFace = leftFace;
                    }
                    return e;
                }

                // Create the pair (origin->dest) and (dest->origin)
                var he = new HalfEdge { Origin = origin, IncidentFace = leftFace };
                var te = new HalfEdge { Origin = dest, IncidentFace = OuterFace };
                he.Twin = te; te.Twin = he;

                HalfEdges.Add(he);
                HalfEdges.Add(te);

                RegisterEdge(origin, dest, he);
                RegisterEdge(dest, origin, te);

                return he;
            }

            // Adds a triangle face defined by vertices (a, b, c) in CCW order.
            // Creates missing half-edges and sets up Next/Prev within the new face loop.
            public Face AddTriangle(int a, int b, int c)
            {
                if (a == b || b == c || c == a) return; // degenerate

                var face = new Face();
                Faces.Add(face);

                HalfEdge eab = GetOrCreateDirectedEdge(a, b, face);
                HalfEdge ebc = GetOrCreateDirectedEdge(b, c, face);
                HalfEdge eca = GetOrCreateDirectedEdge(c, a, face);

                // Link cycle for this face
                eab.Next = ebc; ebc.Prev = eab;
                ebc.Next = eca; eca.Prev = ebc;
                eca.Next = eab; eab.Prev = eca;

                face.OuterComponent = eab;

                return face;
            }

            // Convert to list of undirected edge pairs
            public List<Vector2Int> ToEdgeList()
            {
                var edgeList = new List<Vector2Int>();
                var seen = new HashSet<EdgeKey>();
                foreach (var he in HalfEdges)
                {
                    var key = new EdgeKey(he.Origin, he.Twin.Origin);
                    var revKey = new EdgeKey(he.Twin.Origin, he.Origin);
                    if (!seen.Contains(key) && !seen.Contains(revKey))
                    {
                        edgeList.Add(new Vector2Int(he.Origin, he.Twin.Origin));
                        seen.Add(key);
                    }
                }
                return edgeList;
            }
        }

        public sealed class Dag<T>
        {
            // TODO: implement templated dag
        }

        //========================================================================
        // MONOTONE TRIGNAGULATION ALG
        //========================================================================
        public static List<Vector2Int> TriangulateIndices(Vector2[] positions, int count)
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
            DCEL dcel = new(indices, count);
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
        // DELAUNAY TRIGANGULATION ALG
        //========================================================================
        
        // Point record for Delaunay triangulation
        private readonly struct DelaunayPoint : IEquatable<DelaunayPoint>
        {
            public enum PointType : byte
            {
                P,             PMinus1,     PMinus2
             // regular point, below-right, above-left
            }

            public readonly int Idx;
            public readonly Vector2 Position;
            public readonly PointType Type;

            public DelaunayPoint(int idx, Vector2 position, PointType type = PointType.P)
            {
                Idx = idx;
                Position = position;
                Type = type;
            }

            public bool Equals(DelaunayPoint other)
                => Idx == other.Idx && Type == other.Type;

            public override bool Equals(object obj)
                => obj is DelaunayPoint other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(Idx, Type);

            public static int Compare(DelaunayPoint a, DelaunayPoint b)
            {
                switch (a.Type)
                {
                    case PointType.PMinus1: return 1; 
                    case PointType.PMinus2: return -1;
                    case PointType.P: break;
                }
                switch (b.Type)
                {
                    case PointType.PMinus1: return -1;
                    case PointType.PMinus2: return 1;
                    case PointType.P: break;
                }
                int cmp = a.Position.x.CompareTo(b.Position.x);
                if (cmp != 0) return cmp;
                return a.Position.y.CompareTo(b.Position.y);
            }

            public static bool operator ==(DelaunayPoint left, DelaunayPoint right) => left.Equals(right);
            public static bool operator !=(DelaunayPoint left, DelaunayPoint right) => !left.Equals(right);
            public static bool operator <(DelaunayPoint a, DelaunayPoint b) => Compare(a, b) < 0;
            public static bool operator >(DelaunayPoint a, DelaunayPoint b) => Compare(a, b) > 0;

            public static bool IsLeftOf(DelaunayPoint pj, DelaunayPoint pi, DelaunayPoint pk)
            {
                switch (pi.Type)
                {
                    case PointType.PMinus1: return pj < pi;
                    case PointType.PMinus2: return pj > pi;
                    case PointType.P: break;
                }
                switch (pk.Type)
                {
                    case PointType.PMinus1: return pj > pi;
                    case PointType.PMinus2: return pj < pi;
                    case PointType.P: break;
                }
                return Cross(pj.Position, pi.Position, pk.Position) > 0f;
            }
        }

        /// Triangle type for graph nodes, with point location features.
        private readonly struct DelaunayTriangle : IEquatable<DelaunayTriangle>
        {
            public readonly DelaunayPoint A;
            public readonly DelaunayPoint B;
            public readonly DelaunayPoint C;
            public DelaunayTriangle(DelaunayPoint a, DelaunayPoint b, DelaunayPoint c) { A = a; B = b; C = c; }
            public bool Equals(DelaunayTriangle other)
                => A.Equals(other.A) && B.Equals(other.B) && C.Equals(other.C);
            public override bool Equals(object obj)
                => obj is DelaunayTriangle other && Equals(other);
            public override int GetHashCode()
                => HashCode.Combine(A, B, C);
            public bool Contains(DelaunayPoint px)
                => DelaunayPoint.IsLeftOf(A, B, C) &&
                   DelaunayPoint.IsLeftOf(B, C, A) &&
                   DelaunayPoint.IsLeftOf(C, A, B);

            public IReadOnlyList<int> Indices()
            {
                return new[] { A.Idx, B.Idx, C.Idx };
            }
        }

        public static List<Vector2Int> DelaunayTriangulation(Vector2[] positions, int count)
        {
            var dPoints = new List<DelaunayPoint>(count);
            for (int i = 0; i < count; i++)
            {
                dPoints.Add(new DelaunayPoint(i, positions[i], DelaunayPoint.PointType.P));
            }
            DelaunayPoint pMinus1 = new DelaunayPoint(-1, Vector2.zero, DelaunayPoint.PointType.PMinus1);
            DelaunayPoint pMinus2 = new DelaunayPoint(-2, Vector2.zero, DelaunayPoint.PointType.PMinus2);
            dPoints.Add(pMinus1);
            dPoints.Add(pMinus2);

            // Current triangulation stored in a DAG: node type is a 3-tuple of DelaunayPoints.
            // Root: triangle composed of the highest-position & the two sentinel points.
            // "Highest position" interpreted as greatest Y, then greatest X.
            DelaunayPoint? highest = null;
            foreach (var dp in dPoints)
            {
                if (dp.Type == DelaunayPoint.PointType.P)
                {
                    if (!highest.HasValue ||
                        dp.Position.y > highest.Value.Position.y ||
                            (Mathf.Approximately(dp.Position.y, highest.Value.Position.y) &&
                             dp.Position.x > highest.Value.Position.x))
                    {
                        highest = dp;
                    }
                }
            }
            
            var trisDag = new Dag<DelaunayTriangle>();
            DCEL trisDcel = new();
            Dictionary<DelaunayTriangle, DCEL.Face> trisLookup = new();

            DelaunayTriangle FindLeafContainingPoint(DelaunayPoint px)
            {
                // Breadth-first search for a leaf triangle containing px
                DelaunayTriangle BfsFrom(DelaunayTriangle start)
                {
                    var queue = new Queue<DelaunayTriangle>();
                    var visited = new HashSet<DelaunayTriangle>();
                    queue.Enqueue(start);

                    while (queue.Count > 0)
                    {
                        var tri = queue.Dequeue();
                        if (!visited.Add(tri))
                            continue; // already seen

                        // Reject triangles that do not contain the point
                        if (!tri.Contains(px))
                            continue;

                        // This is the node we want
                        if (!trisDag.HasChildren(tri))
                            return tri;

                        // Enqueue children
                        foreach (var child in trisDag.GetChildren(tri))
                        {
                            queue.Enqueue(child);
                        }
                    }
                    // Shouldn't reach here
                    return start;
                }
                return BfsFrom(trisDag.Root);
            }

            DCEL.Face AddTriangleToDCEL(DelaunayTriangle tri)
            {                
                int a = tri.A.Idx;
                int b = tri.B.Idx;
                int c = tri.C.Idx;

                // Ensure CCW order
                float cross = Cross(tri.A.Position, tri.B.Position, tri.C.Position);
                if (cross < 0f)
                {
                    (c, b) = (b, c);
                }

                var face = trisDcel.AddTriangle(a, b, c);
                return face;
            }

            void LegalizeEdge(DelaunayPoint pi, DelaunayPoint pj)
            {
                // edge is legal if pk is NOT inside the circumcircle of triangle (pi, pj, p)
                // For Delaunay: check if pk is right of or on the circle through pi, pj, p
                
                // If {pi, pj} is part of the initial bounding triangle, it's always legal
                if ((pi.Idx < 0 && pj.Idx < 0) || 
                    (pi.Idx < 0 && pj == highest.Value) || 
                    (pj.Idx < 0 && pi == highest.Value))
                {
                    return;
                }
                
                if (!trisDcel.TryGetEdge(pi.Idx, pj.Idx, out var e))
                    return; // edge doesn't exist
                
                // Get the two adjacent triangles
                int pt_idx = e.Next.Next.Origin;
                int pk_idx = e.Twin.Next.Next.Origin;
                
                // If at least one sentinel, edge is legal iff exactly one sentinel or p-2 in {pk,pt}
                if (pi.Idx < 0 || pj.Idx < 0 || pt_idx < 0 || pk_idx < 0)
                {
                    if (Math.Min(pk_idx, pt_idx) < Math.Min(pi.Idx, pj.Idx))
                        return;
                }

                DelaunayPoint pt = dPoints.Find(dp => dp.Idx == pt_idx);
                DelaunayPoint pk = dPoints.Find(dp => dp.Idx == pk_idx);
                
                // InCircle test: is pk inside circumcircle of (pi, pj, p)?
                // Using determinant form for style points
                Vector2 a = pi.Position - pk.Position;
                Vector2 b = pj.Position - pk.Position;
                Vector2 c = pt.Position - pk.Position;
                
                float det =   (a.x * a.x + a.y * a.y) * (b.x * c.y - b.y * c.x)
                            - (b.x * b.x + b.y * b.y) * (a.x * c.y - a.y * c.x)
                            + (c.x * c.x + c.y * c.y) * (a.x * b.y - a.y * b.x);
                
                // If det > 0, pk is inside circle, edge is illegal and needs flipping
                if (det <= 0f)
                    return; // edge is legal
                
                // TODO: Flip edge (pi, pj) to edge (p, pk) and recursively legalize
            }

            void SubdivideFaceWithPoint(DelaunayTriangle tri, DelaunayPoint p)
            {
                // add 3 triangles
                DCEL.Face f1, f2, f3;
                f1 = AddTriangleToDCEL(new DelaunayTriangle(tri.A, tri.B, p));
                f2 = AddTriangleToDCEL(new DelaunayTriangle(tri.B, tri.C, p));
                f3 = AddTriangleToDCEL(new DelaunayTriangle(tri.C, tri.A, p));

                LegalizeEdge(tri.A, tri.B);
                LegalizeEdge(tri.B, tri.C);
                LegalizeEdge(tri.C, tri.A);
            }

            var rootTriangle = new DelaunayTriangle(highest.Value, pMinus1, pMinus2);
            trisLookup[rootTriangle] = AddTriangleToDCEL(rootTriangle);
            trisDag.AddNode(rootTriangle);
            DelaunayTriangle currentTriangle = rootTriangle;

            // Consider dpoints in arbitrary order
            foreach (var pr in dPoints)
            {
                // Find triangle containing pr
                currentTriangle = FindLeafContainingPoint(pr);
                SubdivideFaceWithPoint(currentTriangle, pr);

                // TODO: Deal with colinear pr during subdivision
            }
            
            // TODO: Finish Delaunay triangulation algorithm
            return new List<Vector2Int>();
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
