using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using HLSLBox.Algorithms;

[ExecuteAlways]
[RequireComponent(typeof(MeshRenderer))]
public class Edge2D : MonoBehaviour
{
	[Header("Source Particles")]
	[SerializeField] protected Particles2D particles;

	[Header("Edge List (pairs of indices)")]
	[SerializeField] protected List<Vector2Int> edges = new List<Vector2Int>();

	[Header("Line Style")]
	[SerializeField, Min(0f)] float lineWidth = 0.01f;
	[SerializeField, Range(0f, 1f)] float edgeSoftness = 0.75f;
	[SerializeField] Color color = Color.white;

	// GPU
	protected Vector2[] vtexPositionsUV; // for CPU-side vertex positions
	protected ComputeBuffer edgesBuffer;
	protected MaterialPropertyBlock mpb;
	protected MeshRenderer mr;
	int[] uploadScratch; // flat int array for int2 pairs

	// Runtime state
	Coroutine edgeUpdater;
	protected bool validateDirty;

	const int STRIDE_EDGE = sizeof(int) * 2;

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
		Algo2D.EnsureRendererAndBlock(this, ref mr, ref mpb);
		if (Application.isPlaying)
		{
			EnsureBuffer();
			Upload();
		}
		UpdateMaterial();
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
	void OnBeforeAssemblyReload()
	{
		Release();
	}
#endif

	protected virtual void OnValidate()
	{
		lineWidth = Mathf.Max(0f, lineWidth);
		edgeSoftness = Mathf.Clamp01(edgeSoftness);
		Algo2D.ClampEdges(edges, int.MaxValue);
		validateDirty = true;
	}

	void Update()
	{
		if (Application.isPlaying)
		{
			if (validateDirty || edgesBuffer == null)
			{
				EnsureBuffer();
				validateDirty = false;
			}
			Upload();
			UpdateMaterial();
		}
		else
		{
			UpdateMaterial();
		}
	}

	protected virtual void StartUpdateLoop()
	{
		if (edgeUpdater != null) StopCoroutine(edgeUpdater);
		edgeUpdater = StartCoroutine(UpdateEdgesLoop());
	}

	protected virtual void StopUpdateLoop()
	{
		if (edgeUpdater != null)
		{
			StopCoroutine(edgeUpdater);
			edgeUpdater = null;
		}
	}

	protected virtual IEnumerator UpdateEdgesLoop()
	{
		var wait = new WaitForSeconds(0.05f);
		while (enabled)
		{
			UpdateEdges();
			yield return wait;
		}
	}

	protected virtual void UpdateEdges() { }

	protected void EnsureBuffer()
	{
		int count = Mathf.Max(1, edges?.Count ?? 0);
		Algo2D.EnsureComputeBuffer(ref edgesBuffer, count, STRIDE_EDGE);
	}

	protected virtual void Release()
	{
		Algo2D.ReleaseComputeBuffer(ref edgesBuffer);
	}

	protected void Upload()
	{
		if (edgesBuffer == null) return;
		if (edges == null) edges = new List<Vector2Int>();
		int count = Mathf.Max(1, edges.Count);
		Algo2D.EnsureComputeBuffer(ref edgesBuffer, count, STRIDE_EDGE);
		int maxIndex = particles != null ? particles.ParticleCount - 1 : int.MaxValue;
		Algo2D.ClampEdges(edges, maxIndex);
		// Create flat int array: each edge contributes 2 ints (x, y)
		Algo2D.EnsureArraySize(ref uploadScratch, count * 2);
		Array.Clear(uploadScratch, 0, count * 2);
		if (edges.Count > 0)
		{
			for (int i = 0; i < edges.Count; i++)
			{
				uploadScratch[i * 2] = edges[i].x;
				uploadScratch[i * 2 + 1] = edges[i].y;
			}
		}
		edgesBuffer.SetData(uploadScratch);
	}

	protected virtual void UpdateMaterial()
	{
		Algo2D.EnsureRendererAndBlock(this, ref mr, ref mpb);
		if (mr == null) return;
		mr.GetPropertyBlock(mpb);
		Algo2D.ApplyParticlesToMaterial(mpb, particles);
		if (edgesBuffer != null)
		{
			mpb.SetBuffer("_EdgePairs", edgesBuffer);
		}
		mpb.SetInt("_EdgeCount", Mathf.Max(0, edges?.Count ?? 0));
		mpb.SetFloat("_LineWidth", lineWidth / 1000f);
		mpb.SetFloat("_SoftEdge", edgeSoftness);
		mpb.SetColor("_Color", color);
		mr.SetPropertyBlock(mpb);
	}

	public virtual void SetEdges(List<Vector2Int> edgeList)
	{
		edges = edgeList ?? new List<Vector2Int>();
		Algo2D.ClampEdges(edges, int.MaxValue);
		validateDirty = true;
		if (Application.isPlaying)
		{
			EnsureBuffer();
			Upload();
		}
		else
		{
#if UNITY_EDITOR
			UpdateMaterial(); // so we don't create GPU resources during validation
#endif
		}
	}
}
