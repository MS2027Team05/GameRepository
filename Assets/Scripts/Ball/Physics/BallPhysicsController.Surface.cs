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
