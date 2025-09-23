using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshRenderer))]
public class ConvexHull : Poly2D
{
	void Reset()
	{
		// Default ConvexHull to auto-seed to quickly show a polygon in scene
		autoSeed = true;
	}

	// QuickHull implementation (made protected static so subclasses/utilities can reuse)
	protected static List<int> QuickHull(int pm, int pM, List<int> candidates, Vector2[] positions)
	{
		if (candidates.Count == 0) return new List<int>();

		Vector2 pos_pm = positions[pm];
		Vector2 pos_pM = positions[pM];
		// Keep only points on the left side of pm->pM (positive cross)
		candidates = candidates.FindAll(p =>
		{
			if (p == pm || p == pM) return false;
			float cross = (pos_pM.x - pos_pm.x) * (positions[p].y - pos_pm.y) -
			              (pos_pM.y - pos_pm.y) * (positions[p].x - pos_pm.x);
			return cross > 0f;
		});
		if (candidates.Count == 0) return new List<int>();

		// Sort by distance from line pm->pM (descending)
		candidates.Sort((i, j) =>
		{
			float d1 = Mathf.Abs((pos_pM.x - pos_pm.x) * (positions[i].y - pos_pm.y) -
							 (pos_pM.y - pos_pm.y) * (positions[i].x - pos_pm.x));
			float d2 = Mathf.Abs((pos_pM.x - pos_pm.x) * (positions[j].y - pos_pm.y) -
							 (pos_pM.y - pos_pm.y) * (positions[j].x - pos_pm.x));
			return d2.CompareTo(d1);
		});

		int pi = candidates[0];
		Vector2 pos_pi = positions[pi];

		var setA = new List<int>();
		var setB = new List<int>();
		for (int k = 1; k < candidates.Count; k++)
		{
			int p = candidates[k];
			if (p == pm || p == pM || p == pi) continue;

			float crossA = (pos_pi.x - pos_pm.x) * (positions[p].y - pos_pm.y)
			             - (pos_pi.y - pos_pm.y) * (positions[p].x - pos_pm.x);
			if (crossA > 0f)
			{
				setA.Add(p);
				continue;
			}
			float crossB = (pos_pM.x - pos_pi.x) * (positions[p].y - pos_pi.y)
			             - (pos_pM.y - pos_pi.y) * (positions[p].x - pos_pi.x);
			if (crossB > 0f)
			{
				setB.Add(p);
			}
		}

		var hull = new List<int>();
		hull.AddRange(QuickHull(pm, pi, setA, positions));
		hull.Add(pi);
		hull.AddRange(QuickHull(pi, pM, setB, positions));
		return hull;
	}

	public static List<int> QuickHullHelper(Vector2[] positions, int count)
	{
		var hull = new List<int>();
		if (count < 3) return hull;
		for (int i = 0; i < count; i++) hull.Add(i);
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

	protected override void UpdateShape()
	{
		if (particles == null) return;
		var posBuffer = particles.PositionsBuffer;
		int count = Mathf.Max(0, particles.ParticleCount);
		if (posBuffer == null || count <= 0) return;

		if (uvReadback == null || uvReadback.Length != count)
		{
			uvReadback = new Vector2[count];
		}

		try
		{
			posBuffer.GetData(uvReadback);
		}
		catch (Exception)
		{
			return;
		}

		var newIdx = QuickHullHelper(uvReadback, count);

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
}
