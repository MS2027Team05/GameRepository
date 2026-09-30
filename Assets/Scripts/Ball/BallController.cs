using Unity.Netcode;
using UnityEngine;

/// <summary>
/// ボール本体の所持状態と物理状態を管理します。
/// ボールの所持者確定はHost(サーバー)のみが行います。
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
public class BallController : NetworkBehaviour
{
	/// <summary>
	/// 現在ボールを持っているプレイヤーのClientId。
	/// </summary>
	private readonly NetworkVariable<ulong> m_CurrentHolderId = new NetworkVariable<ulong>();

	/// <summary>
	/// 現在ボールが誰かに所持されているか。
	/// ClientId = 0のHostプレイヤーと「未所持」を区別するために使用します。
	/// </summary>
	private readonly NetworkVariable<bool> m_HasHolder = new NetworkVariable<bool>();

	private Rigidbody m_Rigidbody;

	/// <summary>
	/// 現在ボールを持っているプレイヤーのID。
	/// </summary>
	public ulong CurrentHolderId => m_CurrentHolderId.Value;

	/// <summary>
	/// 現在ボールが所持されているか。
	/// </summary>
	public bool HasHolder => m_HasHolder.Value;

	private void Awake()
	{
		m_Rigidbody = GetComponent<Rigidbody>();
	}

	public override void OnNetworkSpawn()
	{
		m_HasHolder.OnValueChanged += HandleHolderStateChanged;

		// 途中参加などでも現在の状態を反映できるようにします。
		ApplyHolderState(m_HasHolder.Value);
	}

	public override void OnNetworkDespawn()
	{
		m_HasHolder.OnValueChanged -= HandleHolderStateChanged;
	}

	/// <summary>
	/// 指定されたプレイヤーをボール所持者に設定します。
	/// Host(サーバー)のみ実行できます。
	/// </summary>
	/// <param name="holderClientId">ボールを取得したプレイヤーのClientId</param>
	/// <returns>取得に成功した場合はtrue</returns>
	public bool TrySetHolder(ulong holderClientId)
	{
		if (!IsServer)
		{
			return false;
		}

		// 既に誰かが持っている場合は取得できません。
		if (m_HasHolder.Value)
		{
			return false;
		}

		m_CurrentHolderId.Value = holderClientId;
		m_HasHolder.Value = true;

		return true;
	}

	/// <summary>
	/// 現在の所持者からボールを解放します。
	/// Host(サーバー)のみ実行できます。
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

		// falseへの変更通知時点ではCurrentHolderIdを残しておきます。
		// これにより元所持者の状態を解除できます。
		m_HasHolder.Value = false;

		// 仕様書に合わせ、未所持時は0へ戻します。
		m_CurrentHolderId.Value = 0;
	}

	/// <summary>
	/// NetworkVariableによって所持状態が変化した時に呼ばれます。
	/// </summary>
	private void HandleHolderStateChanged(bool previousValue, bool newValue)
	{
		ApplyHolderState(newValue);
	}

	/// <summary>
	/// 所持状態をボールの見た目と物理状態へ反映します。
	/// </summary>
	private void ApplyHolderState(bool hasHolder)
	{
		if (hasHolder)
		{
			AttachToHolder();
		}
		else
		{
			DetachFromHolder();
		}

		UpdatePlayerBallStates(hasHolder);
	}

	/// <summary>
	/// ボールを現在の所持者の手元へ移動します。
	/// </summary>
	private void AttachToHolder()
	{
		PlayerBallCarrier holder = FindPlayerBallCarrier(m_CurrentHolderId.Value);

		if (holder == null || holder.BallHoldPoint == null)
		{
			return;
		}

		// 所持中は物理演算を停止します。
		m_Rigidbody.linearVelocity = Vector3.zero;
		m_Rigidbody.angularVelocity = Vector3.zero;
		m_Rigidbody.isKinematic = true;

		// 仕様書通り、所持者の手元へ親子付けします。
		transform.SetParent(holder.BallHoldPoint);
		transform.localPosition = Vector3.zero;
		transform.localRotation = Quaternion.identity;
	}

	/// <summary>
	/// ボールを所持者から切り離します。
	/// </summary>
	private void DetachFromHolder()
	{
		transform.SetParent(null);

		// 物理演算はHostだけが担当します。
		if (IsServer)
		{
			m_Rigidbody.isKinematic = false;
		}
		else
		{
			m_Rigidbody.isKinematic = true;
		}
	}

	/// <summary>
	/// currentHolderIdに対応するPlayerBallCarrierを取得します。
	/// </summary>
	private PlayerBallCarrier FindPlayerBallCarrier(ulong holderClientId)
	{
		foreach (NetworkObject networkObject in NetworkManager.SpawnManager.SpawnedObjectsList)
		{
			if (!networkObject.IsPlayerObject)
			{
				continue;
			}

			if (networkObject.OwnerClientId != holderClientId)
			{
				continue;
			}

			return networkObject.GetComponent<PlayerBallCarrier>();
		}

		return null;
	}

	/// <summary>
	/// 全プレイヤーのボール所持状態を更新します。
	/// 実際の速度変更通知は所持者本人の端末のみで行われます。
	/// </summary>
	private void UpdatePlayerBallStates(bool hasHolder)
	{
		foreach (NetworkObject networkObject in NetworkManager.SpawnManager.SpawnedObjectsList)
		{
			if (!networkObject.IsPlayerObject)
			{
				continue;
			}

			PlayerBallCarrier carrier = networkObject.GetComponent<PlayerBallCarrier>();

			if (carrier == null)
			{
				continue;
			}

			bool isHolder =
				hasHolder &&
				networkObject.OwnerClientId == m_CurrentHolderId.Value;

			carrier.SetBallState(isHolder);
		}
	}
}
