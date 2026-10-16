using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 等速直線移動(落下)・空中ホバリング・着地衝突処理を担当するコンポーネント。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class GravityMover : NetworkBehaviour, IGravityMover
{
	[Header("移動パラメータ")]
	// 想定設定値: 20.0f (ゲームスピードに応じて要調整)
	[SerializeField] private float m_BaseFallSpeed;

	[Header("落下中の着地姿勢回転設定")]
	// 想定設定値: 4.0f (落下中に着地面法線へ向ける回転速度)
	[SerializeField] private float m_FallingRotationSpeed;

	[Header("連続衝突判定設定")]
	// 想定設定値: true (所有者端末のRigidbodyで連続衝突判定を有効化)
	[SerializeField] private bool m_EnableContinuousDetectionForOwner;

	[Header("接地・レイヤーマスク設定")]
	// 想定設定値: Layer 7 (Ground等のマスク、未指定時は全レイヤー対象)
	[SerializeField] private LayerMask m_GroundLayerMask;

	[Header("デバッグ表示設定")]
	// 想定設定値: true
	[SerializeField] private bool m_EnableDebugDraw;

	private Rigidbody m_Rigidbody;
	private CapsuleCollider m_CapsuleCollider;
	private PlayerGroundDetector m_GroundDetector;
	private Vector3 m_CurrentDirection;
	private float m_SpeedMultiplier = 1.0f;
	private bool m_IsFalling;
	private bool m_IsAimHovering;

	// 落下目標情報
	private Vector3 m_FallingTargetPoint;
	private bool m_HasFallingTargetPoint;
	private Vector3 m_FallingTargetNormal = Vector3.up;
	private bool m_HasFallingTargetNormal;

	/// <summary>
	/// 飛行移動から地面・壁面への着地が完了した際に発火するイベント。
	/// </summary>
	public event Action OnLanded;

	/// <summary>
	/// 現在等速直線移動(落下)中であるかを取得します。
	/// </summary>
	public bool IsFalling => m_IsFalling;

	private void Awake()
	{
		m_Rigidbody = GetComponent<Rigidbody>();
		m_CapsuleCollider = GetComponent<CapsuleCollider>();
		m_GroundDetector = GetComponent<PlayerGroundDetector>();

		if (m_Rigidbody != null)
		{
			m_Rigidbody.useGravity = false;

			if (!NetworkManager.Singleton || !NetworkManager.Singleton.IsListening)
			{
				if (m_EnableContinuousDetectionForOwner)
				{
					m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
				}
			}
		}
	}

	public override void OnNetworkSpawn()
	{
		base.OnNetworkSpawn();

		if (IsOwner && m_Rigidbody != null && m_EnableContinuousDetectionForOwner)
		{
			m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
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

		if (m_Rigidbody != null)
		{
			m_Rigidbody.position = transform.position;
			m_Rigidbody.isKinematic = false;
		}

		if (m_HasFallingTargetPoint)
		{
			Vector3 targetDirection = m_FallingTargetPoint - transform.position;
			m_CurrentDirection = targetDirection.sqrMagnitude > 0.0001f
				? targetDirection.normalized
				: direction.normalized;
			m_HasFallingTargetPoint = false;
		}
		else
		{
			m_CurrentDirection = direction.normalized;
		}

		m_IsFalling = true;
	}

	/// <summary>
	/// 落下先の着地点と着地面の法線を設定します。
	/// </summary>
	/// <param name="landingPoint">予測着地点</param>
	/// <param name="landingNormal">着地面の法線</param>
	public void SetFallingTarget(Vector3 landingPoint, Vector3 landingNormal)
	{
		m_FallingTargetPoint = landingPoint;
		m_HasFallingTargetPoint = true;
		m_FallingTargetNormal = landingNormal.normalized;
		m_HasFallingTargetNormal = true;
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
	/// エイム中のホバリング状態を開始します。
	/// </summary>
	public void StartAimHover()
	{
		m_IsAimHovering = true;
		m_IsFalling = false;

		if (m_Rigidbody != null)
		{
			m_Rigidbody.linearVelocity = Vector3.zero;
			m_Rigidbody.angularVelocity = Vector3.zero;
			m_Rigidbody.isKinematic = true;
		}
	}

	/// <summary>
	/// エイム中のホバリング状態を終了します。
	/// </summary>
	public void StopAimHover()
	{
		m_IsAimHovering = false;

		if (m_Rigidbody != null)
		{
			m_Rigidbody.position = transform.position;
			m_Rigidbody.isKinematic = false;
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
		if (IsSpawned && !IsOwner)
		{
			return;
		}

		if (m_IsAimHovering)
		{
			return;
		}

		if (m_IsFalling && m_Rigidbody != null)
		{
			float speed = m_BaseFallSpeed > 0f ? m_BaseFallSpeed : 20.0f;
			m_Rigidbody.linearVelocity = m_CurrentDirection * (speed * m_SpeedMultiplier);
			UpdateFallingRotation();
		}
	}

	private void UpdateFallingRotation()
	{
		if (!m_HasFallingTargetNormal || m_Rigidbody == null)
		{
			return;
		}

		Vector3 targetForward = Vector3.ProjectOnPlane(m_CurrentDirection, m_FallingTargetNormal);
		if (targetForward.sqrMagnitude < 0.001f)
		{
			targetForward = Vector3.ProjectOnPlane(transform.forward, m_FallingTargetNormal);
		}

		if (targetForward.sqrMagnitude < 0.001f)
		{
			return;
		}

		targetForward.Normalize();
		Quaternion targetRotation = Quaternion.LookRotation(targetForward, m_FallingTargetNormal);
		float speed = m_FallingRotationSpeed > 0f ? m_FallingRotationSpeed : 4.0f;
		m_Rigidbody.MoveRotation(Quaternion.Slerp(m_Rigidbody.rotation, targetRotation, speed * Time.fixedDeltaTime));
	}

	private void OnCollisionEnter(Collision collision)
	{
		if (IsSpawned && !IsOwner)
		{
			return;
		}

		if (m_IsFalling && collision.contactCount > 0)
		{
			ContactPoint contact = collision.GetContact(0);
			Vector3 hitNormal = contact.normal;

			if (m_EnableDebugDraw)
			{
				Debug.Log($"[GravityMover-Debug] 着地検出: hitNormal={hitNormal}, 目標Normal={m_FallingTargetNormal}, hasTargetNormal={m_HasFallingTargetNormal}, 接触物体={collision.gameObject.name}, contactPoint={contact.point}, playerPos={transform.position}");
			}

			StopFalling();

			Vector3 finalGroundNormal = hitNormal;
			Vector3 snapPoint = contact.point;

			// 1. 目標面近接判定(カス当たり救済): 接触点周囲に本来の目標面が存在する場合は目標面を採用
			if (m_HasFallingTargetNormal)
			{
				float dot = Vector3.Dot(hitNormal, m_FallingTargetNormal);
				if (dot < 0.6f)
				{
					int layerMask = m_GroundLayerMask.value == 0 ? ~0 : m_GroundLayerMask.value;
					Vector3 checkOrigin = contact.point + m_FallingTargetNormal * 0.3f;
					if (Physics.Raycast(checkOrigin, -m_FallingTargetNormal, out RaycastHit targetHit, 0.8f, layerMask, QueryTriggerInteraction.Ignore))
					{
						if (Vector3.Dot(targetHit.normal, m_FallingTargetNormal) > 0.7f)
						{
							finalGroundNormal = targetHit.normal;
							snapPoint = targetHit.point;
							if (m_EnableDebugDraw)
							{
								Debug.LogWarning($"[GravityMover-Debug] カス当たり補正: hitNormal={hitNormal}を補正し、近接目標面={finalGroundNormal}へ着地");
							}
						}
					}
				}
			}

			// 2. 移動中の回転姿勢を着地面法線へ完全に合わせる
			Vector3 targetForward = Vector3.ProjectOnPlane(transform.forward, finalGroundNormal);
			if (targetForward.sqrMagnitude < 0.001f)
			{
				targetForward = Vector3.ProjectOnPlane(m_CurrentDirection, finalGroundNormal);
			}

			if (targetForward.sqrMagnitude > 0.001f)
			{
				targetForward.Normalize();
				Quaternion targetRotation = Quaternion.LookRotation(targetForward, finalGroundNormal);
				m_Rigidbody.MoveRotation(targetRotation);
			}

			// 3. 3D全方位足元サーフェススナップ: 接触面直上にカプセルの底面を密着配置し、空中に浮くスタックを物理的に根絶
			float footOffset = m_CapsuleCollider != null ? m_CapsuleCollider.height * 0.5f : 1.0f;
			Vector3 targetPos = snapPoint + finalGroundNormal * (footOffset + 0.02f);
			m_Rigidbody.position = targetPos;
			m_Rigidbody.linearVelocity = Vector3.zero;

			// 落下中の着地法線情報をクリア
			m_HasFallingTargetNormal = false;

			// 接地センサーに着地完了と法線を通知
			if (m_GroundDetector != null)
			{
				m_GroundDetector.SetLandedNormal(finalGroundNormal);
			}

			// 着地完了イベントを発火
			OnLanded?.Invoke();
		}
	}
}
