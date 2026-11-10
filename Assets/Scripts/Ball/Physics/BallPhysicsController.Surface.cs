using UnityEngine;

/// <summary>
/// BallPhysicsControllerのBallSurface Physics連携処理を定義します。
/// </summary>
public partial class BallPhysicsController
{
	/// <summary>
	/// BallSurfaceへロックしてSurface Physicsを開始します。
	/// </summary>
	private bool TryEnterSurfaceSimulation(
		BallSurfaceSource surfaceSource,
		ContactPoint contact,
		Vector3 incomingVelocity)
	{
		if (surfaceSource == null ||
			!surfaceSource.IsReady)
		{
			return false;
		}

		if (!surfaceSource.TryGetSurfaceHit(
			contact.point,
			contact.normal,
			out RaycastHit surfaceHit))
		{
			return false;
		}

		if (surfaceHit.triangleIndex <
			0)
		{
			return false;
		}

		// Transform Scaleなどが変わった場合にも
		// 現在のBall半径をSurface Simulationへ反映します。
		ConfigureSurfaceSimulation();

		if (!m_SurfaceSimulation.TryInitialize(
			surfaceSource,
			surfaceHit.triangleIndex,
			surfaceHit.point,
			incomingVelocity))
		{
			return false;
		}

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

		switch (result)
		{
			case BallSurfaceSimulation.EAdvanceResult.Moved:
				ApplySurfacePosition(
					false);
				break;

			case BallSurfaceSimulation.EAdvanceResult.ConcaveEdgeReached:
			case BallSurfaceSimulation.EAdvanceResult.BoundaryEdgeReached:
			case BallSurfaceSimulation.EAdvanceResult.UnsupportedEdgeReached:
				ApplySurfacePosition(
					false);
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

		if (!immediate &&
			TryHandleSurfaceObstacleCollision(
				targetColliderCenter,
				colliderCenterOffset))
		{
			return;
		}

		if (immediate)
		{
			m_Rigidbody.position =
				targetRigidbodyPosition;
		}
		else
		{
			m_Rigidbody.MovePosition(
				targetRigidbodyPosition);
		}
	}

	/// <summary>
	/// Surface Physics中のBall中心移動経路に、
	/// Ball LayerのCollision Matrix上で衝突対象となる
	/// 通常Colliderが存在するか確認します。
	/// </summary>
	/// <remarks>
	/// BallSurfaceSourceを持つColliderはSurface Physics側で扱うため、
	/// この通常衝突処理から除外します。
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

		for (int hitIndex = 0;
			hitIndex < hitCount;
			hitIndex++)
		{
			RaycastHit hit =
				m_SurfaceObstacleHits[hitIndex];

			Collider hitCollider =
				hit.collider;

			if (hitCollider == null ||
				hitCollider.attachedRigidbody ==
					m_Rigidbody)
			{
				continue;
			}

			// BallSurface同士の乗り換えは別機能として扱うため、
			// 今回の通常Collider衝突からは除外します。
			if (hitCollider.GetComponent<BallSurfaceSource>() !=
				null)
			{
				continue;
			}

			if (hit.distance >=
				nearestDistance)
			{
				continue;
			}

			hasObstacleHit =
				true;

			nearestHit =
				hit;

			nearestDistance =
				hit.distance;
		}

		if (!hasObstacleHit)
		{
			return false;
		}

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
	/// Surface Physicsが保持している速度をWorld速度へ戻します。
	/// </summary>
	private bool TryGetSurfaceWorldVelocity(
		out Vector3 worldVelocity)
	{
		worldVelocity =
			Vector3.zero;

		if (!m_SurfaceSimulation.TryGetCurrentSurfaceNormal(
			out Vector3 surfaceNormal))
		{
			return false;
		}

		worldVelocity =
			m_SurfaceSimulation.SurfaceVelocity +
			surfaceNormal *
				m_SurfaceSimulation.VerticalVelocity;

		return true;
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
	/// SphereColliderの実ワールド半径を取得します。	/// </summary>
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
		m_SurfaceSimulation.Reset();

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
			EBallMotionState.Idle);
	}
}
