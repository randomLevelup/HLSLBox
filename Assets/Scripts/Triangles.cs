using System;
using System.Collections.Generic;
using UnityEngine;
using HLSLBox.Algorithms;

[ExecuteAlways]
[RequireComponent(typeof(MeshRenderer))]
public class Triangles : Edge2D
{
    protected override void UpdateEdges()
    {
        if (particles == null) return;
        var posBuffer = particles.PositionsBuffer;
        int count = Mathf.Max(0, particles.ParticleCount);
        if (posBuffer == null || count <= 0) return;

        Algo2D.EnsureArraySize(ref vtexPositionsUV, count);
        try { posBuffer.GetData(vtexPositionsUV); } catch (Exception) { return; }

        var newEdges = Algo2D.TriangulateIndices(vtexPositionsUV, count);

        if (!HLSLBox.Algorithms.Algo2D.SequenceEqual(edges, newEdges))
        {
            SetEdges(newEdges);
            UpdateMaterial();
        }
    }
}