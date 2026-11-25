using UnityEngine;

/// <summary>
/// Heightを持ったボールがConcave Edgeを空中で越える場合の専用遷移を担当します。
/// </summary>
/// <remarks>
/// 主な処理の流れ:
/// 1. HeightまたはVerticalVelocityがある状態でConcaveへ入ったことを検出します。
/// 2. Ball本体のWorld軌道とSurface上の参照点を一時的に分離します。
/// 3. World軌道を進めながら隣Surfaceとの接触時刻を探索します。
/// 4. 接触が成立した時点でSurface状態へ再同期し、必要なSurfaceバウンドを適用します。
/// 5. 遷移完了後は通常のSurface Physics状態へ戻します。
///
/// 接地した状態のConcave遷移はBallSurfaceSimulation.Concave.csが担当します。
/// </remarks>
public sealed partial class BallSurfaceSimulation
{
	private bool m_IsConcaveAirborneTransfer;

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

		Vector3 ballCenter;
		Vector3 ballWorldVelocity;

		if (m_HasWorldState)
		{
			ballCenter =
				m_WorldCenter;

			ballWorldVelocity =
				m_WorldVelocity;
		}
		else
		{
			float centerOffset =
				Mathf.Max(
					m_BallRadius +
						m_Height,
					m_MinimumCalculationValue);

			ballCenter =
				edgePoint +
				sourceNormal *
					centerOffset;

			ballWorldVelocity =
				m_SurfaceVelocity +
				sourceNormal *
					m_VerticalVelocity;
		}

		Vector3 beforeCenter =
			ballCenter;

		Vector3 beforeVelocity =
			ballWorldVelocity;

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

		SetWorldState(
			ballCenter,
			ballWorldVelocity);

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

		ValidateTransitionWorldState(
			beforeCenter,
			beforeVelocity,
			"Face -> Concave");

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
		float deltaTime,
		out float unusedTime)
	{
		unusedTime =
			0.0f;

		if (!m_IsConcaveAirborneTransfer)
		{
			unusedTime =
				deltaTime;

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
			RecordTraversalTriangles();

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
				return ReportInvalidGeometry("CurrentTriangleWorldData");
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
					currentNormal,
					out float stationarySegmentUnusedTime))
				{
					unusedTime =
						Mathf.Clamp(
							stationarySegmentUnusedTime,
							0.0f,
							remainingTime);

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
				return ReportInvalidGeometry("ReferenceTangentVelocity");
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
				return ReportInvalidGeometry("ReferenceBarycentric");
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
					return ReportInvalidGeometry("FirstSurfaceCrossing");
				}

				if (simultaneousEdgeIndex >=
					0)
				{
					crossedEdgeIndex =
						ResolveSimultaneousVertexEdge(
							crossedEdgeIndex,
							simultaneousEdgeIndex,
							m_ConcaveReferenceVelocity);
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

			if (TryAdvanceConcaveBallSegment(
				segmentTime,
				vertexA,
				vertexB,
				vertexC,
				currentNormal,
				out float airborneSegmentUnusedTime))
			{
				// Contactまでに使わなかった区間に加え、
				// Referenceがまだ進んでいない後続時間も返します。
				unusedTime =
					Mathf.Clamp(
						remainingTime -
							segmentTime +
							airborneSegmentUnusedTime,
						0.0f,
						remainingTime);

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
				return ReportInvalidGeometry("NeighborTriangleWorldData");
			}

			if (edgeType ==
				BallSurfaceMeshData.EEdgeType.Convex)
			{
				Vector3 beforeCenter =
					m_WorldCenter;

				Vector3 beforeVelocity =
					m_WorldVelocity;

				float expectedRetention = m_HasAppliedConvexTraversalSpeedRetention ?
					1.0f : m_ConvexEdgeSpeedRetention;

				if (BeginConvexEdgeTransition(
					crossedEdgeIndex,
					neighborTriangleIndex,
					neighborEntryEdgeIndex,
					currentNormal,
					neighborNormal))
				{
					ClearConcaveAirborneTransferState();

					ValidateTransitionWorldState(
						beforeCenter,
						beforeVelocity,
						"Concave -> Convex", expectedRetention);

					unusedTime =
						remainingTime;

					return EAdvanceResult.Moved;
				}
			}

			if (!TryMapConcaveReferenceVelocity(
				m_ConcaveReferenceVelocity,
				neighborTriangleIndex,
				neighborEntryEdgeIndex,
				neighborNormal,
				out Vector3 neighborReferenceVelocity))
			{
				return ReportInvalidGeometry("MapConcaveReferenceVelocity");
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
		Vector3 triangleNormal,
		out float unusedTime)
	{
		unusedTime =
			0.0f;

		if (deltaTime <=
			m_MinimumCalculationValue)
		{
			return false;
		}

		Vector3 startCenter =
			m_WorldCenter;

		Vector3 startVelocity =
			m_WorldVelocity;

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
			out Vector3 contactVelocity,
			out float contactTime) &&
			Vector3.Dot(
				contactVelocity,
				triangleNormal) <
				-m_MinimumCalculationValue)
		{
			ApplyConcaveSurfaceBounce(
				contactPoint,
				triangleNormal,
				contactVelocity);

			ClearConcaveAirborneTransferState();

			unusedTime =
				Mathf.Max(
					0.0f,
					deltaTime -
						Mathf.Clamp(
							contactTime,
							0.0f,
							deltaTime));

			return true;
		}

		SetWorldState(
			endCenter,
			endVelocity);

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
			return RecordGeometryFailure("Guard1");
		}

		if (!TryGetTriangleWorldData(
			targetTriangleIndex,
			out Vector3 vertexA,
			out Vector3 vertexB,
			out Vector3 vertexC,
			out _))
		{
			return RecordGeometryFailure("Guard2");
		}

		if (!TryGetConcaveEdgeWorldVertices(
			targetEntryEdgeIndex,
			vertexA,
			vertexB,
			vertexC,
			out Vector3 edgeStart,
			out Vector3 edgeEnd))
		{
			return RecordGeometryFailure("Guard3");
		}

		Vector3 edgeVector =
			edgeEnd -
				edgeStart;

		if (edgeVector.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return RecordGeometryFailure("Guard4");
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
			return RecordGeometryFailure("Guard5");
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
			return RecordGeometryFailure("Guard6");
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
		out Vector3 contactVelocity,
		out float contactTime)
	{
		contactPoint =
			Vector3.zero;

		contactVelocity =
			Vector3.zero;

		contactTime =
			0.0f;

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

		contactTime =
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
		Vector3 normalizedBounceNormal =
			bounceNormal.normalized;

		Vector3 referenceTangentVelocity =
			Vector3.ProjectOnPlane(
				m_ConcaveReferenceVelocity,
				normalizedBounceNormal);

		Vector3 incomingTangentVelocity =
			Vector3.ProjectOnPlane(
				incomingWorldVelocity,
				normalizedBounceNormal);

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
					normalizedBounceNormal));

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

		SetWorldState(
			contactPoint +
			normalizedBounceNormal *
				m_BallRadius,
			desiredSurfaceVelocity +
			normalizedBounceNormal *
				bounceSpeed);
	}

	/// <summary>
	/// Concave専用状態中も、外側から参照される速度値を
	/// 実際のBall World速度と一致させます。
	/// </summary>
	private void SyncConcaveAirbornePublicState()
	{
		if (!m_IsConcaveAirborneTransfer ||
			!m_HasWorldState ||
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
				m_WorldVelocity,
				referenceNormal);

		m_VerticalVelocity =
			Vector3.Dot(
				m_WorldVelocity,
				referenceNormal);

		float signedDistance =
			Vector3.Dot(
				m_WorldCenter -
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
