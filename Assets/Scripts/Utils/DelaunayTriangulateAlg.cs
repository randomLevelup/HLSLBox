using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace HLSLBox.Algorithms
{
    /// <summary>
    /// Delaunay triangulation algorithm using incremental point insertion.
    /// </summary>
    public static class DelaunayTriangulateAlg
    {
        // Point record type
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
                int cmp = a.Position.y.CompareTo(b.Position.y);
                if (cmp != 0) return cmp;
                return b.Position.x.CompareTo(a.Position.x);
            }

            public static bool operator ==(DelaunayPoint left, DelaunayPoint right) => left.Equals(right);
            public static bool operator !=(DelaunayPoint left, DelaunayPoint right) => !left.Equals(right);
            public static bool operator <(DelaunayPoint a, DelaunayPoint b) => Compare(a, b) < 0;
            public static bool operator >(DelaunayPoint a, DelaunayPoint b) => Compare(a, b) > 0;

            // Returns 1 if pj is left of (pi -> pk), -1 if right, 0 if on the edge.
            public static int IsLeftOf(DelaunayPoint pj, DelaunayPoint pi, DelaunayPoint pk)
            {
                // Cases: pj is a sentinel
                switch (pj.Type)
                {
                    case PointType.PMinus1:
                    {
                        if (pk.Type == PointType.PMinus2)
                            return 1;  // edge goes toward above-left, p-1 is left of that
                        if (pi.Type == PointType.PMinus2)
                            return -1; // edge comes from above-left, p-1 is right of that
                        return Math.Sign(Compare(pi, pk)); // p-1 is left iff edge points down-right
                    }
                    case PointType.PMinus2:
                    {
                        if (pk.Type == PointType.PMinus1)
                            return -1; // edge goes toward below-right, p-2 is right of that
                        if (pi.Type == PointType.PMinus1)
                            return 1;  // edge comes from below-right, p-2 is left of that
                        return Math.Sign(Compare(pk, pi)); // p-2 is left iff edge points "up/left"
                    }
                    case PointType.P: break;
                }

                // Cases: pj is a regular point
                switch (pi.Type)
                {
                    case PointType.PMinus1: return Math.Sign(Compare(pi, pj)); // edge from below-right: left means "less than"
                    case PointType.PMinus2: return Math.Sign(Compare(pj, pi)); // edge from above-left: left means "greater than"
                    case PointType.P: break;
                }
                switch (pk.Type)
                {
                    case PointType.PMinus1: return Math.Sign(Compare(pj, pi)); // edge toward below-right: left means "greater than"
                    case PointType.PMinus2: return Math.Sign(Compare(pi, pj)); // edge toward above-left: left means "less than"
                    case PointType.P: break;
                }

                // All three are regular points: use cross product
                Vector2 vik = pk.Position - pi.Position;
                Vector2 vij = pj.Position - pi.Position;
                float cross = vik.x * vij.y - vik.y * vij.x;
                if (cross > 0f)
                    return 1;
                if (cross < 0f)
                    return -1;
                return 0;
            }
        }

        /// Triangle type with point containment test
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
            public int Contains(DelaunayPoint px)
            {
                int ab = DelaunayPoint.IsLeftOf(px, A, B);
                int bc = DelaunayPoint.IsLeftOf(px, B, C);
                int ca = DelaunayPoint.IsLeftOf(px, C, A);

                if (ab < 0 || bc < 0 || ca < 0)
                    return -1; // outside
                if (ab == 0 || bc == 0 || ca == 0)
                    return 0; // on edge
                return 1; // inside
            }

            // For debugging
            public override string ToString() => $"({A.Idx}, {B.Idx}, {C.Idx})";
        }

        public static async Task<List<Vector2Int>> DelaunayTriangulateIndices(Vector2[] positions, int count)
        {
            var debugLogBuilder = new StringBuilder();
            void DebugLog(string message) => debugLogBuilder.AppendLine(message);
            void FlushDebugLog()
            {
                if (debugLogBuilder.Length == 0)
                    return;

                Debug.Log(debugLogBuilder.ToString());
                debugLogBuilder.Clear();
            }

            DebugLog("==========================================\nStarting Delaunay triangulation...");
            var dPoints = new List<DelaunayPoint>(count);
            DelaunayPoint pMinus1 = new(-1, Vector2.zero, DelaunayPoint.PointType.PMinus1);
            DelaunayPoint pMinus2 = new(-2, Vector2.zero, DelaunayPoint.PointType.PMinus2);
            DelaunayPoint? highest = null;

            Dag<DelaunayTriangle> trisDag = new(); // DAG of triangles
            DCEL trisDcel = new(); // DCEL stores current triangulation
            var faceToTriangle = new Dictionary<DCEL.Face, DelaunayTriangle>();

            // Breadth-first search for a leaf triangle containing px
            DelaunayTriangle FindLeafContainingPoint(DelaunayPoint px)
            {
                // Debug.log($"Finding leaf triangle containing point: {px.Idx}");
                var queue = new Queue<DelaunayTriangle>();
                var visited = new HashSet<DelaunayTriangle>();
                queue.Enqueue(trisDag.Root);

                while (queue.Count > 0)
                {
                    var tri = queue.Dequeue();
                    if (!visited.Add(tri))
                        continue; // already seen

                    // Reject triangles that do not contain the point
                    if (tri.Contains(px) < 0)
                    {
                        // Debug.log($"Triangle {tri} does not contain point: {px.Idx}");
                        continue;
                    }

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

                int leftTest = DelaunayPoint.IsLeftOf(tri.C, tri.A, tri.B);
                if (leftTest == 0) throw new InvalidOperationException("Degenerate triangle with collinear points.");

                // Ensure CCW order
                if (leftTest < 0)
                {
                    (c, b) = (b, c);
                }

                var face = trisDcel.AddTriangle(a, b, c);
                if (face != null)
                    faceToTriangle[face] = tri;
                return face;
            }

            async Task LegalizeEdge(DelaunayPoint pi, DelaunayPoint pj)
            {
                // Debug.log($"Legalizing edge: {pi.Idx} and {pj.Idx}");
                // If {pi, pj} is part of the initial bounding triangle, it's always legal
                if ((pi.Idx < 0 && pj.Idx < 0) ||
                    (pi.Idx < 0 && pj == highest.Value) ||
                    (pi == highest.Value && pj.Idx < 0))
                {
                    return;
                }

                // Otherwise, Edge is legal iff pk is OUTSIDE the circumcircle of triangle (pi, pj, pl)
                if (!trisDcel.TryGetEdge(pi.Idx, pj.Idx, out var e))
                    return;

                // Get the auxiliary points
                int pl_idx, pk_idx;
                try
                {
                    pl_idx = e.Next.Next.Origin;
                    pk_idx = e.Twin.Next.Next.Origin;
                }
                catch (NullReferenceException ex)
                {
                    DebugLog($"Edge ({pi.Idx}, {pj.Idx}) has no adjacent triangle; cannot legalize.\nHighest point is {highest?.Idx}");
                    FlushDebugLog();
                    throw new InvalidOperationException($"Edge ({pi.Idx}, {pj.Idx}) has no adjacent triangle", ex);
                }

                // If at least one sentinel, edge is legal iff exactly one sentinel or p-2 in {pk,pl}
                if (pi.Idx < 0 || pj.Idx < 0 || pl_idx < 0 || pk_idx < 0)
                {
                    if (Math.Min(pk_idx, pl_idx) < Math.Min(pi.Idx, pj.Idx))
                        return;
                }

                // TODO: Optimize point lookup
                DelaunayPoint pl = dPoints.Find(dp => dp.Idx == pl_idx);
                DelaunayPoint pk = dPoints.Find(dp => dp.Idx == pk_idx);

                // Is pk inside circumcircle of (pi, pj, pl)?
                Vector2 a = pi.Position - pk.Position;
                Vector2 b = pj.Position - pk.Position;
                Vector2 c = pl.Position - pk.Position;
                // Test using determinant form
                float det =   (a.x * a.x + a.y * a.y) * (b.x * c.y - b.y * c.x)
                            - (b.x * b.x + b.y * b.y) * (a.x * c.y - a.y * c.x)
                            + (c.x * c.x + c.y * c.y) * (a.x * b.y - a.y * b.x);

                if (det <= 0f)
                    return; // edge (pi, pj) is legal

                // else, pk is inside circle ==> edge is illegal
                // Flip (pi, pj) to edge (pl, pk) and recursively legalize
                DebugLog($"Flipping edge ({pi.Idx}, {pj.Idx}) to ({pl.Idx}, {pk.Idx})");
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
                await LegalizeEdge(pi, pl);
                await LegalizeEdge(pk, pj);
            }

            async Task SubdivideFaceWithPoint(DelaunayTriangle tri, DelaunayPoint p)
            {
                // Debug.Log($"Subdividing face with point: {p.Idx} at position {p.Position}");
                // add 3 triangles
                DelaunayTriangle t1 = new DelaunayTriangle(tri.A, tri.B, p);
                DelaunayTriangle t2 = new DelaunayTriangle(tri.B, tri.C, p);
                DelaunayTriangle t3 = new DelaunayTriangle(tri.C, tri.A, p);

                DCEL.Face f1 = AddTriangleToDCEL(t1);
                DCEL.Face f2 = AddTriangleToDCEL(t2);
                DCEL.Face f3 = AddTriangleToDCEL(t3);

                DebugLog($"Subdivided point {p.Idx} into triangles: {t1}, {t2}, {t3}");

                // Update DAG
                trisDag.AddChildren(tri, t1, t2, t3);

                // Recursively legalize edges opposite to the new point
                await LegalizeEdge(tri.A, tri.B);
                await LegalizeEdge(tri.B, tri.C);
                await LegalizeEdge(tri.C, tri.A);
            }

            async Task SubdivideDoubleFaceWithPoint(DelaunayTriangle tri, DelaunayPoint p)
            {
                DebugLog($"Degenerate case: double face at point: {p.Idx} at position {p.Position}");
                
                // Find which edge contains point p
                DelaunayPoint a, b, c;
                int abTest = DelaunayPoint.IsLeftOf(p, tri.A, tri.B);
                int bcTest = DelaunayPoint.IsLeftOf(p, tri.B, tri.C);
                int caTest = DelaunayPoint.IsLeftOf(p, tri.C, tri.A);

                if (abTest == 0)
                {
                    // p is on edge AB
                    a = tri.A;
                    b = tri.B;
                    c = tri.C;
                }
                else if (bcTest == 0)
                {
                    // p is on edge BC
                    a = tri.B;
                    b = tri.C;
                    c = tri.A;
                }
                else // (caTest == 0)
                {
                    // p is on edge CA
                    a = tri.C;
                    b = tri.A;
                    c = tri.B;
                }

                // Find the adjacent triangle sharing edge AB
                trisDcel.TryGetEdge(a.Idx, b.Idx, out var edge);

                // Get the third vertex X of the adjacent triangle
                int xIdx;
                try
                {
                    xIdx = edge.Twin.Next.Next.Origin;
                }
                catch (NullReferenceException ex)
                {
                    DebugLog($"Edge ({a.Idx}, {b.Idx}) has no adjacent triangle while handling degenerate face.");
                    FlushDebugLog();
                    throw new InvalidOperationException($"Edge ({a.Idx}, {b.Idx}) has no adjacent triangle", ex);
                }

                DelaunayPoint x = dPoints.Find(dp => dp.Idx == xIdx);

                // Create four new triangles: APC, BPX, APX, BPC
                DelaunayTriangle apc = new DelaunayTriangle(a, p, c);
                DelaunayTriangle bpc = new DelaunayTriangle(b, p, c);
                DelaunayTriangle apx = new DelaunayTriangle(a, p, x);
                DelaunayTriangle bpx = new DelaunayTriangle(b, p, x);

                // Add triangles to DCEL
                DCEL.Face apcFace = AddTriangleToDCEL(apc);
                DCEL.Face bpcFace = AddTriangleToDCEL(bpc);
                DCEL.Face apxFace = AddTriangleToDCEL(apx);
                DCEL.Face bpxFace = AddTriangleToDCEL(bpx);

                // Update DAG - add the four new triangles as children of the original triangle
                DCEL.Face abcFace = edge.IncidentFace;
                DCEL.Face baxFace = edge.Twin.IncidentFace;

                if (abcFace != null && faceToTriangle.TryGetValue(abcFace, out var abcTri))
                    trisDag.AddChildren(abcTri, apc, bpc, apx, bpx);
                if (baxFace != null && faceToTriangle.TryGetValue(baxFace, out var baxTri))
                    trisDag.AddChildren(baxTri, apc, bpc, apx, bpx);

                // Legalize edges opposite to p
                await LegalizeEdge(a, x);
                await LegalizeEdge(x, b);
                await LegalizeEdge(b, c);
                await LegalizeEdge(c, a);
            }

            /// ALGORITHM SETUP ///

            // Build the point set
            for (int i = 0; i < count; i++)
            {
                dPoints.Add(new DelaunayPoint(i, positions[i], DelaunayPoint.PointType.P));
            }

            // Add sentinel points
            dPoints.Add(pMinus1);
            dPoints.Add(pMinus2);

            // Find the highest point
            foreach (var dp in dPoints)
            {
                if (dp.Type != DelaunayPoint.PointType.P)
                    continue;

                if (!highest.HasValue ||
                    dp.Position.y > highest.Value.Position.y ||
                        (Mathf.Approximately(dp.Position.y, highest.Value.Position.y) &&
                         dp.Position.x > highest.Value.Position.x))
                {
                    highest = dp;
                }
            }
            // Debug.log($"Highest point has position {highest?.Position}");

            if (!highest.HasValue)
                throw new InvalidOperationException("No valid points provided for triangulation");

            // Init root triangle
            var rootTriangle = new DelaunayTriangle(highest.Value, pMinus2, pMinus1);
            AddTriangleToDCEL(rootTriangle);
            trisDag.AddRootNode(rootTriangle);

            // Shuffle points for average-case performance
            var rand = new System.Random(11111);
            int n = dPoints.Count;
            for (int i = n - 1; i > 0; i--)
            {
                int j = rand.Next(i + 1);
                (dPoints[i], dPoints[j]) = (dPoints[j], dPoints[i]);
            }

            // MAIN LOOP: Consider all dpoints except sentinels and highest point
            foreach (var pr in dPoints)
            {
                if (pr.Type != DelaunayPoint.PointType.P || pr == highest.Value)
                    continue;

                // Find triangle containing pr
                DelaunayTriangle currentTriangle = FindLeafContainingPoint(pr);
                if (currentTriangle.Contains(pr) > 0)
                    await SubdivideFaceWithPoint(currentTriangle, pr);
                else
                    await SubdivideDoubleFaceWithPoint(currentTriangle, pr);
            }

            // Convert DCEL faces of DAG leaves to edge list, filtering out sentinel edges
            var result = new List<Vector2Int>();
            var leaves = trisDag.GetLeaves();

            foreach (var leaf in leaves)
            {
                if (leaf.A.Idx < 0 || leaf.B.Idx < 0 || leaf.C.Idx < 0)
                    continue;

                int a = leaf.A.Idx;
                int b = leaf.B.Idx;
                int c = leaf.C.Idx;

                result.Add(new Vector2Int(a, b));
                result.Add(new Vector2Int(b, c));
                result.Add(new Vector2Int(c, a));
            }

            return result;
        }
    }
}
