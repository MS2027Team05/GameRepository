using UnityEngine;

/// <summary>
/// BallSurface上でのボールの移動計算を担当します。
/// </summary>
/// <remarks>
/// Surface Physicsでは運動を、
///
/// ・Surface接線方向
/// ・Surface法線方向
///
/// の2つへ分離して管理します。
///
/// Surface方向はTriangleを展開した空間上で移動し、
/// 法線方向はHeight / VerticalVelocityとして計算します。
///
/// Flat / Smooth Edgeでは頂点ごとに事前生成した
/// Smooth Normalを補間することで、SphereやTorus上でも
/// ボール中心の位置とバウンド方向が連続するようにします。
/// </remarks>
public sealed class BallSurfaceSimulation
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
	/// Sharp Convex Edgeへ入った瞬間に残すSurface速度の割合です。
	/// </summary>
	/// <remarks>
	/// 円弧を回っている最中に毎Frame減速するのではなく、
	/// Convex Edgeへ入った瞬間に1回だけ適用します。
	///
	/// 0.85なら、角へ入る直前のSurface速度の85%を残します。
	/// </remarks>
	private const float m_ConvexEdgeSpeedRetention = 0.85f;

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

	/// <summary>
	/// 現在面の角度二等分線へ向かっていると判定する
	/// 最大角度差です。
	/// </summary>
	private const float m_VertexBisectorMaximumAngle = 8.0f;

	/// <summary>
	/// Vertexから見て現在面上に存在するEdge方向と判定する
	/// Surface Normalとの内積許容値です。
	/// </summary>
	private const float m_VertexFaceEdgeNormalDotTolerance = 0.02f;

	private BallSurfaceSource m_SurfaceSource;
	private BallSurfaceMeshData m_MeshData;

	private int m_CurrentTriangleIndex;

	private Vector3 m_SurfacePoint;
	private Vector3 m_SurfaceVelocity;

	private float m_Height;
	private float m_VerticalVelocity;

	private bool m_IsInitialized;

	/// <summary>
	/// 今回のAdvance中にSharp Convex Edgeを1回以上通過したかどうかです。
	/// </summary>
	/// <remarks>
	/// LastEdgeはAdvance中に後続のFlat / Smooth Edgeで上書きされるため、
	/// デバッグ計測ではこのフラグを使ってConvex通過を取りこぼさないようにします。
	/// </remarks>
	private bool m_DidTraverseConvexEdgeThisStep;

	/// <summary>
	/// 今回のAdvance中にSurface方向へ進んだ距離です。
	/// </summary>
	private float m_SurfaceDistanceTravelledThisStep;

	/// <summary>
	/// 今回のAdvance中にSurfaceへ着地したかどうかです。
	/// </summary>
	private bool m_DidSurfaceImpactThisStep;

	/// <summary>
	/// Surface Physics開始後に成立したバウンド番号です。
	/// </summary>
	private int m_BounceSequence;

	/// <summary>
	/// 直近に成立したバウンド開始時のSurface速度です。
	/// </summary>
	private float m_LastBounceSurfaceSpeed;

	/// <summary>
	/// 直近に成立したバウンド開始時の法線方向速度です。
	/// </summary>
	private float m_LastBounceVerticalSpeed;

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

	/// <summary>
	/// 頂点の角度二等分線から進入し、
	/// 3本目のConvex Edgeへ向きを切り替えている途中かどうかです。
	/// </summary>
	private bool m_IsConvexVertexTransition;

	private Vector3 m_ConvexVertexPoint;
	private Vector3 m_ConvexVertexSourceNormal;
	private Vector3 m_ConvexVertexTargetNormal;
	private Vector3 m_ConvexVertexRotationAxis;

	private float m_ConvexVertexSignedAngle;
	private float m_ConvexVertexCurrentAngle;

	private Vector3 m_ConvexVertexOutgoingEdgeDirection;
	private Vector3 m_ConvexVertexOutgoingEdgeEnd;

	/// <summary>
	/// 頂点遷移完了後、
	/// TriangleではなくConvex EdgeそのものをSurface基準として
	/// 進んでいるかどうかです。
	/// </summary>
	private bool m_IsConvexVertexEdgePath;

	private Vector3 m_ConvexVertexEdgeNormal;
	private Vector3 m_ConvexVertexEdgeDirection;
	private Vector3 m_ConvexVertexEdgeEnd;

	public bool IsInitialized
	{
		get
		{
			return m_IsInitialized;
		}
	}

	/// <summary>
	/// 直近のAdvance中にSharp Convex Edgeを通過したか取得します。
	/// </summary>
	public bool DidTraverseConvexEdgeThisStep
	{
		get
		{
			return m_DidTraverseConvexEdgeThisStep;
		}
	}

	/// <summary>
	/// 直近のAdvance中にSurface方向へ進んだ距離を取得します。
	/// </summary>
	public float SurfaceDistanceTravelledThisStep
	{
		get
		{
			return m_SurfaceDistanceTravelledThisStep;
		}
	}

	/// <summary>
	/// 直近のAdvance中にSurfaceへ着地したか取得します。
	/// </summary>
	public bool DidSurfaceImpactThisStep
	{
		get
		{
			return m_DidSurfaceImpactThisStep;
		}
	}

	/// <summary>
	/// 現在のバウンド番号を取得します。
	/// </summary>
	public int BounceSequence
	{
		get
		{
			return m_BounceSequence;
		}
	}

	/// <summary>
	/// 直近に成立したバウンド開始時のSurface速度を取得します。
	/// </summary>
	public float LastBounceSurfaceSpeed
	{
		get
		{
			return m_LastBounceSurfaceSpeed;
		}
	}

	/// <summary>
	/// 直近に成立したバウンド開始時の法線方向速度を取得します。
	/// </summary>
	public float LastBounceVerticalSpeed
	{
		get
		{
			return m_LastBounceVerticalSpeed;
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
			return m_Height <=
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
		Reset();

		if (surfaceSource == null ||
			!surfaceSource.IsReady)
		{
			return false;
		}

		BallSurfaceMeshData meshData =
			surfaceSource.MeshData;

		if (meshData == null ||
			triangleIndex < 0 ||
			triangleIndex >=
				meshData.TriangleCount)
		{
			return false;
		}

		m_SurfaceSource =
			surfaceSource;

		m_MeshData =
			meshData;

		m_CurrentTriangleIndex =
			triangleIndex;

		if (!TryGetTriangleWorldData(
			triangleIndex,
			out Vector3 vertexA,
			out Vector3 vertexB,
			out Vector3 vertexC,
			out Vector3 triangleNormal))
		{
			Reset();

			return false;
		}

		Vector3 projectedPoint =
			ProjectPointToPlane(
				surfacePoint,
				vertexA,
				triangleNormal);

		if (!TryCalculateBarycentric(
			projectedPoint,
			vertexA,
			vertexB,
			vertexC,
			out Vector3 barycentric))
		{
			Reset();

			return false;
		}

		if (!IsInsideTriangle(
			barycentric))
		{
			Reset();

			return false;
		}

		m_SurfacePoint =
			projectedPoint;

		if (!TryGetSmoothWorldNormal(
			triangleIndex,
			projectedPoint,
			out Vector3 smoothNormal))
		{
			Reset();

			return false;
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

		return true;
	}

	/// <summary>
	/// Surface Physicsを1Physics Step進めます。
	/// </summary>
	public EAdvanceResult Advance(
		float deltaTime)
	{
		ClearLastEdge();

		// LastEdgeはAdvance中に上書きされる可能性があるため、
		// Convex通過だけはStep単位のフラグとして別に保持します。
		m_DidTraverseConvexEdgeThisStep =
			false;

		m_SurfaceDistanceTravelledThisStep =
			0.0f;

		m_DidSurfaceImpactThisStep =
			false;

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
			// Heightが変化している最中、またはSharp Convex Edgeを
			// 回り込んでいる最中だけ内部Stepを細かくします。
			//
			// 接地して普通にSurface上を進んでいるだけなら
			// FixedUpdate全体を1回で処理するため、
			// 常時4倍の計算量にはなりません。
			bool requiresCoupledStep =
				m_Height >
					m_MinimumHeight ||
				Mathf.Abs(
					m_VerticalVelocity) >
					m_MinimumCalculationValue ||
				m_IsConvexEdgeTransition ||
				m_IsConvexVertexTransition;

			float stepTime =
				requiresCoupledStep
					? Mathf.Min(
						remainingTime,
						m_MaximumCoupledStepTime)
					: remainingTime;

			// 同じ短い時間幅で
			// Height方向 → Surface方向の順に進めます。
			//
			// これによりSharp Edgeの円弧半径
			// (BallRadius + Height)が、その時刻のHeightに
			// 近い値を使用するようになります。
			AdvanceVerticalMotion(
				stepTime);

			if (IsGrounded &&
				!m_IsConvexEdgeTransition &&
				!m_IsConvexVertexTransition)
			{
				ApplyGroundedDeceleration(
					stepTime);
			}

			EAdvanceResult surfaceResult =
				AdvanceSurfaceMotion(
					stepTime);

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

			m_DidSurfaceImpactThisStep =
				true;

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

		m_BounceSequence++;

		m_LastBounceSurfaceSpeed =
			m_SurfaceVelocity.magnitude;

		m_LastBounceVerticalSpeed =
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
	/// Surface方向のTriangle移動を進めます。
	/// </summary>
	private EAdvanceResult AdvanceSurfaceMotion(
		float deltaTime)
	{
		float remainingTime =
			deltaTime;

		int transitionCount =
			0;

		int ignoredEntryEdgeIndex =
			-1;

		while (remainingTime >
			m_MinimumCalculationValue)
		{
			if (transitionCount >=
				m_MaximumTriangleTransitionsPerStep)
			{
				return EAdvanceResult.TransitionLimitReached;
			}

			// 現在面の角度二等分線からVertexへ進入した場合は、
			// 左右どちらかの面を選ばず、
			// Vertexから伸びる3本目のConvex Edgeへ向きを切り替えます。
			if (m_IsConvexVertexTransition)
			{
				if (!AdvanceConvexVertexTransition(
					ref remainingTime,
					out bool completed))
				{
					return EAdvanceResult.InvalidGeometry;
				}

				if (!completed)
				{
					return EAdvanceResult.Moved;
				}

				transitionCount++;
				continue;
			}

			// Vertex Transition完了後は、
			// TriangleではなくConvex Edgeそのものを基準線として進みます。
			if (m_IsConvexVertexEdgePath)
			{
				AdvanceConvexVertexEdgePath(
					remainingTime);

				return EAdvanceResult.Moved;
			}

			// Sharp Convex Edgeへ入った後は、
			// SurfacePointを共有Edge上へ置いたまま、
			// Ball中心の法線方向だけをEdge周りに回転させます。
			//
			// これによりCubeの90度角でも
			//
			// 上面Normal
			//   ↓
			// 円弧状に回転
			//   ↓
			// 側面Normal
			//
			// と連続するため、Heightを保持したまま
			// World位置が飛ばずに隣面へ移行できます。
			if (m_IsConvexEdgeTransition)
			{
				if (!AdvanceConvexEdgeTransition(
					ref remainingTime,
					out bool completed,
					out int completedEntryEdgeIndex))
				{
					return EAdvanceResult.InvalidGeometry;
				}

				if (!completed)
				{
					return EAdvanceResult.Moved;
				}

				ignoredEntryEdgeIndex =
					completedEntryEdgeIndex;

				transitionCount++;
				continue;
			}

			if (!TryGetTriangleWorldData(
				m_CurrentTriangleIndex,
				out Vector3 vertexA,
				out Vector3 vertexB,
				out Vector3 vertexC,
				out Vector3 currentNormal))
			{
				return EAdvanceResult.InvalidGeometry;
			}

			m_SurfacePoint =
				ProjectPointToPlane(
					m_SurfacePoint,
					vertexA,
					currentNormal);

			float currentSpeed =
				m_SurfaceVelocity.magnitude;

			if (currentSpeed <=
				m_MinimumCalculationValue)
			{
				m_SurfaceVelocity =
					Vector3.zero;

				return EAdvanceResult.Moved;
			}

			Vector3 tangentVelocity =
				Vector3.ProjectOnPlane(
					m_SurfaceVelocity,
					currentNormal);

			if (tangentVelocity.sqrMagnitude <=
				m_MinimumCalculationValue *
					m_MinimumCalculationValue)
			{
				m_SurfaceVelocity =
					Vector3.zero;

				return EAdvanceResult.Moved;
			}

			m_SurfaceVelocity =
				tangentVelocity.normalized *
					currentSpeed;

			Vector3 segmentStartPoint =
				m_SurfacePoint;

			Vector3 targetPoint =
				segmentStartPoint +
				m_SurfaceVelocity *
					remainingTime;

			if (!TryCalculateBarycentric(
				segmentStartPoint,
				vertexA,
				vertexB,
				vertexC,
				out Vector3 startBarycentric))
			{
				return EAdvanceResult.InvalidGeometry;
			}

			if (!TryCalculateBarycentric(
				targetPoint,
				vertexA,
				vertexB,
				vertexC,
				out Vector3 endBarycentric))
			{
				return EAdvanceResult.InvalidGeometry;
			}

			if (IsInsideTriangle(
				endBarycentric))
			{
				m_SurfaceDistanceTravelledThisStep +=
					currentSpeed *
					remainingTime;

				m_SurfacePoint =
					ProjectPointToPlane(
						targetPoint,
						vertexA,
						currentNormal);

				return EAdvanceResult.Moved;
			}

			if (!TryFindFirstSurfaceCrossing(
				startBarycentric,
				endBarycentric,
				ignoredEntryEdgeIndex,
				out int crossedEdgeIndex,
				out int simultaneousEdgeIndex,
				out float crossingFraction))
			{
				return EAdvanceResult.InvalidGeometry;
			}

			Vector3 crossingPoint;

			if (simultaneousEdgeIndex >=
					0 &&
				TryGetSharedTriangleVertex(
					crossedEdgeIndex,
					simultaneousEdgeIndex,
					vertexA,
					vertexB,
					vertexC,
					out Vector3 crossingVertex))
			{
				float distanceToVertex =
					Vector3.Distance(
						segmentStartPoint,
						crossingVertex);

				float timeToVertex =
					currentSpeed >
						m_MinimumCalculationValue
						? distanceToVertex /
							currentSpeed
						: 0.0f;

				timeToVertex =
					Mathf.Clamp(
						timeToVertex,
						0.0f,
						remainingTime);

				m_SurfaceDistanceTravelledThisStep +=
					currentSpeed *
						timeToVertex;

				crossingPoint =
					crossingVertex;

				crossingFraction =
					remainingTime >
						m_MinimumCalculationValue
						? timeToVertex /
							remainingTime
						: 0.0f;

				// 2本のEdgeへほぼ同時到達した時点で
				// EdgeではなくVertex Crossingとして処理します。
				if (TryBeginSymmetricConvexVertexTransition(
					crossingVertex,
					currentNormal,
					m_SurfaceVelocity))
				{
					m_DidTraverseConvexEdgeThisStep =
						true;

					m_SurfacePoint =
						crossingVertex;

					remainingTime -=
						timeToVertex;

					if (remainingTime <
						0.0f)
					{
						remainingTime =
							0.0f;
					}

					transitionCount++;
					continue;
				}
			}
			else
			{
				float timeToCrossing =
					remainingTime *
						Mathf.Clamp01(
							crossingFraction);

				m_SurfaceDistanceTravelledThisStep +=
					currentSpeed *
						timeToCrossing;

				crossingPoint =
					Vector3.LerpUnclamped(
						segmentStartPoint,
						targetPoint,
						crossingFraction);

				crossingPoint =
					ProjectPointToPlane(
						crossingPoint,
						vertexA,
						currentNormal);
			}

			BallSurfaceMeshData.EEdgeType edgeType =
				m_MeshData.GetEdgeType(
					m_CurrentTriangleIndex,
					crossedEdgeIndex);

			SetLastEdge(
				m_CurrentTriangleIndex,
				crossedEdgeIndex,
				crossingPoint,
				edgeType);

			switch (edgeType)
			{
				case BallSurfaceMeshData.EEdgeType.Boundary:
					m_SurfacePoint =
						crossingPoint;

					return EAdvanceResult.BoundaryEdgeReached;

				case BallSurfaceMeshData.EEdgeType.Invalid:
				case BallSurfaceMeshData.EEdgeType.NonManifold:
					m_SurfacePoint =
						crossingPoint;

					return EAdvanceResult.UnsupportedEdgeReached;
			}

			if (!m_MeshData.TryGetNeighbor(
				m_CurrentTriangleIndex,
				crossedEdgeIndex,
				out int neighborTriangleIndex,
				out int neighborEdgeIndex))
			{
				m_SurfacePoint =
					crossingPoint;

				return EAdvanceResult.UnsupportedEdgeReached;
			}

			if (!TryGetTriangleWorldData(
				neighborTriangleIndex,
				out Vector3 neighborVertexA,
				out _,
				out _,
				out Vector3 neighborNormal))
			{
				return EAdvanceResult.InvalidGeometry;
			}

			if (edgeType ==
				BallSurfaceMeshData.EEdgeType.Concave)
			{
				// ConcaveではBall中心が隣面へ入り込む前に、
				// Sphere半径を考慮して隣面との衝突位置を求めます。
				if (TryHandleConcaveEdgeCollision(
					segmentStartPoint,
					targetPoint,
					crossingPoint,
					crossingFraction,
					currentNormal,
					neighborNormal,
					out float collisionFraction))
				{
					remainingTime *=
						1.0f -
							Mathf.Clamp01(
								collisionFraction);

					ignoredEntryEdgeIndex =
						-1;

					transitionCount++;
					continue;
				}

				// 現在の簡易衝突モデルで安全な衝突点を
				// 求められなかった場合は、従来どおり
				// Edge上で停止側へ倒します。
				m_SurfacePoint =
					crossingPoint;

				return EAdvanceResult.ConcaveEdgeReached;
			}

			if (edgeType ==
				BallSurfaceMeshData.EEdgeType.Convex)
			{
				m_DidTraverseConvexEdgeThisStep =
					true;

				m_SurfacePoint =
					crossingPoint;

				if (!BeginConvexEdgeTransition(
					crossedEdgeIndex,
					neighborTriangleIndex,
					neighborEdgeIndex,
					currentNormal,
					neighborNormal))
				{
					return EAdvanceResult.InvalidGeometry;
				}

				remainingTime *=
					1.0f -
						Mathf.Clamp01(
							crossingFraction);

				transitionCount++;
				continue;
			}

			// Flat / Smooth EdgeはSharpな折れではないため、
			// これまでどおり隣Triangleへ即座に移行します。
			if (edgeType !=
					BallSurfaceMeshData.EEdgeType.Flat &&
				edgeType !=
					BallSurfaceMeshData.EEdgeType.Smooth)
			{
				m_SurfacePoint =
					crossingPoint;

				return EAdvanceResult.UnsupportedEdgeReached;
			}

			float surfaceSpeed =
				m_SurfaceVelocity.magnitude;

			Quaternion transitionRotation =
				Quaternion.FromToRotation(
					currentNormal,
					neighborNormal);

			Vector3 nextVelocity =
				transitionRotation *
					m_SurfaceVelocity;

			nextVelocity =
				Vector3.ProjectOnPlane(
					nextVelocity,
					neighborNormal);

			if (nextVelocity.sqrMagnitude <=
				m_MinimumCalculationValue *
					m_MinimumCalculationValue)
			{
				return EAdvanceResult.InvalidGeometry;
			}

			nextVelocity =
				nextVelocity.normalized *
					surfaceSpeed;

			m_CurrentTriangleIndex =
				neighborTriangleIndex;

			m_SurfaceVelocity =
				nextVelocity;

			m_SurfacePoint =
				ProjectPointToPlane(
					crossingPoint,
					neighborVertexA,
					neighborNormal);

			remainingTime *=
				1.0f -
					Mathf.Clamp01(
						crossingFraction);

			ignoredEntryEdgeIndex =
				neighborEdgeIndex;

			transitionCount++;
		}

		return EAdvanceResult.Moved;
	}

	/// <summary>
	/// 現在面の角度二等分線からConvex Vertexへ進入した場合に、
	/// Vertexから伸びる3本目のConvex Edgeを進行先として設定します。
	/// </summary>
	/// <remarks>
	/// Cubeの頂点では3本のSharp Convex Edgeが接続しています。
	///
	/// 現在面上の2本:
	///   \ /
	///    ●  ← Vertex
	///
	/// この2本の角度二等分線へ向かって進入した場合、
	/// 左右どちらか一方の面を選ぶのではなく、
	/// 現在面から外へ伸びる3本目のEdgeを採用します。
	///
	/// この探索はVertex到達時だけ行うため、
	/// 毎Physics StepでMesh全体を走査することはありません。
	/// </remarks>
	private bool TryBeginSymmetricConvexVertexTransition(
		Vector3 vertexPoint,
		Vector3 currentNormal,
		Vector3 incomingSurfaceVelocity)
	{
		if (incomingSurfaceVelocity.sqrMagnitude <=
				m_MinimumCalculationValue *
					m_MinimumCalculationValue ||
			m_SurfaceSource == null ||
			m_MeshData == null)
		{
			return false;
		}

		Vector3 localVertexPoint =
			m_SurfaceSource.transform.InverseTransformPoint(
				vertexPoint);

		if (!m_MeshData.TryGetIncidentConvexEdges(
				localVertexPoint,
				out BallSurfaceMeshData.ConvexEdgeReference[] incidentEdges) ||
			incidentEdges == null ||
			incidentEdges.Length < 3)
		{
			return false;
		}

		int firstFaceEdgeIndex =
			-1;

		int secondFaceEdgeIndex =
			-1;

		Vector3 firstFaceDirection =
			Vector3.zero;

		Vector3 secondFaceDirection =
			Vector3.zero;

		for (int incidentIndex = 0;
			incidentIndex < incidentEdges.Length;
			incidentIndex++)
		{
			Vector3 otherWorldPoint =
				m_SurfaceSource.transform.TransformPoint(
					incidentEdges[
						incidentIndex].OtherLocalVertex);

			Vector3 direction =
				otherWorldPoint -
					vertexPoint;

			if (direction.sqrMagnitude <=
				m_MinimumCalculationValue *
					m_MinimumCalculationValue)
			{
				continue;
			}

			direction.Normalize();

			float normalDot =
				Mathf.Abs(
					Vector3.Dot(
						direction,
						currentNormal));

			if (normalDot >
				m_VertexFaceEdgeNormalDotTolerance)
			{
				continue;
			}

			if (firstFaceEdgeIndex < 0)
			{
				firstFaceEdgeIndex =
					incidentIndex;

				firstFaceDirection =
					direction;

				continue;
			}

			if (secondFaceEdgeIndex < 0)
			{
				secondFaceEdgeIndex =
					incidentIndex;

				secondFaceDirection =
					direction;

				break;
			}
		}

		if (firstFaceEdgeIndex < 0 ||
			secondFaceEdgeIndex < 0)
		{
			return false;
		}

		Vector3 expectedIncomingDirection =
			-(
				firstFaceDirection +
				secondFaceDirection
			);

		if (expectedIncomingDirection.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return false;
		}

		expectedIncomingDirection.Normalize();

		Vector3 actualIncomingDirection =
			Vector3.ProjectOnPlane(
				incomingSurfaceVelocity,
				currentNormal);

		if (actualIncomingDirection.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return false;
		}

		actualIncomingDirection.Normalize();

		float bisectorAngle =
			Vector3.Angle(
				actualIncomingDirection,
				expectedIncomingDirection);

		if (bisectorAngle >
			m_VertexBisectorMaximumAngle)
		{
			return false;
		}

		int outgoingCandidateIndex =
			-1;

		float bestOutgoingAlignment =
			float.NegativeInfinity;

		for (int incidentIndex = 0;
			incidentIndex < incidentEdges.Length;
			incidentIndex++)
		{
			if (incidentIndex ==
					firstFaceEdgeIndex ||
				incidentIndex ==
					secondFaceEdgeIndex)
			{
				continue;
			}

			Vector3 otherWorldPoint =
				m_SurfaceSource.transform.TransformPoint(
					incidentEdges[
						incidentIndex].OtherLocalVertex);

			Vector3 direction =
				otherWorldPoint -
					vertexPoint;

			if (direction.sqrMagnitude <=
				m_MinimumCalculationValue *
					m_MinimumCalculationValue)
			{
				continue;
			}

			direction.Normalize();

			float alignment =
				Vector3.Dot(
					direction,
					-currentNormal);

			if (alignment <=
				bestOutgoingAlignment)
			{
				continue;
			}

			bestOutgoingAlignment =
				alignment;

			outgoingCandidateIndex =
				incidentIndex;
		}

		if (outgoingCandidateIndex < 0)
		{
			return false;
		}

		BallSurfaceMeshData.ConvexEdgeReference outgoingEdge =
			incidentEdges[
				outgoingCandidateIndex];

		if (!m_MeshData.TryGetNeighbor(
			outgoingEdge.TriangleIndex,
			outgoingEdge.EdgeIndex,
			out int neighborTriangleIndex,
			out _))
		{
			return false;
		}

		if (!TryGetTriangleWorldData(
				outgoingEdge.TriangleIndex,
				out _,
				out _,
				out _,
				out Vector3 firstOutgoingNormal) ||
			!TryGetTriangleWorldData(
				neighborTriangleIndex,
				out _,
				out _,
				out _,
				out Vector3 secondOutgoingNormal))
		{
			return false;
		}

		Vector3 targetEdgeNormal =
			firstOutgoingNormal +
				secondOutgoingNormal;

		if (targetEdgeNormal.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return false;
		}

		targetEdgeNormal.Normalize();

		Vector3 outgoingEdgeEnd =
			m_SurfaceSource.transform.TransformPoint(
				outgoingEdge.OtherLocalVertex);

		Vector3 outgoingDirection =
			outgoingEdgeEnd -
				vertexPoint;

		if (outgoingDirection.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return false;
		}

		outgoingDirection.Normalize();

		Vector3 rotationAxis =
			Vector3.Cross(
				currentNormal,
				targetEdgeNormal);

		float totalAngle =
			Vector3.Angle(
				currentNormal,
				targetEdgeNormal);

		float retainedSpeed =
			incomingSurfaceVelocity.magnitude *
				m_ConvexEdgeSpeedRetention;

		if (retainedSpeed <=
			m_MinimumCalculationValue)
		{
			return false;
		}

		m_SurfaceVelocity =
			outgoingDirection *
				retainedSpeed;

		m_ConvexVertexPoint =
			vertexPoint;

		m_ConvexVertexSourceNormal =
			currentNormal.normalized;

		m_ConvexVertexTargetNormal =
			targetEdgeNormal;

		m_ConvexVertexOutgoingEdgeDirection =
			outgoingDirection;

		m_ConvexVertexOutgoingEdgeEnd =
			outgoingEdgeEnd;

		if (totalAngle <=
				m_MinimumCalculationValue ||
			rotationAxis.sqrMagnitude <=
				m_MinimumCalculationValue *
					m_MinimumCalculationValue)
		{
			m_IsConvexVertexTransition =
				false;

			m_IsConvexVertexEdgePath =
				true;

			m_ConvexVertexEdgeNormal =
				targetEdgeNormal;

			m_ConvexVertexEdgeDirection =
				outgoingDirection;

			m_ConvexVertexEdgeEnd =
				outgoingEdgeEnd;

			return true;
		}

		rotationAxis.Normalize();

		m_IsConvexVertexTransition =
			true;

		m_IsConvexVertexEdgePath =
			false;

		m_ConvexVertexRotationAxis =
			rotationAxis;

		m_ConvexVertexSignedAngle =
			Vector3.SignedAngle(
				m_ConvexVertexSourceNormal,
				m_ConvexVertexTargetNormal,
				m_ConvexVertexRotationAxis);

		m_ConvexVertexCurrentAngle =
			0.0f;

		return true;
	}

	/// <summary>
	/// Convex Vertexから3本目のEdgeへ向くための
	/// World表示上の回り込みを進めます。
	/// </summary>
	private bool AdvanceConvexVertexTransition(
		ref float remainingTime,
		out bool completed)
	{
		completed =
			false;

		if (!m_IsConvexVertexTransition)
		{
			completed =
				true;

			return true;
		}

		float surfaceSpeed =
			m_SurfaceVelocity.magnitude;

		if (surfaceSpeed <=
			m_MinimumCalculationValue)
		{
			return false;
		}

		float offsetRadius =
			Mathf.Max(
				m_BallRadius +
					m_Height,
				m_MinimumCalculationValue);

		float totalAngleMagnitude =
			Mathf.Abs(
				m_ConvexVertexSignedAngle);

		float currentAngleMagnitude =
			Mathf.Abs(
				m_ConvexVertexCurrentAngle);

		float remainingAngle =
			Mathf.Max(
				0.0f,
				totalAngleMagnitude -
					currentAngleMagnitude);

		if (remainingAngle <=
			m_MinimumCalculationValue)
		{
			CompleteConvexVertexTransition();

			completed =
				true;

			return true;
		}

		float angularSpeed =
			surfaceSpeed /
				offsetRadius *
			Mathf.Rad2Deg;

		if (angularSpeed <=
			m_MinimumCalculationValue)
		{
			return false;
		}

		float timeToComplete =
			remainingAngle /
				angularSpeed;

		float stepTime =
			Mathf.Min(
				remainingTime,
				timeToComplete);

		m_SurfaceDistanceTravelledThisStep +=
			surfaceSpeed *
				stepTime;

		float angleStep =
			angularSpeed *
				stepTime *
				Mathf.Sign(
					m_ConvexVertexSignedAngle);

		m_ConvexVertexCurrentAngle +=
			angleStep;

		remainingTime -=
			stepTime;

		if (remainingTime <
			0.0f)
		{
			remainingTime =
				0.0f;
		}

		bool reachedTarget =
			timeToComplete <=
			stepTime +
				m_MinimumCalculationValue;

		if (!reachedTarget)
		{
			return true;
		}

		m_ConvexVertexCurrentAngle =
			m_ConvexVertexSignedAngle;

		CompleteConvexVertexTransition();

		completed =
			true;

		return true;
	}

	/// <summary>
	/// Vertex回り込みを終了し、
	/// 3本目のConvex EdgeをSurface基準線として使用します。
	/// </summary>
	private void CompleteConvexVertexTransition()
	{
		m_IsConvexVertexTransition =
			false;

		m_IsConvexVertexEdgePath =
			true;

		m_SurfacePoint =
			m_ConvexVertexPoint;

		m_ConvexVertexEdgeNormal =
			m_ConvexVertexTargetNormal;

		m_ConvexVertexEdgeDirection =
			m_ConvexVertexOutgoingEdgeDirection;

		m_ConvexVertexEdgeEnd =
			m_ConvexVertexOutgoingEdgeEnd;

		m_SurfaceVelocity =
			m_ConvexVertexEdgeDirection *
				m_SurfaceVelocity.magnitude;
	}

	/// <summary>
	/// Vertexから伸びるConvex Edge上を進めます。
	/// </summary>
	private void AdvanceConvexVertexEdgePath(
		float deltaTime)
	{
		if (!m_IsConvexVertexEdgePath ||
			deltaTime <=
				0.0f)
		{
			return;
		}

		float speed =
			m_SurfaceVelocity.magnitude;

		if (speed <=
			m_MinimumCalculationValue)
		{
			m_SurfaceVelocity =
				Vector3.zero;

			return;
		}

		Vector3 toEdgeEnd =
			m_ConvexVertexEdgeEnd -
				m_SurfacePoint;

		float remainingEdgeDistance =
			Vector3.Dot(
				toEdgeEnd,
				m_ConvexVertexEdgeDirection);

		if (remainingEdgeDistance <=
			m_MinimumCalculationValue)
		{
			m_SurfacePoint =
				m_ConvexVertexEdgeEnd;

			m_SurfaceVelocity =
				Vector3.zero;

			return;
		}

		float requestedDistance =
			speed *
				deltaTime;

		float travelledDistance =
			Mathf.Min(
				requestedDistance,
				remainingEdgeDistance);

		m_SurfacePoint +=
			m_ConvexVertexEdgeDirection *
				travelledDistance;

		m_SurfaceDistanceTravelledThisStep +=
			travelledDistance;

		if (requestedDistance >=
			remainingEdgeDistance)
		{
			m_SurfacePoint =
				m_ConvexVertexEdgeEnd;

			m_SurfaceVelocity =
				Vector3.zero;
		}
	}

	/// <summary>
	/// 現在のVertex Transition Normalを取得します。
	/// </summary>
	private Vector3 GetConvexVertexTransitionNormal()
	{
		Quaternion rotation =
			Quaternion.AngleAxis(
				m_ConvexVertexCurrentAngle,
				m_ConvexVertexRotationAxis);

		Vector3 transitionNormal =
			rotation *
				m_ConvexVertexSourceNormal;

		if (transitionNormal.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return m_ConvexVertexSourceNormal;
		}

		return transitionNormal.normalized;
	}

	/// <summary>
	/// Vertex Transition中のBall中心Offsetを、
	/// 既存Convex Edgeと同じ潰したBezierで取得します。
	/// </summary>
	private Vector3 GetFlattenedConvexVertexBallCenterOffset()
	{
		float offsetRadius =
			Mathf.Max(
				m_BallRadius +
					m_Height,
				m_MinimumCalculationValue);

		float totalAngleMagnitude =
			Mathf.Abs(
				m_ConvexVertexSignedAngle);

		if (totalAngleMagnitude <=
			m_MinimumCalculationValue)
		{
			return m_ConvexVertexSourceNormal *
				offsetRadius;
		}

		float progress =
			Mathf.Clamp01(
				Mathf.Abs(
					m_ConvexVertexCurrentAngle) /
				totalAngleMagnitude);

		float angleSign =
			Mathf.Sign(
				m_ConvexVertexSignedAngle);

		Vector3 sourceTangent =
			Vector3.Cross(
				m_ConvexVertexRotationAxis,
				m_ConvexVertexSourceNormal) *
			angleSign;

		Vector3 targetTangent =
			Vector3.Cross(
				m_ConvexVertexRotationAxis,
				m_ConvexVertexTargetNormal) *
			angleSign;

		if (sourceTangent.sqrMagnitude <=
				m_MinimumCalculationValue *
					m_MinimumCalculationValue ||
			targetTangent.sqrMagnitude <=
				m_MinimumCalculationValue *
					m_MinimumCalculationValue)
		{
			return GetConvexVertexTransitionNormal() *
				offsetRadius;
		}

		sourceTangent.Normalize();
		targetTangent.Normalize();

		float clampedAngleDegrees =
			Mathf.Min(
				totalAngleMagnitude,
				179.0f);

		float angleRadians =
			clampedAngleDegrees *
				Mathf.Deg2Rad;

		float circularHandleLength =
			4.0f /
				3.0f *
			Mathf.Tan(
				angleRadians *
					0.25f) *
			offsetRadius;

		float flattenedHandleLength =
			circularHandleLength *
				m_ConvexCurveFlatten;

		Vector3 point0 =
			m_ConvexVertexSourceNormal *
				offsetRadius;

		Vector3 point1 =
			point0 +
			sourceTangent *
				flattenedHandleLength;

		Vector3 point3 =
			m_ConvexVertexTargetNormal *
				offsetRadius;

		Vector3 point2 =
			point3 -
			targetTangent *
				flattenedHandleLength;

		return EvaluateCubicBezier(
			point0,
			point1,
			point2,
			point3,
			progress);
	}

	/// <summary>
	/// Sharp Convex Edgeの円弧移動を開始します。
	/// </summary>
	private bool BeginConvexEdgeTransition(
		int sourceEdgeIndex,
		int targetTriangleIndex,
		int targetEdgeIndex,
		Vector3 sourceNormal,
		Vector3 targetNormal)
	{
		if (!TryGetEdgeWorldVertices(
			m_CurrentTriangleIndex,
			sourceEdgeIndex,
			out Vector3 edgeStart,
			out Vector3 edgeEnd))
		{
			return false;
		}

		Vector3 edgeVector =
			edgeEnd -
				edgeStart;

		if (edgeVector.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return false;
		}

		Vector3 edgeDirection =
			edgeVector.normalized;

		float signedAngle =
			Vector3.SignedAngle(
				sourceNormal,
				targetNormal,
				edgeDirection);

		if (Mathf.Abs(
			signedAngle) <=
			m_MinimumCalculationValue)
		{
			return false;
		}

		float angleSign =
			Mathf.Sign(
				signedAngle);

		Vector3 crossDirection =
			Vector3.Cross(
				edgeDirection,
				sourceNormal) *
			angleSign;

		if (crossDirection.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return false;
		}

		crossDirection.Normalize();

		float alongSpeed =
			Vector3.Dot(
				m_SurfaceVelocity,
				edgeDirection);

		Vector3 perpendicularVelocity =
			m_SurfaceVelocity -
			edgeDirection *
				alongSpeed;

		float crossSpeed =
			perpendicularVelocity.magnitude;

		if (crossSpeed <=
			m_MinimumCalculationValue)
		{
			return false;
		}

		// MeshのWindingや数値誤差によって
		// 交差方向と円弧方向が反対になっていないか確認します。
		if (Vector3.Dot(
			perpendicularVelocity,
			crossDirection) <
			0.0f)
		{
			return false;
		}

		// Sharp Convex Edgeへ入った瞬間だけ、
		// Surface方向の速度を少し落とします。
		//
		// 円弧移動中に毎Frame減速するのではないため、
		// 一度の角通過で連続的に速度が失われることはありません。
		float retainedAlongSpeed =
			alongSpeed *
			m_ConvexEdgeSpeedRetention;

		float retainedCrossSpeed =
			crossSpeed *
			m_ConvexEdgeSpeedRetention;

		m_SurfaceVelocity *=
			m_ConvexEdgeSpeedRetention;

		m_IsConvexEdgeTransition =
			true;

		m_ConvexTargetTriangleIndex =
			targetTriangleIndex;

		m_ConvexTargetEdgeIndex =
			targetEdgeIndex;

		m_ConvexEdgeDirection =
			edgeDirection;

		// 円弧中にSurfacePointが本物のEdge端を越えないよう、
		// EdgeのWorld端点を保持します。
		m_ConvexEdgeStartPoint =
			edgeStart;

		m_ConvexEdgeEndPoint =
			edgeEnd;

		m_ConvexSourceNormal =
			sourceNormal.normalized;

		m_ConvexTargetNormal =
			targetNormal.normalized;

		m_ConvexSignedAngle =
			signedAngle;

		m_ConvexCurrentAngle =
			0.0f;

		m_ConvexAlongSpeed =
			retainedAlongSpeed;

		m_ConvexCrossSpeed =
			retainedCrossSpeed;

		UpdateConvexTransitionVelocity();

		return true;
	}

	/// <summary>
	/// Sharp Convex Edgeの円弧移動を進めます。
	/// </summary>
	/// <remarks>
	/// 円弧完了までの時間だけでなく、
	/// Edge方向速度によって実Edge端のVertexへ到達する時間も計算します。
	///
	/// Vertexへ先に到達した場合はSurfacePointをVertexへ固定し、
	/// Edge延長線へ出る前にVertex Transitionへ切り替えます。
	/// </remarks>
	private bool AdvanceConvexEdgeTransition(
		ref float remainingTime,
		out bool completed,
		out int completedEntryEdgeIndex)
	{
		completed =
			false;

		completedEntryEdgeIndex =
			-1;

		if (!m_IsConvexEdgeTransition)
		{
			completed =
				true;

			return true;
		}

		if (m_ConvexCrossSpeed <=
			m_MinimumCalculationValue)
		{
			return false;
		}

		float offsetRadius =
			Mathf.Max(
				m_BallRadius +
					m_Height,
				m_MinimumCalculationValue);

		float totalAngleMagnitude =
			Mathf.Abs(
				m_ConvexSignedAngle);

		float currentAngleMagnitude =
			Mathf.Abs(
				m_ConvexCurrentAngle);

		float remainingAngle =
			Mathf.Max(
				0.0f,
				totalAngleMagnitude -
					currentAngleMagnitude);

		if (remainingAngle <=
			m_MinimumCalculationValue)
		{
			return CompleteConvexEdgeTransition(
				out completedEntryEdgeIndex,
				out completed);
		}

		float angularSpeed =
			m_ConvexCrossSpeed /
				offsetRadius *
			Mathf.Rad2Deg;

		if (angularSpeed <=
			m_MinimumCalculationValue)
		{
			return false;
		}

		float timeToComplete =
			remainingAngle /
				angularSpeed;

		float timeToVertex =
			float.PositiveInfinity;

		Vector3 reachedVertexPoint =
			Vector3.zero;

		if (Mathf.Abs(
				m_ConvexAlongSpeed) >
			m_MinimumCalculationValue)
		{
			bool movesTowardEdgeEnd =
				m_ConvexAlongSpeed >
				0.0f;

			reachedVertexPoint =
				movesTowardEdgeEnd
					? m_ConvexEdgeEndPoint
					: m_ConvexEdgeStartPoint;

			Vector3 alongDirection =
				m_ConvexEdgeDirection *
				Mathf.Sign(
					m_ConvexAlongSpeed);

			float distanceToVertex =
				Vector3.Dot(
					reachedVertexPoint -
						m_SurfacePoint,
					alongDirection);

			if (distanceToVertex <=
				m_MinimumCalculationValue)
			{
				timeToVertex =
					0.0f;
			}
			else
			{
				timeToVertex =
					distanceToVertex /
					Mathf.Abs(
						m_ConvexAlongSpeed);
			}
		}

		float stepTime =
			Mathf.Min(
				remainingTime,
				Mathf.Min(
					timeToComplete,
					timeToVertex));

		float convexSurfaceSpeed =
			Mathf.Sqrt(
				m_ConvexAlongSpeed *
					m_ConvexAlongSpeed +
				m_ConvexCrossSpeed *
					m_ConvexCrossSpeed);

		m_SurfaceDistanceTravelledThisStep +=
			convexSurfaceSpeed *
				stepTime;

		m_SurfacePoint +=
			m_ConvexEdgeDirection *
				m_ConvexAlongSpeed *
				stepTime;

		float angleStep =
			angularSpeed *
				stepTime *
				Mathf.Sign(
					m_ConvexSignedAngle);

		m_ConvexCurrentAngle +=
			angleStep;

		remainingTime -=
			stepTime;

		if (remainingTime <
			0.0f)
		{
			remainingTime =
				0.0f;
		}

		bool reachedVertex =
			timeToVertex <=
				stepTime +
					m_MinimumCalculationValue &&
			timeToVertex <=
				timeToComplete +
					m_MinimumCalculationValue;

		if (reachedVertex)
		{
			m_SurfacePoint =
				reachedVertexPoint;

			Vector3 sourceCrossDirection =
				Vector3.Cross(
					m_ConvexEdgeDirection,
					m_ConvexSourceNormal) *
				Mathf.Sign(
					m_ConvexSignedAngle);

			if (sourceCrossDirection.sqrMagnitude >
				m_MinimumCalculationValue *
					m_MinimumCalculationValue)
			{
				sourceCrossDirection.Normalize();
			}

			Vector3 sourceSurfaceVelocity =
				m_ConvexEdgeDirection *
					m_ConvexAlongSpeed +
				sourceCrossDirection *
					m_ConvexCrossSpeed;

			Vector3 sourceNormal =
				m_ConvexSourceNormal;

			ResetConvexEdgeTransition();

			if (TryBeginSymmetricConvexVertexTransition(
				reachedVertexPoint,
				sourceNormal,
				sourceSurfaceVelocity))
			{
				m_DidTraverseConvexEdgeThisStep =
					true;

				completed =
					true;

				completedEntryEdgeIndex =
					-1;

				return true;
			}

			// Vertexとして進行先を安全に決められない場合も、
			// Edge延長線へ飛び出させず頂点で停止します。
			m_SurfaceVelocity =
				Vector3.zero;

			completed =
				false;

			return true;
		}

		bool reachedTarget =
			timeToComplete <=
				stepTime +
					m_MinimumCalculationValue;

		if (reachedTarget)
		{
			m_ConvexCurrentAngle =
				m_ConvexSignedAngle;
		}

		UpdateConvexTransitionVelocity();

		if (!reachedTarget)
		{
			return true;
		}

		return CompleteConvexEdgeTransition(
			out completedEntryEdgeIndex,
			out completed);
	}

	/// <summary>
	/// Convex Edgeの円弧移動を終了し、
	/// Neighbor Triangleへ移行します。
	/// </summary>
	private bool CompleteConvexEdgeTransition(
		out int completedEntryEdgeIndex,
		out bool completed)
	{
		completedEntryEdgeIndex =
			m_ConvexTargetEdgeIndex;

		completed =
			false;

		if (!TryGetTriangleWorldData(
			m_ConvexTargetTriangleIndex,
			out Vector3 targetVertexA,
			out _,
			out _,
			out Vector3 targetNormal))
		{
			return false;
		}

		m_CurrentTriangleIndex =
			m_ConvexTargetTriangleIndex;

		m_SurfacePoint =
			ProjectPointToPlane(
				m_SurfacePoint,
				targetVertexA,
				targetNormal);

		m_SurfaceVelocity =
			Vector3.ProjectOnPlane(
				m_SurfaceVelocity,
				targetNormal);

		float expectedSpeed =
			Mathf.Sqrt(
				m_ConvexAlongSpeed *
					m_ConvexAlongSpeed +
				m_ConvexCrossSpeed *
					m_ConvexCrossSpeed);

		if (expectedSpeed >
				m_MinimumCalculationValue &&
			m_SurfaceVelocity.sqrMagnitude >
				m_MinimumCalculationValue *
					m_MinimumCalculationValue)
		{
			m_SurfaceVelocity =
				m_SurfaceVelocity.normalized *
					expectedSpeed;
		}

		ResetConvexEdgeTransition();

		completed =
			true;

		return true;
	}

	/// <summary>
	/// 現在のConvex Edge円弧上の法線を取得します。
	/// </summary>
	private Vector3 GetConvexTransitionNormal()
	{
		Quaternion rotation =
			Quaternion.AngleAxis(
				m_ConvexCurrentAngle,
				m_ConvexEdgeDirection);

		Vector3 transitionNormal =
			rotation *
				m_ConvexSourceNormal;

		if (transitionNormal.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return m_ConvexSourceNormal;
		}

		return transitionNormal.normalized;
	}

	/// <summary>
	/// Convex Edge円弧上の現在速度を更新します。
	/// </summary>
	private void UpdateConvexTransitionVelocity()
	{
		Vector3 transitionNormal =
			GetConvexTransitionNormal();

		float angleSign =
			Mathf.Sign(
				m_ConvexSignedAngle);

		Vector3 crossDirection =
			Vector3.Cross(
				m_ConvexEdgeDirection,
				transitionNormal) *
			angleSign;

		if (crossDirection.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return;
		}

		crossDirection.Normalize();

		m_SurfaceVelocity =
			m_ConvexEdgeDirection *
				m_ConvexAlongSpeed +
			crossDirection *
				m_ConvexCrossSpeed;
	}

	/// <summary>
	/// Concave Edgeで隣面へSphereが入り込む前の
	/// 実際の衝突位置を求めて反射します。
	/// </summary>
	private bool TryHandleConcaveEdgeCollision(
		Vector3 segmentStartPoint,
		Vector3 targetPoint,
		Vector3 edgePoint,
		float crossingFraction,
		Vector3 currentNormal,
		Vector3 neighborNormal,
		out float collisionFraction)
	{
		collisionFraction =
			0.0f;

		float centerOffset =
			m_BallRadius +
				m_Height;

		Vector3 startCenter =
			segmentStartPoint +
			currentNormal *
				centerOffset;

		Vector3 edgeCenter =
			edgePoint +
			currentNormal *
				centerOffset;

		float startClearance =
			Vector3.Dot(
				startCenter -
					edgePoint,
				neighborNormal) -
			m_BallRadius;

		float edgeClearance =
			Vector3.Dot(
				edgeCenter -
					edgePoint,
				neighborNormal) -
			m_BallRadius;

		// Edge地点まで進んでもBall半径分の空間が残っている場合は、
		// この簡易Concave衝突では処理しません。
		if (edgeClearance >
			m_MinimumCalculationValue)
		{
			return false;
		}

		float fractionToEdge;

		if (startClearance <=
			m_MinimumCalculationValue)
		{
			fractionToEdge =
				0.0f;
		}
		else
		{
			float clearanceDifference =
				startClearance -
					edgeClearance;

			if (clearanceDifference <=
				m_MinimumCalculationValue)
			{
				return false;
			}

			fractionToEdge =
				Mathf.Clamp01(
					startClearance /
						clearanceDifference);
		}

		collisionFraction =
			Mathf.Clamp01(
				crossingFraction *
					fractionToEdge);

		Vector3 collisionSurfacePoint =
			Vector3.LerpUnclamped(
				segmentStartPoint,
				targetPoint,
				collisionFraction);

		collisionSurfacePoint =
			ProjectPointToPlane(
				collisionSurfacePoint,
				segmentStartPoint,
				currentNormal);

		Vector3 worldVelocity =
			m_SurfaceVelocity +
			currentNormal *
				m_VerticalVelocity;

		float incomingNormalSpeed =
			Vector3.Dot(
				worldVelocity,
				neighborNormal);

		if (incomingNormalSpeed >=
			-m_MinimumCalculationValue)
		{
			return false;
		}

		Vector3 normalVelocity =
			neighborNormal *
				incomingNormalSpeed;

		Vector3 tangentialVelocity =
			worldVelocity -
				normalVelocity;

		Vector3 reflectedVelocity =
			tangentialVelocity *
				m_BounceTangentialRetention +
			neighborNormal *
				(
					-incomingNormalSpeed *
					m_BounceNormalRetention
				);

		m_SurfacePoint =
			collisionSurfacePoint;

		m_VerticalVelocity =
			Vector3.Dot(
				reflectedVelocity,
				currentNormal);

		m_SurfaceVelocity =
			Vector3.ProjectOnPlane(
				reflectedVelocity,
				currentNormal);

		if (m_Height <=
				m_MinimumHeight &&
			m_VerticalVelocity <
				0.0f)
		{
			m_VerticalVelocity =
				0.0f;
		}

		// 次の反復で同じPlaneへ数値誤差で再侵入しないよう、
		// Source Surface上で隣面の外向き方向へ微小に戻します。
		Vector3 separationDirection =
			Vector3.ProjectOnPlane(
				neighborNormal,
				currentNormal);

		if (separationDirection.sqrMagnitude >
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			m_SurfacePoint +=
				separationDirection.normalized *
					m_BarycentricTolerance;
		}

		return true;
	}

	/// <summary>
	/// 指定EdgeのWorld座標を取得します。
	/// </summary>
	private bool TryGetEdgeWorldVertices(
		int triangleIndex,
		int edgeIndex,
		out Vector3 edgeStart,
		out Vector3 edgeEnd)
	{
		edgeStart =
			Vector3.zero;

		edgeEnd =
			Vector3.zero;

		if (m_SurfaceSource == null ||
			m_MeshData == null)
		{
			return false;
		}

		m_MeshData.GetEdgeLocalVertices(
			triangleIndex,
			edgeIndex,
			out Vector3 localEdgeStart,
			out Vector3 localEdgeEnd);

		Transform surfaceTransform =
			m_SurfaceSource.transform;

		edgeStart =
			surfaceTransform.TransformPoint(
				localEdgeStart);

		edgeEnd =
			surfaceTransform.TransformPoint(
				localEdgeEnd);

		return (
			edgeEnd -
				edgeStart
			).sqrMagnitude >
			m_MinimumCalculationValue *
				m_MinimumCalculationValue;
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

		// Vertex Transition / Edge Pathは
		// m_SurfaceVelocityそのものを速度として使用するため、
		// 上の共通Scaleだけで同期します。
	}

	/// <summary>
	/// Convex Edge遷移状態を初期化します。
	/// </summary>
	private void ResetConvexEdgeTransition()
	{
		m_IsConvexEdgeTransition =
			false;

		m_ConvexTargetTriangleIndex =
			-1;

		m_ConvexTargetEdgeIndex =
			-1;

		m_ConvexEdgeDirection =
			Vector3.zero;

		m_ConvexEdgeStartPoint =
			Vector3.zero;

		m_ConvexEdgeEndPoint =
			Vector3.zero;

		m_ConvexSourceNormal =
			Vector3.zero;

		m_ConvexTargetNormal =
			Vector3.zero;

		m_ConvexSignedAngle =
			0.0f;

		m_ConvexCurrentAngle =
			0.0f;

		m_ConvexAlongSpeed =
			0.0f;

		m_ConvexCrossSpeed =
			0.0f;
	}

	/// <summary>
	/// Convex Vertex関連の一時状態を初期化します。
	/// </summary>
	private void ResetConvexVertexTransition()
	{
		m_IsConvexVertexTransition =
			false;

		m_ConvexVertexPoint =
			Vector3.zero;

		m_ConvexVertexSourceNormal =
			Vector3.zero;

		m_ConvexVertexTargetNormal =
			Vector3.zero;

		m_ConvexVertexRotationAxis =
			Vector3.zero;

		m_ConvexVertexSignedAngle =
			0.0f;

		m_ConvexVertexCurrentAngle =
			0.0f;

		m_ConvexVertexOutgoingEdgeDirection =
			Vector3.zero;

		m_ConvexVertexOutgoingEdgeEnd =
			Vector3.zero;

		m_IsConvexVertexEdgePath =
			false;

		m_ConvexVertexEdgeNormal =
			Vector3.zero;

		m_ConvexVertexEdgeDirection =
			Vector3.zero;

		m_ConvexVertexEdgeEnd =
			Vector3.zero;
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
	/// Sharp Convex Edgeでは円弧そのものではなく、
	/// 円弧を少し潰した3次Bezierカーブ上のOffsetを返します。
	/// 物理上のSurface速度・Height・バウンド時間には影響せず、
	/// World上で見えるBall中心の軌道だけを変更します。
	/// </remarks>
	public bool TryGetCurrentBallCenterOffset(
		out Vector3 ballCenterOffset)
	{
		ballCenterOffset =
			Vector3.zero;

		if (!m_IsInitialized)
		{
			return false;
		}

		if (m_IsConvexVertexTransition)
		{
			ballCenterOffset =
				GetFlattenedConvexVertexBallCenterOffset();

			return ballCenterOffset.sqrMagnitude >
				m_MinimumCalculationValue *
					m_MinimumCalculationValue;
		}

		if (m_IsConvexEdgeTransition)
		{
			ballCenterOffset =
				GetFlattenedConvexBallCenterOffset();

			return ballCenterOffset.sqrMagnitude >
				m_MinimumCalculationValue *
					m_MinimumCalculationValue;
		}

		if (!TryGetCurrentSurfaceNormal(
			out Vector3 surfaceNormal))
		{
			return false;
		}

		float offsetDistance =
			Mathf.Max(
				m_BallRadius +
					m_Height,
				m_MinimumCalculationValue);

		ballCenterOffset =
			surfaceNormal *
				offsetDistance;

		return true;
	}

	/// <summary>
	/// Sharp Convex Edge上のBall中心Offsetを、
	/// 円弧より少し内側へ潰したBezierカーブとして取得します。
	/// </summary>
	private Vector3 GetFlattenedConvexBallCenterOffset()
	{
		float offsetRadius =
			Mathf.Max(
				m_BallRadius +
					m_Height,
				m_MinimumCalculationValue);

		float totalAngleMagnitude =
			Mathf.Abs(
				m_ConvexSignedAngle);

		if (totalAngleMagnitude <=
			m_MinimumCalculationValue)
		{
			return m_ConvexSourceNormal *
				offsetRadius;
		}

		float progress =
			Mathf.Clamp01(
				Mathf.Abs(
					m_ConvexCurrentAngle) /
				totalAngleMagnitude);

		float angleSign =
			Mathf.Sign(
				m_ConvexSignedAngle);

		Vector3 sourceTangent =
			Vector3.Cross(
				m_ConvexEdgeDirection,
				m_ConvexSourceNormal) *
			angleSign;

		Vector3 targetTangent =
			Vector3.Cross(
				m_ConvexEdgeDirection,
				m_ConvexTargetNormal) *
			angleSign;

		if (sourceTangent.sqrMagnitude <=
				m_MinimumCalculationValue *
					m_MinimumCalculationValue ||
			targetTangent.sqrMagnitude <=
				m_MinimumCalculationValue *
					m_MinimumCalculationValue)
		{
			return GetConvexTransitionNormal() *
				offsetRadius;
		}

		sourceTangent.Normalize();
		targetTangent.Normalize();

		// 円弧を3次Bezierで近似するときの標準Handle長を求め、
		// Flatten倍率で短くすることで中央をEdge側へ潰します。
		float clampedAngleDegrees =
			Mathf.Min(
				totalAngleMagnitude,
				179.0f);

		float angleRadians =
			clampedAngleDegrees *
			Mathf.Deg2Rad;

		float circularHandleLength =
			4.0f /
				3.0f *
			Mathf.Tan(
				angleRadians *
					0.25f) *
			offsetRadius;

		float flattenedHandleLength =
			circularHandleLength *
			m_ConvexCurveFlatten;

		Vector3 point0 =
			m_ConvexSourceNormal *
				offsetRadius;

		Vector3 point1 =
			point0 +
			sourceTangent *
				flattenedHandleLength;

		Vector3 point3 =
			m_ConvexTargetNormal *
				offsetRadius;

		Vector3 point2 =
			point3 -
			targetTangent *
				flattenedHandleLength;

		return EvaluateCubicBezier(
			point0,
			point1,
			point2,
			point3,
			progress);
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

		if (m_IsConvexVertexTransition)
		{
			surfaceNormal =
				GetConvexVertexTransitionNormal();

			return surfaceNormal.sqrMagnitude >
				m_MinimumCalculationValue *
					m_MinimumCalculationValue;
		}

		if (m_IsConvexVertexEdgePath)
		{
			surfaceNormal =
				m_ConvexVertexEdgeNormal;

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

		m_DidTraverseConvexEdgeThisStep =
			false;

		m_SurfaceDistanceTravelledThisStep =
			0.0f;

		m_DidSurfaceImpactThisStep =
			false;

		m_BounceSequence =
			0;

		m_LastBounceSurfaceSpeed =
			0.0f;

		m_LastBounceVerticalSpeed =
			0.0f;

		ResetConvexEdgeTransition();

		ResetConvexVertexTransition();

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

	/// <summary>
	/// Triangle頂点に保存されたSmooth Normalを
	/// Barycentric補間し、現在位置のWorld Normalを取得します。
	/// </summary>
	private bool TryGetSmoothWorldNormal(
		int triangleIndex,
		Vector3 surfacePoint,
		out Vector3 surfaceNormal)
	{
		surfaceNormal =
			Vector3.zero;

		if (!TryGetTriangleWorldData(
			triangleIndex,
			out Vector3 vertexA,
			out Vector3 vertexB,
			out Vector3 vertexC,
			out Vector3 triangleNormal))
		{
			return false;
		}

		if (!TryCalculateBarycentric(
			surfacePoint,
			vertexA,
			vertexB,
			vertexC,
			out Vector3 barycentric))
		{
			return false;
		}

		m_MeshData.GetTriangleLocalCornerNormals(
			triangleIndex,
			out Vector3 localNormalA,
			out Vector3 localNormalB,
			out Vector3 localNormalC);

		Vector3 worldNormalA =
			TransformNormalToWorld(
				localNormalA);

		Vector3 worldNormalB =
			TransformNormalToWorld(
				localNormalB);

		Vector3 worldNormalC =
			TransformNormalToWorld(
				localNormalC);

		Vector3 interpolatedNormal =
			worldNormalA *
				barycentric.x +
			worldNormalB *
				barycentric.y +
			worldNormalC *
				barycentric.z;

		if (interpolatedNormal.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			surfaceNormal =
				triangleNormal;

			return true;
		}

		surfaceNormal =
			interpolatedNormal.normalized;

		// Winding不整合や数値誤差で内向きになった場合は
		// 幾何Triangle法線と同じ側へそろえます。
		if (Vector3.Dot(
			surfaceNormal,
			triangleNormal) <
			0.0f)
		{
			surfaceNormal =
				-surfaceNormal;
		}

		return true;
	}

	/// <summary>
	/// 非一様Scaleにも対応してLocal NormalをWorldへ変換します。
	/// </summary>
	private Vector3 TransformNormalToWorld(
		Vector3 localNormal)
	{
		if (localNormal.sqrMagnitude <=
			Mathf.Epsilon)
		{
			return Vector3.zero;
		}

		Matrix4x4 normalMatrix =
			m_SurfaceSource.transform.worldToLocalMatrix.transpose;

		Vector3 worldNormal =
			normalMatrix.MultiplyVector(
				localNormal);

		if (worldNormal.sqrMagnitude <=
			Mathf.Epsilon)
		{
			return Vector3.zero;
		}

		return worldNormal.normalized;
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

	private bool TryFindFirstSurfaceCrossing(
		Vector3 startBarycentric,
		Vector3 endBarycentric,
		int ignoredEntryEdgeIndex,
		out int crossedEdgeIndex,
		out int simultaneousEdgeIndex,
		out float crossingFraction)
	{
		crossedEdgeIndex =
			-1;

		simultaneousEdgeIndex =
			-1;

		crossingFraction =
			float.MaxValue;

		for (int barycentricIndex = 0;
			barycentricIndex < 3;
			barycentricIndex++)
		{
			float startCoordinate =
				GetBarycentricCoordinate(
					startBarycentric,
					barycentricIndex);

			float endCoordinate =
				GetBarycentricCoordinate(
					endBarycentric,
					barycentricIndex);

			if (endCoordinate >=
				-m_BarycentricTolerance)
			{
				continue;
			}

			startCoordinate =
				Mathf.Max(
					0.0f,
					startCoordinate);

			float denominator =
				startCoordinate -
					endCoordinate;

			if (denominator <=
				m_MinimumCalculationValue)
			{
				continue;
			}

			float fraction =
				Mathf.Clamp01(
					startCoordinate /
						denominator);

			int edgeIndex =
				ConvertBarycentricIndexToEdgeIndex(
					barycentricIndex);

			if (edgeIndex ==
					ignoredEntryEdgeIndex &&
				fraction <=
					m_EntryEdgeIgnoreFraction)
			{
				continue;
			}

			if (crossedEdgeIndex < 0 ||
				fraction <
					crossingFraction -
						m_VertexCrossingFractionTolerance)
			{
				crossingFraction =
					fraction;

				crossedEdgeIndex =
					edgeIndex;

				simultaneousEdgeIndex =
					-1;

				continue;
			}

			if (Mathf.Abs(
					fraction -
						crossingFraction) <=
				m_VertexCrossingFractionTolerance &&
				edgeIndex !=
					crossedEdgeIndex)
			{
				simultaneousEdgeIndex =
					edgeIndex;

				crossingFraction =
					Mathf.Min(
						crossingFraction,
						fraction);
			}
		}

		return crossedEdgeIndex >=
			0;
	}

	/// <summary>
	/// 同一Triangleの2本のEdgeが共有する頂点を取得します。
	/// </summary>
	private bool TryGetSharedTriangleVertex(
		int firstEdgeIndex,
		int secondEdgeIndex,
		Vector3 vertexA,
		Vector3 vertexB,
		Vector3 vertexC,
		out Vector3 sharedVertex)
	{
		sharedVertex =
			Vector3.zero;

		if (firstEdgeIndex ==
			secondEdgeIndex)
		{
			return false;
		}

		if ((firstEdgeIndex == 0 &&
				secondEdgeIndex == 1) ||
			(firstEdgeIndex == 1 &&
				secondEdgeIndex == 0))
		{
			sharedVertex =
				vertexB;

			return true;
		}

		if ((firstEdgeIndex == 1 &&
				secondEdgeIndex == 2) ||
			(firstEdgeIndex == 2 &&
				secondEdgeIndex == 1))
		{
			sharedVertex =
				vertexC;

			return true;
		}

		if ((firstEdgeIndex == 2 &&
				secondEdgeIndex == 0) ||
			(firstEdgeIndex == 0 &&
				secondEdgeIndex == 2))
		{
			sharedVertex =
				vertexA;

			return true;
		}

		return false;
	}

	private int ConvertBarycentricIndexToEdgeIndex(
		int barycentricIndex)
	{
		switch (barycentricIndex)
		{
			case 0:
				return 1;

			case 1:
				return 2;

			case 2:
				return 0;

			default:
				return -1;
		}
	}

	private float GetBarycentricCoordinate(
		Vector3 barycentric,
		int index)
	{
		switch (index)
		{
			case 0:
				return barycentric.x;

			case 1:
				return barycentric.y;

			case 2:
				return barycentric.z;

			default:
				return 0.0f;
		}
	}

	private void SetLastEdge(
		int triangleIndex,
		int edgeIndex,
		Vector3 edgePoint,
		BallSurfaceMeshData.EEdgeType edgeType)
	{
		m_LastEdgeTriangleIndex =
			triangleIndex;

		m_LastEdgeIndex =
			edgeIndex;

		m_LastEdgePoint =
			edgePoint;

		m_LastEdgeType =
			edgeType;
	}

	private void ClearLastEdge()
	{
		m_LastEdgeTriangleIndex =
			-1;

		m_LastEdgeIndex =
			-1;

		m_LastEdgePoint =
			Vector3.zero;

		m_LastEdgeType =
			BallSurfaceMeshData.EEdgeType.Boundary;
	}
}
