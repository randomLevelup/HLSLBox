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
	protected Texture2D edgesTex;
	protected Texture2D virtualPositionsTex; // optional extra vertices (UV space)
	protected int virtualVertexCount;
	protected MaterialPropertyBlock mpb;
	protected MeshRenderer mr;

	// Runtime state
	Coroutine edgeUpdater;
	protected bool validateDirty;


	void Awake()
	{
		Algo2D.EnsureRendererAndBlock(this, ref mr, ref mpb);
		if (Application.isPlaying)
		{
			EnsureTextures();
			Upload();
		}
		UpdateMaterial();
	}

	protected virtual void OnEnable()
	{
		Algo2D.EnsureRendererAndBlock(this, ref mr, ref mpb);
		if (Application.isPlaying)
		{
			EnsureTextures();
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
			if (validateDirty || edgesTex == null)
			{
				EnsureTextures();
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

	protected void EnsureTextures()
	{
		int count = Mathf.Max(1, edges?.Count ?? 0);
		if (edgesTex != null && edgesTex.width != count)
		{
			ReleaseTexture(ref edgesTex);
		}
		if (edgesTex == null)
		{
			edgesTex = CreateDataTexture(count, 1, "EdgesTex");
		}
	}

	protected virtual void Release()
	{
		ReleaseTexture(ref edgesTex);
		ReleaseTexture(ref virtualPositionsTex);
	}

	protected void Upload()
	{
		if (edgesTex == null) return;
		if (edges == null) edges = new List<Vector2Int>();
		int count = Mathf.Max(1, edges.Count);
		if (edgesTex.width != count)
		{
			EnsureTextures();
		}
		int maxIndex = GetMaxVertexIndex();
		Algo2D.ClampEdges(edges, maxIndex);
		Color[] colors = new Color[count];
		for (int i = 0; i < count; i++)
		{
			if (i < edges.Count)
			{
				colors[i] = new Color(edges[i].x, edges[i].y, 0f, 0f);
			}
			else
			{
				colors[i] = new Color(0f, 0f, 0f, 0f);
			}
		}
		edgesTex.SetPixels(colors);
		edgesTex.Apply(false, false);
	}

	protected Texture2D CreateDataTexture(int width, int height, string name)
	{
		width = Mathf.Max(1, width);
		height = Mathf.Max(1, height);
        var tex = new Texture2D(width, height, TextureFormat.RGFloat, false, true);
		return tex;
	}

	protected void ReleaseTexture(ref Texture2D tex)
	{
		if (tex == null) return;
		if (Application.isPlaying) Destroy(tex);
		else DestroyImmediate(tex);
		tex = null;
	}

	protected virtual void UpdateMaterial()
	{
		Algo2D.EnsureRendererAndBlock(this, ref mr, ref mpb);
		if (mr == null) return;
		mr.GetPropertyBlock(mpb);
		Algo2D.ApplyParticlesToMaterial(mpb, particles);
		if (edgesTex != null)
		{
			mpb.SetTexture("_EdgePairsTex", edgesTex);
			mpb.SetVector("_EdgeTexSize", new Vector4(edgesTex.width, edgesTex.height, 0f, 0f));
		}
		if (virtualPositionsTex != null && virtualVertexCount > 0)
		{
			mpb.SetTexture("_VirtualPositionsTex", virtualPositionsTex);
			mpb.SetVector("_VirtualTexSize", new Vector4(virtualPositionsTex.width, virtualPositionsTex.height, 0f, 0f));
		}
		mpb.SetInt("_EdgeCount", Mathf.Max(0, edges?.Count ?? 0));
		mpb.SetInt("_VirtualCount", Mathf.Max(0, virtualVertexCount));
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
			EnsureTextures();
			Upload();
		}
		else
		{
#if UNITY_EDITOR
			UpdateMaterial(); // so we don't create GPU resources during validation
#endif
		}
	}

	// Optional hook for subclasses to expose extra virtual vertices to the shader
	protected void SetVirtualVertices(List<Vector2> virtualVerts)
	{
		virtualVertexCount = Mathf.Max(0, virtualVerts?.Count ?? 0);
		if (virtualVertexCount <= 0)
		{
			ReleaseTexture(ref virtualPositionsTex);
			return;
		}
		if (virtualPositionsTex != null && virtualPositionsTex.width != virtualVertexCount)
		{
			ReleaseTexture(ref virtualPositionsTex);
		}
		if (virtualPositionsTex == null)
		{
			virtualPositionsTex = CreateDataTexture(virtualVertexCount, 1, "VirtualVertsTex");
		}
		Color[] colors = new Color[virtualVertexCount];
		for (int i = 0; i < virtualVertexCount; i++)
		{
			colors[i] = new Color(virtualVerts[i].x, virtualVerts[i].y, 0f, 0f);
		}
		virtualPositionsTex.SetPixels(colors);
		virtualPositionsTex.Apply(false, false);
	}

	// Max index accounting for optional virtual vertices
	protected virtual int GetMaxVertexIndex()
	{
		int baseCount = particles != null ? particles.ParticleCount : 0;
		int maxIndex = baseCount + Mathf.Max(0, virtualVertexCount) - 1;
		return maxIndex >= 0 ? maxIndex : int.MaxValue;
	}
}
