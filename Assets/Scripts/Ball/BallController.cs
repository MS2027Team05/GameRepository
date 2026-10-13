using System;
using Unity.Netcode;
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
public class BallController : NetworkBehaviour
{
	[Header("ボールの当たり判定")]
	[SerializeField] private Collider m_PhysicsCollider;
	[SerializeField] private Collider m_SensorCollider;

	[Header("シュート設定")]
	[SerializeField] private float m_ShootPower;

	[Header("再キャッチ防止")]
	[SerializeField] private float m_RepickupDelay;

	private readonly NetworkVariable<ulong> m_CurrentHolderId =
		new NetworkVariable<ulong>();

	private readonly NetworkVariable<ulong>
		m_CurrentHolderNetworkObjectId =
			new NetworkVariable<ulong>();

	private readonly NetworkVariable<bool> m_HasHolder =
		new NetworkVariable<bool>();

	private Rigidbody m_Rigidbody;

	private bool m_HasRepickupLock;
	private ulong m_LastHolderClientId;
	private float m_RepickupUnlockTime;

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
	}

	/// <summary>
	/// NetworkObjectのSpawn時に所持状態の変更通知を登録し、
	/// 現在の所持状態を反映します。
	/// </summary>
	public override void OnNetworkSpawn()
	{
		m_HasHolder.OnValueChanged +=
			HandleHolderStateChanged;

		if (m_HasHolder.Value)
		{
			ApplyHolderState(true);
		}
	}

	/// <summary>
	/// NetworkObjectのDespawn時に所持状態の変更通知を解除します。
	/// </summary>
	public override void OnNetworkDespawn()
	{
		m_HasHolder.OnValueChanged -=
			HandleHolderStateChanged;
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
		if (!IsServer)
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

		return true;
	}

	/// <summary>
	/// Host上で所持中のボールを指定方向へ射出します。
	/// </summary>
	public bool TryShoot(
		Vector3 shootDirection)
	{
		if (!IsServer)
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

		m_Rigidbody.linearVelocity =
			Vector3.zero;

		m_Rigidbody.angularVelocity =
			Vector3.zero;

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

		if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(
			holderNetworkObjectId,
			out NetworkObject holderNetworkObject))
		{
			return;
		}

		PlayerBallCarrier holder =
			holderNetworkObject.GetComponent<PlayerBallCarrier>();

		if (holder == null)
		{
			return;
		}

		holder.SetBallState(
			hasBall);
	}
}
