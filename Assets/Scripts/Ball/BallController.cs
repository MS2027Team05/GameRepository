// BallController.cs
using System;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// ボール本体の所持状態と物理状態を管理します。
/// </summary>
/// <remarks>
/// 主な処理の流れ:
/// 1. Hostがプレイヤーからの取得要求を検証します。
/// 2. 取得成功時はプレイヤーへNetworkObjectを親子付けします。
/// 3. 所持状態をNetworkVariableで全Clientへ同期します。
/// 4. シュート時は親子関係を解除して物理挙動へ戻します。
/// 5. 強制解放時も同じく親子関係を解除します。
/// 6. シュート・解放直後は元所持者のみ一定時間再キャッチを禁止します。
/// </remarks>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(NetworkTransform))]
public class BallController : NetworkBehaviour
{
	[Header("ボールの当たり判定")]
	[Tooltip("ボールの物理衝突とキャッチ判定に使用するColliderです。")]
	[SerializeField] private Collider m_PhysicsCollider;
	[Tooltip("ボール周辺の検知に使用するSensor用Colliderです。")]
	[SerializeField] private Collider m_SensorCollider;

	[Header("シュート設定")]
	[Tooltip("ボールを射出するときの初速度に使用する値です(おおむねm/s)。")]
	[SerializeField] private float m_ShootPower;

	[Header("再キャッチ防止")]
	[Tooltip("射出または強制解放した元の所持者が、再びキャッチできるまでの時間です(秒)。")]
	[SerializeField] private float m_RepickupDelay;

	[Header("味方パス受取ボーナス判定")]
	[Tooltip("味方パス回復の資格を失効させるボール速度のしきい値です(m/s)。この速度以下になると資格が失われ、再加速しても復活しません。")]
	[SerializeField] private float m_PassBonusMinSpeed;

	private readonly NetworkVariable<ulong> m_CurrentHolderId =
		new NetworkVariable<ulong>();

	private readonly NetworkVariable<ulong>
		m_CurrentHolderNetworkObjectId =
			new NetworkVariable<ulong>();

	private readonly NetworkVariable<bool> m_HasHolder =
		new NetworkVariable<bool>();

	private Rigidbody m_Rigidbody;
	private NetworkTransform m_NetworkTransform;
	private BallPhysicsController m_BallPhysics;
	private PlayerBallCarrier m_CurrentHolderCarrier;
	private Vector3 m_InitialPosition;
	private Quaternion m_InitialRotation;
	public bool IsMatchPaused { get; private set; }

	private bool m_HasRepickupLock;
	private ulong m_LastHolderClientId;
	private float m_RepickupUnlockTime;

	// Host側で、最後の正常なシュートが味方パスとして受取可能か記録します。
	private bool m_IsPassInFlight;
	private ETeamType m_LastPasserTeam;

	/// <summary>
	/// ボールがシュートされたときにHost側で通知します。
	/// </summary>
	public event Action OnShot;

	/// <summary>
	/// タックルなどによってボールが強制解放されたときに
	/// Host側で通知します。
	/// </summary>
	public event Action OnReleased;

	/// <summary>
	/// 現在ボールを所持しているプレイヤーのClientIdを取得します。
	/// </summary>
	public ulong CurrentHolderId
	{
		get
		{
			return m_CurrentHolderId.Value;
		}
	}

	/// <summary>
	/// 現在ボールに所持者が存在するか取得します。
	/// </summary>
	public bool HasHolder
	{
		get
		{
			return m_HasHolder.Value;
		}
	}

	/// <summary>
	/// Rigidbodyの参照を取得します。
	/// </summary>
	private void Awake()
	{
		m_Rigidbody =
			GetComponent<Rigidbody>();
		m_NetworkTransform = GetComponent<NetworkTransform>();
		m_BallPhysics = GetComponent<BallPhysicsController>();
	}

	/// <summary>
	/// NetworkObjectのSpawn時に所持状態の変更通知を登録し、
	/// 現在の所持状態を反映します。
	/// </summary>
	public override void OnNetworkSpawn()
	{
		if (IsServer)
		{
			m_InitialPosition = transform.position;
			m_InitialRotation = transform.rotation;
			SetMatchPaused(MatchManager.Instance != null && MatchManager.Instance.MatchState != MatchManager.EMatchState.Playing);
		}
		m_HasHolder.OnValueChanged +=
			HandleHolderStateChanged;
		m_CurrentHolderNetworkObjectId.OnValueChanged +=
			HandleHolderNetworkObjectIdChanged;

		if (m_HasHolder.Value)
		{
			ApplyHolderState(true);
		}
	}

	private void FixedUpdate()
	{
		if (!IsSpawned || !IsServer)
		{
			return;
		}

		UpdatePassBonusEligibility();
	}

	private void LateUpdate()
	{
		UpdateHeldPosition();
	}

	/// <summary>
	/// 射出後のボール速度が設定値以下になったら、味方パスの回復資格を失効させます。
	/// 一度失効した資格は、ボールが再加速しても復活しません。
	/// </summary>
	private void UpdatePassBonusEligibility()
	{
		if (!m_IsPassInFlight || m_Rigidbody == null)
		{
			return;
		}

		float minimumSpeed = Mathf.Max(0.0f, m_PassBonusMinSpeed);
		if (m_Rigidbody.linearVelocity.sqrMagnitude > minimumSpeed * minimumSpeed)
		{
			return;
		}

		m_IsPassInFlight = false;
		m_LastPasserTeam = ETeamType.None;
	}

	/// <summary>
	/// NetworkObjectのDespawn時に所持状態の変更通知を解除します。
	/// </summary>
	public override void OnNetworkDespawn()
	{
		m_HasHolder.OnValueChanged -=
			HandleHolderStateChanged;
		m_CurrentHolderNetworkObjectId.OnValueChanged -=
			HandleHolderNetworkObjectIdChanged;
	}

	/// <summary>
	/// 指定されたColliderがキャッチ判定用Colliderか確認します。
	/// </summary>
	/// <param name="targetCollider">
	/// 判定対象のColliderです。
	/// </param>
	/// <returns>
	/// キャッチ判定用Colliderの場合はtrueです。
	/// </returns>
	public bool IsCatchCollider(
		Collider targetCollider)
	{
		return targetCollider ==
			m_PhysicsCollider;
	}

	/// <summary>
	/// 指定されたColliderがSensor用Colliderか確認します。
	/// </summary>
	/// <param name="targetCollider">
	/// 判定対象のColliderです。
	/// </param>
	/// <returns>
	/// Sensor用Colliderの場合はtrueです。
	/// </returns>
	public bool IsSensorCollider(
		Collider targetCollider)
	{
		return targetCollider ==
			m_SensorCollider;
	}

	/// <summary>
	/// Host上で指定プレイヤーをボールの所持者として設定します。
	/// </summary>
	public bool TrySetHolder(
		ulong holderClientId,
		ulong holderNetworkObjectId)
	{
		if (!IsServer || !CanPlayMatch())
		{
			return false;
		}

		if (m_HasHolder.Value)
		{
			return false;
		}

		if (m_HasRepickupLock &&
			Time.time >= m_RepickupUnlockTime)
		{
			m_HasRepickupLock = false;
		}

		if (m_HasRepickupLock &&
			holderClientId == m_LastHolderClientId)
		{
			return false;
		}

		if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(
			holderNetworkObjectId,
			out NetworkObject holderNetworkObject))
		{
			return false;
		}

		if (holderNetworkObject.OwnerClientId !=
			holderClientId)
		{
			return false;
		}

		PlayerBallCarrier holder =
			holderNetworkObject.GetComponent<PlayerBallCarrier>();

		if (holder == null ||
			holder.BallHoldPoint == null)
		{
			return false;
		}

		// キャッチ直前の速度を確認します。AttachToHolder後は速度が0になるため先に判定。
		UpdatePassBonusEligibility();

		if (!AttachToHolder(
			holderNetworkObject,
			holder))
		{
			return false;
		}

		m_CurrentHolderId.Value =
			holderClientId;

		m_CurrentHolderNetworkObjectId.Value =
			holderNetworkObjectId;

		m_HasHolder.Value =
			true;

		// 所持確定時に一度だけ判定し、強制ドロップや敵からの取得では付与しません。
		bool isFriendlyPass = m_IsPassInFlight &&
			holderClientId != m_LastHolderClientId &&
			m_LastPasserTeam != ETeamType.None;

		if (isFriendlyPass)
		{
			PlayerNetworkProfile receiverProfile = holderNetworkObject.GetComponent<PlayerNetworkProfile>();
			isFriendlyPass = receiverProfile != null && receiverProfile.Team == m_LastPasserTeam;
		}

		m_IsPassInFlight = false;
		m_LastPasserTeam = ETeamType.None;

		if (isFriendlyPass)
		{
			holder.NotifyFriendlyPassReceivedServer();
		}

		return true;
	}

	//

	/// <summary>
	/// Host上で現在の所持者からボールを奪い、
	/// 指定プレイヤーへ直接所持者を切り替えます。
	/// </summary>
	public bool TryStealHolder(
		ulong newHolderClientId,
		ulong newHolderNetworkObjectId)
	{
		if (!IsServer)
			return false;

		if (!m_HasHolder.Value)
		{
			// 誰も持っていない場合は通常取得
			return TrySetHolder(
				newHolderClientId,
				newHolderNetworkObjectId);
		}

		// 攻撃側プレイヤーを取得
		if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(
			newHolderNetworkObjectId,
			out NetworkObject newHolderNetworkObject))
		{
			return false;
		}

		if (newHolderNetworkObject.OwnerClientId != newHolderClientId)
			return false;

		PlayerBallCarrier newHolder =
			newHolderNetworkObject.GetComponent<PlayerBallCarrier>();

		if (newHolder == null ||
			newHolder.BallHoldPoint == null ||
			newHolder.HasBall)
		{
			return false;
		}

		// 現在の所持者を取得
		if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(
			m_CurrentHolderNetworkObjectId.Value,
			out NetworkObject oldHolderNetworkObject))
		{
			return false;
		}

		PlayerBallCarrier oldHolder =
			oldHolderNetworkObject.GetComponent<PlayerBallCarrier>();

		if (oldHolder == null)
			return false;

		// 現在の親子関係を解除
		if (!DetachFromHolder())
			return false;

		// 新しい所持者へ装着
		if (!AttachToHolder(
			newHolderNetworkObject,
			newHolder))
		{
			// 装着に失敗した場合は元の所持者へ戻す
			AttachToHolder(
				oldHolderNetworkObject,
				oldHolder);

			return false;
		}

		// 元の所持者の状態を解除
		oldHolder.SetBallState(false);

		// 所持者情報を更新
		m_CurrentHolderId.Value =
			newHolderClientId;

		m_CurrentHolderNetworkObjectId.Value =
			newHolderNetworkObjectId;

		// 新しい所持者の状態を設定
		newHolder.SetBallState(true);

		return true;
	}


	/// <summary>
	/// Host上で所持中のボールを指定方向へ射出します。
	/// </summary>
	public bool TryShoot(
		Vector3 shootDirection)
	{
		if (!IsServer || !CanPlayMatch())
		{
			return false;
		}

		if (!m_HasHolder.Value)
		{
			return false;
		}

		if (shootDirection.sqrMagnitude <=
			Mathf.Epsilon)
		{
			return false;
		}

		shootDirection.Normalize();

		ulong previousHolderClientId =
			m_CurrentHolderId.Value;

		// TryShoot成功時のみ使用するパス元チームを先に取得しておきます。
		ETeamType passerTeam = ETeamType.None;
		if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(
			m_CurrentHolderNetworkObjectId.Value,
			out NetworkObject previousHolderObject))
		{
			PlayerNetworkProfile profile = previousHolderObject.GetComponent<PlayerNetworkProfile>();
			if (profile != null)
			{
				passerTeam = profile.Team;
			}
		}

		StartRepickupLock(
			previousHolderClientId);

		if (!DetachFromHolder())
		{
			m_HasRepickupLock =
				false;

			return false;
		}

		m_HasHolder.Value =
			false;

		m_CurrentHolderId.Value =
			0;

		m_Rigidbody.linearVelocity =
			shootDirection *
			m_ShootPower;

		m_LastPasserTeam = passerTeam;
		m_IsPassInFlight = true;

		// BallPhysicsControllerへ、
		// シュート開始を通知します。
		OnShot?.Invoke();

		return true;
	}

	/// <summary>
	/// Host上で現在の所持者からボールを強制的に解放します。
	/// </summary>
	public void ReleaseBall()
	{
		if (!IsServer)
		{
			return;
		}

		if (!m_HasHolder.Value)
		{
			return;
		}

		ulong previousHolderClientId =
			m_CurrentHolderId.Value;

		StartRepickupLock(
			previousHolderClientId);

		if (!DetachFromHolder())
		{
			m_HasRepickupLock =
				false;

			return;
		}

		m_HasHolder.Value =
			false;

		m_CurrentHolderId.Value =
			0;

		// 強制解放されたボールは、味方パスボーナスの対象外です。
		m_IsPassInFlight = false;
		m_LastPasserTeam = ETeamType.None;

		// シュートではない解放として、
		// BallPhysicsControllerへ通知します。
		OnReleased?.Invoke();
	}

	/// <summary>
	/// 指定プレイヤーに対する再キャッチ禁止時間を開始します。
	/// </summary>
	private void StartRepickupLock(
		ulong holderClientId)
	{
		m_LastHolderClientId =
			holderClientId;

		m_RepickupUnlockTime =
			Time.time +
			Mathf.Max(
				0.0f,
				m_RepickupDelay);

		m_HasRepickupLock =
			m_RepickupDelay > 0.0f;
	}

	private bool CanPlayMatch()
	{
		return !IsMatchPaused && (MatchManager.Instance == null || MatchManager.Instance.MatchState == MatchManager.EMatchState.Playing);
	}

	/// <summary>
	/// サーバー側でボールの物理挙動と取得・シュートを停止します。
	/// </summary>
	public void SetMatchPaused(bool paused)
	{
		if (!IsServer || !IsSpawned) return;
		IsMatchPaused = paused;
		if (!m_Rigidbody.isKinematic)
		{
			m_Rigidbody.linearVelocity = Vector3.zero;
			m_Rigidbody.angularVelocity = Vector3.zero;
		}
		m_Rigidbody.isKinematic = paused || HasHolder;
	}

	/// <summary>
	/// 所持・再取得制限・物理状態を初期化し、生成時の位置へ戻して静止させます。
	/// </summary>
	public bool ResetForRestart()
	{
		if (!IsServer || !IsSpawned) return false;
		ReleaseBall();
		if (HasHolder || (transform.parent != null && !NetworkObject.TryRemoveParent(true)))
		{
			SetMatchPaused(true);
			return false;
		}

		m_HasRepickupLock = false;
		m_LastHolderClientId = 0;
		m_RepickupUnlockTime = 0f;
		m_CurrentHolderId.Value = 0;
		m_CurrentHolderCarrier = null;
		// Clientの所持解除通知が旧所持者を参照できるよう、所持者NetworkObjectIdは保持します。
		m_BallPhysics?.ResetMotionState();
		m_Rigidbody.useGravity = false;
		m_NetworkTransform.Teleport(m_InitialPosition, m_InitialRotation, transform.localScale);
		SetMatchPaused(true);
		return true;
	}

	/// <summary>
	/// NetworkVariableの所持状態変更を
	/// ローカル状態へ反映します。
	/// </summary>
	private void HandleHolderStateChanged(
		bool previousValue,
		bool newValue)
	{
		ApplyHolderState(
			newValue);

		if (!newValue)
		{
			m_CurrentHolderCarrier = null;
		}
	}

	/// <summary>
	/// 所持者のNetworkObjectId変更を検知し、旧所持者と新所持者の状態を更新します。
	/// </summary>
	private void HandleHolderNetworkObjectIdChanged(
		ulong previousValue,
		ulong newValue)
	{
		if (previousValue != 0 && previousValue != newValue)
		{
			UpdateCarrierBallState(previousValue, false);
		}

		if (newValue != 0 && m_HasHolder.Value)
		{
			UpdateCarrierBallState(newValue, true);
		}
	}

	/// <summary>
	/// 所持状態に合わせてColliderと
	/// プレイヤー側の状態を更新します。
	/// </summary>
	private void ApplyHolderState(
		bool hasHolder)
	{
		if (m_PhysicsCollider != null)
		{
			m_PhysicsCollider.enabled =
				!hasHolder;
		}

		if (m_SensorCollider != null)
		{
			m_SensorCollider.enabled =
				true;
		}

		SetHolderBallState(
			hasHolder);
	}

	/// <summary>
	/// Host上でボールを指定プレイヤーへ親子付けし、
	/// 所持位置へ移動します。
	/// </summary>
	private bool AttachToHolder(
		NetworkObject holderNetworkObject,
		PlayerBallCarrier holder)
	{
		if (!IsServer)
		{
			return false;
		}

		// Surface Physics中は既にKinematicのため、
		// Dynamic状態の場合だけ速度を停止します。
		if (!m_Rigidbody.isKinematic)
		{
			m_Rigidbody.linearVelocity =
				Vector3.zero;

			m_Rigidbody.angularVelocity =
				Vector3.zero;
		}

		m_Rigidbody.isKinematic =
			true;

		if (m_PhysicsCollider != null)
		{
			m_PhysicsCollider.enabled =
				false;
		}

		bool parentSucceeded =
			NetworkObject.TrySetParent(
				holderNetworkObject,
				false);

		if (!parentSucceeded)
		{
			if (m_PhysicsCollider != null)
			{
				m_PhysicsCollider.enabled =
					true;
			}

			m_Rigidbody.isKinematic =
				false;

			return false;
		}

		Transform holderTransform =
			holderNetworkObject.transform;

		transform.localPosition =
			holderTransform.InverseTransformPoint(
				holder.BallHoldPoint.position);

		transform.localRotation =
			Quaternion.Inverse(
				holderTransform.rotation) *
			holder.BallHoldPoint.rotation;

		m_CurrentHolderCarrier = holder;

		if (m_NetworkTransform != null && m_NetworkTransform.CanCommitToTransform)
		{
			m_NetworkTransform.Teleport(
				transform.position,
				transform.rotation,
				transform.localScale);
		}

		return true;
	}

	/// <summary>
	/// Host上でボールと所持者の親子関係を解除し、
	/// Rigidbodyによる物理挙動へ戻します。
	/// </summary>
	private bool DetachFromHolder()
	{
		if (!IsServer)
		{
			return false;
		}

		if (transform.parent != null)
		{
			bool removeSucceeded =
				NetworkObject.TryRemoveParent(
					true);

			if (!removeSucceeded)
			{
				return false;
			}
		}

		m_CurrentHolderCarrier = null;

		m_Rigidbody.isKinematic =
			false;

		if (m_PhysicsCollider != null)
		{
			m_PhysicsCollider.enabled =
				true;
		}

		return true;
	}

	/// <summary>
	/// 現在記録されている所持者へ
	/// ボール所持状態を通知します。
	/// </summary>
	private void SetHolderBallState(
		bool hasBall)
	{
		ulong holderNetworkObjectId =
			m_CurrentHolderNetworkObjectId.Value;

		if (holderNetworkObjectId == 0)
		{
			return;
		}

		UpdateCarrierBallState(
			holderNetworkObjectId,
			hasBall);
	}

	/// <summary>
	/// 指定されたNetworkObjectIdのプレイヤーへボール所持状態を通知します。
	/// </summary>
	private void UpdateCarrierBallState(
		ulong carrierNetworkObjectId,
		bool hasBall)
	{
		if (NetworkManager == null || NetworkManager.SpawnManager == null)
		{
			return;
		}

		if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(
			carrierNetworkObjectId,
			out NetworkObject carrierNetworkObject))
		{
			return;
		}

		PlayerBallCarrier carrier =
			carrierNetworkObject.GetComponent<PlayerBallCarrier>();

		if (carrier == null)
		{
			return;
		}

		carrier.SetBallState(hasBall);

		if (hasBall)
		{
			m_CurrentHolderCarrier = carrier;
		}
		else if (m_CurrentHolderCarrier == carrier)
		{
			m_CurrentHolderCarrier = null;
		}
	}

	/// <summary>
	/// ボール所持中、プレイヤーの腰ボーン(BallHoldPoint)へ追従させます。
	/// </summary>
	private void UpdateHeldPosition()
	{
		if (!m_HasHolder.Value)
		{
			return;
		}

		if (m_CurrentHolderCarrier == null)
		{
			ulong holderNetworkObjectId = m_CurrentHolderNetworkObjectId.Value;
			if (holderNetworkObjectId != 0 &&
				NetworkManager != null &&
				NetworkManager.SpawnManager != null &&
				NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(holderNetworkObjectId, out NetworkObject holderNetworkObject))
			{
				m_CurrentHolderCarrier = holderNetworkObject.GetComponent<PlayerBallCarrier>();
			}
		}

		if (m_CurrentHolderCarrier == null || m_CurrentHolderCarrier.BallHoldPoint == null)
		{
			return;
		}

		Transform holderTransform = m_CurrentHolderCarrier.transform;
		Transform holdPoint = m_CurrentHolderCarrier.BallHoldPoint;

		transform.localPosition =
			holderTransform.InverseTransformPoint(
				holdPoint.position);

		transform.localRotation =
			Quaternion.Inverse(
				holderTransform.rotation) *
			holdPoint.rotation;
	}
}
