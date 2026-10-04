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
	private readonly NetworkVariable<ulong> m_CurrentHolderId =
		new NetworkVariable<ulong>();

	/// <summary>
	/// 現在ボールが誰かに所持されているか。
	/// ClientId = 0のHostプレイヤーと「未所持」を区別するために使用します。
	/// </summary>
	private readonly NetworkVariable<bool> m_HasHolder =
		new NetworkVariable<bool>();

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

		Debug.Log(
			$"[BallController] Awake: " +
			$"Rigidbody={(m_Rigidbody != null ? "取得成功" : "取得失敗")}");
	}

	public override void OnNetworkSpawn()
	{
		Debug.Log(
			$"[BallController] OnNetworkSpawn: " +
			$"NetworkObjectId={NetworkObjectId}, " +
			$"IsServer={IsServer}, " +
			$"IsOwner={IsOwner}");

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
	public bool TrySetHolder(ulong holderClientId)
	{
		Debug.Log(
			$"[BallController] TrySetHolder開始: " +
			$"holderClientId={holderClientId}, " +
			$"IsServer={IsServer}, " +
			$"HasHolder={m_HasHolder.Value}");

		if (!IsServer)
		{
			Debug.LogWarning(
				"[BallController] TrySetHolder失敗: " +
				"サーバーではありません。");

			return false;
		}

		if (m_HasHolder.Value)
		{
			Debug.Log(
				$"[BallController] TrySetHolder失敗: " +
				$"既にClientId={m_CurrentHolderId.Value}が所持しています。");

			return false;
		}

		m_CurrentHolderId.Value = holderClientId;

		Debug.Log(
			$"[BallController] currentHolderIdを更新: " +
			$"{m_CurrentHolderId.Value}");

		m_HasHolder.Value = true;

		Debug.Log(
			$"[BallController] ボール所持確定: " +
			$"ClientId={m_CurrentHolderId.Value}");

		return true;
	}

	/// <summary>
	/// 現在の所持者からボールを解放します。
	/// </summary>
	public void ReleaseBall()
	{
		Debug.Log(
			$"[BallController] ReleaseBall開始: " +
			$"IsServer={IsServer}, " +
			$"HasHolder={m_HasHolder.Value}, " +
			$"CurrentHolderId={m_CurrentHolderId.Value}");

		if (!IsServer)
		{
			Debug.LogWarning(
				"[BallController] ReleaseBall中断: サーバーではありません。");

			return;
		}

		if (!m_HasHolder.Value)
		{
			Debug.Log(
				"[BallController] ReleaseBall中断: " +
				"現在誰もボールを持っていません。");

			return;
		}

		m_HasHolder.Value = false;
		m_CurrentHolderId.Value = 0;

		Debug.Log("[BallController] ボールをフリー状態に戻しました。");
	}

	private void HandleHolderStateChanged(
		bool previousValue,
		bool newValue)
	{
		Debug.Log(
			$"[BallController] 所持状態変更: " +
			$"{previousValue} -> {newValue}, " +
			$"CurrentHolderId={m_CurrentHolderId.Value}");

		ApplyHolderState(newValue);
	}

	private void ApplyHolderState(bool hasHolder)
	{
		Debug.Log(
			$"[BallController] ApplyHolderState: " +
			$"hasHolder={hasHolder}");

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

	private void AttachToHolder()
	{
		Debug.Log(
			$"[BallController] 所持者検索開始: " +
			$"ClientId={m_CurrentHolderId.Value}");

		PlayerBallCarrier holder =
			FindPlayerBallCarrier(m_CurrentHolderId.Value);

		if (holder == null)
		{
			Debug.LogError(
				$"[BallController] Attach失敗: " +
				$"ClientId={m_CurrentHolderId.Value}のPlayerBallCarrierが見つかりません。");

			return;
		}

		if (holder.BallHoldPoint == null)
		{
			Debug.LogError(
				$"[BallController] Attach失敗: " +
				$"ClientId={m_CurrentHolderId.Value}のBallHoldPointが未設定です。");

			return;
		}

		Debug.Log(
			$"[BallController] 所持者取得成功: " +
			$"Player={holder.gameObject.name}, " +
			$"BallHoldPoint={holder.BallHoldPoint.name}");

		m_Rigidbody.linearVelocity = Vector3.zero;
		m_Rigidbody.angularVelocity = Vector3.zero;
		m_Rigidbody.isKinematic = true;

		transform.SetParent(holder.BallHoldPoint);
		transform.localPosition = Vector3.zero;
		transform.localRotation = Quaternion.identity;

		Debug.Log(
			$"[BallController] AttachToHolder成功: " +
			$"ClientId={m_CurrentHolderId.Value}");
	}

	private void DetachFromHolder()
	{
		Debug.Log(
			$"[BallController] DetachFromHolder: " +
			$"IsServer={IsServer}");

		transform.SetParent(null);

		if (IsServer)
		{
			m_Rigidbody.isKinematic = false;
		}
		else
		{
			m_Rigidbody.isKinematic = true;
		}
	}

	private PlayerBallCarrier FindPlayerBallCarrier(
		ulong holderClientId)
	{
		foreach (NetworkObject networkObject in
			NetworkManager.SpawnManager.SpawnedObjectsList)
		{
			if (!networkObject.IsPlayerObject)
			{
				continue;
			}

			if (networkObject.OwnerClientId != holderClientId)
			{
				continue;
			}

			PlayerBallCarrier carrier =
				networkObject.GetComponent<PlayerBallCarrier>();

			if (carrier != null)
			{
				Debug.Log(
					$"[BallController] PlayerBallCarrier発見: " +
					$"ClientId={holderClientId}, " +
					$"NetworkObjectId={networkObject.NetworkObjectId}");
			}

			return carrier;
		}

		Debug.LogError(
			$"[BallController] PlayerBallCarrier検索失敗: " +
			$"ClientId={holderClientId}");

		return null;
	}

	private void UpdatePlayerBallStates(bool hasHolder)
	{
		Debug.Log(
			$"[BallController] プレイヤー所持状態更新開始: " +
			$"hasHolder={hasHolder}, " +
			$"CurrentHolderId={m_CurrentHolderId.Value}");

		foreach (NetworkObject networkObject in
			NetworkManager.SpawnManager.SpawnedObjectsList)
		{
			if (!networkObject.IsPlayerObject)
			{
				continue;
			}

			PlayerBallCarrier carrier =
				networkObject.GetComponent<PlayerBallCarrier>();

			if (carrier == null)
			{
				Debug.LogWarning(
					$"[BallController] PlayerBallCarrierなし: " +
					$"NetworkObjectId={networkObject.NetworkObjectId}");

				continue;
			}

			bool isHolder =
				hasHolder &&
				networkObject.OwnerClientId ==
				m_CurrentHolderId.Value;

			Debug.Log(
				$"[BallController] SetBallState呼び出し: " +
				$"ClientId={networkObject.OwnerClientId}, " +
				$"isHolder={isHolder}");

			carrier.SetBallState(isHolder);
		}
	}
}
