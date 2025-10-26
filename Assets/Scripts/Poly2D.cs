using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using HLSLBox.Algorithms;

[ExecuteAlways]
[RequireComponent(typeof(MeshRenderer))]
public class Poly2D : MonoBehaviour
{
	[Header("Source Particles")]
	[SerializeField] protected Particles2D particles;

	[Header("Polygon Indices (subset, ordered)")]
	[Tooltip("Ordered list of indices into the Particles2D list. Can be open or closed based on 'Closed Polygon'.")]
	[SerializeField] protected List<int> indices = new List<int>();

	[Header("Line Style")]
	[SerializeField, Min(0f)] float lineWidth = 0.01f; // in UV units (fraction of bounds)
	[SerializeField, Range(0f, 1f)] float edgeSoftness = 0.75f;
	[SerializeField] Color color = Color.white;
	[SerializeField] protected bool closed = true;
    
	[Header("Vertex Display")]
	[SerializeField, Tooltip("Toggle drawing discs at polygon vertices in the polygon shader")] bool showVertices = false;
	[SerializeField, Min(0f), Tooltip("Vertex radius in UV units (use ~0.005–0.02)")] float vertexRadiusUV = 0.01f;

	// GPU
	protected ComputeBuffer indicesBuffer; // int per vertex index
	protected MaterialPropertyBlock mpb;
	protected MeshRenderer mr;
	int[] uploadScratch;

	// Runtime behavior
	Coroutine shapeUpdater; // periodic updater for closest points
	protected Vector2[] uvReadback; // reuse buffer for GPU->CPU readback
    // Defer GPU buffer (re)creation from OnValidate to Update to avoid editor-time leaks
    bool validateDirty;

	const int STRIDE_INT = sizeof(int);

	void Awake()
	{
		Algo2D.EnsureRendererAndBlock(this, ref mr, ref mpb);
		if (Application.isPlaying)
		{
			EnsureBuffer();
			Upload();
		}
		UpdateMaterial();
	}

	protected virtual void OnEnable()
	{
		// Ensure dependencies are ready in both play mode and edit mode (ExecuteAlways)
		Algo2D.EnsureRendererAndBlock(this, ref mr, ref mpb);
		if (Application.isPlaying)
		{
			EnsureBuffer();
			Upload();
		}
		UpdateMaterial();

		// Start periodic updater
		StartUpdateLoop();
	}

	void OnDisable()
	{
		StopUpdateLoop();
		Release();
	}

	void OnDestroy()
	{
		StopUpdateLoop();
		Release();
	}

#if UNITY_EDITOR
	// Ensure buffers are released prior to domain reloads / recompile in the Editor
	void OnBeforeAssemblyReload()
	{
		Release();
	}
#endif

	void OnValidate()
	{
		lineWidth = Mathf.Max(0f, lineWidth);
		vertexRadiusUV = Mathf.Max(0f, vertexRadiusUV);
		edgeSoftness = Mathf.Clamp01(edgeSoftness);
		TrimIndices();
		validateDirty = true;
	}

	void Update()
	{
		// In case indices change at runtime or particles existence changes
		if (Application.isPlaying)
		{
			if (validateDirty || indicesBuffer == null)
			{
				EnsureBuffer();
				validateDirty = false;
			}
			Upload();
			UpdateMaterial();
		}
		else
		{
			// In edit mode, avoid allocating GPU buffers; still update material for color/props
			UpdateMaterial();
		}
	}

	void StartUpdateLoop()
	{
		if (shapeUpdater != null) StopCoroutine(shapeUpdater);
		shapeUpdater = StartCoroutine(UpdateShapeLoop());
	}

	void StopUpdateLoop()
	{
		if (shapeUpdater != null)
		{
			StopCoroutine(shapeUpdater);
			shapeUpdater = null;
		}
	}

	IEnumerator UpdateShapeLoop()
	{
		var wait = new WaitForSeconds(0.05f); // ~50 ms
		while (enabled)
		{
			UpdateShape();
			yield return wait;
		}
	}

	protected virtual void UpdateShape() { }

	protected void EnsureBuffer()
	{
		int count = Mathf.Max(1, indices?.Count ?? 0);
		Algo2D.EnsureComputeBuffer(ref indicesBuffer, count, STRIDE_INT);
	}

	protected void Release()
	{
		Algo2D.ReleaseComputeBuffer(ref indicesBuffer);
	}

	// Public wrapper for editor hooks to force-release GPU buffers
	public void ReleaseBuffers() => Release();

	protected void Upload()
	{
		if (indicesBuffer == null) return;
		if (indices == null) indices = new List<int>();
		int n = Mathf.Max(1, indices.Count);
		Algo2D.EnsureComputeBuffer(ref indicesBuffer, n, STRIDE_INT);
		int maxIndex = particles != null ? particles.ParticleCount - 1 : int.MaxValue;
		Algo2D.ClampIndices(indices, maxIndex);
		Algo2D.EnsureArraySize(ref uploadScratch, n);
		Array.Clear(uploadScratch, 0, n);
		for (int i = 0; i < indices.Count; i++) uploadScratch[i] = indices[i];
		indicesBuffer.SetData(uploadScratch);
	}

	protected void UpdateMaterial()
	{
		Algo2D.EnsureRendererAndBlock(this, ref mr, ref mpb);
		if (mr == null) return;
		mr.GetPropertyBlock(mpb);
		Algo2D.ApplyParticlesToMaterial(mpb, particles);
		// Polygon specific
		if (indicesBuffer != null)
		{
			mpb.SetBuffer("_PolyIndices", indicesBuffer);
		}
		mpb.SetInt("_PolyCount", Mathf.Max(0, indices?.Count ?? 0));
		mpb.SetInt("_PolyClosed", closed ? 1 : 0);
		mpb.SetFloat("_LineWidth", lineWidth / 1000f);
		mpb.SetFloat("_SoftEdge", edgeSoftness);
		mpb.SetColor("_Color", color);
		// Vertex display toggles
		mpb.SetInt("_ShowVertices", showVertices ? 1 : 0);
		mpb.SetVector("_VertexRadiusUV", new Vector4(vertexRadiusUV, vertexRadiusUV, 0f, 0f));
		mr.SetPropertyBlock(mpb);
	}

	protected void TrimIndices()
	{
		if (indices == null) return;
		Algo2D.ClampIndices(indices, int.MaxValue);
	}
}

