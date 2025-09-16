using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshRenderer))]
public class Poly2D : MonoBehaviour
{
	[Header("Source Particles")]
	[SerializeField] Particles2D particles;

	[Header("Polygon Indices (subset, ordered)")]
	[Tooltip("Ordered list of indices into the Particles2D list. Can be open or closed based on 'Closed Polygon'.")]
	[SerializeField] List<int> indices = new List<int>();

	[Header("Line Style")]
	[SerializeField, Min(0f)] float lineWidth = 0.01f; // in UV units (fraction of bounds)
	[SerializeField, Range(0f, 1f)] float edgeSoftness = 0.75f;
	[SerializeField] Color color = Color.white;
	[SerializeField] bool closed = true;

	// GPU
	ComputeBuffer indicesBuffer; // int per vertex index
	MaterialPropertyBlock mpb;
	MeshRenderer mr;

	// Runtime behavior
	bool seeded; // ensure we only auto-seed once per play session

	const int STRIDE_INT = sizeof(int);

	void Awake()
	{
		mr = GetComponent<MeshRenderer>();
		mpb = new MaterialPropertyBlock();
		EnsureBuffer();
		Upload();
		UpdateMaterial();
	}

	void OnEnable()
	{
		// Ensure dependencies are ready in both play mode and edit mode (ExecuteAlways)
		if (mr == null) mr = GetComponent<MeshRenderer>();
		if (mpb == null) mpb = new MaterialPropertyBlock();
		EnsureBuffer();
		Upload();
		UpdateMaterial();
	}

	void OnDisable() => Release();
	void OnDestroy() => Release();

	void OnValidate()
	{
		lineWidth = Mathf.Max(0f, lineWidth);
		edgeSoftness = Mathf.Clamp01(edgeSoftness);
		TrimIndices();
		if (Application.isPlaying)
		{
			EnsureBuffer();
			Upload();
			UpdateMaterial();
		}
	}

	void Update()
	{
		// Attempt auto-find particles at runtime if missing
		if (Application.isPlaying && particles == null)
		{
			TryFindParticles();
		}

		// Auto-seed with 3 random unique points on first run
		if (Application.isPlaying && !seeded)
		{
			TryAutoSeed();
		}

		// Add a new random point on Space key
		if (Application.isPlaying && Input.GetKeyDown(KeyCode.Space))
		{
			if (AddRandomIndex())
			{
				EnsureBuffer();
				Upload();
			}
		}

		// In case indices change at runtime or particles existance changes
		Upload();
		UpdateMaterial();
	}

	void EnsureBuffer()
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

	void Release()
	{
		if (indicesBuffer != null)
		{
			indicesBuffer.Release();
			indicesBuffer = null;
		}
	}

	void Upload()
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

	void UpdateMaterial()
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
		mr.SetPropertyBlock(mpb);
	}

	void TrimIndices()
	{
		if (indices == null) return;
		// Remove negatives
		for (int i = 0; i < indices.Count; i++)
		{
			if (indices[i] < 0) indices[i] = 0;
		}
	}

	void TryFindParticles()
	{
		// Lightweight scene search; only in play mode to avoid editor-time changes
		if (!Application.isPlaying) return;
		var found = FindObjectOfType<Particles2D>();
		if (found != null) particles = found;
	}

	void TryAutoSeed()
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
		}
		seeded = true;
	}

	bool AddRandomIndex()
	{
		if (particles == null) return false;
		int count = Mathf.Max(0, particles.ParticleCount);
		if (count <= 0) return false;
		if (indices == null) indices = new List<int>();

		// Keep indices unique; if full, do nothing
		if (indices.Count >= count) return false;
		var used = new HashSet<int>(indices);
		int safety = 0;
		while (safety++ < 200)
		{
			int r = UnityEngine.Random.Range(0, count);
			if (used.Add(r))
			{
				indices.Add(r);
				return true;
			}
		}
		return false;
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

