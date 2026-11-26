using UnityEngine;

public sealed partial class BallSurfaceSimulation
{
	// 接触点補正は実際の接触面から大きく移動させない範囲に制限します。
	private const float m_MaximumInitialPointCorrection = 0.01f;
	// 初期化を仮計算する専用Simulationです。初回のみ生成して再利用します。
	private BallSurfaceSimulation m_InitializationCandidate;
	/// <summary>
	/// Collisionの入射速度からSurface Physicsを開始します。
	/// </summary>
	public bool TryInitialize(
		BallSurfaceSource surfaceSource,
		int triangleIndex,
		Vector3 surfacePoint,
		Vector3 incomingVelocity)
	{
		return TryInitialize(surfaceSource, triangleIndex, surfacePoint, incomingVelocity, out _);
	}

	/// <summary>
	/// 初期化結果の詳細を返し、成功した場合だけSurface状態を更新します。
	/// </summary>
	public bool TryInitialize(
		BallSurfaceSource surfaceSource,
		int triangleIndex,
		Vector3 surfacePoint,
		Vector3 incomingVelocity,
		out EInitializeResult result,
		System.Func<Vector3, bool> validateCenter = null,
		float initialHeight = 0.0f)
	{
		// 成功するまで現在のSurface・Edge・Vertex・World状態を変更しません。
		if (m_InitializationCandidate == null)
		{
			m_InitializationCandidate = new BallSurfaceSimulation();
		}

		BallSurfaceSimulation candidate = m_InitializationCandidate;
		candidate.ConfigurePhysics(
			m_GravityAcceleration,
			m_BounceSpeedThreshold,
			m_BounceNormalRetention,
			m_BounceTangentialRetention,
			m_MinBounceNormalSpeed,
			m_MaxBounceNormalSpeed,
			m_MaxBounceTangentialSpeed,
			m_GroundedDeceleration,
			m_GroundedStopSpeedThreshold,
			m_BallRadius);

		result = candidate.InitializeCandidate(
			surfaceSource,
			triangleIndex,
			surfacePoint,
			incomingVelocity,
			initialHeight);

		if (result != EInitializeResult.Success)
		{
			candidate.Reset();
			return false;
		}

		// 配置検証に失敗した場合も現在のSimulationを変更しません。
		if (validateCenter != null && !validateCenter(candidate.m_WorldCenter))
		{
			result = EInitializeResult.InvalidPlacement;
			candidate.Reset();
			return false;
		}

		// 初期化成功時はFace状態から開始するため、古い遷移情報を消去して
		// 検証済みのFace状態とWorld状態だけを確定します。
		Reset();
		m_SurfaceSource = candidate.m_SurfaceSource;
		m_MeshData = candidate.m_MeshData;
		m_CurrentTriangleIndex = candidate.m_CurrentTriangleIndex;
		m_SurfacePoint = candidate.m_SurfacePoint;
		m_SurfaceVelocity = candidate.m_SurfaceVelocity;
		m_Height = candidate.m_Height;
		m_VerticalVelocity = candidate.m_VerticalVelocity;
		m_IsInitialized = true;
		SetWorldState(candidate.m_WorldCenter, candidate.m_WorldVelocity);
		candidate.Reset();
		return true;
	}

	/// <summary>
	/// 衝突時の球中心を維持できる既存Traversalへ仮初期化します。
	/// 成功時だけ仮Simulationを返し、元のSimulationを次回の仮計算に再利用します。
	/// </summary>
	internal bool TryInitializePreservingImpactCenter(BallSurfaceSource source, int triangleIndex,
		Vector3 safeCenter, Vector3 hitNormal, Vector3 incomingVelocity, float tolerance,
		System.Func<Vector3, bool> validateCenter, out BallSurfaceSimulation initialized,
		out string feature)
	{
		initialized = null;
		feature = "Unknown";
		if (source == null || !source.IsReady || !IsFiniteInitializationVector(safeCenter) ||
			!IsFiniteInitializationVector(hitNormal) || !IsFiniteInitializationVector(incomingVelocity) ||
			!(tolerance > 0.0f) || !source.TryGetTriangleClosestWorldPoint(triangleIndex,
				safeCenter, out Vector3 point, out Vector3 faceNormal, out float distanceSquared))
		{
			return false;
		}

		float distance = Mathf.Sqrt(distanceSquared);
		// SphereCastの縮小半径と安全位置の余白だけを補正対象にします。
		float centerTolerance = tolerance * 2.0f;
		if (!(distance > tolerance) || !(Mathf.Abs(distance - m_BallRadius) <= centerTolerance))
		{
			return false;
		}

		Vector3 normal = (safeCenter - point) / distance;
		if (Vector3.Dot(normal, faceNormal) < -0.001f ||
			Vector3.Dot(normal, hitNormal.normalized) < 0.98f ||
			Vector3.Dot(incomingVelocity, normal) >= -m_MinimumCalculationValue)
		{
			return false;
		}

		Vector3 center = point + normal * Mathf.Max(distance, m_BallRadius);
		if (m_InitializationCandidate == null)
		{
			m_InitializationCandidate = new BallSurfaceSimulation();
		}

		BallSurfaceSimulation candidate = m_InitializationCandidate;
		candidate.ConfigurePhysics(m_GravityAcceleration, m_BounceSpeedThreshold,
			m_BounceNormalRetention, m_BounceTangentialRetention, m_MinBounceNormalSpeed,
			m_MaxBounceNormalSpeed, m_MaxBounceTangentialSpeed, m_GroundedDeceleration,
			m_GroundedStopSpeedThreshold, m_BallRadius);
		candidate.Reset();
		candidate.m_SurfaceSource = source;
		candidate.m_MeshData = source.MeshData;
		candidate.m_CurrentTriangleIndex = triangleIndex;
		candidate.m_IsInitialized = true;
		if (!candidate.TryPreparePreservedImpact(point, center, normal, incomingVelocity,
			centerTolerance, out feature) || !candidate.RefreshWorldStateFromCurrentTraversal() ||
			!IsFiniteInitializationVector(candidate.m_WorldCenter) ||
			!IsFiniteInitializationVector(candidate.m_WorldVelocity) ||
			Vector3.Dot(candidate.m_WorldVelocity, normal) < -0.001f ||
			(candidate.m_WorldCenter - safeCenter).sqrMagnitude > centerTolerance * centerTolerance ||
			(validateCenter != null && !validateCenter(candidate.m_WorldCenter)))
		{
			candidate.Reset();
			return false;
		}

		// Edge / Vertexの内部状態も含めてそのまま確定するため、Face用の部分コピーは使いません。
		// 古いSimulationを次回候補として再利用し、相互参照を残しません。
		m_InitializationCandidate = null;
		Reset();
		candidate.m_InitializationCandidate = this;
		initialized = candidate;
		return true;
	}

	private bool TryPreparePreservedImpact(Vector3 point, Vector3 center, Vector3 normal,
		Vector3 incomingVelocity, float tolerance, out string feature)
	{
		feature = "Face";
		if (!TryGetTriangleWorldData(m_CurrentTriangleIndex, out Vector3 a, out Vector3 b,
			out Vector3 c, out Vector3 faceNormal))
		{
			return false;
		}

		int hitTriangle = m_CurrentTriangleIndex;
		// 接触点から実際の球中心へ向かう法線を使い、既存のBounce設定を1回だけ適用します。
		m_SurfaceVelocity = Vector3.ProjectOnPlane(incomingVelocity, normal);
		m_VerticalVelocity = 0.0f;
		HandleSurfaceImpact(Mathf.Max(0.0f, -Vector3.Dot(incomingVelocity, normal)));
		Vector3 outgoing = m_SurfaceVelocity + normal * m_VerticalVelocity;
		float toleranceSquared = tolerance * tolerance;
		Vector3 vertex = a;
		if ((point - b).sqrMagnitude < (point - vertex).sqrMagnitude)
		{
			vertex = b;
		}
		if ((point - c).sqrMagnitude < (point - vertex).sqrMagnitude)
		{
			vertex = c;
		}

		// 面を再現できる場合は従来のFace表現を使います。
		// Smooth Normalによる中心移動も逆算し、誤差範囲に収まる場合だけ採用します。
		if (TryPreparePreservedImpactFace(center, outgoing, a, b, c, faceNormal, tolerance))
		{
			return true;
		}

		if ((point - vertex).sqrMagnitude <= toleranceSquared)
		{
			feature = "Vertex";
			m_SurfacePoint = vertex;
			SetWorldState(center, outgoing);
			// 衝突の減衰は適用済みです。角への状態切替では重複して減衰させません。
			m_HasAppliedConvexTraversalSpeedRetention = true;
			if (!BeginConvexVertexSpherePatch(vertex))
			{
				return false;
			}

			// 既存のVertex球面パッチの内側にRadial Directionがあることを検証します。
			foreach (BallSurfaceMeshData.ConvexEdgeReference edge in m_ConvexVertexSphereIncidentEdges)
			{
				if (!TryGetVertexSphereBoundaryData(edge, out Vector3 direction, out _, out _))
				{
					return false;
				}
				float interior = Vector3.Dot(m_ConvexVertexSphereInteriorDirection, direction);
				if (Mathf.Abs(interior) <= m_VertexSphereBoundaryTolerance ||
					Vector3.Dot(m_ConvexVertexSphereRadialDirection, direction) * Mathf.Sign(interior) <
						-m_VertexSphereBoundaryTolerance)
				{
					return false;
				}
			}
			return true;
		}

		// 実Convex Edgeだけを対象にし、速度方向に合うSource / Targetの向きを試します。
		for (int edgeIndex = 0; edgeIndex < 3; edgeIndex++)
		{
			if (!TryGetConcaveEdgeWorldVertices(edgeIndex, a, b, c, out Vector3 start, out Vector3 end))
			{
				continue;
			}
			Vector3 delta = end - start;
			if (!(delta.sqrMagnitude > toleranceSquared))
			{
				continue;
			}
			float t = Mathf.Clamp01(Vector3.Dot(point - start, delta) / delta.sqrMagnitude);
			Vector3 edgePoint = start + delta * t;
			if ((point - edgePoint).sqrMagnitude > toleranceSquared ||
				m_MeshData.GetEdgeType(hitTriangle, edgeIndex) != BallSurfaceMeshData.EEdgeType.Convex ||
				!m_MeshData.TryGetNeighbor(hitTriangle, edgeIndex, out int neighbor, out int neighborEdge) ||
				!TryGetTriangleWorldData(neighbor, out _, out _, out _, out Vector3 neighborNormal))
			{
				continue;
			}

			feature = "Edge";
			Vector3 axis = delta.normalized;
			Vector3 radial = (center - edgePoint).normalized;
			if (Mathf.Abs(Vector3.Dot(radial, axis)) > 0.002f ||
				!IsDirectionOnConvexEdgeNormalArc(radial, faceNormal, neighborNormal))
			{
				continue;
			}
			m_CurrentTriangleIndex = hitTriangle;
			m_SurfacePoint = edgePoint;
			SetWorldState(center, outgoing);
			m_HasAppliedConvexTraversalSpeedRetention = true;
			if (BeginConvexEdgeTransition(edgeIndex, neighbor, neighborEdge, faceNormal, neighborNormal))
			{
				return true;
			}

			m_CurrentTriangleIndex = neighbor;
			if (BeginConvexEdgeTransition(neighborEdge, hitTriangle, edgeIndex, neighborNormal, faceNormal))
			{
				return true;
			}
			m_CurrentTriangleIndex = hitTriangle;
		}
		return false;
	}

	private bool TryPreparePreservedImpactFace(Vector3 center, Vector3 outgoing,
		Vector3 a, Vector3 b, Vector3 c, Vector3 faceNormal, float tolerance)
	{
		float planeDistance = Vector3.Dot(center - a, faceNormal);
		if (!(planeDistance >= m_BallRadius - tolerance))
		{
			return false;
		}
		Vector3 point = ProjectPointToPlane(center, a, faceNormal);
		const int maximumIterations = 8;
		for (int iteration = 0; iteration < maximumIterations; iteration++)
		{
			Vector3 closest = BallSurfaceSource.GetClosestPointOnTriangle(point, a, b, c);
			if ((closest - point).sqrMagnitude > tolerance * tolerance ||
				!TryGetSmoothWorldNormal(m_CurrentTriangleIndex, closest, out Vector3 smoothNormal))
			{
				return false;
			}
			smoothNormal.Normalize();
			float normalDot = Vector3.Dot(smoothNormal, faceNormal);
			if (!(normalDot > 0.0001f))
			{
				return false;
			}
			float offset = Mathf.Max(m_BallRadius, planeDistance / normalDot);
			if (!(offset <= m_BallRadius * 1.1f))
			{
				return false;
			}
			Vector3 reconstructed = closest + smoothNormal * offset;
			if ((reconstructed - center).sqrMagnitude <= tolerance * tolerance * 0.25f)
			{
				float vertical = Vector3.Dot(outgoing, faceNormal) / normalDot;
				if (vertical < -m_MinimumCalculationValue)
				{
					return false;
				}
				m_SurfacePoint = closest;
				m_Height = offset - m_BallRadius;
				m_VerticalVelocity = Mathf.Max(0.0f, vertical);
				m_SurfaceVelocity = Vector3.ProjectOnPlane(outgoing - smoothNormal * m_VerticalVelocity, faceNormal);
				return true;
			}
			point = ProjectPointToPlane(center - smoothNormal * offset, a, faceNormal);
		}
		return false;
	}

	/// <summary>
	/// 仮計算用Simulationに接触面の状態とバウンド結果を構築します。
	/// </summary>
	private EInitializeResult InitializeCandidate(
		BallSurfaceSource surfaceSource,
		int triangleIndex,
		Vector3 surfacePoint,
		Vector3 incomingVelocity,
		float initialHeight)
	{
		Reset();

		if (surfaceSource == null || !surfaceSource.IsReady)
		{
			return EInitializeResult.InvalidSurface;
		}

		BallSurfaceMeshData meshData = surfaceSource.MeshData;
		if (meshData == null || triangleIndex < 0 ||
			triangleIndex >= meshData.TriangleCount)
		{
			return EInitializeResult.InvalidTriangle;
		}

		if (!IsFiniteInitializationVector(surfacePoint) ||
			!IsFiniteInitializationVector(incomingVelocity) ||
			float.IsNaN(initialHeight) || float.IsInfinity(initialHeight) ||
			initialHeight < 0.0f || initialHeight > m_BallRadius)
		{
			return EInitializeResult.InvalidInput;
		}

		m_SurfaceSource = surfaceSource;
		m_MeshData = meshData;
		m_CurrentTriangleIndex = triangleIndex;

		if (!TryGetTriangleWorldData(
			triangleIndex,
			out Vector3 vertexA,
			out Vector3 vertexB,
			out Vector3 vertexC,
			out Vector3 triangleNormal) ||
			!IsFiniteInitializationVector(triangleNormal) ||
			triangleNormal.sqrMagnitude <= m_MinimumCalculationValue * m_MinimumCalculationValue)
		{
			return EInitializeResult.InvalidTriangleGeometry;
		}

		Vector3 projectedPoint = ProjectPointToPlane(surfacePoint, vertexA, triangleNormal);
		if (!TryCalculateBarycentric(
			projectedPoint, vertexA, vertexB, vertexC, out Vector3 barycentric) ||
			!IsFiniteInitializationVector(barycentric))
		{
			return EInitializeResult.InvalidTriangleGeometry;
		}

		// 面の外へわずかにはみ出した点は同じTriangleの最近接点へ補正します。
		// 許容範囲を広げて別の壁や離れた点を採用することはしません。
		if (barycentric.x < 0.0f || barycentric.y < 0.0f || barycentric.z < 0.0f)
		{
			Vector3 correctedPoint = BallSurfaceSource.GetClosestPointOnTriangle(
				projectedPoint, vertexA, vertexB, vertexC);
			float correctionLimit = Mathf.Max(
				m_MinimumCalculationValue,
				Mathf.Min(m_MaximumInitialPointCorrection,
					Mathf.Min(surfaceSource.SurfaceCollider.contactOffset, m_BallRadius * 0.02f)));

			if (!IsFiniteInitializationVector(correctedPoint) ||
				(correctedPoint - projectedPoint).sqrMagnitude > correctionLimit * correctionLimit ||
				!TryCalculateBarycentric(correctedPoint, vertexA, vertexB, vertexC, out barycentric) ||
				!IsFiniteInitializationVector(barycentric) || !IsInsideTriangle(barycentric))
			{
				return EInitializeResult.PointOutsideTriangle;
			}

			projectedPoint = correctedPoint;
		}

		m_SurfacePoint = projectedPoint;

		if (!TryGetSmoothWorldNormal(triangleIndex, projectedPoint, out Vector3 smoothNormal) ||
			!IsFiniteInitializationVector(smoothNormal) ||
			smoothNormal.sqrMagnitude <= m_MinimumCalculationValue * m_MinimumCalculationValue)
		{
			return EInitializeResult.InvalidNormal;
		}

		// 入射速度はSmooth Normalを基準に
		// Surface方向と高さ方向へ分離します。
		Vector3 desiredSurfaceVelocity =
			Vector3.ProjectOnPlane(
				incomingVelocity,
				smoothNormal);

		float desiredSurfaceSpeed =
			desiredSurfaceVelocity.magnitude;

		// SurfacePoint自体はTriangle平面上を移動するため、
		// 最終的なSurfaceVelocityは現在Triangleへ投影します。
		Vector3 triangleSurfaceVelocity =
			Vector3.ProjectOnPlane(
				desiredSurfaceVelocity,
				triangleNormal);

		if (desiredSurfaceSpeed >
				m_MinimumCalculationValue &&
			triangleSurfaceVelocity.sqrMagnitude >
				m_MinimumCalculationValue *
					m_MinimumCalculationValue)
		{
			m_SurfaceVelocity =
				triangleSurfaceVelocity.normalized *
					desiredSurfaceSpeed;
		}
		else
		{
			m_SurfaceVelocity =
				Vector3.zero;
		}

		float incomingNormalSpeed =
			Vector3.Dot(
				incomingVelocity,
				smoothNormal);

		float impactSpeed =
			Mathf.Max(
				0.0f,
				-incomingNormalSpeed);

		m_Height =
			0.0f;

		m_VerticalVelocity =
			0.0f;

		m_IsInitialized =
			true;

		HandleSurfaceImpact(
			impactSpeed);

		// 補正位置はRigidbodyだけでなくSurfaceのHeightにも反映します。
		m_Height = initialHeight;

		if (!RefreshWorldStateFromCurrentTraversal() ||
			!m_HasWorldState ||
			!IsFiniteInitializationVector(m_WorldCenter) ||
			!IsFiniteInitializationVector(m_WorldVelocity) ||
			!IsFiniteInitializationVector(m_SurfaceVelocity) ||
			float.IsNaN(m_VerticalVelocity) || float.IsInfinity(m_VerticalVelocity))
		{
			return EInitializeResult.InvalidWorldState;
		}

		return EInitializeResult.Success;
	}

	/// <summary>
	/// 初期化に使用する座標・速度が有限の値であるか確認します。
	/// </summary>
	private static bool IsFiniteInitializationVector(Vector3 value)
	{
		return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
			!float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
			!float.IsNaN(value.z) && !float.IsInfinity(value.z);
	}
}
