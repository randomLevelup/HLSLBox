using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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

	[Header("Behavior")]
	[SerializeField] protected bool autoSeed = false; // default off for base class
    
	[Header("Vertex Display")]
	[SerializeField, Tooltip("Toggle drawing discs at polygon vertices in the polygon shader")] bool showVertices = false;
	[SerializeField, Min(0f), Tooltip("Vertex radius in UV units (use ~0.005–0.02)")] float vertexRadiusUV = 0.01f;

	// GPU
	protected ComputeBuffer indicesBuffer; // int per vertex index
	protected MaterialPropertyBlock mpb;
	protected MeshRenderer mr;

	// Runtime behavior
	protected bool seeded; // ensure we only auto-seed once per play session
	Coroutine closestUpdater; // periodic updater for closest points
	protected Vector2[] uvReadback; // reuse buffer for GPU->CPU readback
    // Defer GPU buffer (re)creation from OnValidate to Update to avoid editor-time leaks
    bool validateDirty;

	const int STRIDE_INT = sizeof(int);

	void Awake()
	{
		mr = GetComponent<MeshRenderer>();
		mpb = new MaterialPropertyBlock();
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
		if (mr == null) mr = GetComponent<MeshRenderer>();
		if (mpb == null) mpb = new MaterialPropertyBlock();
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
		// IMPORTANT: Do not create ComputeBuffers here; Unity may call OnValidate frequently
		// in play mode and during assembly reloads. Allocate in Update instead.
		validateDirty = true;
	}

	void Update()
	{
		// Attempt auto-find particles at runtime if missing
		if (Application.isPlaying && particles == null)
		{
			TryFindParticles();
		}

		// Optional auto-seed
		if (Application.isPlaying && autoSeed && !seeded)
		{
			TryAutoSeed();
		}

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
		if (closestUpdater != null) StopCoroutine(closestUpdater);
		closestUpdater = StartCoroutine(UpdateShapeLoop());
	}

	void StopUpdateLoop()
	{
		if (closestUpdater != null)
		{
			StopCoroutine(closestUpdater);
			closestUpdater = null;
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
		int count = Mathf.Max(1, indices.Count);
		if (indicesBuffer != null && indicesBuffer.count != count)
		{
			Release();
		}
		if (indicesBuffer == null)
		{
			indicesBuffer = new ComputeBuffer(count, STRIDE_INT);
		}
	}

	protected void Release()
	{
		if (indicesBuffer != null)
		{
			indicesBuffer.Release();
			indicesBuffer = null;
		}
	}

	// Public wrapper for editor hooks to force-release GPU buffers
	public void ReleaseBuffers() => Release();

	protected void Upload()
	{
		if (indicesBuffer == null) return;
		if (indices == null) indices = new List<int>();
		// Ensure size
		if (indicesBuffer.count != Mathf.Max(1, indices.Count))
		{
			EnsureBuffer();
		}
		// Copy with clamping into array for upload
		int n = Mathf.Max(1, indices.Count);
		var data = new int[n];
		if (particles != null)
		{
			int max = Mathf.Max(0, particles.ParticleCount - 1);
			for (int i = 0; i < indices.Count; i++) data[i] = Mathf.Clamp(indices[i], 0, max);
		}
		else
		{
			for (int i = 0; i < indices.Count; i++) data[i] = Mathf.Max(0, indices[i]);
		}
		if (n == 1) data[0] = data[0]; // no-op; ensures non-empty buffer
		indicesBuffer.SetData(data);
	}

	protected void UpdateMaterial()
	{
		if (mr == null) mr = GetComponent<MeshRenderer>();
		if (mr == null) return;
		if (mpb == null) mpb = new MaterialPropertyBlock();
		mr.GetPropertyBlock(mpb);
		// Provide shared particle positions buffer to shader
		if (particles != null && particles.PositionsBuffer != null)
		{
			mpb.SetBuffer("_Positions", particles.PositionsBuffer);
			mpb.SetInt("_ParticleCount", particles.ParticleCount);
		}
		else
		{
			mpb.SetInt("_ParticleCount", 0);
		}
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
		// Remove negatives
		for (int i = 0; i < indices.Count; i++)
		{
			if (indices[i] < 0) indices[i] = 0;
		}
	}

	protected void TryFindParticles()
	{
		// Lightweight scene search; only in play mode to avoid editor-time changes
		if (!Application.isPlaying) return;
		var found = FindObjectOfType<Particles2D>();
		if (found != null) particles = found;
	}

	protected void TryAutoSeed()
	{
		if (seeded) return;
		if (particles == null) return;
		int count = Mathf.Max(0, particles.ParticleCount);
		if (count <= 0) return;

		// If user has already provided indices, respect them; otherwise seed up to 3 unique
		if (indices == null) indices = new List<int>();
		if (indices.Count < 3)
		{
			int target = Mathf.Min(3, count);
			// Build a set of used indices
			var used = new HashSet<int>(indices);
			int safety = 0;
			while (indices.Count < target && safety < 100)
			{
				int r = UnityEngine.Random.Range(0, count);
				if (used.Add(r)) indices.Add(r);
				safety++;
			}
			EnsureBuffer();
			Upload();
			UpdateMaterial();
		}
		seeded = true;
	}

	// Public API
	public void SetParticles(Particles2D src) => particles = src;
	public void SetIndices(List<int> orderedIndices)
	{
		indices = orderedIndices ?? new List<int>();
		EnsureBuffer();
		Upload();
	}
}

