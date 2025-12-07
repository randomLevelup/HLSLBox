using System;
using System.Collections.Generic;
using UnityEngine;
using HLSLBox.Algorithms;

[ExecuteAlways]
[RequireComponent(typeof(MeshRenderer))]
public class Interx : Poly2D
{
    [Header("Intersect Mode")]
    [SerializeField] Simple shapeA;
    [SerializeField] Simple shapeB;

    protected override void UpdateShape()
    {
        if (shapeA == null || shapeB == null || particles == null) return;
        int count = Mathf.Max(0, particles.ParticleCount);
        if (count <= 0) return;

        // Fetch current particle positions (UV space)
        Algo2D.EnsureArraySize(ref vtexPositionsUV, count);
        if (!particles.TryCopyPositionsUV(ref vtexPositionsUV)) return;

        // Build polygons for A and B in UV space
        var polyA = GatherPolygon(shapeA.indices, vtexPositionsUV);
        var polyB = GatherPolygon(shapeB.indices, vtexPositionsUV);
        EnsureCCW(polyA);
        EnsureCCW(polyB);
        if (polyA.Count < 3 || polyB.Count < 3)
        {
            indices.Clear();
            SetVirtualVertices(null);
            return;
        }

        // Compute simple polygon intersection (Weiler-Atherton clipping)
        var intersection = SimplePolygonIntersect(polyA, polyB);
        if (intersection.Count < 3)
        {
            indices.Clear();
            SetVirtualVertices(null);
            return;
        }

        // Treat intersection vertices as virtual vertices appended after real particles
        SetVirtualVertices(intersection);
        indices = new List<int>(intersection.Count);
        int baseIndex = particles.ParticleCount;
        for (int i = 0; i < intersection.Count; i++)
        {
            indices.Add(baseIndex + i);
        }
    }

    // Called by the custom editor button
    public void ResetEdges()
    {
        if (shapeA == null || shapeB == null) return;
        if (particles == null) return;
        
        // First, recompute shapeA's convex hull with all particles
        shapeA.ReConvexify();
        
        // Build set of indices used by shapeA to exclude from shapeB
        var excludeSet = new HashSet<int>();
        if (shapeA != null && shapeA.indices != null)
        {
            foreach (var idx in shapeA.indices)
            {
                excludeSet.Add(idx);
            }
        }
        
        // Recompute shapeB's convex hull excluding shapeA's vertices
        shapeB.ReConvexify(excludeSet);
    }

    // Collect ordered polygon vertices in UV space using provided indices
    List<Vector2> GatherPolygon(List<int> idx, Vector2[] positions)
    {
        var poly = new List<Vector2>(idx.Count);
        for (int i = 0; i < idx.Count; i++)
        {
            int k = Mathf.Clamp(idx[i], 0, positions.Length - 1);
            poly.Add(positions[k]);
        }
        return poly;
    }

    // Simple polygon intersection using Weiler-Atherton clipping
    List<Vector2> SimplePolygonIntersect(List<Vector2> polyA, List<Vector2> polyB)
    {
        // Find all edge-edge intersection points
        var intersections = new List<IntersectionPoint>();
        
        for (int i = 0; i < polyA.Count; i++)
        {
            var a1 = polyA[i];
            var a2 = polyA[(i + 1) % polyA.Count];
            
            for (int j = 0; j < polyB.Count; j++)
            {
                var b1 = polyB[j];
                var b2 = polyB[(j + 1) % polyB.Count];
                
                if (SegmentIntersect(a1, a2, b1, b2, out Vector2 point, out float tA, out float tB))
                {
                    intersections.Add(new IntersectionPoint
                    {
                        point = point,
                        edgeA = i,
                        edgeB = j,
                        tA = tA,
                        tB = tB
                    });
                }
            }
        }

        // If no intersections, check containment
        if (intersections.Count == 0)
        {
            if (PointInPolygon(polyA[0], polyB)) return new List<Vector2>(polyA);
            if (PointInPolygon(polyB[0], polyA)) return new List<Vector2>(polyB);
            return new List<Vector2>();
        }

        // Build result polygon by walking intersection points
        var result = new List<Vector2>();
        var visitedIntersections = new HashSet<int>();
        
        // Start from first intersection
        int currentIntersection = 0;
        bool onPolyA = true;
        int maxIterations = (polyA.Count + polyB.Count + intersections.Count) * 2;
        int iterations = 0;
        
        result.Add(intersections[0].point);
        visitedIntersections.Add(0);
        
        while (iterations++ < maxIterations)
        {
            // Find next intersection point
            int nextIntersection = -1;
            
            if (onPolyA)
            {
                // Walk along polyA edges
                int edgeStart = intersections[currentIntersection].edgeA;
                float tStart = intersections[currentIntersection].tA;
                
                for (int i = 0; i < intersections.Count; i++)
                {
                    if (visitedIntersections.Contains(i)) continue;
                    if (intersections[i].edgeA == edgeStart && intersections[i].tA > tStart)
                    {
                        if (nextIntersection == -1 || intersections[i].tA < intersections[nextIntersection].tA)
                            nextIntersection = i;
                    }
                    else if (intersections[i].edgeA > edgeStart)
                    {
                        if (nextIntersection == -1 || intersections[i].edgeA < intersections[nextIntersection].edgeA ||
                            (intersections[i].edgeA == intersections[nextIntersection].edgeA && intersections[i].tA < intersections[nextIntersection].tA))
                            nextIntersection = i;
                    }
                }
            }
            else
            {
                // Walk along polyB edges
                int edgeStart = intersections[currentIntersection].edgeB;
                float tStart = intersections[currentIntersection].tB;
                
                for (int i = 0; i < intersections.Count; i++)
                {
                    if (visitedIntersections.Contains(i)) continue;
                    if (intersections[i].edgeB == edgeStart && intersections[i].tB > tStart)
                    {
                        if (nextIntersection == -1 || intersections[i].tB < intersections[nextIntersection].tB)
                            nextIntersection = i;
                    }
                    else if (intersections[i].edgeB > edgeStart)
                    {
                        if (nextIntersection == -1 || intersections[i].edgeB < intersections[nextIntersection].edgeB ||
                            (intersections[i].edgeB == intersections[nextIntersection].edgeB && intersections[i].tB < intersections[nextIntersection].tB))
                            nextIntersection = i;
                    }
                }
            }
            
            if (nextIntersection == -1 || nextIntersection == 0) break;
            
            result.Add(intersections[nextIntersection].point);
            visitedIntersections.Add(nextIntersection);
            currentIntersection = nextIntersection;
            onPolyA = !onPolyA;
        }
        
        return result.Count >= 3 ? result : new List<Vector2>();
    }

    struct IntersectionPoint
    {
        public Vector2 point;
        public int edgeA;
        public int edgeB;
        public float tA;
        public float tB;
    }

    // Test if two line segments intersect and return the intersection point
    bool SegmentIntersect(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2, out Vector2 point, out float tA, out float tB)
    {
        point = Vector2.zero;
        tA = tB = 0f;
        
        Vector2 da = a2 - a1;
        Vector2 db = b2 - b1;
        float denom = Cross(da, db);
        
        if (Mathf.Abs(denom) < 1e-8f) return false; // parallel
        
        Vector2 diff = b1 - a1;
        tA = Cross(diff, db) / denom;
        tB = Cross(diff, da) / denom;
        
        if (tA >= 0f && tA <= 1f && tB >= 0f && tB <= 1f)
        {
            point = a1 + da * tA;
            return true;
        }
        
        return false;
    }

    // Point-in-polygon test using ray casting
    bool PointInPolygon(Vector2 point, List<Vector2> polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            if (((polygon[i].y > point.y) != (polygon[j].y > point.y)) &&
                (point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x))
            {
                inside = !inside;
            }
        }
        return inside;
    }

    float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;

    // Ensure polygon winding is CCW (in-place)
    void EnsureCCW(List<Vector2> poly)
    {
        if (poly == null || poly.Count < 3) return;
        float area = 0f;
        for (int i = 0; i < poly.Count; i++)
        {
            var p0 = poly[i];
            var p1 = poly[(i + 1) % poly.Count];
            area += (p1.x - p0.x) * (p1.y + p0.y);
        }
        // Negative area indicates clockwise
        if (area > 0f)
        {
            poly.Reverse();
        }
    }
}