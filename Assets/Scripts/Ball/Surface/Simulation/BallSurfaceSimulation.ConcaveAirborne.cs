using UnityEngine;

/// <summary>
/// 空中でConcave Edgeを越える間だけ、
/// Ball本体のWorld軌道とSurface上の基準点を分離して管理します。
/// </summary>
/// <remarks>
/// 通常のFlat / Smooth / Convex処理には入らず、
/// Heightがある状態でConcaveへ入った場合だけ使用します。
/// </remarks>
public sealed partial class BallSurfaceSimulation
{
	private bool m_IsConcaveAirborneTransfer;

	private Vector3 m_ConcaveBallCenter;
	private Vector3 m_ConcaveBallWorldVelocity;

	private Vector3 m_ConcaveReferenceVelocity;
	private Vector3 m_ConcaveReferenceNormal;
	private int m_ConcaveReferenceEntryEdgeIndex;

	/// <summary>
	/// 現在、Concave専用の空中遷移を開始すべき状態かを返します。
	/// </summary>
	private bool ShouldBeginConcaveAirborneTransfer()
	{
		return !m_IsConcaveAirborneTransfer &&
			(
				m_Height >
					m_MinimumHeight ||
				Mathf.Abs(
					m_VerticalVelocity) >
					m_MinimumCalculationValue
			);
	}

	/// <summary>
	/// 空中でConcave Edgeを越えた瞬間に専用遷移を開始します。
	/// </summary>
	/// <remarks>
	/// BallのWorld位置・World速度はEdge通過で変更しません。
	/// Surface上の基準点だけを隣Triangleへ移し、
	/// 以後はBallと基準点を別々に進めます。
	/// </remarks>
	private bool TryBeginConcaveAirborneTransfer(
		Vector3 edgePoint,
		int currentTriangleIndex,
		Vector3 currentTriangleNormal,
		int neighborTriangleIndex,
		int neighborEntryEdgeIndex,
		Vector3 neighborTriangleNormal)
	{
		if (!ShouldBeginConcaveAirborneTransfer())
		{
			return false;
		}

		if (currentTriangleNormal.sqrMagnitude <=
				m_MinimumCalculationValue *
					m_MinimumCalculationValue ||
			neighborTriangleNormal.sqrMagnitude <=
				m_MinimumCalculationValue *
					m_MinimumCalculationValue)
		{
			return false;
		}

		Vector3 sourceNormal =
			currentTriangleNormal.normalized;

		float centerOffset =
			Mathf.Max(
				m_BallRadius +
					m_Height,
				m_MinimumCalculationValue);

		// Concaveへ入った瞬間のBall中心は、
		// Smooth Normalから作り直しません。
		//
		// Edgeへ到達した時点の現在面Normalと
		// 既存HeightからWorld位置をそのまま引き継ぎます。
		Vector3 ballCenter =
			edgePoint +
			sourceNormal *
				centerOffset;

		// World速度も現在面のSurface / Vertical成分から
		// そのまま復元し、Concave切替だけでは方向を変えません。
		Vector3 ballWorldVelocity =
			m_SurfaceVelocity +
			sourceNormal *
				m_VerticalVelocity;

		if (!TryMapConcaveReferenceVelocity(
			m_SurfaceVelocity,
			neighborTriangleIndex,
			neighborEntryEdgeIndex,
			neighborTriangleNormal,
			out Vector3 neighborReferenceVelocity))
		{
			return false;
		}

		if (!TryGetTriangleWorldData(
			neighborTriangleIndex,
			out Vector3 neighborVertexA,
			out _,
			out _,
			out _))
		{
			return false;
		}

		m_IsConcaveAirborneTransfer =
			true;

		m_ConcaveBallCenter =
			ballCenter;

		m_ConcaveBallWorldVelocity =
			ballWorldVelocity;

		m_CurrentTriangleIndex =
			neighborTriangleIndex;

		m_SurfacePoint =
			ProjectPointToPlane(
				edgePoint,
				neighborVertexA,
				neighborTriangleNormal);

		m_ConcaveReferenceVelocity =
			neighborReferenceVelocity;

		m_ConcaveReferenceNormal =
			neighborTriangleNormal.normalized;

		m_ConcaveReferenceEntryEdgeIndex =
			neighborEntryEdgeIndex;

		SyncConcaveAirbornePublicState();

		return true;
	}

	/// <summary>
	/// Concave専用空中状態を1Step進めます。
	/// </summary>
	/// <remarks>
	/// Referenceが複数Triangleを越える場合も、
	/// Triangleごとの時間区間でBallとの接触を確認します。
	///
	/// これによりReferenceだけ先の面へ移動して、
	/// 途中の面へのBounceを取りこぼすことを防ぎます。
	/// </remarks>
	private EAdvanceResult AdvanceConcaveAirborneTransfer(
		float deltaTime)
	{
		if (!m_IsConcaveAirborneTransfer)
		{
			return EAdvanceResult.Moved;
		}

		if (deltaTime <=
			0.0f)
		{
			return EAdvanceResult.Moved;
		}

		float remainingTime =
			deltaTime;

		int transitionCount =
			0;

		while (remainingTime >
			m_MinimumCalculationValue)
		{
			if (transitionCount >=
				m_MaximumTriangleTransitionsPerStep)
			{
				return EAdvanceResult.TransitionLimitReached;
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

			float referenceSpeed =
				m_ConcaveReferenceVelocity.magnitude;

			if (referenceSpeed <=
				m_MinimumCalculationValue)
			{
				m_ConcaveReferenceVelocity =
					Vector3.zero;

				m_ConcaveReferenceNormal =
					currentNormal;

				if (TryAdvanceConcaveBallSegment(
					remainingTime,
					vertexA,
					vertexB,
					vertexC,
					currentNormal))
				{
					return EAdvanceResult.Moved;
				}

				remainingTime =
					0.0f;

				break;
			}

			Vector3 tangentVelocity =
				Vector3.ProjectOnPlane(
					m_ConcaveReferenceVelocity,
					currentNormal);

			if (tangentVelocity.sqrMagnitude <=
				m_MinimumCalculationValue *
					m_MinimumCalculationValue)
			{
				return EAdvanceResult.InvalidGeometry;
			}

			m_ConcaveReferenceVelocity =
				tangentVelocity.normalized *
					referenceSpeed;

			Vector3 startPoint =
				m_SurfacePoint;

			Vector3 targetPoint =
				startPoint +
				m_ConcaveReferenceVelocity *
					remainingTime;

			if (!TryCalculateBarycentric(
				startPoint,
				vertexA,
				vertexB,
				vertexC,
				out Vector3 startBarycentric) ||
				!TryCalculateBarycentric(
					targetPoint,
					vertexA,
					vertexB,
					vertexC,
					out Vector3 endBarycentric))
			{
				return EAdvanceResult.InvalidGeometry;
			}

			bool crossesEdge =
				!IsInsideTriangle(
					endBarycentric);

			float segmentFraction =
				1.0f;

			int crossedEdgeIndex =
				-1;

			int simultaneousEdgeIndex =
				-1;

			Vector3 segmentEndPoint =
				targetPoint;

			if (crossesEdge)
			{
				if (!TryFindFirstSurfaceCrossing(
					startBarycentric,
					endBarycentric,
					m_ConcaveReferenceEntryEdgeIndex,
					out crossedEdgeIndex,
					out simultaneousEdgeIndex,
					out segmentFraction))
				{
					return EAdvanceResult.InvalidGeometry;
				}

				if (simultaneousEdgeIndex >=
					0)
				{
					crossedEdgeIndex =
						ResolveSimultaneousVertexEdge(
							crossedEdgeIndex,
							simultaneousEdgeIndex);
				}

				segmentFraction =
					Mathf.Clamp01(
						segmentFraction);

				segmentEndPoint =
					Vector3.LerpUnclamped(
						startPoint,
						targetPoint,
						segmentFraction);

				segmentEndPoint =
					ProjectPointToPlane(
						segmentEndPoint,
						vertexA,
						currentNormal);
			}
			else
			{
				segmentEndPoint =
					ProjectPointToPlane(
						targetPoint,
						vertexA,
						currentNormal);
			}

			float segmentTime =
				remainingTime *
					segmentFraction;

			// Referenceが現在Triangle上を進んだ時間だけ、
			// BallもWorld空間で進めます。
			// この区間の接触先は必ず現在Triangleです。
			if (TryAdvanceConcaveBallSegment(
				segmentTime,
				vertexA,
				vertexB,
				vertexC,
				currentNormal))
			{
				return EAdvanceResult.Moved;
			}

			m_SurfacePoint =
				segmentEndPoint;

			m_ConcaveReferenceNormal =
				currentNormal;

			remainingTime -=
				segmentTime;

			if (!crossesEdge)
			{
				break;
			}

			BallSurfaceMeshData.EEdgeType edgeType =
				m_MeshData.GetEdgeType(
					m_CurrentTriangleIndex,
					crossedEdgeIndex);

			SetLastEdge(
				m_CurrentTriangleIndex,
				crossedEdgeIndex,
				segmentEndPoint,
				edgeType);

			if (edgeType ==
					BallSurfaceMeshData.EEdgeType.Boundary ||
				edgeType ==
					BallSurfaceMeshData.EEdgeType.Invalid ||
				edgeType ==
					BallSurfaceMeshData.EEdgeType.NonManifold)
			{
				return EAdvanceResult.ConcaveEdgeReached;
			}

			if (!m_MeshData.TryGetNeighbor(
				m_CurrentTriangleIndex,
				crossedEdgeIndex,
				out int neighborTriangleIndex,
				out int neighborEntryEdgeIndex))
			{
				return EAdvanceResult.ConcaveEdgeReached;
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

			// Ball本体のWorld速度は一切変更せず、
			// Reference速度だけを共有Edge基準で隣面へ写します。
			if (!TryMapConcaveReferenceVelocity(
				m_ConcaveReferenceVelocity,
				neighborTriangleIndex,
				neighborEntryEdgeIndex,
				neighborNormal,
				out Vector3 neighborReferenceVelocity))
			{
				return EAdvanceResult.InvalidGeometry;
			}

			m_CurrentTriangleIndex =
				neighborTriangleIndex;

			m_SurfacePoint =
				ProjectPointToPlane(
					segmentEndPoint,
					neighborVertexA,
					neighborNormal);

			m_ConcaveReferenceVelocity =
				neighborReferenceVelocity;

			m_ConcaveReferenceNormal =
				neighborNormal;

			m_ConcaveReferenceEntryEdgeIndex =
				neighborEntryEdgeIndex;

			transitionCount++;

			// crossingFractionが極端に0へ寄った場合の無限Loop防止です。
			if (segmentTime <=
				m_MinimumCalculationValue)
			{
				remainingTime =
					Mathf.Max(
						0.0f,
						remainingTime -
							m_MinimumCalculationValue);
			}
		}

		SyncConcaveAirbornePublicState();

		return EAdvanceResult.Moved;
	}

	/// <summary>
	/// Concave専用空中状態のBallだけをWorld空間で指定時間進め、
	/// 現在Triangleへの最初のSphere接触を確認します。
	/// </summary>
	/// <returns>
	/// 現在Triangleへ接触し通常Surface Physicsへ戻った場合はtrueです。
	/// </returns>
	private bool TryAdvanceConcaveBallSegment(
		float deltaTime,
		Vector3 vertexA,
		Vector3 vertexB,
		Vector3 vertexC,
		Vector3 triangleNormal)
	{
		if (deltaTime <=
			m_MinimumCalculationValue)
		{
			return false;
		}

		Vector3 startCenter =
			m_ConcaveBallCenter;

		Vector3 startVelocity =
			m_ConcaveBallWorldVelocity;

		// Ballの速度方向は強制変更せず、
		// Referenceが現在いるTriangleの幾何Normal方向へ
		// この区間の重力だけを適用します。
		Vector3 acceleration =
			-triangleNormal.normalized *
				m_GravityAcceleration;

		Vector3 endCenter =
			startCenter +
			startVelocity *
				deltaTime +
			0.5f *
				acceleration *
				deltaTime *
				deltaTime;

		Vector3 endVelocity =
			startVelocity +
			acceleration *
				deltaTime;

		if (TryGetConcaveSurfaceContact(
			startCenter,
			endCenter,
			startVelocity,
			acceleration,
			deltaTime,
			vertexA,
			vertexB,
			vertexC,
			triangleNormal,
			out Vector3 contactPoint,
			out Vector3 contactVelocity) &&
			Vector3.Dot(
				contactVelocity,
				triangleNormal) <
				-m_MinimumCalculationValue)
		{
			// Sharp Edge付近のSmooth Normal補間で
			// Bounce方向が斜めになることを避けるため、
			// 実際に接触したTriangleの幾何Normalを使用します。
			ApplyConcaveSurfaceBounce(
				contactPoint,
				triangleNormal,
				contactVelocity);

			ClearConcaveAirborneTransferState();

			return true;
		}

		m_ConcaveBallCenter =
			endCenter;

		m_ConcaveBallWorldVelocity =
			endVelocity;

		return false;
	}

	/// <summary>
	/// Reference速度を共有Edge基準で隣Triangleへ写します。
	/// </summary>
	/// <remarks>
	/// QuaternionでNormal同士を回転させるのではなく、
	///
	/// ・共有Edgeに沿うWorld成分
	/// ・Edgeを横切る速度量
	///
	/// に分離します。
	///
	/// Edge方向成分はWorld方向のまま維持し、
	/// 横断成分だけを隣Triangleの内側方向へ向けます。
	/// </remarks>
	private bool TryMapConcaveReferenceVelocity(
		Vector3 sourceVelocity,
		int targetTriangleIndex,
		int targetEntryEdgeIndex,
		Vector3 targetNormal,
		out Vector3 targetVelocity)
	{
		targetVelocity =
			Vector3.zero;

		float sourceSpeed =
			sourceVelocity.magnitude;

		if (sourceSpeed <=
			m_MinimumCalculationValue)
		{
			return false;
		}

		if (!TryGetTriangleWorldData(
			targetTriangleIndex,
			out Vector3 vertexA,
			out Vector3 vertexB,
			out Vector3 vertexC,
			out _))
		{
			return false;
		}

		if (!TryGetConcaveEdgeWorldVertices(
			targetEntryEdgeIndex,
			vertexA,
			vertexB,
			vertexC,
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

		// World上のEdge方向ベクトル成分そのものを残すため、
		// Edge頂点順が逆でも結果は変わりません。
		Vector3 alongEdgeVelocity =
			edgeDirection *
				Vector3.Dot(
					sourceVelocity,
					edgeDirection);

		Vector3 crossEdgeVelocity =
			sourceVelocity -
				alongEdgeVelocity;

		float crossEdgeSpeed =
			crossEdgeVelocity.magnitude;

		Vector3 edgeMidPoint =
			(
				edgeStart +
				edgeEnd
			) *
			0.5f;

		Vector3 triangleCenter =
			(
				vertexA +
				vertexB +
				vertexC
			) /
			3.0f;

		Vector3 targetInsideDirection =
			triangleCenter -
				edgeMidPoint;

		targetInsideDirection =
			Vector3.ProjectOnPlane(
				targetInsideDirection,
				targetNormal);

		// Edge方向成分を取り除き、
		// 共有EdgeからTriangle内部へ向かう方向だけにします。
		targetInsideDirection -=
			edgeDirection *
				Vector3.Dot(
					targetInsideDirection,
					edgeDirection);

		if (targetInsideDirection.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return false;
		}

		targetInsideDirection.Normalize();

		Vector3 mappedVelocity =
			alongEdgeVelocity +
			targetInsideDirection *
				crossEdgeSpeed;

		if (mappedVelocity.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return false;
		}

		// 数値誤差でReference速度量が増減しないようにします。
		targetVelocity =
			mappedVelocity.normalized *
				sourceSpeed;

		return true;
	}

	/// <summary>
	/// TriangleのEdge番号からWorld上の2頂点を取得します。
	/// </summary>
	private bool TryGetConcaveEdgeWorldVertices(
		int edgeIndex,
		Vector3 vertexA,
		Vector3 vertexB,
		Vector3 vertexC,
		out Vector3 edgeStart,
		out Vector3 edgeEnd)
	{
		edgeStart =
			Vector3.zero;

		edgeEnd =
			Vector3.zero;

		switch (edgeIndex)
		{
			case 0:
				edgeStart =
					vertexA;

				edgeEnd =
					vertexB;

				return true;

			case 1:
				edgeStart =
					vertexB;

				edgeEnd =
					vertexC;

				return true;

			case 2:
				edgeStart =
					vertexC;

				edgeEnd =
					vertexA;

				return true;

			default:
				return false;
		}
	}

	/// <summary>
	/// Concave専用空中状態の1区間で、
	/// 現在TriangleへSphereが接触したか確認します。
	/// </summary>
	private bool TryGetConcaveSurfaceContact(
		Vector3 startCenter,
		Vector3 endCenter,
		Vector3 startVelocity,
		Vector3 acceleration,
		float deltaTime,
		Vector3 vertexA,
		Vector3 vertexB,
		Vector3 vertexC,
		Vector3 triangleNormal,
		out Vector3 contactPoint,
		out Vector3 contactVelocity)
	{
		contactPoint =
			Vector3.zero;

		contactVelocity =
			Vector3.zero;

		float startClearance =
			Vector3.Dot(
				startCenter -
					vertexA,
				triangleNormal) -
			m_BallRadius;

		float startNormalSpeed =
			Vector3.Dot(
				startVelocity,
				triangleNormal);

		float normalAcceleration =
			Vector3.Dot(
				acceleration,
				triangleNormal);

		if (!TryFindConcaveContactTime(
			startClearance,
			startNormalSpeed,
			normalAcceleration,
			deltaTime,
			out float hitTime))
		{
			return false;
		}

		Vector3 contactCenter =
			startCenter +
			startVelocity *
				hitTime +
			0.5f *
				acceleration *
				hitTime *
				hitTime;

		contactPoint =
			contactCenter -
			triangleNormal *
				m_BallRadius;

		contactPoint =
			ProjectPointToPlane(
				contactPoint,
				vertexA,
				triangleNormal);

		if (!TryCalculateBarycentric(
			contactPoint,
			vertexA,
			vertexB,
			vertexC,
			out Vector3 contactBarycentric) ||
			!IsInsideTriangle(
				contactBarycentric))
		{
			return false;
		}

		contactVelocity =
			startVelocity +
			acceleration *
				hitTime;

		return true;
	}

	/// <summary>
	/// Concave Airborne中のSphereとTriangle Planeの
	/// 最初の接触時刻を放物運動から求めます。
	/// </summary>
	/// <remarks>
	/// Clearance(t) =
	///     StartClearance
	///     + NormalSpeed * t
	///     + 0.5 * NormalAcceleration * t^2
	///
	/// の最初の有効な0点を採用します。
	/// </remarks>
	private bool TryFindConcaveContactTime(
		float startClearance,
		float startNormalSpeed,
		float normalAcceleration,
		float maximumTime,
		out float hitTime)
	{
		hitTime =
			0.0f;

		if (maximumTime <=
			m_MinimumCalculationValue)
		{
			return false;
		}

		if (startClearance <=
			m_MinimumCalculationValue)
		{
			if (startNormalSpeed >=
				-m_MinimumCalculationValue)
			{
				return false;
			}

			hitTime =
				0.0f;

			return true;
		}

		float quadraticA =
			0.5f *
				normalAcceleration;

		float quadraticB =
			startNormalSpeed;

		float quadraticC =
			startClearance;

		if (Mathf.Abs(
				quadraticA) <=
			m_MinimumCalculationValue)
		{
			if (quadraticB >=
				-m_MinimumCalculationValue)
			{
				return false;
			}

			float linearHitTime =
				-quadraticC /
					quadraticB;

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

		float discriminant =
			quadraticB *
				quadraticB -
			4.0f *
				quadraticA *
				quadraticC;

		if (discriminant <
			0.0f)
		{
			return false;
		}

		float squareRoot =
			Mathf.Sqrt(
				Mathf.Max(
					0.0f,
					discriminant));

		float denominator =
			2.0f *
				quadraticA;

		float firstRoot =
			(
				-quadraticB -
					squareRoot
			) /
			denominator;

		float secondRoot =
			(
				-quadraticB +
					squareRoot
			) /
			denominator;

		if (firstRoot >
			secondRoot)
		{
			float swap =
				firstRoot;

			firstRoot =
				secondRoot;

			secondRoot =
				swap;
		}

		if (IsValidConcaveContactTime(
			firstRoot,
			quadraticB,
			normalAcceleration,
			maximumTime))
		{
			hitTime =
				firstRoot;

			return true;
		}

		if (IsValidConcaveContactTime(
			secondRoot,
			quadraticB,
			normalAcceleration,
			maximumTime))
		{
			hitTime =
				secondRoot;

			return true;
		}

		return false;
	}

	/// <summary>
	/// 接触候補時刻が区間内にあり、
	/// Surfaceへ向かう速度で接触しているか確認します。
	/// </summary>
	private bool IsValidConcaveContactTime(
		float candidateTime,
		float startNormalSpeed,
		float normalAcceleration,
		float maximumTime)
	{
		if (candidateTime <
				0.0f ||
			candidateTime >
				maximumTime)
		{
			return false;
		}

		float contactNormalSpeed =
			startNormalSpeed +
			normalAcceleration *
				candidateTime;

		return contactNormalSpeed <
			-m_MinimumCalculationValue;
	}

	/// <summary>
	/// 実際に追跡Surfaceへ接触した瞬間だけ通常のBounceを適用します。
	/// </summary>
	private void ApplyConcaveSurfaceBounce(
		Vector3 contactPoint,
		Vector3 bounceNormal,
		Vector3 incomingWorldVelocity)
	{
		// Concave空中中に実Ballが上下へ動いても、
		// Surface上での進行方向は面を展開したときの
		// Reference進行方向を引き継ぎます。
		//
		// ただし速度量までReferenceから復元すると、
		// 実Ballの接触直前の接線速度より大きい値へ戻り、
		// 接触後に加速したように見える場合があります。
		//
		// そのため方向だけReferenceから取得し、
		// 速度量は実Ballの接触直前の接線速度を使用します。
		Vector3 referenceTangentVelocity =
			Vector3.ProjectOnPlane(
				m_ConcaveReferenceVelocity,
				bounceNormal);

		Vector3 incomingTangentVelocity =
			Vector3.ProjectOnPlane(
				incomingWorldVelocity,
				bounceNormal);

		float desiredSurfaceSpeed =
			incomingTangentVelocity.magnitude *
			m_BounceTangentialRetention;

		Vector3 desiredSurfaceVelocity;

		if (referenceTangentVelocity.sqrMagnitude >
			m_MinimumCalculationValue *
				m_MinimumCalculationValue &&
			desiredSurfaceSpeed >
				m_MinimumCalculationValue)
		{
			desiredSurfaceVelocity =
				referenceTangentVelocity.normalized *
					desiredSurfaceSpeed;
		}
		else
		{
			desiredSurfaceVelocity =
				incomingTangentVelocity *
				m_BounceTangentialRetention;
		}

		if (m_MaxBounceTangentialSpeed >
				0.0f &&
			desiredSurfaceVelocity.magnitude >
				m_MaxBounceTangentialSpeed)
		{
			desiredSurfaceVelocity =
				desiredSurfaceVelocity.normalized *
					m_MaxBounceTangentialSpeed;
		}

		m_SurfaceVelocity =
			desiredSurfaceVelocity;

		float impactSpeed =
			Mathf.Max(
				0.0f,
				-Vector3.Dot(
					incomingWorldVelocity,
					bounceNormal));

		float bounceSpeed =
			0.0f;

		if (impactSpeed >=
			m_BounceSpeedThreshold)
		{
			bounceSpeed =
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
		}

		m_SurfacePoint =
			contactPoint;

		m_Height =
			0.0f;

		m_VerticalVelocity =
			bounceSpeed;
	}

	/// <summary>
	/// Concave専用状態中も、外側から参照される速度値を
	/// 実際のBall World速度と一致させます。
	/// </summary>
	private void SyncConcaveAirbornePublicState()
	{
		if (!m_IsConcaveAirborneTransfer ||
			m_ConcaveReferenceNormal.sqrMagnitude <=
				m_MinimumCalculationValue *
					m_MinimumCalculationValue)
		{
			return;
		}

		Vector3 referenceNormal =
			m_ConcaveReferenceNormal.normalized;

		m_SurfaceVelocity =
			Vector3.ProjectOnPlane(
				m_ConcaveBallWorldVelocity,
				referenceNormal);

		m_VerticalVelocity =
			Vector3.Dot(
				m_ConcaveBallWorldVelocity,
				referenceNormal);

		float signedDistance =
			Vector3.Dot(
				m_ConcaveBallCenter -
					m_SurfacePoint,
				referenceNormal);

		m_Height =
			Mathf.Max(
				0.0f,
				signedDistance -
					m_BallRadius);
	}

	/// <summary>
	/// Concave専用状態だけをクリアします。
	/// </summary>
	private void ClearConcaveAirborneTransferState()
	{
		m_IsConcaveAirborneTransfer =
			false;

		m_ConcaveBallCenter =
			Vector3.zero;

		m_ConcaveBallWorldVelocity =
			Vector3.zero;

		m_ConcaveReferenceVelocity =
			Vector3.zero;

		m_ConcaveReferenceNormal =
			Vector3.zero;

		m_ConcaveReferenceEntryEdgeIndex =
			-1;
	}

	private void ResetConcaveAirborneTransfer()
	{
		ClearConcaveAirborneTransferState();
	}
}
