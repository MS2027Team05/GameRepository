using UnityEngine;

/// <summary>
/// BallPhysicsControllerの追尾パス物理処理を定義します。
/// </summary>
public partial class BallPhysicsController
{
	private const float m_HomingBounceRedirectDuration = 0.2f;
	private const float m_HomingBounceTargetTangentialBias = 0.35f;
	private const float m_HomingObstructedTurnSpeedRadians = Mathf.PI;
	private const float m_MaxHomingDuration = 4.0f;
	private const int m_MaxHomingBounceCount = 12;
	private const float m_HomingClearCastRadiusScale = 0.85f;

	[Header("追尾パス")]
	[Tooltip("追尾を終了する距離です。")]
	[SerializeField]
	[Min(0.1f)]
	private float m_HomingEndDistance = 0.5f;

	/// <summary>
	/// 追尾中の現在速度です。通常投げと同一の減速式で減少します。
	/// </summary>
	private float m_CurrentHomingSpeed;

	/// <summary>
	/// 追尾対象のTransformです。
	/// </summary>
	private Transform m_HomingTarget;

	/// <summary>
	/// 追尾対象からキャッシュしたColliderです。
	/// </summary>
	private Collider m_HomingTargetCollider;

	/// <summary>
	/// ゴールなど、固定座標を追尾する場合の位置です。
	/// </summary>
	private Vector3 m_HomingTargetPosition;

	/// <summary>
	/// Transformを追尾するかどうかです。
	/// falseの場合は固定座標を追尾します。
	/// </summary>
	private bool m_IsHomingTargetTransform;

	/// <summary>
	/// 追尾開始からの経過時間です。
	/// </summary>
	private float m_HomingElapsedTime;

	/// <summary>
	/// 追尾中の累積バウンド回数です。
	/// </summary>
	private int m_HomingBounceCount;

	/// <summary>
	/// バウンド直後に反射方向を維持する残り時間です。
	/// </summary>
	private float m_HomingBounceRedirectRemaining;

	/// <summary>
	/// 追尾中の障害物開通判定用レイヤーマスクです。
	/// </summary>
	private int m_HomingObstacleMask;

	/// <summary>
	/// 障害物レイヤーマスクが構築済みかどうかです。
	/// </summary>
	private bool m_HasHomingObstacleMask;

	/// <summary>
	/// Transformを持つ対象への追尾を開始します。
	/// 味方プレイヤー用です。
	/// </summary>
	public void BeginHoming(Transform target, float initialSpeed)
	{
		if (!IsServer || target == null)
		{
			return;
		}

		m_HomingTarget = target;
		m_HomingTargetCollider =
			target.GetComponent<Collider>() ??
			target.GetComponentInChildren<Collider>();
		m_IsHomingTargetTransform = true;

		StartHoming(initialSpeed);
	}

	/// <summary>
	/// 固定座標への追尾を開始します。
	/// 相手ゴール用です。
	/// </summary>
	public void BeginHoming(Vector3 targetPosition, float initialSpeed)
	{
		if (!IsServer)
		{
			return;
		}

		m_HomingTarget = null;
		m_HomingTargetCollider = null;
		m_HomingTargetPosition = targetPosition;
		m_IsHomingTargetTransform = false;

		StartHoming(initialSpeed);
	}

	/// <summary>
	/// 追尾状態を開始します。
	/// </summary>
	private void StartHoming(float initialSpeed)
	{
		m_Rigidbody.isKinematic = false;
		m_Rigidbody.useGravity = false;

		m_Rigidbody.linearVelocity = Vector3.zero;
		m_Rigidbody.angularVelocity = Vector3.zero;

		m_SurfaceGravity.Clear();

		m_FlightDirection = Vector3.zero;
		m_PrePhysicsVelocity = Vector3.zero;
		m_CurrentHomingSpeed = Mathf.Max(0.0f, initialSpeed);
		m_HomingElapsedTime = 0.0f;
		m_HomingBounceCount = 0;
		m_HomingBounceRedirectRemaining = 0.0f;
		if (!m_HasHomingObstacleMask)
		{
			m_HomingObstacleMask = BuildHomingObstacleMask();
			m_HasHomingObstacleMask = true;
		}

		if (m_CurrentHomingSpeed <= Mathf.Max(0.0f, m_StopSpeedThreshold))
		{
			ClearHomingState();
			BeginSurfaceSeeking();
			return;
		}

		ChangeMotionState(EBallMotionState.Homing);
	}

	/// <summary>
	/// 追尾対象へ向かってボールを移動させます。
	/// 障害物がある間は反射・旋回しながら進み、視線が通ったら直進追尾へ戻します。
	/// 速度減衰は通常飛行と同一の計算式・パラメータを使用します。
	/// </summary>
	private void UpdateHoming()
	{
		if (!TryGetHomingTargetPosition(out Vector3 targetPosition))
		{
			ClearHomingState();
			BeginSurfaceSeeking();
			return;
		}

		m_HomingElapsedTime += Time.fixedDeltaTime;
		if (m_HomingElapsedTime >= m_MaxHomingDuration)
		{
			EndHoming();
			return;
		}

		Vector3 currentPosition = m_Rigidbody.position;
		Vector3 toTarget = targetPosition - currentPosition;

		// 対象に十分近づいたら通常飛行へ移行し、速度を維持したまま対象方向へ進ませる
		float endDistance = Mathf.Max(0.1f, m_HomingEndDistance);
		if (toTarget.sqrMagnitude <= endDistance * endDistance)
		{
			EndHoming();
			return;
		}

		if (toTarget.sqrMagnitude <= Mathf.Epsilon)
		{
			return;
		}

		if (m_HomingBounceRedirectRemaining > 0.0f)
		{
			m_HomingBounceRedirectRemaining = Mathf.Max(
				0.0f,
				m_HomingBounceRedirectRemaining - Time.fixedDeltaTime);
		}

		Vector3 targetDirection = toTarget.normalized;
		bool hasClearPath = HasClearPathToTarget(
			currentPosition,
			targetPosition);

		Vector3 desiredDirection = ResolveHomingFlightDirection(
			targetDirection,
			hasClearPath);

		m_CurrentHomingSpeed = Mathf.MoveTowards(
			m_CurrentHomingSpeed,
			0.0f,
			m_FlightDeceleration * Time.fixedDeltaTime);

		Vector3 nextVelocity = desiredDirection * m_CurrentHomingSpeed;

		m_Rigidbody.linearVelocity = nextVelocity;
		m_PrePhysicsVelocity = nextVelocity;
		m_FlightDirection = desiredDirection;

		if (m_CurrentHomingSpeed <= m_StopSpeedThreshold)
		{
			EndHoming();
		}
	}

	/// <summary>
	/// 追尾中に障害物へ接触した際、バウンド自体では減衰させずに反射方向へ進ませます。
	/// </summary>
	private void HandleHomingCollision(
		Collider hitCollider,
		ContactPoint contact,
		Vector3 incomingVelocity)
	{
		if (IsHomingTargetCollider(hitCollider))
		{
			EndHoming();
			return;
		}

		m_HomingBounceCount++;
		if (m_HomingBounceCount > m_MaxHomingBounceCount ||
			m_CurrentHomingSpeed <= m_StopSpeedThreshold)
		{
			EndHoming();
			return;
		}

		Vector3 referenceIncoming = incomingVelocity.sqrMagnitude > Mathf.Epsilon
			? incomingVelocity
			: m_FlightDirection * m_CurrentHomingSpeed;

		Vector3 bounceVelocity = m_Bounce.CalculateBounceVelocity(
			referenceIncoming,
			contact.normal,
			1.0f,
			1.0f,
			m_MinBounceNormalSpeed,
			0.0f,
			0.0f);

		Vector3 bounceDirection = bounceVelocity.sqrMagnitude > Mathf.Epsilon
			? bounceVelocity.normalized
			: contact.normal.normalized;

		if (TryGetHomingTargetPosition(out Vector3 targetPosition))
		{
			Vector3 toTarget = targetPosition - m_Rigidbody.position;
			Vector3 tangentToTarget = Vector3.ProjectOnPlane(toTarget, contact.normal);
			if (tangentToTarget.sqrMagnitude > Mathf.Epsilon)
			{
				Vector3 biasedDirection =
					bounceDirection +
					tangentToTarget.normalized * m_HomingBounceTargetTangentialBias;

				if (biasedDirection.sqrMagnitude > Mathf.Epsilon)
				{
					bounceDirection = biasedDirection.normalized;
				}
			}
		}

		Vector3 nextVelocity = bounceDirection * m_CurrentHomingSpeed;

		m_Rigidbody.isKinematic = false;
		m_Rigidbody.linearVelocity = nextVelocity;
		m_PrePhysicsVelocity = nextVelocity;
		m_FlightDirection = bounceDirection;
		m_HomingBounceRedirectRemaining = m_HomingBounceRedirectDuration;
	}

	/// <summary>
	/// 現在の追尾目標座標を取得します。
	/// </summary>
	private bool TryGetHomingTargetPosition(out Vector3 targetPosition)
	{
		if (!m_IsHomingTargetTransform)
		{
			targetPosition = m_HomingTargetPosition;
			return true;
		}

		if (m_HomingTarget == null)
		{
			targetPosition = Vector3.zero;
			return false;
		}

		targetPosition = m_HomingTargetCollider != null
			? m_HomingTargetCollider.bounds.center
			: m_HomingTarget.position;

		return true;
	}

	/// <summary>
	/// バウンド回避猶予および視線開通状況に応じて、次フレームの進行方向を決定します。
	/// </summary>
	private Vector3 ResolveHomingFlightDirection(
		Vector3 targetDirection,
		bool hasClearPath)
	{
		if (hasClearPath &&
			(m_HomingBounceRedirectRemaining <= 0.0f ||
			m_FlightDirection.sqrMagnitude <= Mathf.Epsilon ||
			Vector3.Dot(m_FlightDirection, targetDirection) > 0.0f))
		{
			m_HomingBounceRedirectRemaining = 0.0f;
			return targetDirection;
		}

		if (m_FlightDirection.sqrMagnitude <= Mathf.Epsilon)
		{
			return targetDirection;
		}

		if (m_HomingBounceRedirectRemaining > 0.0f)
		{
			return m_FlightDirection.normalized;
		}

		Vector3 steeredDirection = Vector3.RotateTowards(
			m_FlightDirection.normalized,
			targetDirection,
			m_HomingObstructedTurnSpeedRadians * Time.fixedDeltaTime,
			0.0f);

		return steeredDirection.sqrMagnitude > Mathf.Epsilon
			? steeredDirection.normalized
			: targetDirection;
	}

	/// <summary>
	/// 現在位置から追尾目標までの間に地形などの障害物がないか判定します。
	/// </summary>
	private bool HasClearPathToTarget(
		Vector3 currentPosition,
		Vector3 targetPosition)
	{
		Vector3 centerOffset = m_BallCollider != null
			? transform.TransformVector(m_BallCollider.center)
			: Vector3.zero;

		Vector3 castOrigin = currentPosition + centerOffset;
		Vector3 toTargetFromOrigin = targetPosition - castOrigin;
		float castDistanceSqr = toTargetFromOrigin.sqrMagnitude;
		if (castDistanceSqr <= m_MinimumCalculationDistance * m_MinimumCalculationDistance)
		{
			return true;
		}

		float castRadius = m_BallCollider != null
			? Mathf.Max(GetBallWorldRadius() * m_HomingClearCastRadiusScale, m_MinimumCalculationDistance)
			: m_MinimumCalculationDistance;

		float castDistance = Mathf.Sqrt(castDistanceSqr);
		Vector3 castDirection = toTargetFromOrigin / castDistance;

		int hitCount = Physics.SphereCastNonAlloc(
			castOrigin,
			castRadius,
			castDirection,
			m_SurfaceObstacleHits,
			castDistance,
			m_HomingObstacleMask,
			QueryTriggerInteraction.Ignore);

		for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
		{
			Collider hitCollider = m_SurfaceObstacleHits[hitIndex].collider;
			if (hitCollider == null ||
				hitCollider.attachedRigidbody == m_Rigidbody ||
				IsHomingTargetCollider(hitCollider))
			{
				continue;
			}

			return false;
		}

		return true;
	}

	/// <summary>
	/// 追尾中の開通判定で使用する障害物レイヤーマスクを構築します。
	/// </summary>
	private int BuildHomingObstacleMask()
	{
		int mask = m_BallCollisionMask;

		int playerLayer = LayerMask.NameToLayer("Player");
		if (playerLayer >= 0)
		{
			mask &= ~(1 << playerLayer);
		}

		int ballLayer = LayerMask.NameToLayer("Ball");
		if (ballLayer >= 0)
		{
			mask &= ~(1 << ballLayer);
		}

		int goalLayer = LayerMask.NameToLayer("Goal");
		if (goalLayer >= 0)
		{
			mask &= ~(1 << goalLayer);
		}

		return mask;
	}

	/// <summary>
	/// 指定Colliderが現在の追尾対象に属しているか判定します。
	/// </summary>
	private bool IsHomingTargetCollider(Collider targetCollider)
	{
		if (targetCollider == null || !m_IsHomingTargetTransform || m_HomingTarget == null)
		{
			return false;
		}

		return targetCollider == m_HomingTargetCollider ||
			targetCollider.transform == m_HomingTarget ||
			targetCollider.transform.IsChildOf(m_HomingTarget);
	}

	/// <summary>
	/// 追尾を終了して通常飛行状態へ戻します。
	/// </summary>
	private void EndHoming()
	{
		ClearHomingState();

		Vector3 currentVelocity =
			m_Rigidbody.linearVelocity;

		if (currentVelocity.sqrMagnitude > Mathf.Epsilon)
		{
			m_FlightDirection = currentVelocity.normalized;
		}

		ChangeMotionState(EBallMotionState.Flying);
	}

	/// <summary>
	/// 追尾状態の内部変数を初期化します。
	/// </summary>
	private void ClearHomingState()
	{
		m_HomingTarget = null;
		m_HomingTargetCollider = null;
		m_HomingTargetPosition = Vector3.zero;
		m_IsHomingTargetTransform = false;
		m_CurrentHomingSpeed = 0.0f;
		m_HomingElapsedTime = 0.0f;
		m_HomingBounceCount = 0;
		m_HomingBounceRedirectRemaining = 0.0f;
	}
}
