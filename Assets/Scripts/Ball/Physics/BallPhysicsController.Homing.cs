using UnityEngine;

/// <summary>
/// パスやゴール方向へボールを追尾移動させるHoming状態を担当します。
/// </summary>
/// <remarks>
/// 主な処理の流れ:
/// 1. Transformまたは固定World座標を追尾対象として登録します。
/// 2. Homing開始時に他の自由移動状態を整理します。
/// 3. FixedUpdateごとに対象方向へ一定速度で移動します。
/// 4. 終了距離へ到達した場合はHomingを解除します。
///
/// 通常のFlying・Surface Physicsとは独立したMotionStateとして管理します。
/// </remarks>
public partial class BallPhysicsController
{
	[Header("追尾パス")]
	[Tooltip("追尾中の移動速度です。")]
	[SerializeField]
	[Min(0.1f)]
	private float m_HomingSpeed = 25.0f;

	[Tooltip("追尾を終了する距離です。")]
	[SerializeField]
	[Min(0.1f)]
	private float m_HomingEndDistance = 0.5f;

	/// <summary>
	/// 追尾対象のTransformです。
	/// </summary>
	private Transform m_HomingTarget;

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
	/// Transformを持つ対象への追尾を開始します。
	/// 味方プレイヤー用です。
	/// </summary>
	public void BeginHoming(Transform target)
	{
		if (!IsServer || target == null)
		{
			return;
		}

		m_HomingTarget = target;
		m_IsHomingTargetTransform = true;

		StartHoming();
	}

	/// <summary>
	/// 固定座標への追尾を開始します。
	/// 相手ゴール用です。
	/// </summary>
	public void BeginHoming(Vector3 targetPosition)
	{
		if (!IsServer)
		{
			return;
		}

		m_HomingTarget = null;
		m_HomingTargetPosition = targetPosition;
		m_IsHomingTargetTransform = false;

		StartHoming();
	}

	/// <summary>
	/// 追尾状態を開始します。
	/// </summary>
	private void StartHoming()
	{
		m_Rigidbody.isKinematic = false;
		m_Rigidbody.useGravity = false;

		m_Rigidbody.linearVelocity = Vector3.zero;
		m_Rigidbody.angularVelocity = Vector3.zero;

		m_SurfaceGravity.Clear();

		m_FlightDirection = Vector3.zero;
		m_PrePhysicsVelocity = Vector3.zero;

		ChangeMotionState(EBallMotionState.Homing);
	}

	/// <summary>
	/// 追尾対象へ向かってボールを移動させます。
	/// </summary>
	private void UpdateHoming()
	{
		Vector3 targetPosition;

		// 味方プレイヤーの場合は現在位置を取得
		if (m_IsHomingTargetTransform)
		{
			if (m_HomingTarget == null)
			{
				BeginSurfaceSeeking();
				return;
			}

			targetPosition = m_HomingTarget.position;
		}
		// ゴールの場合は保存した座標を使用
		else
		{
			targetPosition = m_HomingTargetPosition;
		}

		Vector3 toTarget =
			targetPosition - m_Rigidbody.position;

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

		// 対象の現在位置に向かって移動
		Vector3 direction = toTarget.normalized;

		m_Rigidbody.linearVelocity =
			direction * m_HomingSpeed;

		m_PrePhysicsVelocity =
			m_Rigidbody.linearVelocity;

		m_FlightDirection =
			direction;
	}

	/// <summary>
	/// 追尾を終了して通常飛行状態へ戻します。
	/// </summary>
	private void EndHoming()
	{
		m_HomingTarget = null;

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
		m_HomingTargetPosition = Vector3.zero;
		m_IsHomingTargetTransform = false;
	}
}
