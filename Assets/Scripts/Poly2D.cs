using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using HLSLBox.Algorithms;

[ExecuteAlways]
[RequireComponent(typeof(MeshRenderer))]
public class Poly2D : Edge2D
{
	[Header("Polygon Indices (subset, ordered)")]
	[Tooltip("Ordered list of indices into the Particles2D list. Can be open or closed based on 'Closed Polygon'.")]
	[SerializeField] public List<int> indices = new List<int>();

	[SerializeField] protected bool closed = true;

	[Header("Vertex Display")]
	[SerializeField, Tooltip("Toggle drawing discs at polygon vertices in the polygon shader")] bool showVertices = false;
	[SerializeField, Min(0f), Tooltip("Vertex radius in UV units (use ~0.005–0.02)")] float vertexRadiusUV = 0.01f;

	// Polygon-only GPU data for vertex display
	protected Texture2D indicesTex; // stores vertex indices in R channel

	// Build base edge pairs from our ordered indices
	protected void RebuildEdgesFromIndices()
	{
		if (indices == null)
		{
			base.SetEdges(new List<Vector2Int>());
			return;
		}
		// Clamp first to particle count if available
		int maxIndex = GetMaxVertexIndex();
		Algo2D.ClampIndices(indices, maxIndex);
		var newEdges = new List<Vector2Int>(Mathf.Max(0, (indices.Count - 1) + (closed && indices.Count > 1 ? 1 : 0)));
		for (int i = 0; i < indices.Count - 1; i++)
		{
			newEdges.Add(new Vector2Int(indices[i], indices[i + 1]));
		}
		if (closed && indices.Count > 1)
		{
			newEdges.Add(new Vector2Int(indices[indices.Count - 1], indices[0]));
		}
		base.SetEdges(newEdges);
	}

	protected override void OnValidate()
	{
		base.OnValidate();
		vertexRadiusUV = Mathf.Max(0f, vertexRadiusUV);
		TrimIndices();
		// Don't rebuild edges during validation to prevent buffer leaks
		// The actual edge rebuild will happen in OnEnable or Update during play
		validateDirty = true;
	}

	protected override void UpdateMaterial()
	{
		base.UpdateMaterial();
		// After base sets common properties, attach polygon-specific buffers and props
		if (indicesTex != null)
		{
			mpb.SetTexture("_PolyIndicesTex", indicesTex);
			mpb.SetVector("_PolyTexSize", new Vector4(indicesTex.width, indicesTex.height, 0f, 0f));
		}
		mpb.SetInt("_PolyCount", Mathf.Max(0, indices?.Count ?? 0));
		mpb.SetInt("_PolyClosed", closed ? 1 : 0);
		mpb.SetInt("_ShowVertices", showVertices ? 1 : 0);
		mpb.SetVector("_VertexRadiusUV", new Vector4(vertexRadiusUV, vertexRadiusUV, 0f, 0f));
		mr.SetPropertyBlock(mpb);
	}

	protected override IEnumerator UpdateEdgesLoop()
	{
		var wait = new WaitForSeconds(0.05f);
		while (enabled)
		{
			UpdateShape();
			RebuildEdgesFromIndices();
			yield return wait;
		}
	}

	protected virtual void UpdateShape() { }

	protected override void Release()
	{
		base.Release();
		ReleaseTexture(ref indicesTex);
	}

	void EnsureIndicesTexture()
	{
		int count = Mathf.Max(1, indices?.Count ?? 0);
		if (indicesTex != null && indicesTex.width != count)
		{
			ReleaseTexture(ref indicesTex);
		}
		if (indicesTex == null)
		{
			indicesTex = CreateDataTexture(count, 1, "PolyIndicesTex");
		}
	}

	void UploadIndices()
	{
		if (indicesTex == null) return;
		int n = Mathf.Max(1, indices?.Count ?? 0);
		if (indicesTex.width != n)
		{
			EnsureIndicesTexture();
		}
		int maxIndex = GetMaxVertexIndex();
		Algo2D.ClampIndices(indices, maxIndex);
		Color[] colors = new Color[n];
		for (int i = 0; i < n; i++)
		{
			float idx = (indices != null && i < indices.Count) ? indices[i] : 0f;
			colors[i] = new Color(idx, 0f, 0f, 0f);
		}
		indicesTex.SetPixels(colors);
		indicesTex.Apply(false, false);
	}

	protected void TrimIndices()
	{
		if (indices == null) return;
		Algo2D.ClampIndices(indices, int.MaxValue);
	}

	protected override void OnEnable()
	{
		base.OnEnable();
		RebuildEdgesFromIndices();
		if (Application.isPlaying)
		{
			EnsureIndicesTexture();
			UploadIndices();
		}
		// Ensure edges reflect current indices immediately
		UpdateMaterial();
	}

	void LateUpdate()
	{
		// Keep polygon index buffer in sync each frame during play/edit as needed
		if (Application.isPlaying)
		{
			EnsureIndicesTexture();
			UploadIndices();
		}
	}
}

