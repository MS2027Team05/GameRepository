using Unity.Netcode;
using UnityEngine;

/// <summary>
/// プレイヤー側のボール所持処理を担当します。
/// ボールへの接触通知、所持状態、強制解放を管理します。
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class PlayerBallCarrier : NetworkBehaviour, IBallCarrier
{
	[Header("ボール所持位置")]
	[SerializeField] private Transform m_BallHoldPoint;

	private PlayerController m_PlayerController;
	private bool m_HasBall;

	/// <summary>
	/// 現在このプレイヤーがボールを所持しているか。
	/// </summary>
	public bool HasBall => m_HasBall;

	/// <summary>
	/// ボールを表示する手元位置。
	/// </summary>
	public Transform BallHoldPoint => m_BallHoldPoint;

	private void Awake()
	{
		m_PlayerController = GetComponent<PlayerController>();
	}

	/// <summary>
	/// ボールに接触した時にHostへ取得を要求します。
	/// </summary>
	private void OnTriggerEnter(Collider other)
	{
		// 自分が操作しているプレイヤーだけが取得要求を送ります。
		if (!IsOwner)
		{
			return;
		}

		// 既にボールを持っている場合は何もしません。
		if (m_HasBall)
		{
			return;
		}

		BallController ballController = other.GetComponentInParent<BallController>();

		if (ballController == null)
		{
			return;
		}

		if (!ballController.IsSpawned)
		{
			return;
		}

		RequestPickupServerRpc(ballController.NetworkObjectId);
	}

	/// <summary>
	/// ボールに接触したことをHostへ通知します。
	/// </summary>
	/// <param name="ballNetworkObjectId">接触したボールのNetworkObjectId</param>
	[ServerRpc]
	private void RequestPickupServerRpc(ulong ballNetworkObjectId)
	{
		if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(
			ballNetworkObjectId,
			out NetworkObject ballNetworkObject))
		{
			return;
		}

		BallController ballController =
			ballNetworkObject.GetComponent<BallController>();

		if (ballController == null)
		{
			return;
		}

		// Host側でボールがフリーか確認し、所持者を確定します。
		ballController.TrySetHolder(OwnerClientId);
	}

	/// <summary>
	/// タックル等によってボールを強制的に手放します。
	/// </summary>
	public void ForceReleaseBall()
	{
		if (!IsSpawned)
		{
			return;
		}

		// Host側から呼ばれた場合は、そのまま解放処理を行います。
		if (IsServer)
		{
			ReleaseOwnedBallServer();
			return;
		}

		// 所持者本人から呼ばれた場合はHostへ要求します。
		if (IsOwner)
		{
			RequestReleaseBallServerRpc();
		}
	}

	/// <summary>
	/// 所持者本人からHostへボール解放を要求します。
	/// </summary>
	[ServerRpc]
	private void RequestReleaseBallServerRpc()
	{
		ReleaseOwnedBallServer();
	}

	/// <summary>
	/// このプレイヤーが持っているボールをHost側で解放します。
	/// </summary>
	private void ReleaseOwnedBallServer()
	{
		if (!IsServer)
		{
			return;
		}

		foreach (NetworkObject networkObject in NetworkManager.SpawnManager.SpawnedObjectsList)
		{
			BallController ballController =
				networkObject.GetComponent<BallController>();

			if (ballController == null)
			{
				continue;
			}

			if (!ballController.HasHolder)
			{
				continue;
			}

			if (ballController.CurrentHolderId != OwnerClientId)
			{
				continue;
			}

			ballController.ReleaseBall();
			return;
		}
	}

	/// <summary>
	/// BallControllerから所持状態の変化を受け取ります。
	/// </summary>
	/// <param name="hasBall">ボールを所持している場合はtrue</param>
	public void SetBallState(bool hasBall)
	{
		// 同じ状態なら再通知しません。
		if (m_HasBall == hasBall)
		{
			return;
		}

		m_HasBall = hasBall;

		// 移動速度を変更するのは所持者本人のPCだけです。
		if (!IsOwner)
		{
			return;
		}

		if (m_PlayerController == null)
		{
			return;
		}

		m_PlayerController.NotifyBallStateChanged(hasBall);
	}
}
