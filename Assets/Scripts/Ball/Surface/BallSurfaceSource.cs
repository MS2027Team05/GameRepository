using UnityEngine;

/// <summary>
/// 1つのStatic MeshColliderをBallSurface Physicsで利用できるSurfaceとして公開します。
/// </summary>
/// <remarks>
/// 主な処理の流れ:
/// 1. 同じGameObjectのNon-Convex MeshColliderから共有Meshを取得します。
/// 2. BallSurfaceMeshDataを生成し、TriangleとEdgeのTopologyを事前解析します。
/// 3. 接触点付近へのRaycastからMeshColliderのtriangleIndexを特定します。
/// 4. 通常Rayで取得できない場合は両側からのFallback探索を行います。
/// 5. Surface追跡では現在位置に最も近いTriangleとSurface点を取得します。
///
/// Ball側はMeshCollider単体ではなく、このComponentを1つのBallSurfaceとして扱います。
/// 現在は移動・変形しないStaticなNon-Convex MeshColliderを前提とします。
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

	/// <summary>
	/// Flying中のSurface追跡で1回に辿る
	/// Triangle数の上限です。
	/// </summary>
	private const int m_MaximumSurfaceTrackingTransitions = 64;

	/// <summary>
	/// 角付近でほぼ同距離のTriangleへ
	/// 毎Step切り替わることを防ぐ距離許容値です。
	/// </summary>
	private const float m_SurfaceTrackingSwitchDistanceTolerance = 0.001f;

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
	/// 指定したTriangleから隣接Triangleを辿り、
	/// World座標に最も近いSurface位置を追跡します。
	/// </summary>
	/// <remarks>
	/// Flying中の疑似重力用です。
	///
	/// Mesh全体を毎Physics Step総当たりせず、
	/// 前回Triangleとその隣接Triangleだけを順番に調べます。
	///
	/// ほぼ同距離の隣接Triangleには切り替えないことで、
	/// Edge付近で重力方向が細かく反転することを防ぎます。
	/// </remarks>
	public bool TryTrackNearestSurface(
		Vector3 worldPoint,
		int startTriangleIndex,
		out int trackedTriangleIndex,
		out Vector3 nearestSurfacePoint,
		out Vector3 nearestSurfaceNormal)
	{
		trackedTriangleIndex =
			-1;

		nearestSurfacePoint =
			Vector3.zero;

		nearestSurfaceNormal =
			Vector3.zero;

		if (!IsReady)
		{
			return false;
		}

		if (startTriangleIndex < 0 ||
			startTriangleIndex >=
				m_MeshData.TriangleCount)
		{
			return TryFindNearestSurface(
				worldPoint,
				out trackedTriangleIndex,
				out nearestSurfacePoint,
				out nearestSurfaceNormal);
		}

		int currentTriangleIndex =
			startTriangleIndex;

		if (!TryGetTriangleClosestWorldPoint(
			currentTriangleIndex,
			worldPoint,
			out Vector3 currentSurfacePoint,
			out Vector3 currentSurfaceNormal,
			out float currentDistanceSquared))
		{
			return false;
		}

		float switchToleranceSquared =
			m_SurfaceTrackingSwitchDistanceTolerance *
			m_SurfaceTrackingSwitchDistanceTolerance;

		for (int transitionIndex = 0;
			transitionIndex <
				m_MaximumSurfaceTrackingTransitions;
			transitionIndex++)
		{
			int bestTriangleIndex =
				currentTriangleIndex;

			Vector3 bestSurfacePoint =
				currentSurfacePoint;

			Vector3 bestSurfaceNormal =
				currentSurfaceNormal;

			float bestDistanceSquared =
				currentDistanceSquared;

			for (int edgeIndex = 0;
				edgeIndex < 3;
				edgeIndex++)
			{
				if (!m_MeshData.TryGetNeighbor(
					currentTriangleIndex,
					edgeIndex,
					out int neighborTriangleIndex,
					out _))
				{
					continue;
				}

				if (!TryGetTriangleClosestWorldPoint(
					neighborTriangleIndex,
					worldPoint,
					out Vector3 neighborSurfacePoint,
					out Vector3 neighborSurfaceNormal,
					out float neighborDistanceSquared))
				{
					continue;
				}

				if (neighborDistanceSquared +
						switchToleranceSquared >=
					bestDistanceSquared)
				{
					continue;
				}

				bestTriangleIndex =
					neighborTriangleIndex;

				bestSurfacePoint =
					neighborSurfacePoint;

				bestSurfaceNormal =
					neighborSurfaceNormal;

				bestDistanceSquared =
					neighborDistanceSquared;
			}

			if (bestTriangleIndex ==
				currentTriangleIndex)
			{
				break;
			}

			currentTriangleIndex =
				bestTriangleIndex;

			currentSurfacePoint =
				bestSurfacePoint;

			currentSurfaceNormal =
				bestSurfaceNormal;

			currentDistanceSquared =
				bestDistanceSquared;
		}

		trackedTriangleIndex =
			currentTriangleIndex;

		nearestSurfacePoint =
			currentSurfacePoint;

		nearestSurfaceNormal =
			currentSurfaceNormal;

		return true;
	}

	/// <summary>
	/// Mesh全体からWorld座標に最も近いSurface位置を取得します。
	/// </summary>
	/// <remarks>
	/// Flying・SeekingSurfaceの初回探索、およびFlyingの追跡で
	/// 全体の再探索が必要な場合に使用します。
	/// </remarks>
	public bool TryFindNearestSurface(
		Vector3 worldPoint,
		out int nearestTriangleIndex,
		out Vector3 nearestSurfacePoint,
		out Vector3 nearestSurfaceNormal)
	{
		nearestTriangleIndex =
			-1;

		nearestSurfacePoint =
			Vector3.zero;

		nearestSurfaceNormal =
			Vector3.zero;

		if (!IsReady)
		{
			return false;
		}

		float nearestDistanceSquared =
			float.MaxValue;

		for (int triangleIndex = 0;
			triangleIndex <
				m_MeshData.TriangleCount;
			triangleIndex++)
		{
			if (!TryGetTriangleClosestWorldPoint(
				triangleIndex,
				worldPoint,
				out Vector3 surfacePoint,
				out Vector3 surfaceNormal,
				out float distanceSquared))
			{
				continue;
			}

			if (distanceSquared >=
				nearestDistanceSquared)
			{
				continue;
			}

			nearestDistanceSquared =
				distanceSquared;

			nearestTriangleIndex =
				triangleIndex;

			nearestSurfacePoint =
				surfacePoint;

			nearestSurfaceNormal =
				surfaceNormal;
		}

		return nearestTriangleIndex >=
			0;
	}

	/// <summary>
	/// 指定Triangle上でWorld座標に最も近い点を取得します。
	/// </summary>
	private bool TryGetTriangleClosestWorldPoint(
		int triangleIndex,
		Vector3 worldPoint,
		out Vector3 closestPoint,
		out Vector3 triangleNormal,
		out float distanceSquared)
	{
		closestPoint =
			Vector3.zero;

		triangleNormal =
			Vector3.zero;

		distanceSquared =
			float.MaxValue;

		if (m_MeshData == null ||
			triangleIndex < 0 ||
			triangleIndex >=
				m_MeshData.TriangleCount)
		{
			return false;
		}

		m_MeshData.GetTriangleLocalVertices(
			triangleIndex,
			out Vector3 localVertexA,
			out Vector3 localVertexB,
			out Vector3 localVertexC);

		Vector3 vertexA =
			transform.TransformPoint(
				localVertexA);

		Vector3 vertexB =
			transform.TransformPoint(
				localVertexB);

		Vector3 vertexC =
			transform.TransformPoint(
				localVertexC);

		Vector3 normal =
			Vector3.Cross(
				vertexB -
					vertexA,
				vertexC -
					vertexA);

		if (normal.sqrMagnitude <=
			Mathf.Epsilon)
		{
			return false;
		}

		triangleNormal =
			normal.normalized;

		closestPoint =
			GetClosestPointOnTriangle(
				worldPoint,
				vertexA,
				vertexB,
				vertexC);

		distanceSquared =
			(
				closestPoint -
				worldPoint
			).sqrMagnitude;

		return true;
	}

	/// <summary>
	/// 三角形上で指定点に最も近い点を取得します。
	/// </summary>
	internal static Vector3 GetClosestPointOnTriangle(
		Vector3 point,
		Vector3 vertexA,
		Vector3 vertexB,
		Vector3 vertexC)
	{
		Vector3 edgeAB =
			vertexB -
				vertexA;

		Vector3 edgeAC =
			vertexC -
				vertexA;

		Vector3 toPointFromA =
			point -
				vertexA;

		float dotABFromA =
			Vector3.Dot(
				edgeAB,
				toPointFromA);

		float dotACFromA =
			Vector3.Dot(
				edgeAC,
				toPointFromA);

		if (dotABFromA <=
				0.0f &&
			dotACFromA <=
				0.0f)
		{
			return vertexA;
		}

		Vector3 toPointFromB =
			point -
				vertexB;

		float dotABFromB =
			Vector3.Dot(
				edgeAB,
				toPointFromB);

		float dotACFromB =
			Vector3.Dot(
				edgeAC,
				toPointFromB);

		if (dotABFromB >=
				0.0f &&
			dotACFromB <=
				dotABFromB)
		{
			return vertexB;
		}

		float vertexCRegion =
			dotABFromA *
				dotACFromB -
			dotABFromB *
				dotACFromA;

		if (vertexCRegion <=
				0.0f &&
			dotABFromA >=
				0.0f &&
			dotABFromB <=
				0.0f)
		{
			float edgeProgress =
				dotABFromA /
				(
					dotABFromA -
					dotABFromB
				);

			return vertexA +
				edgeAB *
					edgeProgress;
		}

		Vector3 toPointFromC =
			point -
				vertexC;

		float dotABFromC =
			Vector3.Dot(
				edgeAB,
				toPointFromC);

		float dotACFromC =
			Vector3.Dot(
				edgeAC,
				toPointFromC);

		if (dotACFromC >=
				0.0f &&
			dotABFromC <=
				dotACFromC)
		{
			return vertexC;
		}

		float vertexBRegion =
			dotABFromC *
				dotACFromA -
			dotABFromA *
				dotACFromC;

		if (vertexBRegion <=
				0.0f &&
			dotACFromA >=
				0.0f &&
			dotACFromC <=
				0.0f)
		{
			float edgeProgress =
				dotACFromA /
				(
					dotACFromA -
					dotACFromC
				);

			return vertexA +
				edgeAC *
					edgeProgress;
		}

		float vertexARegion =
			dotABFromB *
				dotACFromC -
			dotABFromC *
				dotACFromB;

		if (vertexARegion <=
				0.0f &&
			dotACFromB -
				dotABFromB >=
				0.0f &&
			dotABFromC -
				dotACFromC >=
				0.0f)
		{
			float edgeProgress =
				(
					dotACFromB -
					dotABFromB
				) /
				(
					dotACFromB -
						dotABFromB +
					dotABFromC -
						dotACFromC
				);

			return vertexB +
				(
					vertexC -
						vertexB
				) *
				edgeProgress;
		}

		float inverseArea =
			1.0f /
			(
				vertexARegion +
				vertexBRegion +
				vertexCRegion
			);

		float barycentricB =
			vertexBRegion *
			inverseArea;

		float barycentricC =
			vertexCRegion *
			inverseArea;

		return vertexA +
			edgeAB *
				barycentricB +
			edgeAC *
				barycentricC;
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
	/// 接触したTriangleと面上の位置を取得します。
	/// SphereCastなど、接触時のめり込み量がない場合は従来の探索を使用します。
	/// </summary>
	public bool TryResolveContactTriangle(
		Vector3 contactPoint,
		Vector3 contactNormal,
		out int triangleIndex,
		out Vector3 surfacePoint)
	{
		return TryResolveContactTriangle(
			contactPoint,
			contactNormal,
			0.0f,
			out triangleIndex,
			out surfacePoint);
	}

	/// <summary>
	/// Collisionの接触点からTriangleを探索します。
	/// 通常探索が失敗した場合だけ、負のSeparationから接触点を補正して再探索します。
	/// 補正後も元の探索距離と法線の条件を使用します。
	/// </summary>
	public bool TryResolveContactTriangle(
		Vector3 contactPoint,
		Vector3 contactNormal,
		float contactSeparation,
		out int triangleIndex,
		out Vector3 surfacePoint)
	{
		triangleIndex = -1;
		surfacePoint = Vector3.zero;

		if (!IsReady || !(contactNormal.sqrMagnitude > Mathf.Epsilon) ||
			float.IsNaN(contactPoint.x) || float.IsInfinity(contactPoint.x) ||
			float.IsNaN(contactPoint.y) || float.IsInfinity(contactPoint.y) ||
			float.IsNaN(contactPoint.z) || float.IsInfinity(contactPoint.z) ||
			float.IsNaN(contactNormal.sqrMagnitude) || float.IsInfinity(contactNormal.sqrMagnitude))
		{
			Debug.LogWarning(
				$"[BallSurface][ContactTriangleNotFound] Surface={name} Reason=InvalidSurfaceOrContactInput IsReady={IsReady} ContactPoint={contactPoint.ToString("F6")} ContactNormal={contactNormal.ToString("F6")} RayExecuted=false", this);
			return false;
		}

		bool rayFound = TryGetSurfaceHit(contactPoint, contactNormal, out RaycastHit hit);
		if (rayFound &&
			hit.triangleIndex >= 0 && hit.triangleIndex < m_MeshData.TriangleCount)
		{
			triangleIndex = hit.triangleIndex;
			surfacePoint = hit.point;
			return true;
		}

		float maximumDistance = Mathf.Min(0.05f,
			Mathf.Max(m_MinimumProbeOffset, m_SurfaceCollider.contactOffset * 2.0f));
		Vector3 normal = contactNormal.normalized;

		if (TryFindContactTriangleByClosestPoint(
			contactPoint, normal, maximumDistance,
			out triangleIndex, out surfacePoint, out _))
		{
			return true;
		}

		// 負のSeparationはめり込みを表します。接触法線方向へその距離だけ戻し、
		// 補正した点の近くに実際の接触面があるか確認します。
		// SphereCastなどSeparationがない場合には補正しません。
		if (contactSeparation < 0.0f && !float.IsInfinity(contactSeparation))
		{
			float correctionDistance = -contactSeparation;
			Vector3 correctedPoint = contactPoint + normal * correctionDistance;

			if (!float.IsNaN(correctedPoint.x) && !float.IsInfinity(correctedPoint.x) &&
				!float.IsNaN(correctedPoint.y) && !float.IsInfinity(correctedPoint.y) &&
				!float.IsNaN(correctedPoint.z) && !float.IsInfinity(correctedPoint.z))
			{
				// 補正後は広いRay探索を行わず、従来と同じ距離・法線条件で判定します。
				if (TryFindContactTriangleByClosestPoint(
					correctedPoint, normal, maximumDistance,
					out triangleIndex, out surfacePoint, out float normalDot))
				{
					Debug.Log(
						$"[BallSurface][ContactTriangleCorrectionSucceeded] Surface={name} ContactSeparation={contactSeparation:F6} CorrectionDistance={correctionDistance:F6} OriginalPoint={contactPoint.ToString("F6")} CorrectedPoint={correctedPoint.ToString("F6")} Triangle={triangleIndex} SurfacePoint={surfacePoint.ToString("F6")} DistanceBefore={Vector3.Distance(contactPoint, surfacePoint):F6} DistanceAfter={Vector3.Distance(correctedPoint, surfacePoint):F6} MaxDistance={maximumDistance:F6} Angle={Mathf.Acos(Mathf.Clamp(normalDot, -1.0f, 1.0f)) * Mathf.Rad2Deg:F3}deg MaxAngle=60.000deg DistancePass=True AnglePass=True", this);
					return true;
				}

				Debug.LogWarning(
					$"[BallSurface][ContactTriangleCorrectionFailed] Surface={name} ContactSeparation={contactSeparation:F6} CorrectionDistance={correctionDistance:F6} OriginalPoint={contactPoint.ToString("F6")} CorrectedPoint={correctedPoint.ToString("F6")} MaxDistance={maximumDistance:F6} MaxAngle=60.000deg", this);
				LogContactTriangleSearchFailure(correctedPoint, normal, maximumDistance, false, -1, false);
			}
		}

		LogContactTriangleSearchFailure(contactPoint, normal, maximumDistance, rayFound, hit.triangleIndex);
		return false;
	}

	/// <summary>
	/// 指定点に近く、接触法線と整合するTriangleの最近接点を取得します。
	/// 通常探索とめり込み補正後の探索で同じ採用条件を使用します。
	/// </summary>
	private bool TryFindContactTriangleByClosestPoint(
		Vector3 contactPoint,
		Vector3 normal,
		float maximumDistance,
		out int triangleIndex,
		out Vector3 surfacePoint,
		out float bestNormalDot)
	{
		triangleIndex = -1;
		surfacePoint = Vector3.zero;
		float bestDistanceSquared = maximumDistance * maximumDistance;
		bestNormalDot = -1.0f;

		for (int index = 0; index < m_MeshData.TriangleCount; index++)
		{
			if (!TryGetTriangleClosestWorldPoint(index, contactPoint,
				out Vector3 point, out Vector3 triangleNormal, out float distanceSquared))
			{
				continue;
			}

			float normalDot = Vector3.Dot(normal, triangleNormal);
			// 法線差が60度を超える面や、距離・法線が不正な候補は採用しません。
			if (!(normalDot >= 0.5f) || !(distanceSquared <= bestDistanceSquared))
			{
				continue;
			}

			if (triangleIndex >= 0 && distanceSquared == bestDistanceSquared && normalDot <= bestNormalDot)
			{
				continue;
			}

			triangleIndex = index;
			surfacePoint = point;
			bestDistanceSquared = distanceSquared;
			bestNormalDot = normalDot;
		}

		return triangleIndex >= 0;
	}

	/// <summary>
	/// 接触Triangleの補完探索に失敗した場合だけ、距離と法線の判定結果を記録します。
	/// 診断用の再探索であり、採用条件やSimulation状態は変更しません。
	/// </summary>
	private void LogContactTriangleSearchFailure(
		Vector3 contactPoint,
		Vector3 contactNormal,
		float maximumDistance,
		bool rayFound,
		int rayTriangleIndex,
		bool rayExecuted = true)
	{
		const float minimumNormalDot = 0.5f;
		float maximumDistanceSquared = maximumDistance * maximumDistance;
		int invalidCount = 0;
		int distanceOnlyCount = 0;
		int angleOnlyCount = 0;
		int bothCount = 0;
		int passedCount = 0;
		int nearestIndex = -1;
		float nearestDistanceSquared = float.PositiveInfinity;
		float nearestNormalDot = 0.0f;
		Vector3 nearestPoint = Vector3.zero;
		Vector3 nearestNormal = Vector3.zero;
		int nearestAnglePassedIndex = -1;
		float nearestAnglePassedDistanceSquared = float.PositiveInfinity;
		int bestDistancePassedIndex = -1;
		float bestDistancePassedDot = float.NegativeInfinity;

		for (int index = 0; index < m_MeshData.TriangleCount; index++)
		{
			if (!TryGetTriangleClosestWorldPoint(index, contactPoint,
				out Vector3 point, out Vector3 triangleNormal, out float distanceSquared))
			{
				invalidCount++;
				continue;
			}

			float normalDot = Vector3.Dot(contactNormal, triangleNormal);
			if (float.IsNaN(distanceSquared) || float.IsInfinity(distanceSquared) ||
				float.IsNaN(normalDot) || float.IsInfinity(normalDot) || distanceSquared < 0.0f)
			{
				invalidCount++;
				continue;
			}

			bool distancePassed = distanceSquared <= maximumDistanceSquared;
			bool anglePassed = normalDot >= minimumNormalDot;
			if (distancePassed && anglePassed)
			{
				passedCount++;
			}
			else if (!distancePassed && !anglePassed)
			{
				bothCount++;
			}
			else if (!distancePassed)
			{
				distanceOnlyCount++;
			}
			else
			{
				angleOnlyCount++;
			}

			if (distanceSquared < nearestDistanceSquared)
			{
				nearestIndex = index;
				nearestDistanceSquared = distanceSquared;
				nearestNormalDot = normalDot;
				nearestPoint = point;
				nearestNormal = triangleNormal;
			}

			if (anglePassed && distanceSquared < nearestAnglePassedDistanceSquared)
			{
				nearestAnglePassedIndex = index;
				nearestAnglePassedDistanceSquared = distanceSquared;
			}

			if (distancePassed && normalDot > bestDistancePassedDot)
			{
				bestDistancePassedIndex = index;
				bestDistancePassedDot = normalDot;
			}
		}

		string nearestDetails = nearestIndex >= 0
			? $"Triangle={nearestIndex} Distance={Mathf.Sqrt(nearestDistanceSquared):F6} Angle={Mathf.Acos(Mathf.Clamp(nearestNormalDot, -1.0f, 1.0f)) * Mathf.Rad2Deg:F3}deg NormalDot={nearestNormalDot:F6} DistancePass={nearestDistanceSquared <= maximumDistanceSquared} AnglePass={nearestNormalDot >= minimumNormalDot} Point={nearestPoint.ToString("F6")} Normal={nearestNormal.ToString("F6")}"
			: "None";
		string anglePassedDetails = nearestAnglePassedIndex >= 0
			? $"Triangle={nearestAnglePassedIndex} Distance={Mathf.Sqrt(nearestAnglePassedDistanceSquared):F6}"
			: "None";
		string distancePassedDetails = bestDistancePassedIndex >= 0
			? $"Triangle={bestDistancePassedIndex} Angle={Mathf.Acos(Mathf.Clamp(bestDistancePassedDot, -1.0f, 1.0f)) * Mathf.Rad2Deg:F3}deg NormalDot={bestDistancePassedDot:F6}"
			: "None";

		Debug.LogWarning(
			$"[BallSurface][ContactTriangleNotFound] Surface={name} ContactPoint={contactPoint.ToString("F6")} ContactNormal={contactNormal.ToString("F6")}\n" +
			$"RayExecuted={rayExecuted} RayFound={rayFound} RayTriangle={(rayFound ? rayTriangleIndex : -1)} ContactOffset={m_SurfaceCollider.contactOffset:F6} MaxDistance={maximumDistance:F6} MaxAngle={Mathf.Acos(minimumNormalDot) * Mathf.Rad2Deg:F3}deg MinNormalDot={minimumNormalDot:F6}\n" +
			$"Triangles={m_MeshData.TriangleCount} Invalid={invalidCount} DistanceOnlyRejected={distanceOnlyCount} AngleOnlyRejected={angleOnlyCount} BothRejected={bothCount} Passed={passedCount}\n" +
			$"Nearest: {nearestDetails}\n" +
			$"NearestAnglePassed: {anglePassedDetails}\n" +
			$"BestAngleWithinDistance: {distancePassedDetails}", this);
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
