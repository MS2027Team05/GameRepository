using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 飛行中と空中停止後で共通のAttachSurface探索を担当します。
/// </summary>
/// <remarks>
/// 主な処理の流れ:
/// 1. OverlapSphereで探索範囲内のColliderを取得します。
/// 2. BallSurfaceでは既存のTriangle最近接点計算を使用します。
/// 3. 従来のColliderでは形状に対応する最近接点計算を使用します。
/// 4. 探索範囲内にある最近接点の距離を比較し、最も近いSurfaceを返します。
///
/// 探索結果にはCollider、接触候補点、BallSurfaceとTriangle番号を保持します。
/// 探索範囲、探索間隔、発見後の移動と状態遷移はController側が管理します。
/// </remarks>
public sealed class BallSurfaceSearch
{
	/// <summary>
	/// Raycastに依存しないSurface探索結果です。
	/// </summary>
	public readonly struct SearchResult
	{
		public Collider Collider { get; }
		public BallSurfaceSource SurfaceSource { get; }
		public int TriangleIndex { get; }
		public Vector3 Point { get; }

		public SearchResult(
			Collider collider,
			BallSurfaceSource surfaceSource,
			int triangleIndex,
			Vector3 point)
		{
			Collider = collider;
			SurfaceSource = surfaceSource;
			TriangleIndex = triangleIndex;
			Point = point;
		}
	}

	private const int m_InitialCandidateCapacity = 64;
	private Collider[] m_Candidates = new Collider[m_InitialCandidateCapacity];
	private readonly List<Vector3> m_LegacyMeshVertices = new List<Vector3>();
	private readonly List<int> m_LegacyMeshTriangles = new List<int>();
	private readonly HashSet<int> m_ReportedUnsupportedColliders = new HashSet<int>();

	/// <summary>
	/// 指定された範囲内で最も近いSurfaceを取得します。
	/// </summary>
	/// <param name="origin">探索開始位置です。</param>
	/// <param name="searchDistance">探索する最大距離です。</param>
	/// <param name="surfaceMask">探索対象Layerです。</param>
	/// <param name="ignoredRigidbody">探索から除外するボール自身のRigidbodyです。</param>
	/// <param name="nearestSurface">最も近いSurfaceの情報です。</param>
	/// <returns>有効なSurfaceを発見した場合はtrueです。</returns>
	public bool TrySearchNearest(
		Vector3 origin,
		float searchDistance,
		LayerMask surfaceMask,
		Rigidbody ignoredRigidbody,
		out SearchResult nearestSurface)
	{
		nearestSurface = default;

		if (searchDistance <= 0.0f)
		{
			return false;
		}

		int candidateCount = CollectCandidates(
			origin,
			searchDistance,
			surfaceMask);

		float nearestDistanceSquared = searchDistance * searchDistance;
		bool hasSurface = false;

		for (int candidateIndex = 0;
			candidateIndex < candidateCount;
			candidateIndex++)
		{
			Collider candidate = m_Candidates[candidateIndex];
			if (candidate == null ||
				(ignoredRigidbody != null && candidate.attachedRigidbody == ignoredRigidbody))
			{
				continue;
			}

			// Boundsまでの距離は実際のSurfaceまでの距離以下なので、
			// 既に見つけた候補より遠いColliderの詳細計算は省略できます。
			if (candidate.bounds.SqrDistance(origin) > nearestDistanceSquared)
			{
				continue;
			}

			if (!TryGetCandidateSurface(candidate, origin, out SearchResult result))
			{
				continue;
			}

			float distanceSquared = (result.Point - origin).sqrMagnitude;
			if (float.IsNaN(distanceSquared) || float.IsInfinity(distanceSquared) ||
				distanceSquared > nearestDistanceSquared ||
				(hasSurface && distanceSquared == nearestDistanceSquared))
			{
				continue;
			}

			nearestDistanceSquared = distanceSquared;
			nearestSurface = result;
			hasSurface = true;
		}

		// 使用済み参照を残さず、配列自体は次の探索で再利用します。
		Array.Clear(m_Candidates, 0, candidateCount);
		return hasSurface;
	}

	/// <summary>
	/// 探索範囲内のColliderを取得します。配列が満杯なら拡張して再取得します。
	/// </summary>
	private int CollectCandidates(
		Vector3 origin,
		float searchDistance,
		LayerMask surfaceMask)
	{
		while (true)
		{
			int candidateCount = Physics.OverlapSphereNonAlloc(
				origin,
				searchDistance,
				m_Candidates,
				surfaceMask,
				QueryTriggerInteraction.Ignore);

			if (candidateCount < m_Candidates.Length)
			{
				return candidateCount;
			}

			// 満杯の配列だけでは本当の最近接候補が含まれる保証がありません。
			// 拡張は必要になったときだけ行い、以降の探索で再利用します。
			m_Candidates = new Collider[m_Candidates.Length * 2];
		}
	}

	/// <summary>
	/// Colliderの種類に応じてSurface上の最近接点を取得します。
	/// </summary>
	private bool TryGetCandidateSurface(
		Collider candidate,
		Vector3 origin,
		out SearchResult result)
	{
		result = default;

		if (candidate.TryGetComponent(out BallSurfaceSource surfaceSource) &&
			(candidate is MeshCollider))
		{
			// BallSurfaceは非Convex MeshColliderのため、標準ClosestPointを使用しません。
			// 初期化できないSurfaceは吸引先として選ばないようにします。
			if (!surfaceSource.IsReady || surfaceSource.SurfaceCollider != candidate ||
				!surfaceSource.TryFindNearestSurface(
					origin,
					out int triangleIndex,
					out Vector3 surfacePoint,
					out _))
			{
				return false;
			}

			result = new SearchResult(candidate, surfaceSource, triangleIndex, surfacePoint);
			return true;
		}

		Vector3 closestPoint;
		if (candidate is MeshCollider meshCollider && !meshCollider.convex)
		{
			// 旧AttachSurfaceとの互換用です。BallSurfaceSourceがなくても、
			// 読み取り可能な非Convex MeshはTriangleから直接最近接点を求めます。
			if (!TryFindLegacyMeshPoint(meshCollider, origin, out closestPoint))
			{
				return false;
			}
		}
		else if (candidate is BoxCollider || candidate is SphereCollider ||
			candidate is CapsuleCollider || candidate is MeshCollider)
		{
			closestPoint = candidate.ClosestPoint(origin);
		}
		else
		{
			ReportUnsupportedCollider(candidate);
			return false;
		}

		// 固定点と探索開始位置が一致すると移動方向を作れません。
		// 標準ClosestPointはCollider内部から呼ぶと入力座標を返す場合もあります。
		if ((closestPoint - origin).sqrMagnitude == 0.0f)
		{
			return false;
		}

		result = new SearchResult(candidate, null, -1, closestPoint);
		return true;
	}

	/// <summary>
	/// BallSurfaceSourceを持たない従来の非Convex Mesh上の最近接点を取得します。
	/// </summary>
	/// <remarks>
	/// 頂点とTriangleのListは再利用します。BallSurface用の最近接点計算と
	/// 同じ幾何計算を使用し、Surface Physicsの初期化は行いません。
	/// </remarks>
	private bool TryFindLegacyMeshPoint(
		MeshCollider meshCollider,
		Vector3 origin,
		out Vector3 closestPoint)
	{
		closestPoint = Vector3.zero;
		Mesh mesh = meshCollider.sharedMesh;
		if (mesh == null || !mesh.isReadable)
		{
			ReportUnsupportedCollider(meshCollider);
			return false;
		}

		mesh.GetVertices(m_LegacyMeshVertices);
		// 各頂点のWorld変換は一度だけ行います。
		Transform meshTransform = meshCollider.transform;
		for (int vertexIndex = 0; vertexIndex < m_LegacyMeshVertices.Count; vertexIndex++)
		{
			m_LegacyMeshVertices[vertexIndex] = meshTransform.TransformPoint(m_LegacyMeshVertices[vertexIndex]);
		}

		float nearestDistanceSquared = float.PositiveInfinity;
		bool hasPoint = false;
		for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
		{
			if (mesh.GetTopology(subMeshIndex) != MeshTopology.Triangles)
			{
				continue;
			}

			mesh.GetTriangles(m_LegacyMeshTriangles, subMeshIndex, true);
			for (int index = 0; index + 2 < m_LegacyMeshTriangles.Count; index += 3)
			{
				Vector3 vertexA = m_LegacyMeshVertices[m_LegacyMeshTriangles[index]];
				Vector3 vertexB = m_LegacyMeshVertices[m_LegacyMeshTriangles[index + 1]];
				Vector3 vertexC = m_LegacyMeshVertices[m_LegacyMeshTriangles[index + 2]];
				if (Vector3.Cross(vertexB - vertexA, vertexC - vertexA).sqrMagnitude <= Mathf.Epsilon)
				{
					continue;
				}

				Vector3 point = BallSurfaceSource.GetClosestPointOnTriangle(
					origin,
					vertexA,
					vertexB,
					vertexC);
				float distanceSquared = (point - origin).sqrMagnitude;
				if (float.IsNaN(distanceSquared) || float.IsInfinity(distanceSquared) ||
					distanceSquared >= nearestDistanceSquared)
				{
					continue;
				}

				nearestDistanceSquared = distanceSquared;
				closestPoint = point;
				hasPoint = true;
			}
		}

		return hasPoint;
	}

	/// <summary>
	/// 最近接点計算に対応していないColliderを一度だけ警告します。
	/// </summary>
	private void ReportUnsupportedCollider(Collider candidate)
	{
		if (m_ReportedUnsupportedColliders.Add(candidate.GetInstanceID()))
		{
			Debug.LogWarning(
				$"{candidate.name}: AttachSurfaceの最近接点探索に対応していないColliderです。非Convex Meshの場合はRead/Write Enabledも確認してください。",
				candidate);
		}
	}
}
