using Unity.Netcode;
using UnityEngine;

/// <summary>
/// ボール本体の所持状態と物理状態を管理します。
/// </summary>
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

	public ulong CurrentHolderId =>
		m_CurrentHolderId.Value;

	public bool HasHolder =>
		m_HasHolder.Value;

	private void Awake()
	{
		m_Rigidbody =
			GetComponent<Rigidbody>();
	}

	public override void OnNetworkSpawn()
	{
		m_HasHolder.OnValueChanged +=
			HandleHolderStateChanged;

		if (m_HasHolder.Value)
		{
			ApplyHolderState(true);
		}
	}

	public override void OnNetworkDespawn()
	{
		m_HasHolder.OnValueChanged -=
			HandleHolderStateChanged;
	}

	public bool IsCatchCollider(
		Collider targetCollider)
	{
		return targetCollider ==
			m_PhysicsCollider;
	}

	public bool IsSensorCollider(
		Collider targetCollider)
	{
		return targetCollider ==
			m_SensorCollider;
	}

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

		// 再キャッチ禁止時間が終わっていたら解除します。
		if (m_HasRepickupLock &&
			Time.time >= m_RepickupUnlockTime)
		{
			m_HasRepickupLock = false;
		}

		// 直前まで所持していた本人だけ、
		// 一定時間ボールを再取得できません。
		if (m_HasRepickupLock &&
			holderClientId == m_LastHolderClientId)
		{
			Debug.Log(
				$"[BallController] 再キャッチ防止: " +
				$"ClientId={holderClientId}");

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

		m_HasHolder.Value = true;

		return true;
	}

	/// <summary>
	/// Host側でボールを指定方向へ射出します。
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

		// Colliderを戻す前に再キャッチ防止を開始します。
		StartRepickupLock(
			previousHolderClientId);

		if (!DetachFromHolder())
		{
			m_HasRepickupLock = false;

			return false;
		}

		// 元所持者へSetBallState(false)を通知します。
		m_HasHolder.Value = false;

		m_CurrentHolderId.Value = 0;

		m_Rigidbody.linearVelocity =
			shootDirection * m_ShootPower;

		Debug.Log(
			$"[BallController] シュート: " +
			$"Direction={shootDirection}, " +
			$"Power={m_ShootPower}");

		return true;
	}

	/// <summary>
	/// タックル等でボールを解放します。
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

		// 落とした本人がその場で即再取得するのを防ぎます。
		StartRepickupLock(
			previousHolderClientId);

		if (!DetachFromHolder())
		{
			m_HasRepickupLock = false;

			return;
		}

		m_HasHolder.Value = false;

		m_CurrentHolderId.Value = 0;

		Debug.Log(
			$"[BallController] ボール解放: " +
			$"元所持者ClientId={previousHolderClientId}");
	}

	private void StartRepickupLock(
		ulong holderClientId)
	{
		m_LastHolderClientId =
			holderClientId;

		m_RepickupUnlockTime =
			Time.time + Mathf.Max(
				0.0f,
				m_RepickupDelay);

		m_HasRepickupLock =
			m_RepickupDelay > 0.0f;
	}

	private void HandleHolderStateChanged(
		bool previousValue,
		bool newValue)
	{
		ApplyHolderState(newValue);
	}

	private void ApplyHolderState(
		bool hasHolder)
	{
		// 所持中は物理Colliderだけ無効にします。
		if (m_PhysicsCollider != null)
		{
			m_PhysicsCollider.enabled =
				!hasHolder;
		}

		// ゴール判定用Sensorは常に有効です。
		if (m_SensorCollider != null)
		{
			m_SensorCollider.enabled = true;
		}

		SetHolderBallState(
			hasHolder);
	}

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

		m_Rigidbody.isKinematic = true;

		if (m_PhysicsCollider != null)
		{
			m_PhysicsCollider.enabled = false;
		}

		bool parentSucceeded =
			NetworkObject.TrySetParent(
				holderNetworkObject,
				false);

		if (!parentSucceeded)
		{
			if (m_PhysicsCollider != null)
			{
				m_PhysicsCollider.enabled = true;
			}

			m_Rigidbody.isKinematic = false;

			return false;
		}

		transform.position =
			holder.BallHoldPoint.position;

		transform.rotation =
			holder.BallHoldPoint.rotation;

		return true;
	}

	private bool DetachFromHolder()
	{
		if (!IsServer)
		{
			return false;
		}

		if (transform.parent != null)
		{
			bool removeSucceeded =
				NetworkObject.TryRemoveParent(true);

			if (!removeSucceeded)
			{
				return false;
			}
		}

		m_Rigidbody.isKinematic = false;

		if (m_PhysicsCollider != null)
		{
			m_PhysicsCollider.enabled = true;
		}

		return true;
	}

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
