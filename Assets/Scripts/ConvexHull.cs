using System;
using System.Collections.Generic;
using UnityEngine;
using HLSLBox.Algorithms;

[ExecuteAlways]
[RequireComponent(typeof(MeshRenderer))]
public class ConvexHull : Poly2D
{
	protected override void UpdateShape()
	{
		if (particles == null) return;
		int count = Mathf.Max(0, particles.ParticleCount);
		if (count <= 0) return;

		Algo2D.EnsureArraySize(ref vtexPositionsUV, count);
		if (!particles.TryCopyPositionsUV(ref vtexPositionsUV)) return;

		var newIdx = Algo2D.ConvexHullIndices(vtexPositionsUV, count);

		if (!HLSLBox.Algorithms.Algo2D.SequenceEqual(indices, newIdx))
		{
			indices = newIdx;
			EnsureTextures();
			Upload();
			UpdateMaterial();
		}
	}
}
