using DG.Tweening;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// キャラクターの物理移動・等速直線運動・ホバリング・接地姿勢制御を担当するクラス。
/// PlayerControllerからIGravityMoverインターフェース経由で制御されます。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class GravityMover : NetworkBehaviour, IGravityMover
{
	[Header("移動パラメータ")]
	// 想定設定値: 20.0f (ゲームスピードに応じて要調整)
	[SerializeField] private float m_BaseFallSpeed;

	[Header("姿勢補正アニメーション設定")]
	// 想定設定値: 0.25f (接地時の回転補正にかける秒数)
	[SerializeField] private float m_AlignDuration;

	// 想定設定値: Ease.OutQuad (衝突時の衝撃を和らげる減速イージング)
	[SerializeField] private Ease m_AlignEase;

	private Rigidbody m_Rigidbody;
	private Vector3 m_CurrentDirection;
	private float m_SpeedMultiplier = 1.0f;
	private bool m_IsFalling;
	private bool m_IsGrounded;
	private Tween m_AlignTween;

	/// <summary>
	/// 現在等速直線移動(落下)中であるかを取得します。
	/// </summary>
	public bool IsFalling
	{
		get => m_IsFalling;
	}

	/// <summary>
	/// 地面または壁面に接地しているかを取得します。
	/// </summary>
	public bool IsGrounded
	{
		get => m_IsGrounded;
	}

	private void Awake()
	{
		m_Rigidbody = GetComponent<Rigidbody>();
		if (m_Rigidbody != null)
		{
			// 無重力状態で自前ベクトル推進を行うため物理重力を無効化
			m_Rigidbody.useGravity = false;
		}
	}

	/// <summary>
	/// 指定された方向へ等速直線移動(落下)を開始します。
	/// </summary>
	/// <param name="direction">進行方向ベクトル</param>
	public void StartFalling(Vector3 direction)
	{
		if (direction.sqrMagnitude < 0.0001f)
		{
			return;
		}

		// 進行中の姿勢補正アニメーションがあれば中断
		m_AlignTween?.Kill();

		m_CurrentDirection = direction.normalized;
		m_IsFalling = true;
		m_IsGrounded = false;

		// 進行方向へ即座に向きを設定
		transform.forward = m_CurrentDirection;
	}

	/// <summary>
	/// 落下を停止し、空中で浮遊・静止(ホバリング)させます。
	/// </summary>
	public void StopFalling()
	{
		m_IsFalling = false;

		if (m_Rigidbody != null)
		{
			m_Rigidbody.linearVelocity = Vector3.zero;
			m_Rigidbody.angularVelocity = Vector3.zero;
		}
	}

	/// <summary>
	/// 移動速度の倍率を設定します(ボール所持時のデバフ等)。
	/// </summary>
	/// <param name="multiplier">速度倍率</param>
	public void SetSpeedMultiplier(float multiplier)
	{
		m_SpeedMultiplier = multiplier;
	}

	private void FixedUpdate()
	{
		// ネットワーク同期下では所有者(IsOwner)のみ物理挙動を計算
		if (IsSpawned && !IsOwner)
		{
			return;
		}

		if (m_IsFalling && m_Rigidbody != null)
		{
			m_Rigidbody.linearVelocity = m_CurrentDirection * (m_BaseFallSpeed * m_SpeedMultiplier);
			if (m_CurrentDirection != Vector3.zero)
			{
				transform.forward = m_CurrentDirection;
			}
		}
	}

	private void OnCollisionEnter(Collision collision)
	{
		// ネットワーク同期下では所有者(IsOwner)のみ判定
		if (IsSpawned && !IsOwner)
		{
			return;
		}

		if (collision.contactCount > 0)
		{
			ContactPoint contact = collision.GetContact(0);
			Vector3 hitNormal = contact.normal;

			// 落下を停止してホバリング状態へ遷移
			StopFalling();

			// 既存の姿勢補正アニメーションを中断
			m_AlignTween?.Kill();

			// キャラクターの足元(transform.up)が衝突面の法線を向くように姿勢補正
			Quaternion targetRotation = Quaternion.FromToRotation(transform.up, hitNormal) * transform.rotation;

			if (m_AlignDuration > 0f)
			{
				m_AlignTween = transform.DORotateQuaternion(targetRotation, m_AlignDuration)
					.SetEase(m_AlignEase);
			}
			else
			{
				transform.rotation = targetRotation;
			}

			m_IsGrounded = true;
		}
	}

	private void OnCollisionExit(Collision collision)
	{
		if (IsSpawned && !IsOwner)
		{
			return;
		}

		m_IsGrounded = false;
	}

	public override void OnDestroy()
	{
		m_AlignTween?.Kill();
		base.OnDestroy();
	}
}
