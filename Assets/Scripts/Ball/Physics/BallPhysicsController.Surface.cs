using UnityEngine;

/// <summary>
/// BallPhysicsControllerとBallSurfaceSimulationを接続し、
/// BallSurface上の移動・衝突・離脱をRigidbodyへ反映します。
/// </summary>
/// <remarks>
/// 主な処理の流れ:
/// 1. BallSurfaceSourceへの接触地点から開始Triangleを取得します。
/// 2. BallSurfaceSimulationを初期化し、RigidbodyをKinematicへ切り替えます。
/// 3. FixedUpdateごとにSurfaceSimulationを進め、計算されたBall中心位置を反映します。
/// 4. Surface移動経路はSphereCastで確認し、通常Colliderへのすり抜けを防ぎます。
/// 5. 同一BallSurface内でも今回のSurface移動が管理しないTriangleは別壁として扱います。
/// 6. 同一Mesh内の別壁へ当たった場合はHit TriangleでSimulationを再初期化し、
///    Flyingへ切り替えずSurface Physics用バウンドを継続します。
/// 7. Surface端から離脱する場合は出口位置と速度を一度保留し、
///    次のFixedUpdateでDynamic Rigidbodyへ戻してFlyingを再開します。
/// 8. 不正なGeometryや再初期化失敗時は、貫通を避けるため安全停止します。
///
/// Surface中は通常のRigidbody衝突解決に任せず、
/// BallSurfaceSimulationのWorld状態を物理挙動の基準として扱います。
/// </remarks>
public partial class BallPhysicsController
{
	private bool m_HasPendingSurfaceExit;
	private Vector3 m_PendingSurfaceExitVelocity;

	// Editor / Development Build専用の直近Surface更新履歴です。
	private const int m_SurfaceTraceCapacity = 12;
	private System.Text.StringBuilder[] m_SurfaceTrace;
	private int m_SurfaceTraceNext;
	private int m_SurfaceTraceCount;
	private int m_SurfaceTraceCurrent = -1;

	/// <summary>
	/// 物理更新または初回Surface進入の診断記録を開始します。
	/// </summary>
	[System.Diagnostics.Conditional("UNITY_EDITOR")]
	[System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
	private void BeginSurfaceTrace(string eventName, bool clearHistory = false)
	{
		if (m_SurfaceTrace == null)
		{
			m_SurfaceTrace = new System.Text.StringBuilder[m_SurfaceTraceCapacity];
		}

		if (clearHistory)
		{
			m_SurfaceTraceNext = 0;
			m_SurfaceTraceCount = 0;
		}

		m_SurfaceTraceCurrent = m_SurfaceTraceNext;
		m_SurfaceTraceNext = (m_SurfaceTraceNext + 1) % m_SurfaceTraceCapacity;
		m_SurfaceTraceCount = Mathf.Min(m_SurfaceTraceCount + 1, m_SurfaceTraceCapacity);
		System.Text.StringBuilder record = m_SurfaceTrace[m_SurfaceTraceCurrent];
		if (record == null)
		{
			record = new System.Text.StringBuilder(2048);
			m_SurfaceTrace[m_SurfaceTraceCurrent] = record;
		}

		record.Clear();
		Vector3 center = m_Rigidbody.position + transform.TransformVector(m_BallCollider.center);
		record.AppendLine($"[Step] Event={eventName} FixedTime={Time.fixedTime:F6} Frame={Time.frameCount}");
		record.AppendLine($"BeforeTriangle={m_SurfaceSimulation.CurrentTriangleIndex} Center={center.ToString("F6")} BallRadius={GetBallWorldRadius():F6}");
		if (m_SurfaceSimulation.TryGetWorldVelocity(out Vector3 velocity))
		{
			record.AppendLine($"BeforeVelocity={velocity.ToString("F6")}");
		}
	}

	/// <summary>
	/// 現在の診断記録へ情報を追加します。Release Buildでは呼び出されません。
	/// </summary>
	[System.Diagnostics.Conditional("UNITY_EDITOR")]
	[System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
	private void RecordSurfaceTrace(string message)
	{
		if (m_SurfaceTraceCurrent >= 0 && m_SurfaceTrace != null)
		{
			m_SurfaceTrace[m_SurfaceTraceCurrent].AppendLine(message);
		}
	}

	/// <summary>
	/// 安全停止直前に履歴を古い順でまとめて出力します。
	/// </summary>
	[System.Diagnostics.Conditional("UNITY_EDITOR")]
	[System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
	private void DumpSurfaceTrace()
	{
		if (m_SurfaceTrace == null || m_SurfaceTraceCount == 0)
		{
			return;
		}

		System.Text.StringBuilder report = new System.Text.StringBuilder();
		report.AppendLine($"[BallSurface][ObstacleTrace] Ball={name} State={m_MotionState} Records={m_SurfaceTraceCount}");
		int first = (m_SurfaceTraceNext - m_SurfaceTraceCount + m_SurfaceTraceCapacity) % m_SurfaceTraceCapacity;
		for (int index = 0; index < m_SurfaceTraceCount; index++)
		{
			report.Append(m_SurfaceTrace[(first + index) % m_SurfaceTraceCapacity]);
		}

		Debug.LogWarning(report.ToString(), this);
		m_SurfaceTraceCount = 0;
		m_SurfaceTraceCurrent = -1;
		m_SurfaceTraceNext = 0;
	}


	/// <summary>
	/// BallSurfaceへロックしてSurface Physicsを開始します。
	/// </summary>
	private bool TryEnterSurfaceSimulation(
		BallSurfaceSource surfaceSource,
		ContactPoint contact,
		Vector3 incomingVelocity,
		out BallSurfaceSimulation.EInitializeResult result)
	{
		if (surfaceSource == null || !surfaceSource.IsReady)
		{
			result = BallSurfaceSimulation.EInitializeResult.InvalidSurface;
			return false;
		}

		if (!surfaceSource.TryResolveContactTriangle(
			contact.point, contact.normal, contact.separation,
			out int triangleIndex, out Vector3 surfacePoint))
		{
			result = BallSurfaceSimulation.EInitializeResult.ContactTriangleNotFound;
			return false;
		}

		// Transform Scaleなどが変わった場合にも現在のBall半径を反映します。
		ConfigureSurfaceSimulation();
		if (!m_SurfaceSimulation.TryInitialize(
			surfaceSource, triangleIndex, surfacePoint, incomingVelocity, out result))
		{
			return false;
		}

		// 初期化の成功後に以前の出口保留状態を破棄します。
		ClearPendingSurfaceExit();

		BeginSurfaceTrace("EnterSurface", true);

		// Dynamic Rigidbodyの間に速度を停止します。
		m_Rigidbody.linearVelocity =
			Vector3.zero;

		m_Rigidbody.angularVelocity =
			Vector3.zero;

		m_Rigidbody.isKinematic =
			true;

		m_FlightDirection =
			Vector3.zero;

		m_PrePhysicsVelocity =
			Vector3.zero;

		m_SurfaceGravity.Clear();

		ChangeMotionState(
			EBallMotionState.Surface);

		ApplySurfacePosition(
			true);

		return true;
	}

	/// <summary>
	/// Surface Physicsを1Physics Step進めます。
	/// </summary>
	private void UpdateSurfaceSimulation()
	{
		BeginSurfaceTrace("FixedUpdate");

		// Surface端へ到達したFrameではKinematicのまま出口位置を確定し、
		// 次のFixedUpdate開始時にDynamicへ戻します。
		// FixedUpdate途中でDynamicへ戻すと、その直後のPhysics更新で
		// 1Step分が追加で進むため、二重進行を避けます。
		if (m_HasPendingSurfaceExit)
		{
			CompletePendingSurfaceExit();
			return;
		}

		if (!m_SurfaceSimulation.IsInitialized)
		{
			Debug.LogWarning(
				$"{name}: Surface Physicsが未初期化の状態になったため、現在位置で停止します。",
				this);

			StopSurfaceMotionSafely();

			return;
		}

		BallSurfaceSimulation.EAdvanceResult result =
			m_SurfaceSimulation.Advance(
				Time.fixedDeltaTime);

		RecordSurfaceTrace($"Advance={result} AfterTriangle={m_SurfaceSimulation.CurrentTriangleIndex} HasLastEdge={m_SurfaceSimulation.HasLastEdge} LastEdgeTriangle={m_SurfaceSimulation.LastEdgeTriangleIndex} LastEdge={m_SurfaceSimulation.LastEdgeIndex} LastEdgeType={m_SurfaceSimulation.LastEdgeType}");

		switch (result)
		{
			case BallSurfaceSimulation.EAdvanceResult.Moved:
				ApplySurfacePosition(
					false);
				break;

			case BallSurfaceSimulation.EAdvanceResult.ConcaveEdgeReached:
			case BallSurfaceSimulation.EAdvanceResult.BoundaryEdgeReached:
			case BallSurfaceSimulation.EAdvanceResult.UnsupportedEdgeReached:
				if (!BeginPendingSurfaceExit())
				{
					Debug.LogWarning(
						$"{name}: Surface出口のWorld状態を取得できなかったため停止します。 Result={result}",
						this);

					StopSurfaceMotionSafely();
				}
				break;

			case BallSurfaceSimulation.EAdvanceResult.NotInitialized:
			case BallSurfaceSimulation.EAdvanceResult.InvalidGeometry:
			case BallSurfaceSimulation.EAdvanceResult.TransitionLimitReached:
				Debug.LogWarning(
					$"{name}: Surface Physicsを継続できないため、現在位置で停止します。 Result={result}",
					this);

				StopSurfaceMotionSafely();
				break;
		}
	}

	/// <summary>
	/// Surface Physicsの座標をWorld座標へ戻します。
	/// </summary>
	private void ApplySurfacePosition(
		bool immediate)
	{
		if (!m_SurfaceSimulation.TryGetCurrentBallCenterOffset(
			out Vector3 ballCenterOffset))
		{
			return;
		}

		Vector3 targetColliderCenter =
			m_SurfaceSimulation.SurfacePoint +
			ballCenterOffset;

		Vector3 colliderCenterOffset =
			transform.TransformVector(
				m_BallCollider.center);

		Vector3 targetRigidbodyPosition =
			targetColliderCenter -
			colliderCenterOffset;

		RecordSurfaceTrace($"ApplyTarget={targetColliderCenter.ToString("F6")} Immediate={immediate} Triangle={m_SurfaceSimulation.CurrentTriangleIndex}");

		if (!immediate &&
			TryHandleSurfaceObstacleCollision(
				targetColliderCenter,
				colliderCenterOffset))
		{
			return;
		}

		if (immediate)
		{
			RecordSurfaceTrace("Apply=DirectPosition (SphereCastSkipped)");
			m_Rigidbody.position =
				targetRigidbodyPosition;
		}
		else
		{
			RecordSurfaceTrace("Apply=MovePosition");
			m_Rigidbody.MovePosition(
				targetRigidbodyPosition);
		}
	}

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
		RecordSurfaceTrace($"SurfaceFilter={(isTraversalTriangle ? (currentTriangleIndex == hitTriangleIndex ? "CurrentTriangle" : "TraversalTriangle") : (IsCurrentOrNeighborSurfaceTriangle(currentTriangleIndex, hitTriangleIndex) ? "NeighborNotTraversed" : "SeparateWall"))} CurrentTriangle={currentTriangleIndex} ResolvedHitTriangle={hitTriangleIndex}");
		return !isTraversalTriangle;
	}

	/// <summary>
	/// 指定Triangleが現在Triangle、またはその直接隣接Triangleか取得します。
	/// </summary>
	private bool IsCurrentOrNeighborSurfaceTriangle(
		int currentTriangleIndex,
		int targetTriangleIndex)
	{
		if (targetTriangleIndex ==
			currentTriangleIndex)
		{
			return true;
		}

		BallSurfaceSource currentSurfaceSource =
			m_SurfaceSimulation.SurfaceSource;

		if (currentSurfaceSource == null ||
			currentSurfaceSource.MeshData == null)
		{
			return false;
		}

		BallSurfaceMeshData meshData =
			currentSurfaceSource.MeshData;

		for (int edgeIndex = 0;
			edgeIndex < 3;
			edgeIndex++)
		{
			if (!meshData.TryGetNeighbor(
				currentTriangleIndex,
				edgeIndex,
				out int neighborTriangleIndex,
				out _))
			{
				continue;
			}

			if (neighborTriangleIndex ==
				targetTriangleIndex)
			{
				return true;
			}
		}

		return false;
	}


	/// <summary>
	/// 同一BallSurface内の別壁へ衝突した場合に、
	/// Hit TriangleへSurface Physicsを再接続してバウンドを継続します。
	/// </summary>
	/// <remarks>
	/// 通常Collider用のApplyBounceVelocityを使用するとSurfaceSimulationがResetされ、
	/// RigidbodyがDynamicへ戻ってFlyingへ遷移します。
	///
	/// このケースでは衝突直前のWorld速度をBallSurfaceSimulation.TryInitializeへ渡し、
	/// Hit Triangle側でSurface Physics専用のバウンド計算を行います。
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
				m_SurfaceSimulation.TryGetWorldCenter(out Vector3 preservedCenter);
				RecordSurfaceTrace($"ImpactCenterPreserved=Success Feature={feature} HitTriangle={triangleIndex} Center={preservedCenter.ToString("F6")} Delta={Vector3.Distance(safeColliderCenter, preservedCenter):F6}");
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

	/// <summary>
	/// Surface端へ到達したWorld位置と速度を保持し、
	/// 次のFixedUpdateでFlyingへ戻す準備をします。
	/// </summary>
	private bool BeginPendingSurfaceExit()
	{
		if (!m_SurfaceSimulation.TryGetWorldCenter(
			out Vector3 worldCenter) ||
			!m_SurfaceSimulation.TryGetWorldVelocity(
				out Vector3 worldVelocity))
		{
			return false;
		}

		Vector3 colliderCenterOffset =
			transform.TransformVector(
				m_BallCollider.center);

		// このFixedUpdateではKinematicを維持したまま、
		// Surface計算で到達した正確な出口位置だけを反映します。
		m_Rigidbody.position =
			worldCenter -
			colliderCenterOffset;

		m_PendingSurfaceExitVelocity =
			worldVelocity;

		m_HasPendingSurfaceExit =
			true;

		m_SurfaceSimulation.Reset();
		m_SurfaceGravity.Clear();

		return true;
	}

	/// <summary>
	/// 前FixedUpdateで確定したSurface出口速度をRigidbodyへ渡し、
	/// 通常のFlying状態へ戻します。
	/// </summary>
	private void CompletePendingSurfaceExit()
	{
		Vector3 exitVelocity =
			m_PendingSurfaceExitVelocity;

		ClearPendingSurfaceExit();

		m_Rigidbody.isKinematic =
			false;

		m_Rigidbody.linearVelocity =
			exitVelocity;

		m_Rigidbody.angularVelocity =
			Vector3.zero;

		m_FlightDirection =
			exitVelocity.sqrMagnitude >
				Mathf.Epsilon
				? exitVelocity.normalized
				: Vector3.zero;

		m_PrePhysicsVelocity =
			exitVelocity;

		m_SurfaceGravity.Clear();

		m_NextAttractSearchTime =
			Time.time +
			m_AttractSearchInterval;

		ChangeMotionState(
			EBallMotionState.Flying);
	}

	/// <summary>
	/// 保留中のSurface出口状態を破棄します。
	/// </summary>
	private void ClearPendingSurfaceExit()
	{
		m_HasPendingSurfaceExit =
			false;

		m_PendingSurfaceExitVelocity =
			Vector3.zero;
	}

	/// <summary>
	/// Surface Physicsが保持している速度をWorld速度へ戻します。
	/// </summary>
	private bool TryGetSurfaceWorldVelocity(
		out Vector3 worldVelocity)
	{
		return m_SurfaceSimulation.TryGetWorldVelocity(
			out worldVelocity);
	}

	/// <summary>
	/// Ball自身のLayerに設定されているCollision Matrixから、
	/// Physics Query用のLayerMaskを構築します。
	/// </summary>
	private int BuildBallCollisionMask()
	{
		int collisionMask =
			0;

		int ballLayer =
			gameObject.layer;

		for (int layerIndex = 0;
			layerIndex < 32;
			layerIndex++)
		{
			if (Physics.GetIgnoreLayerCollision(
				ballLayer,
				layerIndex))
			{
				continue;
			}

			collisionMask |=
				1 << layerIndex;
		}

		return collisionMask;
	}

	/// <summary>
	/// SphereColliderのScaleを考慮したWorld空間上の実半径を取得します。
	/// </summary>
	/// <remarks>
	/// 非一様Scaleの場合でもColliderを内側へ小さく見積もらないよう、XYZの最大Scaleを使用します。
	/// Surface Offset、SphereCast、場外復帰位置などの安全距離計算に使用します。
	/// </remarks>
	private float GetBallWorldRadius()
	{
		Vector3 lossyScale =
			m_BallCollider.transform.lossyScale;

		float maximumScale =
			Mathf.Max(
				Mathf.Abs(
					lossyScale.x),
				Mathf.Abs(
					lossyScale.y),
				Mathf.Abs(
					lossyScale.z));

		return m_BallCollider.radius *
			maximumScale;
	}

	/// <summary>
	/// BallSurfaceへの接触は成立したものの、
	/// Triangleを取得できずSurface Physicsを開始できなかった場合に、
	/// 接触地点の外側へボールを置いて安全に停止します。
	/// </summary>
	private void StopSafelyAtSurfaceContact(
		ContactPoint contact)
	{
		Vector3 targetRigidbodyPosition =
			m_Rigidbody.position;

		bool hasSafePosition =
			false;

		Vector3 contactNormal =
			contact.normal;

		if (contactNormal.sqrMagnitude >
			Mathf.Epsilon)
		{
			contactNormal.Normalize();

			Vector3 colliderCenterOffset =
				transform.TransformVector(
					m_BallCollider.center);

			Vector3 currentColliderCenter =
				m_Rigidbody.position +
				colliderCenterOffset;

			// Contact Normalの向きに依存せず、
			// 現在のBall中心が存在する側へ必ず退避させます。
			if (Vector3.Dot(
				currentColliderCenter -
					contact.point,
				contactNormal) <
				0.0f)
			{
				contactNormal =
					-contactNormal;
			}

			float safeCenterDistance =
				GetBallWorldRadius() +
				m_MinimumCalculationDistance;

			Vector3 targetColliderCenter =
				contact.point +
				contactNormal *
					safeCenterDistance;

			targetRigidbodyPosition =
				targetColliderCenter -
				colliderCenterOffset;

			hasSafePosition =
				true;
		}

		StopSurfaceMotionSafely();

		if (hasSafePosition)
		{
			m_Rigidbody.position =
				targetRigidbodyPosition;
		}
	}

	/// <summary>
	/// Surface Physicsを安全停止し、
	/// 周辺Surfaceの再探索を行わず現在位置を維持します。
	/// </summary>
	private void StopSurfaceMotionSafely()
	{
		DumpSurfaceTrace();
		m_SurfaceSimulation.Reset();

		if (!m_Rigidbody.isKinematic)
		{
			m_Rigidbody.linearVelocity = Vector3.zero;
			m_Rigidbody.angularVelocity = Vector3.zero;
		}

		m_Rigidbody.isKinematic =
			true;

		m_FlightDirection =
			Vector3.zero;

		m_PrePhysicsVelocity =
			Vector3.zero;

		m_SurfaceGravity.Clear();


		ChangeMotionState(
			EBallMotionState.Idle);
	}
}
