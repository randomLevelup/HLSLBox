using System;
using System.Collections.Generic;
using UnityEngine;

namespace HLSLBox.Algorithms
{
    /// <summary>
    /// Doubly-Connected Edge List (DCEL) data structure for planar subdivisions.
    /// </summary>
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

        public HalfEdge GetOrCreateEdge(int origin, int dest, Face leftFace)
        {
            if (TryGetEdge(origin, dest, out var e))
            {
                // Edge exists -> reassign its incident face
                e.IncidentFace = leftFace;
                return e;
            }

            // Create the pair (origin->dest) and (dest->origin)
            var he = new HalfEdge { Origin = origin, IncidentFace = leftFace };
            var twin = new HalfEdge { Origin = dest, IncidentFace = OuterFace };
            he.Twin = twin;
            twin.Twin = he;

            HalfEdges.Add(he);
            HalfEdges.Add(twin);

            RegisterEdge(origin, dest, he);
            RegisterEdge(dest, origin, twin);
            return he;
        }

        // Adds a triangle face defined by vertices (a, b, c) in CCW order.
        // Creates missing half-edges and sets up Next/Prev within the new face loop.
        public Face AddTriangle(int a, int b, int c)
        {
            // Debug.log($"Adding triangle face with vertices: {a}, {b}, {c}");
            if (a == b || b == c || c == a) return null; // degenerate

            var face = new Face();
            Faces.Add(face);

            HalfEdge eab = GetOrCreateEdge(a, b, face);
            HalfEdge ebc = GetOrCreateEdge(b, c, face);
            HalfEdge eca = GetOrCreateEdge(c, a, face);

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
}
