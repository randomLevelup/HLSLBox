using System;
using System.Collections.Generic;
using UnityEngine;

namespace HLSLBox.Algorithms
{
    /// <summary>
    /// Delaunay triangulation algorithm using incremental point insertion.
    /// </summary>
    public static class DelaunayTriangulateAlg
    {
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
                // Inline cross product computation
                Vector2 vij = pj.Position - pi.Position;
                Vector2 vik = pk.Position - pi.Position;
                return (vij.x * vik.y - vij.y * vik.x) > 0f;
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
                => DelaunayPoint.IsLeftOf(px, A, B) &&
                   DelaunayPoint.IsLeftOf(px, B, C) &&
                   DelaunayPoint.IsLeftOf(px, C, A);

            public IReadOnlyList<int> Indices()
            {
                return new[] { A.Idx, B.Idx, C.Idx };
            }
        }

        public static List<Vector2Int> DelaunayTriangulateIndices(Vector2[] positions, int count)
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
            
            Dag<DelaunayTriangle> trisDag = new(); // DAG of triangles
            DCEL trisDcel = new(); // DCEL stores current triangulation
            var faceToTriangle = new Dictionary<DCEL.Face, DelaunayTriangle>();

            // Breadth-first search for a leaf triangle containing px
            DelaunayTriangle FindLeafContainingPoint(DelaunayPoint px)
            {
                var queue = new Queue<DelaunayTriangle>();
                var visited = new HashSet<DelaunayTriangle>();
                queue.Enqueue(trisDag.Root);

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
                throw new InvalidOperationException("Failed to find leaf triangle containing px");
            }

            DCEL.Face AddTriangleToDCEL(DelaunayTriangle tri)
            {                
                int a = tri.A.Idx;
                int b = tri.B.Idx;
                int c = tri.C.Idx;

                // Ensure CCW order using inline cross product
                Vector2 ab = tri.B.Position - tri.A.Position;
                Vector2 ac = tri.C.Position - tri.A.Position;
                float cross = ab.x * ac.y - ab.y * ac.x;
                if (cross < 0f)
                {
                    (c, b) = (b, c);
                }

                var face = trisDcel.AddTriangle(a, b, c);
                if (face != null)
                    faceToTriangle[face] = tri;
                return face;
            }

            // void LinkTwinWithFace(int originIdx, int destIdx, DCEL.Face incidentFace)
            // {
            //     if (trisDcel.TryGetEdge(originIdx, destIdx, out var e))
            //         e.Twin.IncidentFace = incidentFace;
            // }

            void LegalizeEdge(DelaunayPoint pi, DelaunayPoint pj)
            {                
                // If {pi, pj} is part of the initial bounding triangle, it's always legal
                if ((pi.Idx < 0 && pj.Idx < 0) || 
                    (pi.Idx < 0 && pj == highest.Value) || 
                    (pj.Idx < 0 && pi == highest.Value))
                {
                    return;
                }
                
                // Otherwise, Edge is legal iff pk is OUTSIDE the circumcircle of triangle (pi, pj, pl)

                if (!trisDcel.TryGetEdge(pi.Idx, pj.Idx, out var e))
                    return; // edge doesn't exist
                
                // Get the auxiliary points
                int pl_idx = e.Next.Next.Origin;
                int pk_idx = e.Twin.Next.Next.Origin;
                
                // If at least one sentinel, edge is legal iff exactly one sentinel or p-2 in {pk,pl}
                if (pi.Idx < 0 || pj.Idx < 0 || pl_idx < 0 || pk_idx < 0)
                {
                    if (Math.Min(pk_idx, pl_idx) < Math.Min(pi.Idx, pj.Idx))
                        return;
                }

                DelaunayPoint pl = dPoints.Find(dp => dp.Idx == pl_idx);
                DelaunayPoint pk = dPoints.Find(dp => dp.Idx == pk_idx);
                
                // Is pk inside circumcircle of (pi, pj, pl)?
                // Test using determinant form
                Vector2 a = pi.Position - pk.Position;
                Vector2 b = pj.Position - pk.Position;
                Vector2 c = pl.Position - pk.Position;
                
                float det =   (a.x * a.x + a.y * a.y) * (b.x * c.y - b.y * c.x)
                            - (b.x * b.x + b.y * b.y) * (a.x * c.y - a.y * c.x)
                            + (c.x * c.x + c.y * c.y) * (a.x * b.y - a.y * b.x);
                
                if (det <= 0f)
                    return; // edge (pi, pj) is legal

                // pk is inside circle ==> edge is illegal
                // Flip (pi, pj) to edge (pl, pk) and recursively legalize
                var jlkTri = new DelaunayTriangle(pj, pl, pk);
                var iklTri = new DelaunayTriangle(pi, pk, pl);

                DCEL.Face jlkFace = AddTriangleToDCEL(jlkTri);
                DCEL.Face iklFace = AddTriangleToDCEL(iklTri);
                DCEL.Face ijlFace = e.IncidentFace;
                DCEL.Face jikFace = e.Twin.IncidentFace;

                // Update DAG
                if (ijlFace != null && faceToTriangle.TryGetValue(ijlFace, out var ijlTri))
                    trisDag.AddChildren(ijlTri, jlkTri, iklTri);
                if (jikFace != null && faceToTriangle.TryGetValue(jikFace, out var jikTri))
                    trisDag.AddChildren(jikTri, jlkTri, iklTri);

                // Recursively legalize new edges
                LegalizeEdge(pi, pl);
                LegalizeEdge(pk, pj);
            }

            void SubdivideFaceWithPoint(DelaunayTriangle tri, DelaunayPoint p)
            {
                // add 3 triangles
                DelaunayTriangle t1 = new DelaunayTriangle(tri.A, tri.B, p);
                DelaunayTriangle t2 = new DelaunayTriangle(tri.B, tri.C, p);
                DelaunayTriangle t3 = new DelaunayTriangle(tri.C, tri.A, p);
                
                DCEL.Face f1 = AddTriangleToDCEL(t1);
                DCEL.Face f2 = AddTriangleToDCEL(t2);
                DCEL.Face f3 = AddTriangleToDCEL(t3);

                // Link new half-edges between the three new faces
                // LinkTwinWithFace(tri.A.Idx, p.Idx, f3);
                // LinkTwinWithFace(tri.B.Idx, p.Idx, f1);
                // LinkTwinWithFace(tri.C.Idx, p.Idx, f2);
                // LinkTwinWithFace(p.Idx, tri.A.Idx, f1);
                // LinkTwinWithFace(p.Idx, tri.B.Idx, f2);
                // LinkTwinWithFace(p.Idx, tri.C.Idx, f3);

                // Update DAG: original triangle has 3 children
                trisDag.AddChildren(tri, t1, t2, t3);

                // Recursively legalize edges opposite to the new point
                LegalizeEdge(tri.A, tri.B);
                LegalizeEdge(tri.B, tri.C);
                LegalizeEdge(tri.C, tri.A);
            }

            var rootTriangle = new DelaunayTriangle(highest.Value, pMinus1, pMinus2);
            AddTriangleToDCEL(rootTriangle);
            trisDag.AddRootNode(rootTriangle);

            // Consider dpoints in insertion order (skip sentinels and highest point)
            foreach (var pr in dPoints)
            {
                if (pr.Type != DelaunayPoint.PointType.P || pr == highest.Value)
                    continue;

                // Find triangle containing pr
                DelaunayTriangle currentTriangle = FindLeafContainingPoint(pr);
                SubdivideFaceWithPoint(currentTriangle, pr);
            }
            
            // Convert DCEL to edge list, filtering out sentinel edges
            return trisDcel.ToEdgeList();
        }
    }
}
