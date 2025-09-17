using System;
using System.Collections;
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
	Coroutine closestUpdater; // periodic updater for closest points
	Vector2[] uvReadback; // reuse buffer for GPU->CPU readback

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

		// In case indices change at runtime or particles existance changes
		Upload();
		UpdateMaterial();
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

	List<int> QuickHull(int pm, int pM, List<int> indices, Vector2[] positions)
	{
		// Base case: no points left
		if (indices.Count == 0) return new List<int>();
		
		// Filter points to only those on the correct side of line pm->pM
		Vector2 pos_pm = positions[pm];
		Vector2 pos_pM = positions[pM];
		indices = indices.FindAll(p =>
		{
			if (p == pm || p == pM) return false;
			float cross = (pos_pM.x - pos_pm.x) * (positions[p].y - pos_pm.y) -
			              (pos_pM.y - pos_pm.y) * (positions[p].x - pos_pm.x);
			return cross > 0f;
		});
		if (indices.Count == 0) return new List<int>();
		
		// Sort by distance from line pm->pM
		indices.Sort((i, j) =>
		{
			// cross product magnitude = twice the signed area => distance from line
			float d1 = Mathf.Abs((pos_pM.x - pos_pm.x) * (positions[i].y - pos_pm.y) -
								 (pos_pM.y - pos_pm.y) * (positions[i].x - pos_pm.x));
			float d2 = Mathf.Abs((pos_pM.x - pos_pm.x) * (positions[j].y - pos_pm.y) -
								 (pos_pM.y - pos_pm.y) * (positions[j].x - pos_pm.x));
			// descending: farthest first
			return d2.CompareTo(d1);
		});

		// Farthest point from line pm->pM
		int pi = indices[0];
		Vector2 pos_pi = positions[pi];

		// Derive two subsets of points:
		// - setA: points strictly right of segment pm->pi
		// - setB: points strictly right of segment pi->pM
		var setA = new List<int>();
		var setB = new List<int>();
		for (int k = 1; k < indices.Count; k++)
		{
			int p = indices[k];
			if (p == pm || p == pM || p == pi) continue;

			// cross = (pi - pm) x (p - pm)
			float crossA = (pos_pi.x - pos_pm.x) * (positions[p].y - pos_pm.y)
			             - (pos_pi.y - pos_pm.y) * (positions[p].x - pos_pm.x);
			if (crossA > 0f)
			{
				setA.Add(p);
				continue;
			}
			// cross = (pM - pi) x (p - pi)
			float crossB = (pos_pM.x - pos_pi.x) * (positions[p].y - pos_pi.y)
			             - (pos_pM.y - pos_pi.y) * (positions[p].x - pos_pi.x);
			if (crossB > 0f)
			{
				setB.Add(p);
			}
			// else: discard
		}

		// Recursively find hull points between the endpoints (do NOT include pm/pM here)
		var hull = new List<int>();
		hull.AddRange(QuickHull(pm, pi, setA, positions)); // points between pm and pi
		hull.Add(pi); // include the pivot between the two halves
		hull.AddRange(QuickHull(pi, pM, setB, positions)); // points between pi and pM

		return hull;
	}

	List<int> QuickHullHelper(Vector2[] positions, int count)
	{
		var hull = new List<int>();
		if (count < 3) return hull; // Need at least 3 points for a hull
		for (int i = 0; i < count; i++) hull.Add(i);

		// sort by x coordinate (leftmost -> rightmost)
		hull.Sort((i, j) => positions[i].x.CompareTo(positions[j].x));

		int pm = hull[0];
		int pM = hull[hull.Count - 1];

		var upperHull = QuickHull(pm, pM, hull, positions);
		upperHull.Insert(0, pm);
		upperHull.Add(pM);

		var lowerHull = QuickHull(pM, pm, hull, positions);

		hull = upperHull;
		hull.AddRange(lowerHull);
		return hull;
	}

	void UpdateShape()
	{
		if (particles == null) return;
		var posBuffer = particles.PositionsBuffer;
		int count = Mathf.Max(0, particles.ParticleCount);
		if (posBuffer == null || count <= 0) return;

		// Ensure readback buffer
		if (uvReadback == null || uvReadback.Length != count)
		{
			uvReadback = new Vector2[count];
		}

		// Read back UV positions [0..1]
		try
		{
			posBuffer.GetData(uvReadback);
		}
		catch (Exception)
		{
			// If readback fails (e.g., buffer disposed), skip this tick
			return;
		}

		// Find up to 3 closest to UV center (0.5, 0.5)
		Vector2 center = new Vector2(0.5f, 0.5f);
		int k = Mathf.Min(3, count);
		// Top-k selection by distance squared
		float d0 = float.PositiveInfinity, d1 = float.PositiveInfinity, d2 = float.PositiveInfinity;
		int i0 = -1, i1 = -1, i2 = -1;
		for (int i = 0; i < count; i++)
		{
			Vector2 p = uvReadback[i];
			float dx = p.x - center.x; float dy = p.y - center.y;
			float d = dx * dx + dy * dy;
			if (d < d0)
			{
				d2 = d1; i2 = i1;
				d1 = d0; i1 = i0;
				d0 = d;  i0 = i;
			}
			else if (d < d1)
			{
				d2 = d1; i2 = i1;
				d1 = d;  i1 = i;
			}
			else if (d < d2)
			{
				d2 = d;  i2 = i;
			}
		}

		// Build ordered indices by angle around center for visual stability
		List<int> newIdx = new List<int>(k);
		if (k >= 1 && i0 >= 0) newIdx.Add(i0);
		if (k >= 2 && i1 >= 0) newIdx.Add(i1);
		if (k >= 3 && i2 >= 0) newIdx.Add(i2);

		if (newIdx.Count > 1)
		{
			newIdx.Sort((a, b) =>
			{
				Vector2 pa = uvReadback[a] - center;
				Vector2 pb = uvReadback[b] - center;
				float aa = Mathf.Atan2(pa.y, pa.x);
				float ab = Mathf.Atan2(pb.y, pb.x);
				return aa.CompareTo(ab);
			});
		}

		// If unchanged, skip updates
		bool changed = indices == null || indices.Count != newIdx.Count;
		if (!changed && indices != null)
		{
			for (int i = 0; i < newIdx.Count; i++)
			{
				if (indices[i] != newIdx[i]) { changed = true; break; }
			}
		}

		if (changed)
		{
			indices = newIdx;
			EnsureBuffer();
			Upload();
			UpdateMaterial();
		}
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

	// Public API
	public void SetParticles(Particles2D src) => particles = src;
	public void SetIndices(List<int> orderedIndices)
	{
		indices = orderedIndices ?? new List<int>();
		EnsureBuffer();
		Upload();
	}
}

