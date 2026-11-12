using UnityEngine;

/// <summary>
/// BallSurface Physicsで使用する
/// 1つの惑星・Ground Surfaceを表します。
/// </summary>
/// <remarks>
/// 同じGameObjectに存在するMeshColliderから
/// Triangle構造を読み取り、Surface Physics用の
/// BallSurfaceMeshDataを生成して保持します。
///
/// Ball側はColliderそのものではなく、
/// このComponentを1つのSurfaceとして扱います。
///
/// 現在は移動・変形しないStaticかつ
/// Non-ConvexなMeshColliderを対象とします。
/// </remarks>
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshCollider))]
public sealed class BallSurfaceSource : MonoBehaviour
{
	/// <summary>
	/// 接触地点からTriangleを特定する際に使用する
	/// 通常探索Rayの最低開始距離です。
	/// </summary>
	private const float m_MinimumProbeOffset = 0.02f;

	/// <summary>
	/// 通常探索でTriangleを取得できなかった場合に使用する
	/// Fallback探索Rayの最低開始距離です。
	/// </summary>
	private const float m_MinimumFallbackProbeOffset = 0.10f;

	/// <summary>
	/// Fallback探索Rayへ追加する最低余裕距離です。
	/// </summary>
	private const float m_MinimumFallbackRayMargin = 0.10f;

	private MeshCollider m_SurfaceCollider;

	private BallSurfaceMeshData m_MeshData;

	/// <summary>
	/// このSurfaceで使用しているMeshColliderを取得します。
	/// </summary>
	public MeshCollider SurfaceCollider
	{
		get
		{
			return m_SurfaceCollider;
		}
	}

	/// <summary>
	/// 事前解析済みのSurface Mesh情報を取得します。
	/// </summary>
	public BallSurfaceMeshData MeshData
	{
		get
		{
			return m_MeshData;
		}
	}

	/// <summary>
	/// Surface Physicsに使用可能なMeshデータが
	/// 正常に生成されているか取得します。
	/// </summary>
	public bool IsReady
	{
		get
		{
			return m_SurfaceCollider != null &&
				m_SurfaceCollider.enabled &&
				m_SurfaceCollider.gameObject.activeInHierarchy &&
				m_MeshData != null &&
				m_MeshData.IsSupportedSurface;
		}
	}

	/// <summary>
	/// MeshColliderを取得して、
	/// Surface Physics用のMesh情報を構築します。
	/// </summary>
	private void Awake()
	{
		m_SurfaceCollider =
			GetComponent<MeshCollider>();

		BuildSurfaceData();
	}

	/// <summary>
	/// MeshColliderが参照しているMeshから
	/// Triangle・Edge・隣接Triangle情報を構築します。
	/// </summary>
	private void BuildSurfaceData()
	{
		if (m_SurfaceCollider == null)
		{
			Debug.LogError(
				$"{name}: BallSurfaceSourceにMeshColliderが存在しません。",
				this);

			return;
		}

		if (m_SurfaceCollider.isTrigger)
		{
			Debug.LogError(
				$"{name}: BallSurface用MeshColliderはIs TriggerをOFFにしてください。",
				this);

			return;
		}

		if (m_SurfaceCollider.convex)
		{
			Debug.LogError(
				$"{name}: BallSurface用MeshColliderはConvexをOFFにしてください。",
				this);

			return;
		}

		if (m_SurfaceCollider.attachedRigidbody != null)
		{
			Debug.LogError(
				$"{name}: BallSurface用MeshColliderはStatic Objectとして使用してください。Rigidbodyへ接続されたSurfaceは現在未対応です。",
				this);

			return;
		}

		Mesh surfaceMesh =
			m_SurfaceCollider.sharedMesh;

		if (surfaceMesh == null)
		{
			Debug.LogError(
				$"{name}: MeshColliderにMeshが設定されていません。",
				this);

			return;
		}

		if (!surfaceMesh.isReadable)
		{
			Debug.LogError(
				$"{name}: Surface Meshを読み取れません。Model Import SettingsのRead/Write EnabledをONにしてください。",
				this);

			return;
		}

		m_MeshData =
			new BallSurfaceMeshData(
				surfaceMesh);

		if (!m_MeshData.IsSupportedSurface)
		{
			Debug.LogError(
				$"{name}: BallSurfaceとして使用できないMesh構造です。 " +
				$"Triangles={m_MeshData.TriangleCount}, " +
				$"BoundaryEdges={m_MeshData.BoundaryEdgeCount}, " +
				$"NonManifoldEdges={m_MeshData.NonManifoldEdgeCount}, " +
				$"DegenerateTriangles={m_MeshData.DegenerateTriangleCount}, " +
				$"InvalidEdges={m_MeshData.InvalidEdgeCount}, " +
				$"OutwardWinding={m_MeshData.HasOutwardWinding}",
				this);

			return;
		}

		Debug.Log(
			$"{name}: BallSurface Meshを構築しました。 " +
			$"Triangles={m_MeshData.TriangleCount}, " +
			$"FlatEdges={m_MeshData.FlatEdgeCount}, " +
			$"ConvexEdges={m_MeshData.ConvexEdgeCount}, " +
			$"ConcaveEdges={m_MeshData.ConcaveEdgeCount}",
			this);
	}

	/// <summary>
	/// Surface上の接触地点から、
	/// その地点を構成しているTriangleを取得します。
	/// </summary>
	/// <remarks>
	/// CollisionのContactPointにはtriangleIndexが存在しないため、
	/// MeshCollider自身へRayを飛ばして
	/// RaycastHit.triangleIndexを取得します。
	///
	/// まず従来通りContactPointの近傍を短いRayで探索します。
	///
	/// ContactPointがPhysicsの接触補正や数値誤差によって
	/// Mesh表面から少しずれており、通常探索で取得できなかった場合だけ、
	/// より大きなOffsetとRay距離を使用して再探索します。
	///
	/// どちらの探索もContact Normalの正負両側から行い、
	/// 両方がHitした場合は元のContactPointへ近いHitを採用します。
	/// </remarks>
	/// <param name="surfacePoint">
	/// Collisionから取得したSurface接触地点です。
	/// </param>
	/// <param name="surfaceNormal">
	/// Collisionから取得したSurface法線です。
	/// </param>
	/// <param name="surfaceHit">
	/// Triangle Indexを含むRaycast結果です。
	/// </param>
	/// <returns>
	/// Triangleを正常に取得できた場合はtrueです。
	/// </returns>
	public bool TryGetSurfaceHit(
		Vector3 surfacePoint,
		Vector3 surfaceNormal,
		out RaycastHit surfaceHit)
	{
		surfaceHit =
			default;

		if (!IsReady)
		{
			return false;
		}

		if (surfaceNormal.sqrMagnitude <=
			Mathf.Epsilon)
		{
			return false;
		}

		Vector3 normal =
			surfaceNormal.normalized;

		float probeOffset =
			Mathf.Max(
				m_SurfaceCollider.contactOffset *
					4.0f,
				m_MinimumProbeOffset);

		float rayDistance =
			probeOffset *
				2.0f +
			m_MinimumProbeOffset;

		// まずは従来と同じ短い探索を行います。
		if (TryProbeSurfaceFromBothSides(
			surfacePoint,
			normal,
			probeOffset,
			rayDistance,
			out surfaceHit))
		{
			return true;
		}

		// ContactPointがMesh表面からわずかにずれている場合や、
		// Ray開始点が接触境界付近に入り込んだ場合を補完するため、
		// より広いFallback探索を行います。
		float fallbackProbeOffset =
			Mathf.Max(
				probeOffset *
					4.0f,
				m_MinimumFallbackProbeOffset);

		float fallbackRayDistance =
			fallbackProbeOffset *
				2.0f +
			Mathf.Max(
				probeOffset,
				m_MinimumFallbackRayMargin);

		return TryProbeSurfaceFromBothSides(
			surfacePoint,
			normal,
			fallbackProbeOffset,
			fallbackRayDistance,
			out surfaceHit);
	}

	/// <summary>
	/// ContactPointの両側からMeshColliderへRayを飛ばし、
	/// 最もContactPointに近いTriangle Hitを取得します。
	/// </summary>
	private bool TryProbeSurfaceFromBothSides(
		Vector3 surfacePoint,
		Vector3 surfaceNormal,
		float probeOffset,
		float rayDistance,
		out RaycastHit surfaceHit)
	{
		surfaceHit =
			default;

		if (probeOffset <=
				0.0f ||
			rayDistance <=
				0.0f)
		{
			return false;
		}

		// Contact Normal側からSurfaceへ向けて探索します。
		bool positiveSideHit =
			TryRaycastSurface(
				surfacePoint +
					surfaceNormal *
						probeOffset,
				-surfaceNormal,
				rayDistance,
				out RaycastHit positiveHit);

		// Contact Normalとは反対側からもSurfaceへ向けて探索します。
		//
		// Contact Normalの向きやContactPointの数値誤差によって
		// 片側だけではMeshを拾えないケースを補完します。
		bool negativeSideHit =
			TryRaycastSurface(
				surfacePoint -
					surfaceNormal *
						probeOffset,
				surfaceNormal,
				rayDistance,
				out RaycastHit negativeHit);

		if (!positiveSideHit &&
			!negativeSideHit)
		{
			return false;
		}

		if (positiveSideHit &&
			!negativeSideHit)
		{
			surfaceHit =
				positiveHit;

			return true;
		}

		if (!positiveSideHit &&
			negativeSideHit)
		{
			surfaceHit =
				negativeHit;

			return true;
		}

		// 両方向からTriangleが取得できた場合は、
		// CollisionのContactPointへ近い方を採用します。
		float positiveDistanceSquared =
			(
				positiveHit.point -
				surfacePoint
			).sqrMagnitude;

		float negativeDistanceSquared =
			(
				negativeHit.point -
				surfacePoint
			).sqrMagnitude;

		surfaceHit =
			positiveDistanceSquared <=
				negativeDistanceSquared
				? positiveHit
				: negativeHit;

		return true;
	}

	/// <summary>
	/// このBallSurfaceのMeshColliderに対して
	/// Triangle取得用Raycastを実行します。
	/// </summary>
	/// <param name="rayOrigin">
	/// Rayの開始位置です。
	/// </param>
	/// <param name="rayDirection">
	/// Rayの進行方向です。
	/// </param>
	/// <param name="rayDistance">
	/// Rayの最大距離です。
	/// </param>
	/// <param name="surfaceHit">
	/// 正常に取得できたRaycastHitです。
	/// </param>
	/// <returns>
	/// 有効なTriangle Indexを持つHitを取得できた場合はtrueです。
	/// </returns>
	private bool TryRaycastSurface(
		Vector3 rayOrigin,
		Vector3 rayDirection,
		float rayDistance,
		out RaycastHit surfaceHit)
	{
		surfaceHit =
			default;

		if (rayDirection.sqrMagnitude <=
			Mathf.Epsilon)
		{
			return false;
		}

		Ray ray =
			new Ray(
				rayOrigin,
				rayDirection.normalized);

		if (!m_SurfaceCollider.Raycast(
			ray,
			out surfaceHit,
			rayDistance))
		{
			return false;
		}

		return surfaceHit.triangleIndex >=
			0;
	}
}
