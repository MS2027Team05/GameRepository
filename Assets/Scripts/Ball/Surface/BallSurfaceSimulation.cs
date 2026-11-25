using UnityEngine;

/// <summary>
/// BallSurface上でのボール移動をTriangle単位で計算するSurface Physics本体です。
/// </summary>
/// <remarks>
/// 主な処理の流れ:
/// 1. 接触Triangle・SurfacePoint・入射速度からSimulationを初期化します。
/// 2. 速度をSurface接線方向とSurface法線方向へ分離して保持します。
/// 3. 各Physics Stepで法線方向のHeight運動とSurface方向の移動を進めます。
/// 4. Triangle境界へ到達するとEdge種別に応じたpartial処理へ遷移します。
/// 5. Flat・Smoothは隣Triangleへ連続移動し、ConvexはEdge回り込み、
///    Concaveは内角遷移、Vertexは球面パッチとして処理します。
/// 6. Ball本体のWorld中心とWorld速度はWorldStateを共通基準として維持します。
/// 7. 接地・バウンド時はSurface Physics専用の速度保持率と上限値を適用します。
///
/// Rigidbodyを直接操作するクラスではありません。
/// 計算結果のWorld位置・速度はBallPhysicsController.Surface.csがUnity側へ反映します。
/// </remarks>
public sealed partial class BallSurfaceSimulation
{
	public enum EAdvanceResult
	{
		Moved,
		NotInitialized,
		ConcaveEdgeReached,
		BoundaryEdgeReached,
		UnsupportedEdgeReached,
		InvalidGeometry,
		TransitionLimitReached
	}

	/// <summary>
	/// Surface初期化の成功または失敗理由です。
	/// </summary>
	public enum EInitializeResult
	{
		Success,
		InvalidSurface,
		ContactTriangleNotFound,
		InvalidTriangle,
		InvalidInput,
		InvalidTriangleGeometry,
		PointOutsideTriangle,
		InvalidNormal,
		InvalidWorldState
	}

	// 接触点補正は実際の接触面から大きく移動させない範囲に制限します。
	private const float m_MaximumInitialPointCorrection = 0.01f;
	private const float m_BarycentricTolerance = 0.0001f;
	private const float m_EntryEdgeIgnoreFraction = 0.0001f;
	private const float m_MinimumCalculationValue = 0.000001f;
	private const float m_MinimumHeight = 0.0001f;
	private const int m_MaximumTriangleTransitionsPerStep = 128;
	private const int m_MaximumVerticalImpactsPerStep = 8;

	/// <summary>
	/// Height方向とSurface方向を同期して進める際の
	/// 1回あたりの最大時間です。
	/// </summary>
	/// <remarks>
	/// 通常のFixedUpdate全体を先にHeight更新してから
	/// Surface移動すると、バウンド中にSharp Edgeへ入った際、
	/// Edge計算が「フレーム終端のHeight」を使ってしまいます。
	///
	/// 空中時だけ短い内部Stepへ分割することで、
	/// Heightと角回りの円弧半径をほぼ同時刻として扱います。
	/// </remarks>
	private const float m_MaximumCoupledStepTime = 0.005f;

	/// <summary>
	/// Sharp Convex Edgeへ入った瞬間に残すWorld速度の割合です。
	/// </summary>
	/// <remarks>
	/// 円弧を回っている最中に毎Frame減速するのではなく、
	/// Convex Edgeへ入った瞬間に1回だけ適用します。
	///
	/// World速度全体へ同じ倍率を掛けるため、進行方向は変えず、
	/// 角へ入った瞬間の運動エネルギーだけを減らします。
	/// </remarks>
	private const float m_ConvexEdgeSpeedRetention = 0.65f;

	/// <summary>
	/// Sharp Convex Edgeの表示カーブを円弧からどれだけ潰すかを表します。
	/// </summary>
	/// <remarks>
	/// 1.0なら通常の円弧に近いBezierカーブです。
	/// 1.0より小さくすると角の中央がEdge側へ寄り、
	/// World上の軌道が短くなります。
	///
	/// 今回は0.70として、円弧の自然な入り方を残しつつ
	/// 中央だけ少し潰します。
	/// </remarks>
	private const float m_ConvexCurveFlatten = 0.70f;

	/// <summary>
	/// 2本のEdge Crossingを同時到達として扱う
	/// Crossing Fractionの許容差です。
	/// </summary>
	private const float m_VertexCrossingFractionTolerance = 0.001f;


	// 初期化を仮計算する専用Simulationです。初回のみ生成して再利用します。
	private BallSurfaceSimulation m_InitializationCandidate;

	private BallSurfaceSource m_SurfaceSource;
	private BallSurfaceMeshData m_MeshData;

	private int m_CurrentTriangleIndex;

	private Vector3 m_SurfacePoint;
	private Vector3 m_SurfaceVelocity;

	private float m_Height;
	private float m_VerticalVelocity;

	private bool m_IsInitialized;


	private int m_LastEdgeTriangleIndex;
	private int m_LastEdgeIndex;
	private Vector3 m_LastEdgePoint;
	private BallSurfaceMeshData.EEdgeType m_LastEdgeType;

	private float m_GravityAcceleration;
	private float m_BounceSpeedThreshold;
	private float m_BounceNormalRetention;
	private float m_BounceTangentialRetention;
	private float m_MinBounceNormalSpeed;
	private float m_MaxBounceNormalSpeed;
	private float m_MaxBounceTangentialSpeed;
	private float m_GroundedDeceleration;
	private float m_GroundedStopSpeedThreshold;
	private float m_BallRadius;

	/// <summary>
	/// Sharp Convex Edgeを回り込んでいる途中かどうかです。
	/// </summary>
	private bool m_IsConvexEdgeTransition;

	private int m_ConvexTargetTriangleIndex;
	private int m_ConvexTargetEdgeIndex;

	private Vector3 m_ConvexEdgeDirection;
	private Vector3 m_ConvexEdgeStartPoint;
	private Vector3 m_ConvexEdgeEndPoint;
	private Vector3 m_ConvexSourceNormal;
	private Vector3 m_ConvexTargetNormal;

	private float m_ConvexSignedAngle;
	private float m_ConvexCurrentAngle;

	private float m_ConvexAlongSpeed;
	private float m_ConvexCrossSpeed;

	public bool IsInitialized
	{
		get
		{
			return m_IsInitialized;
		}
	}

	public BallSurfaceSource SurfaceSource
	{
		get
		{
			return m_SurfaceSource;
		}
	}

	public int CurrentTriangleIndex
	{
		get
		{
			return m_CurrentTriangleIndex;
		}
	}

	public Vector3 SurfacePoint
	{
		get
		{
			return m_SurfacePoint;
		}
	}

	public Vector3 SurfaceVelocity
	{
		get
		{
			return m_SurfaceVelocity;
		}
	}

	public float SurfaceSpeed
	{
		get
		{
			return m_SurfaceVelocity.magnitude;
		}
	}

	public float Height
	{
		get
		{
			return m_Height;
		}
	}

	public float VerticalVelocity
	{
		get
		{
			return m_VerticalVelocity;
		}
	}

	public bool IsGrounded
	{
		get
		{
			return !m_IsConcaveAirborneTransfer &&
				m_Height <=
					m_MinimumHeight &&
				m_VerticalVelocity <=
					0.0f;
		}
	}

	public bool HasLastEdge
	{
		get
		{
			return m_LastEdgeIndex >=
				0;
		}
	}

	public BallSurfaceMeshData.EEdgeType LastEdgeType
	{
		get
		{
			return m_LastEdgeType;
		}
	}

	public Vector3 LastEdgePoint
	{
		get
		{
			return m_LastEdgePoint;
		}
	}

	public int LastEdgeTriangleIndex
	{
		get
		{
			return m_LastEdgeTriangleIndex;
		}
	}

	public int LastEdgeIndex
	{
		get
		{
			return m_LastEdgeIndex;
		}
	}

	public BallSurfaceSimulation()
	{
		Reset();
	}

	/// <summary>
	/// Surface Physicsのパラメータを設定します。
	/// </summary>
	public void ConfigurePhysics(
		float gravityAcceleration,
		float bounceSpeedThreshold,
		float bounceNormalRetention,
		float bounceTangentialRetention,
		float minBounceNormalSpeed,
		float maxBounceNormalSpeed,
		float maxBounceTangentialSpeed,
		float groundedDeceleration,
		float groundedStopSpeedThreshold,
		float ballRadius)
	{
		m_GravityAcceleration =
			Mathf.Max(
				0.0f,
				gravityAcceleration);

		m_BounceSpeedThreshold =
			Mathf.Max(
				0.0f,
				bounceSpeedThreshold);

		m_BounceNormalRetention =
			Mathf.Clamp01(
				bounceNormalRetention);

		m_BounceTangentialRetention =
			Mathf.Clamp01(
				bounceTangentialRetention);

		m_MinBounceNormalSpeed =
			Mathf.Max(
				0.0f,
				minBounceNormalSpeed);

		m_MaxBounceNormalSpeed =
			Mathf.Max(
				0.0f,
				maxBounceNormalSpeed);

		m_MaxBounceTangentialSpeed =
			Mathf.Max(
				0.0f,
				maxBounceTangentialSpeed);

		m_GroundedDeceleration =
			Mathf.Max(
				0.0f,
				groundedDeceleration);

		m_GroundedStopSpeedThreshold =
			Mathf.Max(
				0.0f,
				groundedStopSpeedThreshold);

		m_BallRadius =
			Mathf.Max(
				0.0f,
				ballRadius);
	}

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
		out EInitializeResult result)
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
			incomingVelocity);

		if (result != EInitializeResult.Success)
		{
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
	/// 仮計算用Simulationに接触面の状態とバウンド結果を構築します。
	/// </summary>
	private EInitializeResult InitializeCandidate(
		BallSurfaceSource surfaceSource,
		int triangleIndex,
		Vector3 surfacePoint,
		Vector3 incomingVelocity)
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
			!IsFiniteInitializationVector(incomingVelocity))
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

	/// <summary>
	/// Surface Physicsを1Physics Step進めます。
	/// </summary>
	public EAdvanceResult Advance(
		float deltaTime)
	{
		ClearLastEdge();

		if (!m_IsInitialized ||
			m_SurfaceSource == null ||
			m_MeshData == null)
		{
			return EAdvanceResult.NotInitialized;
		}

		if (deltaTime <=
			0.0f)
		{
			return EAdvanceResult.Moved;
		}

		float remainingTime =
			deltaTime;

		while (remainingTime >
			m_MinimumCalculationValue)
		{
			// 空中でConcaveへ入った場合だけ、
			// 通常のHeight / Surface進行から完全に分離します。
			if (m_IsConcaveAirborneTransfer)
			{
				float concaveStepTime =
					Mathf.Min(
						remainingTime,
						m_MaximumCoupledStepTime);

				EAdvanceResult concaveResult =
					AdvanceConcaveAirborneTransfer(
						concaveStepTime,
						out float unusedConcaveTime);

				if (concaveResult !=
					EAdvanceResult.Moved)
				{
					return concaveResult;
				}

				float consumedConcaveTime =
					Mathf.Clamp(
						concaveStepTime -
							unusedConcaveTime,
						0.0f,
						concaveStepTime);

				remainingTime -=
					consumedConcaveTime;

				// Concaveから別Traversalへ切り替わった場合は、
				// 未消費時間を新Traversalで続けて処理します。
				if (unusedConcaveTime >
					m_MinimumCalculationValue)
				{
					continue;
				}

				continue;
			}

			bool requiresCoupledStep =
				m_Height >
					m_MinimumHeight ||
				Mathf.Abs(
					m_VerticalVelocity) >
					m_MinimumCalculationValue ||
				m_IsConvexEdgeTransition ||
				m_IsConvexVertexSpherePatch;

			float stepTime =
				requiresCoupledStep
					? Mathf.Min(
						remainingTime,
						m_MaximumCoupledStepTime)
					: remainingTime;

			AdvanceVerticalMotion(
				stepTime);

			if (IsGrounded &&
				!m_IsConvexEdgeTransition &&
				!m_IsConvexVertexSpherePatch)
			{
				ApplyGroundedDeceleration(
					stepTime);
			}

			EAdvanceResult surfaceResult =
				AdvanceSurfaceMotion(
					stepTime);

			if (!RefreshWorldStateFromCurrentTraversal())
			{
				return EAdvanceResult.InvalidGeometry;
			}

			if (surfaceResult !=
				EAdvanceResult.Moved)
			{
				return surfaceResult;
			}

			remainingTime -=
				stepTime;
		}

		return EAdvanceResult.Moved;
	}

	/// <summary>
	/// Surface法線方向の運動を更新します。
	/// </summary>
	/// <remarks>
	/// Height = h0 + v0*t - 0.5*g*t^2
	/// の着地時刻を求めることで、
	/// FixedUpdate途中に着地しても残り時間で
	/// すぐ次のバウンドへ移行します。
	/// </remarks>
	private void AdvanceVerticalMotion(
		float deltaTime)
	{
		float remainingTime =
			deltaTime;

		int impactCount =
			0;

		while (remainingTime >
			m_MinimumCalculationValue)
		{
			if (m_Height <=
					m_MinimumHeight &&
				m_VerticalVelocity <=
					0.0f)
			{
				m_Height =
					0.0f;

				m_VerticalVelocity =
					0.0f;

				return;
			}

			if (impactCount >=
				m_MaximumVerticalImpactsPerStep)
			{
				// 極端に短い周期のバウンドが1Step内で
				// 繰り返される場合は安定側へ倒します。
				m_Height =
					0.0f;

				m_VerticalVelocity =
					0.0f;

				return;
			}

			if (!TryCalculateSurfaceHitTime(
				remainingTime,
				out float hitTime))
			{
				IntegrateVerticalMotion(
					remainingTime);

				return;
			}

			// 着地する瞬間まで正確に進めます。
			IntegrateVerticalMotion(
				hitTime);

			m_Height =
				0.0f;

			float impactSpeed =
				Mathf.Max(
					0.0f,
					-m_VerticalVelocity);

			m_VerticalVelocity =
				0.0f;


			bool bounced =
				HandleSurfaceImpact(
					impactSpeed);

			remainingTime -=
				hitTime;

			impactCount++;

			if (!bounced)
			{
				return;
			}
		}
	}

	/// <summary>
	/// 現在のHeight / VerticalVelocityから、
	/// 指定時間内にSurfaceへ到達する時刻を求めます。
	/// </summary>
	private bool TryCalculateSurfaceHitTime(
		float maximumTime,
		out float hitTime)
	{
		hitTime =
			0.0f;

		if (maximumTime <=
			0.0f)
		{
			return false;
		}

		if (m_GravityAcceleration <=
			m_MinimumCalculationValue)
		{
			if (m_VerticalVelocity >=
				-m_MinimumCalculationValue)
			{
				return false;
			}

			float linearHitTime =
				m_Height /
				-m_VerticalVelocity;

			if (linearHitTime <
					0.0f ||
				linearHitTime >
					maximumTime)
			{
				return false;
			}

			hitTime =
				linearHitTime;

			return true;
		}

		// h(t) = h0 + v0*t - 0.5*g*t^2 = 0
		//
		// 正の解:
		// t = (v0 + sqrt(v0^2 + 2*g*h0)) / g
		float discriminant =
			m_VerticalVelocity *
				m_VerticalVelocity +
			2.0f *
				m_GravityAcceleration *
				Mathf.Max(
					0.0f,
					m_Height);

		if (discriminant <
			0.0f)
		{
			return false;
		}

		float candidateTime =
			(
				m_VerticalVelocity +
				Mathf.Sqrt(
					discriminant)
			) /
			m_GravityAcceleration;

		// Height=0かつ上昇中なら、
		// candidateTimeは次の着地時刻になります。
		if (candidateTime <=
			m_MinimumCalculationValue)
		{
			return false;
		}

		if (candidateTime >
			maximumTime)
		{
			return false;
		}

		hitTime =
			candidateTime;

		return true;
	}

	/// <summary>
	/// 法線方向の等加速度運動を指定時間だけ進めます。
	/// </summary>
	private void IntegrateVerticalMotion(
		float deltaTime)
	{
		if (deltaTime <=
			0.0f)
		{
			return;
		}

		m_Height +=
			m_VerticalVelocity *
				deltaTime -
			0.5f *
				m_GravityAcceleration *
				deltaTime *
				deltaTime;

		m_VerticalVelocity -=
			m_GravityAcceleration *
				deltaTime;

		if (m_Height <
			0.0f)
		{
			m_Height =
				0.0f;
		}
	}

	/// <summary>
	/// Surfaceへ衝突した際のバウンドを処理します。
	/// </summary>
	/// <returns>
	/// 再バウンドした場合はtrueです。
	/// </returns>
	private bool HandleSurfaceImpact(
		float impactSpeed)
	{
		ApplySurfaceSpeedScale(
			m_BounceTangentialRetention);

		ClampSurfaceSpeed(
			m_MaxBounceTangentialSpeed);

		if (impactSpeed <
			m_BounceSpeedThreshold)
		{
			m_VerticalVelocity =
				0.0f;

			return false;
		}

		float bounceSpeed =
			impactSpeed *
				m_BounceNormalRetention;

		if (m_MinBounceNormalSpeed >
			0.0f)
		{
			bounceSpeed =
				Mathf.Max(
					bounceSpeed,
					m_MinBounceNormalSpeed);
		}

		if (m_MaxBounceNormalSpeed >
			0.0f)
		{
			bounceSpeed =
				Mathf.Min(
					bounceSpeed,
					m_MaxBounceNormalSpeed);
		}

		if (bounceSpeed <=
			m_MinimumCalculationValue)
		{
			m_VerticalVelocity =
				0.0f;

			return false;
		}

		m_VerticalVelocity =
			bounceSpeed;


		return true;
	}

	/// <summary>
	/// 接地中のSurface方向速度を減速します。
	/// </summary>
	private void ApplyGroundedDeceleration(
		float deltaTime)
	{
		float currentSpeed =
			m_SurfaceVelocity.magnitude;

		if (currentSpeed <=
			m_GroundedStopSpeedThreshold)
		{
			m_SurfaceVelocity =
				Vector3.zero;

			return;
		}

		if (m_GroundedDeceleration <=
			0.0f)
		{
			return;
		}

		float nextSpeed =
			Mathf.MoveTowards(
				currentSpeed,
				0.0f,
				m_GroundedDeceleration *
					deltaTime);

		if (nextSpeed <=
			m_GroundedStopSpeedThreshold)
		{
			m_SurfaceVelocity =
				Vector3.zero;

			return;
		}

		m_SurfaceVelocity =
			m_SurfaceVelocity.normalized *
				nextSpeed;
	}

	/// <summary>
	/// Surface方向速度を指定された最大速度へ制限します。
	/// </summary>
	/// <remarks>
	/// Sharp Convex Edge遷移中も同じ倍率を適用するため、
	/// ApplySurfaceSpeedScale経由で速度を制限します。
	/// </remarks>
	private void ClampSurfaceSpeed(
		float maximumSpeed)
	{
		float clampedMaximumSpeed =
			Mathf.Max(
				0.0f,
				maximumSpeed);

		if (clampedMaximumSpeed <=
			0.0f)
		{
			return;
		}

		float currentSpeed =
			m_SurfaceVelocity.magnitude;

		if (currentSpeed <=
				clampedMaximumSpeed ||
			currentSpeed <=
				m_MinimumCalculationValue)
		{
			return;
		}

		ApplySurfaceSpeedScale(
			clampedMaximumSpeed /
				currentSpeed);
	}

	/// <summary>
	/// Surface方向速度へ一様な倍率を適用します。
	/// </summary>
	private void ApplySurfaceSpeedScale(
		float scale)
	{
		float clampedScale =
			Mathf.Max(
				0.0f,
				scale);

		m_SurfaceVelocity *=
			clampedScale;

		if (m_IsConvexEdgeTransition)
		{
			m_ConvexAlongSpeed *=
				clampedScale;

			m_ConvexCrossSpeed *=
				clampedScale;
		}

	}

	/// <summary>
	/// 現在のSurfacePointからBall中心までのWorld Offsetを取得します。
	/// </summary>
	/// <remarks>
	/// Flat / Smooth Edgeでは従来通り、
	///
	/// SurfaceNormal * (BallRadius + Height)
	///
	/// を返します。
	///
	/// Sharp Convex EdgeではEdge用Offsetを返し、
	/// Convex VertexではVertex中心の球面パッチ上のOffsetを返します。
	/// 球面パッチはSurface速度から回転角を求めるため、
	/// Vertex補間による追加移動を発生させません。
	/// </remarks>
	public bool TryGetCurrentBallCenterOffset(
		out Vector3 ballCenterOffset)
	{
		ballCenterOffset =
			Vector3.zero;

		if (!m_IsInitialized ||
			!m_HasWorldState)
		{
			return false;
		}

		ballCenterOffset =
			m_WorldCenter -
			m_SurfacePoint;

		return true;
	}

	/// <summary>
	/// 3次Bezierカーブ上の位置を取得します。
	/// </summary>
	private Vector3 EvaluateCubicBezier(
		Vector3 point0,
		Vector3 point1,
		Vector3 point2,
		Vector3 point3,
		float progress)
	{
		float clampedProgress =
			Mathf.Clamp01(
				progress);

		float inverseProgress =
			1.0f -
			clampedProgress;

		float inverseProgressSquared =
			inverseProgress *
			inverseProgress;

		float progressSquared =
			clampedProgress *
			clampedProgress;

		return
			point0 *
				inverseProgressSquared *
				inverseProgress +
			3.0f *
				point1 *
				inverseProgressSquared *
				clampedProgress +
			3.0f *
				point2 *
				inverseProgress *
				progressSquared +
			point3 *
				progressSquared *
				clampedProgress;
	}

	/// <summary>
	/// 現在位置で使用する連続したSurface Normalを取得します。
	/// </summary>
	public bool TryGetCurrentSurfaceNormal(
		out Vector3 surfaceNormal)
	{
		surfaceNormal =
			Vector3.zero;

		if (!m_IsInitialized)
		{
			return false;
		}

		if (m_IsConcaveAirborneTransfer)
		{
			surfaceNormal =
				m_ConcaveReferenceNormal;

			return surfaceNormal.sqrMagnitude >
				m_MinimumCalculationValue *
					m_MinimumCalculationValue;
		}


		if (m_IsConvexVertexSpherePatch)
		{
			surfaceNormal =
				m_ConvexVertexSphereRadialDirection;

			return surfaceNormal.sqrMagnitude >
				m_MinimumCalculationValue *
					m_MinimumCalculationValue;
		}

		if (m_IsConvexEdgeTransition)
		{
			surfaceNormal =
				GetConvexTransitionNormal();

			return surfaceNormal.sqrMagnitude >
				m_MinimumCalculationValue *
					m_MinimumCalculationValue;
		}

		return TryGetSmoothWorldNormal(
			m_CurrentTriangleIndex,
			m_SurfacePoint,
			out surfaceNormal);
	}

	public void Reset()
	{
		m_SurfaceSource =
			null;

		m_MeshData =
			null;

		m_CurrentTriangleIndex =
			-1;

		m_SurfacePoint =
			Vector3.zero;

		m_SurfaceVelocity =
			Vector3.zero;

		m_Height =
			0.0f;

		m_VerticalVelocity =
			0.0f;

		m_IsInitialized =
			false;

		ResetConcaveAirborneTransfer();

		ResetConvexEdgeTransition();

		ResetConvexVertexSpherePatch();

		ResetWorldState();

		ClearLastEdge();
	}

	/// <summary>
	/// TriangleのWorld頂点と幾何法線を取得します。
	/// </summary>
	private bool TryGetTriangleWorldData(
		int triangleIndex,
		out Vector3 vertexA,
		out Vector3 vertexB,
		out Vector3 vertexC,
		out Vector3 surfaceNormal)
	{
		vertexA =
			Vector3.zero;

		vertexB =
			Vector3.zero;

		vertexC =
			Vector3.zero;

		surfaceNormal =
			Vector3.zero;

		if (m_SurfaceSource == null ||
			m_MeshData == null ||
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

		Transform surfaceTransform =
			m_SurfaceSource.transform;

		vertexA =
			surfaceTransform.TransformPoint(
				localVertexA);

		vertexB =
			surfaceTransform.TransformPoint(
				localVertexB);

		vertexC =
			surfaceTransform.TransformPoint(
				localVertexC);

		Vector3 edgeAB =
			vertexB -
				vertexA;

		Vector3 edgeAC =
			vertexC -
				vertexA;

		Vector3 normal =
			Vector3.Cross(
				edgeAB,
				edgeAC);

		if (normal.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return false;
		}

		surfaceNormal =
			normal.normalized;

		return true;
	}

	private Vector3 ProjectPointToPlane(
		Vector3 point,
		Vector3 planePoint,
		Vector3 planeNormal)
	{
		float distance =
			Vector3.Dot(
				point -
					planePoint,
				planeNormal);

		return point -
			planeNormal *
				distance;
	}

	private bool TryCalculateBarycentric(
		Vector3 point,
		Vector3 vertexA,
		Vector3 vertexB,
		Vector3 vertexC,
		out Vector3 barycentric)
	{
		barycentric =
			Vector3.zero;

		Vector3 vector0 =
			vertexB -
				vertexA;

		Vector3 vector1 =
			vertexC -
				vertexA;

		Vector3 vector2 =
			point -
				vertexA;

		float dot00 =
			Vector3.Dot(
				vector0,
				vector0);

		float dot01 =
			Vector3.Dot(
				vector0,
				vector1);

		float dot11 =
			Vector3.Dot(
				vector1,
				vector1);

		float dot20 =
			Vector3.Dot(
				vector2,
				vector0);

		float dot21 =
			Vector3.Dot(
				vector2,
				vector1);

		float denominator =
			dot00 *
				dot11 -
			dot01 *
				dot01;

		if (Mathf.Abs(
			denominator) <=
			m_MinimumCalculationValue)
		{
			return false;
		}

		float inverseDenominator =
			1.0f /
				denominator;

		float coordinateB =
			(
				dot11 *
					dot20 -
				dot01 *
					dot21
			) *
			inverseDenominator;

		float coordinateC =
			(
				dot00 *
					dot21 -
				dot01 *
					dot20
			) *
			inverseDenominator;

		float coordinateA =
			1.0f -
				coordinateB -
				coordinateC;

		barycentric =
			new Vector3(
				coordinateA,
				coordinateB,
				coordinateC);

		return true;
	}

	private bool IsInsideTriangle(
		Vector3 barycentric)
	{
		return barycentric.x >=
				-m_BarycentricTolerance &&
			barycentric.y >=
				-m_BarycentricTolerance &&
			barycentric.z >=
				-m_BarycentricTolerance;
	}


}
