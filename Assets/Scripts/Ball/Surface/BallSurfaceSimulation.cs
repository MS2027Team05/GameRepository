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
				m_IsConvexVertexSpherePatch;

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
				!m_IsConvexVertexSpherePatch)
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

		if (!m_IsInitialized)
		{
			return false;
		}

		if (m_IsConvexVertexSpherePatch)
		{
			ballCenterOffset =
				GetConvexVertexSphereBallCenterOffset();

			return ballCenterOffset.sqrMagnitude >
				m_MinimumCalculationValue *
					m_MinimumCalculationValue;
		}

		if (m_IsConvexEdgeTransition)
		{
			ballCenterOffset =
				GetConvexBallCenterOffset();

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

		ResetConvexVertexSpherePatch();

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
