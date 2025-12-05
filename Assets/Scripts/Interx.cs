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
        // TODO: Implement intersection logic to update indices based on shapeA and shapeB
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
}