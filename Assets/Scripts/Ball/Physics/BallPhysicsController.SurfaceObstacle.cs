using UnityEngine;

public partial class BallPhysicsController
{
	/// <summary>
	/// Surface Physicsで予定しているBall中心の移動経路をSphereCastし、
	/// 経路上の障害物へ到達する前に衝突処理へ切り替えます。
	/// </summary>
	/// <remarks>
	/// 通常Colliderは既存のRigidbodyバウンド処理へ渡します。
	///
	/// BallSurfaceSourceへのHitは一律に無視しません。
	/// 別BallSurfaceSourceはSurface乗り換え側の責務として無視しますが、
	/// 現在と同じBallSurfaceSourceでも、今回の移動が管理しないTriangleは同一Mesh内の別壁として扱います。
	///
	/// これによりConvex Edgeの回り込み中などに、予定軌道上の別壁を
	/// Kinematic Rigidbodyがすり抜けることを防ぎます。
	/// </remarks>
	private bool TryHandleSurfaceObstacleCollision(
		Vector3 targetColliderCenter,
		Vector3 colliderCenterOffset)
	{
		Vector3 currentColliderCenter =
			m_Rigidbody.position +
			colliderCenterOffset;

		Vector3 displacement =
			targetColliderCenter -
			currentColliderCenter;

		float moveDistance =
			displacement.magnitude;

		if (moveDistance <=
			m_MinimumCalculationDistance)
		{
			RecordSurfaceTrace($"CastSkipped=ShortMove Distance={moveDistance:F6}");
			return false;
		}

		Vector3 moveDirection =
			displacement /
			moveDistance;

		float castRadius =
			Mathf.Max(
				GetBallWorldRadius() -
					m_MinimumCalculationDistance,
				m_MinimumCalculationDistance);

		int hitCount =
			Physics.SphereCastNonAlloc(
				currentColliderCenter,
				castRadius,
				moveDirection,
				m_SurfaceObstacleHits,
				moveDistance,
				m_BallCollisionMask,
				QueryTriggerInteraction.Ignore);

		RecordSurfaceTrace($"CastStartCenter={currentColliderCenter.ToString("F6")} CastTargetCenter={targetColliderCenter.ToString("F6")} CastRadius={castRadius:F6} MoveDistance={moveDistance:F6} Hits={hitCount} BufferSize={m_SurfaceObstacleHits.Length} BufferMayBeFull={hitCount == m_SurfaceObstacleHits.Length}");

		if (hitCount <=
			0)
		{
			return false;
		}

		bool hasObstacleHit =
			false;

		RaycastHit nearestHit =
			default;

		float nearestDistance =
			float.PositiveInfinity;

		BallSurfaceSource nearestHitSurfaceSource =
			null;

		for (int hitIndex = 0;
			hitIndex < hitCount;
			hitIndex++)
		{
			RaycastHit hit =
				m_SurfaceObstacleHits[hitIndex];

			Collider hitCollider =
				hit.collider;

			RecordSurfaceTrace($"Hit[{hitIndex}] Collider={(hitCollider != null ? hitCollider.name : "null")} Triangle={hit.triangleIndex} Distance={hit.distance:F6} Point={hit.point.ToString("F6")} Normal={hit.normal.ToString("F6")}");

			if (hitCollider == null ||
				hitCollider.attachedRigidbody ==
					m_Rigidbody)
			{
				RecordSurfaceTrace("Decision=IgnoreSelfOrNull");
				continue;
			}

			// BallSurface同士の乗り換えは別機能として扱うため、
			// 今回の通常Collider衝突からは除外します。
			BallSurfaceSource hitSurfaceSource =
				hitCollider.GetComponent<BallSurfaceSource>();

			if (hitSurfaceSource !=
				null &&
				!ShouldTreatSurfaceHitAsObstacle(
					hitSurfaceSource,
					hit))
			{
				RecordSurfaceTrace("Decision=IgnoreSurfaceHit");
				continue;
			}

			if (hit.distance >=
				nearestDistance)
			{
				RecordSurfaceTrace("Decision=FartherThanCurrentCandidate");
				continue;
			}

			RecordSurfaceTrace("Decision=ObstacleCandidate");
			hasObstacleHit =
				true;

			nearestHit =
				hit;

			nearestDistance =
				hit.distance;

			nearestHitSurfaceSource =
				hitSurfaceSource;
		}

		if (!hasObstacleHit)
		{
			RecordSurfaceTrace("CastResult=AllHitsIgnored");
			return false;
		}

		RecordSurfaceTrace($"SelectedHit Triangle={nearestHit.triangleIndex} HitDistance={nearestHit.distance:F6} HitPoint={nearestHit.point.ToString("F6")} HitNormal={nearestHit.normal.ToString("F6")}");

		if (!TryGetSurfaceWorldVelocity(
			out Vector3 incomingVelocity))
		{
			Debug.LogWarning(
				$"{name}: Surface Physics中の障害物衝突速度を取得できなかったため停止します。",
				this);

			StopSurfaceMotionSafely();
			return true;
		}

		float safeMoveDistance =
			Mathf.Max(
				0.0f,
				nearestHit.distance -
					m_MinimumCalculationDistance);

		Vector3 safeColliderCenter =
			currentColliderCenter +
			moveDirection *
				safeMoveDistance;

		RecordSurfaceTrace($"SafeCenter={safeColliderCenter.ToString("F6")} IncomingVelocity={incomingVelocity.ToString("F6")}");

		if (nearestHitSurfaceSource !=
			null)
		{
			if (TryContinueSameSurfaceBounce(
				nearestHitSurfaceSource,
				nearestHit,
				incomingVelocity,
				safeColliderCenter,
				out BallSurfaceSimulation.EInitializeResult initializeResult))
			{
				RecordSurfaceTrace("SameSurfaceBounce=Success");
				return true;
			}

			RecordSurfaceTrace($"SameSurfaceBounce=Failed Result={initializeResult}");

			// 同一Surface内の別壁として検出できたにもかかわらず、
			// Surface Physicsへ再接続できなかった場合は、
			// 壁を貫通させず安全位置で停止します。
			m_Rigidbody.position =
				safeColliderCenter -
				colliderCenterOffset;

			Debug.LogWarning(
				$"{name}: 同一BallSurface内の別壁へ衝突しましたが、Surface Physicsの再初期化に失敗したため停止します。 Result={initializeResult} Surface={nearestHitSurfaceSource.name} HitTriangle={nearestHit.triangleIndex} HitPoint={nearestHit.point}",
				this);

			StopSurfaceMotionSafely();
			return true;
		}

		m_Rigidbody.position =
			safeColliderCenter -
			colliderCenterOffset;

		Vector3 bounceVelocity =
			CalculateBounceVelocity(
				incomingVelocity,
				nearestHit.normal);

		ApplyBounceVelocity(
			bounceVelocity);

		m_SurfaceGravity.Clear();

		m_NextAttractSearchTime =
			Time.time;

		ChangeMotionState(
			EBallMotionState.Flying);

		return true;
	}



	/// <summary>
	/// BallSurfaceSourceへのSphereCast Hitを、
	/// 同一Mesh内の別壁として衝突処理する必要があるか判定します。
	/// </summary>
	/// <remarks>
	/// 今回のAdvanceで使用した支持面と遷移の構成面だけを除外します。
	/// 同じBallSurfaceSourceでも、単に隣接しているだけのTriangleは障害物として扱います。
	///
	/// 別BallSurfaceSourceはこの処理では扱わず、Surface乗り換え機能側へ責務を残します。
	/// </remarks>
	private bool ShouldTreatSurfaceHitAsObstacle(
		BallSurfaceSource hitSurfaceSource,
		RaycastHit hit)
	{
		BallSurfaceSource currentSurfaceSource =
			m_SurfaceSimulation.SurfaceSource;

		if (hitSurfaceSource == null ||
			currentSurfaceSource == null ||
			hitSurfaceSource !=
				currentSurfaceSource)
		{
			RecordSurfaceTrace("SurfaceFilter=DifferentOrMissingSource");
			return false;
		}

		int currentTriangleIndex =
			m_SurfaceSimulation.CurrentTriangleIndex;

		if (currentTriangleIndex <
			0)
		{
			RecordSurfaceTrace("SurfaceFilter=InvalidCurrentTriangle");
			return false;
		}

		int hitTriangleIndex =
			hit.triangleIndex;

		if (hitSurfaceSource.MeshData == null || hitTriangleIndex < 0 ||
			hitTriangleIndex >= hitSurfaceSource.MeshData.TriangleCount)
		{
			if (!hitSurfaceSource.TryResolveContactTriangle(
				hit.point, hit.normal, out hitTriangleIndex, out _))
			{
				RecordSurfaceTrace("SurfaceFilter=TriangleRecoveryFailed");
				return false;
			}
		}

		if (hitTriangleIndex <
			0)
		{
			RecordSurfaceTrace("SurfaceFilter=InvalidHitTriangle");
			return false;
		}

		bool isTraversalTriangle = m_SurfaceSimulation.IsTraversalTriangle(hitTriangleIndex);
		RecordSurfaceTrace($"SurfaceFilter={(isTraversalTriangle ? (currentTriangleIndex == hitTriangleIndex ? "CurrentTriangle" : "TraversalTriangle") : "NonTraversalTriangle")} CurrentTriangle={currentTriangleIndex} ResolvedHitTriangle={hitTriangleIndex}");
		return !isTraversalTriangle;
	}



	/// <summary>
	/// 同一BallSurface内の別壁へ衝突した場合に、
	/// Hit TriangleへSurface Physicsを再接続してバウンドを継続します。
	/// </summary>
	/// <remarks>
	/// 通常Collider用のApplyBounceVelocityを使用するとSurfaceSimulationがResetされ、
	/// RigidbodyがDynamicへ戻ってFlyingへ遷移します。
	///
	/// 球中心を維持できる再初期化を優先し、必要な場合だけ従来の配置補正を試します。
	/// 接触面と補正経路を検証し、安全な状態だけを確定します。
	/// そのためRigidbodyはKinematic、MotionStateはSurfaceのまま維持されます。
	///
	/// triangleIndexを直接取得できない場合は接触点と法線から再取得します。
	/// </remarks>
	private bool TryContinueSameSurfaceBounce(
		BallSurfaceSource surfaceSource,
		RaycastHit obstacleHit,
		Vector3 incomingVelocity,
		Vector3 safeColliderCenter,
		out BallSurfaceSimulation.EInitializeResult result)
	{
		if (surfaceSource == null || !surfaceSource.IsReady)
		{
			result = BallSurfaceSimulation.EInitializeResult.InvalidSurface;
			return false;
		}

		int triangleIndex = obstacleHit.triangleIndex;
		Vector3 surfacePoint = obstacleHit.point;
		float radius = GetBallWorldRadius();
		float tolerance = m_MinimumCalculationDistance;
		bool startsOverlapping = obstacleHit.distance <= 0.0f;
		// 開始時重複ではHitPointとHitNormalを接触面の情報として使いません。
		if (startsOverlapping)
		{
			if (!surfaceSource.TryGetTriangleClosestWorldPoint(triangleIndex, safeColliderCenter,
				out surfacePoint, out Vector3 faceNormal, out float distanceSquared) ||
				!(distanceSquared <= (radius + tolerance) * (radius + tolerance)) ||
				Vector3.Dot(safeColliderCenter - surfacePoint, faceNormal) < -tolerance ||
				Vector3.Dot(incomingVelocity, faceNormal) >= 0.0f)
			{
				RecordSurfaceTrace($"OverlapContactRecovery=Rejected Triangle={triangleIndex}");
				result = BallSurfaceSimulation.EInitializeResult.ContactTriangleNotFound;
				return false;
			}
			RecordSurfaceTrace($"OverlapContactRecovery=Success Triangle={triangleIndex} Point={surfacePoint.ToString("F6")} FaceNormal={faceNormal.ToString("F6")} Distance={Mathf.Sqrt(distanceSquared):F6}");
		}
		else if (triangleIndex < 0 || triangleIndex >= surfaceSource.MeshData.TriangleCount)
		{
			if (!surfaceSource.TryResolveContactTriangle(
				obstacleHit.point, obstacleHit.normal, out triangleIndex, out surfacePoint))
			{
				result = BallSurfaceSimulation.EInitializeResult.ContactTriangleNotFound;
				return false;
			}
		}

		// 配置候補は既存の仮初期化で生成し、検証に成功した場合だけ確定します。
		ConfigureSurfaceSimulation();
		// 有効なSweep Hitから、球中心を維持できる既存Traversalへの初期化を優先します。
		// 開始時重複のHit Normalは使用せず、従来の復元処理に任せます。
		if (!startsOverlapping)
		{
			if (m_SurfaceSimulation.TryInitializePreservingImpactCenter(surfaceSource, triangleIndex,
				safeColliderCenter, obstacleHit.normal, incomingVelocity, tolerance,
				center => surfaceSource.IsBouncePlacementClear(safeColliderCenter, center,
					radius, tolerance, out _, out _), out BallSurfaceSimulation preserved, out string feature))
			{
				m_SurfaceSimulation = preserved;
#if BALL_SURFACE_DIAGNOSTICS
				m_SurfaceSimulation.TryGetWorldCenter(out Vector3 preservedCenter);
				RecordSurfaceTrace($"ImpactCenterPreserved=Success Feature={feature} HitTriangle={triangleIndex} Center={preservedCenter.ToString("F6")} Delta={Vector3.Distance(safeColliderCenter, preservedCenter):F6}");
#endif
				result = BallSurfaceSimulation.EInitializeResult.Success;
				CompleteSameSurfaceBounce();
				return true;
			}
			RecordSurfaceTrace($"ImpactCenterPreserved=Rejected Feature={feature} HitTriangle={triangleIndex}");
		}
		int lastBlockedTriangle = -1;
		Vector3 lastCandidateCenter = Vector3.zero;
		float remainingBudget = Mathf.Min(radius * 0.1f, surfaceSource.SurfaceCollider.contactOffset * 2.0f);
		bool ValidateCenter(Vector3 candidateCenter)
		{
			lastBlockedTriangle = -1;
			lastCandidateCenter = candidateCenter;
			float maxCorrection = radius + tolerance;
			if (!((candidateCenter - safeColliderCenter).sqrMagnitude <= maxCorrection * maxCorrection))
			{
				RecordSurfaceTrace($"BouncePlacement=Rejected Reason=CorrectionTooFar SafeCenter={safeColliderCenter.ToString("F6")} CandidateCenter={candidateCenter.ToString("F6")}");
				return false;
			}

			// 配置先の重複と、補正経路だけの重複を区別します。
			if (!surfaceSource.IsBouncePlacementClear(candidateCenter, candidateCenter,
				radius, tolerance, out lastBlockedTriangle, out float endpointDistance))
			{
				RecordSurfaceTrace($"BouncePlacement=Rejected Reason=EndpointOverlap CandidateCenter={candidateCenter.ToString("F6")} BlockedTriangle={lastBlockedTriangle} BlockedDistance={endpointDistance:F6}");
				return false;
			}

			if (surfaceSource.IsBouncePlacementClear(safeColliderCenter, candidateCenter,
				radius, tolerance, out int pathBlockedTriangle, out float pathBlockedDistance))
			{
				RecordSurfaceTrace($"BouncePlacement=Accepted Reason=DirectPath CandidateCenter={candidateCenter.ToString("F6")}");
				return true;
			}

			// 配置先が安全な場合だけ、経路の迂回を試します。
			// 経路失敗ではlastBlockedTriangleを更新せず、配置先の押し出し補正と混同しません。
			RecordSurfaceTrace($"BouncePlacement=Rejected Reason=PathOverlap CandidateCenter={candidateCenter.ToString("F6")} BlockedTriangle={pathBlockedTriangle} BlockedDistance={pathBlockedDistance:F6}");
			if (pathBlockedTriangle >= 0 &&
				surfaceSource.TryGetBouncePlacementDetour(safeColliderCenter, candidateCenter,
					pathBlockedTriangle, radius, tolerance, remainingBudget, out Vector3 waypoint))
			{
				RecordSurfaceTrace($"BouncePlacementDetour=Success BlockedTriangle={pathBlockedTriangle} Waypoint={waypoint.ToString("F6")} CandidateCenter={candidateCenter.ToString("F6")} Budget={remainingBudget:F6}");
				return true;
			}

			RecordSurfaceTrace($"BouncePlacementDetour=Rejected BlockedTriangle={pathBlockedTriangle} BlockedDistance={pathBlockedDistance:F6} Budget={remainingBudget:F6}");
			return false;
		}
		Vector3 attemptPoint = surfacePoint;
		float attemptHeight = 0.0f;
		bool initialized = m_SurfaceSimulation.TryInitialize(surfaceSource, triangleIndex, attemptPoint,
			incomingVelocity, out result, ValidateCenter);
		if (!initialized &&
			(result == BallSurfaceSimulation.EInitializeResult.InvalidPlacement ||
				result == BallSurfaceSimulation.EInitializeResult.PointOutsideTriangle) &&
			surfaceSource.TryGetTriangleClosestWorldPoint(triangleIndex, safeColliderCenter,
				out Vector3 correctedPoint, out _, out _) &&
			(correctedPoint - surfacePoint).sqrMagnitude <= radius * radius &&
			(correctedPoint - surfacePoint).sqrMagnitude > tolerance * tolerance)
		{
			attemptPoint = correctedPoint;
			RecordSurfaceTrace($"BouncePlacementRetry Triangle={triangleIndex} Point={attemptPoint.ToString("F6")}");
			initialized = m_SurfaceSimulation.TryInitialize(surfaceSource, triangleIndex, attemptPoint,
				incomingVelocity, out result, ValidateCenter);
		}

		// 微小な重複だけを対象とし、補正量と再試行回数に上限を設けます。
		// 既存の補正経路・全Triangle検証・ボール半径による制限は緩和しません。
		const int maximumCorrectionAttempts = 4;
		for (int attempt = 0; !initialized && attempt < maximumCorrectionAttempts; attempt++)
		{
			if (result != BallSurfaceSimulation.EInitializeResult.InvalidPlacement || lastBlockedTriangle < 0)
			{
				break;
			}

			int blockedTriangle = lastBlockedTriangle;
			if (!surfaceSource.TryGetBouncePlacementCorrection(triangleIndex, blockedTriangle,
				attemptPoint, lastCandidateCenter, radius, tolerance, remainingBudget,
				out Vector3 nextPoint, out float heightIncrease))
			{
				RecordSurfaceTrace($"BouncePlacementDepenetration=Rejected Triangle={triangleIndex} BlockedTriangle={blockedTriangle} Budget={remainingBudget:F6}");
				break;
			}

			remainingBudget -= (nextPoint - attemptPoint).magnitude + heightIncrease;
			attemptPoint = nextPoint;
			attemptHeight += heightIncrease;
			RecordSurfaceTrace($"BouncePlacementDepenetration=Retry Attempt={attempt + 1} Triangle={triangleIndex} BlockedTriangle={blockedTriangle} Point={attemptPoint.ToString("F6")} Height={attemptHeight:F6} Budget={remainingBudget:F6}");
			initialized = m_SurfaceSimulation.TryInitialize(surfaceSource, triangleIndex, attemptPoint,
				incomingVelocity, out result, ValidateCenter, attemptHeight);
			if (initialized)
			{
				RecordSurfaceTrace($"BouncePlacementDepenetration=Success Triangle={triangleIndex} Height={attemptHeight:F6}");
			}
		}

		if (!initialized)
		{
			return false;
		}

		CompleteSameSurfaceBounce();
		return true;
	}

	/// <summary>
	/// 検証済みのSurface初期化状態をRigidbodyへ反映します。
	/// </summary>
	private void CompleteSameSurfaceBounce()
	{
		m_FlightDirection =
			Vector3.zero;

		m_PrePhysicsVelocity =
			Vector3.zero;

		m_SurfaceGravity.Clear();

		ChangeMotionState(
			EBallMotionState.Surface);

		// 新しいTriangleの接触位置へ即時反映します。
		// immediate=trueのためObstacle SphereCastは再実行しません。
		ApplySurfacePosition(
			true);
	}
}
