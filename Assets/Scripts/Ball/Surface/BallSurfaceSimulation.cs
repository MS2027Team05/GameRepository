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
				m_IsConvexEdgeTransition;

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
				!m_IsConvexEdgeTransition)
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
				m_SurfacePoint =
					ProjectPointToPlane(
						targetPoint,
						vertexA,
						currentNormal);

				return EAdvanceResult.Moved;
			}

			if (!TryFindFirstCrossedEdge(
				startBarycentric,
				endBarycentric,
				ignoredEntryEdgeIndex,
				out int crossedEdgeIndex,
				out float crossingFraction))
			{
				return EAdvanceResult.InvalidGeometry;
			}

			Vector3 crossingPoint =
				Vector3.LerpUnclamped(
					segmentStartPoint,
					targetPoint,
					crossingFraction);

			crossingPoint =
				ProjectPointToPlane(
					crossingPoint,
					vertexA,
					currentNormal);

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

		m_IsConvexEdgeTransition =
			true;

		m_ConvexTargetTriangleIndex =
			targetTriangleIndex;

		m_ConvexTargetEdgeIndex =
			targetEdgeIndex;

		m_ConvexEdgeDirection =
			edgeDirection;

		m_ConvexSourceNormal =
			sourceNormal.normalized;

		m_ConvexTargetNormal =
			targetNormal.normalized;

		m_ConvexSignedAngle =
			signedAngle;

		m_ConvexCurrentAngle =
			0.0f;

		m_ConvexAlongSpeed =
			alongSpeed;

		m_ConvexCrossSpeed =
			crossSpeed;

		UpdateConvexTransitionVelocity();

		return true;
	}

	/// <summary>
	/// Sharp Convex Edgeの円弧移動を進めます。
	/// </summary>
	/// <remarks>
	/// Ball中心は
	///
	/// EdgePoint + TransitionNormal * (BallRadius + Height)
	///
	/// に配置されます。
	///
	/// SurfacePointを共有Edge上に保ったまま
	/// TransitionNormalを回転させるため、
	/// Cubeの角でもWorld位置が連続します。
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

		float stepTime =
			Mathf.Min(
				remainingTime,
				timeToComplete);

		// Edge方向の速度成分は円弧移動中もそのまま進めます。
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

		if (!m_IsConvexEdgeTransition)
		{
			return;
		}

		m_ConvexAlongSpeed *=
			clampedScale;

		m_ConvexCrossSpeed *=
			clampedScale;
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

		ResetConvexEdgeTransition();

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

	private bool TryFindFirstCrossedEdge(
		Vector3 startBarycentric,
		Vector3 endBarycentric,
		int ignoredEntryEdgeIndex,
		out int crossedEdgeIndex,
		out float crossingFraction)
	{
		crossedEdgeIndex =
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
				startCoordinate /
					denominator;

			fraction =
				Mathf.Clamp01(
					fraction);

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

			if (fraction >=
				crossingFraction)
			{
				continue;
			}

			crossingFraction =
				fraction;

			crossedEdgeIndex =
				edgeIndex;
		}

		return crossedEdgeIndex >=
			0;
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
