using Unity.Netcode;
using UnityEngine;

/// <summary>
/// プレイヤー側のボール所持処理を担当します。
/// </summary>
/// <remarks>
/// 主な処理の流れ:
/// 1. Ownerがボールとの接触を検出します。
/// 2. ServerRpcでHostへ取得を要求します。
/// 3. HostがBallControllerへ取得処理を依頼します。
/// 4. シュート・強制解放もHost側で所持状態を確認して確定します。
/// 5. ボール所持状態の変化をPlayerControllerへ通知します。
/// </remarks>
[RequireComponent(typeof(NetworkObject))]
public class PlayerBallCarrier :
	NetworkBehaviour,
	IBallCarrier
{
	[Header("ボール所持位置")]
	[SerializeField] private Transform m_BallHoldPoint;

	private PlayerController m_PlayerController;
	private bool m_HasBall;

	/// <summary>
	/// このプレイヤーが現在ボールを所持しているか取得します。
	/// </summary>
	public bool HasBall
	{
		get
		{
			return m_HasBall;
		}
	}

	/// <summary>
	/// ボールを所持中に配置する位置を取得します。
	/// </summary>
	public Transform BallHoldPoint
	{
		get
		{
			return m_BallHoldPoint;
		}
	}

	/// <summary>
	/// 使用するコンポーネントの参照を取得します。
	/// </summary>
	private void Awake()
	{
		m_PlayerController =
			GetComponent<PlayerController>();
	}

	/// <summary>
	/// Owner側でボールとの接触を検出し、
	/// Hostへ取得要求を送信します。
	/// </summary>
	/// <param name="other">
	/// 接触したColliderです。
	/// </param>
	private void OnTriggerEnter(
		Collider other)
	{
		if (!IsOwner)
		{
			return;
		}

		if (m_HasBall)
		{
			return;
		}

		BallController ballController =
			other.GetComponentInParent<BallController>();

		if (ballController == null)
		{
			return;
		}

		// ゴール判定など別用途のColliderでは
		// キャッチ処理を開始しません。
		if (!ballController.IsCatchCollider(other))
		{
			return;
		}

		// NetworkObjectとしてSpawnされる前のボールには
		// ServerRpcによる取得要求を行いません。
		if (!ballController.IsSpawned)
		{
			return;
		}

		RequestPickupServerRpc(
			ballController.NetworkObjectId);
	}

	/// <summary>
	/// Host上で指定されたボールの取得を試みます。
	/// </summary>
	/// <param name="ballNetworkObjectId">
	/// 取得対象となるボールのNetworkObjectIdです。
	/// </param>
	[ServerRpc]
	private void RequestPickupServerRpc(
		ulong ballNetworkObjectId)
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

		// 最終的な取得可否はBallController側で判定します。
		ballController.TrySetHolder(
			OwnerClientId,
			NetworkObjectId);
	}

	/// <summary>
	/// 所持中のボールを指定方向へ射出するようHostへ要求します。
	/// </summary>
	/// <param name="direction">
	/// ボールを射出する方向です。
	/// </param>
	public void Shoot(
		Vector3 direction)
	{
		if (!IsOwner)
		{
			return;
		}

		if (!m_HasBall)
		{
			return;
		}

		if (direction.sqrMagnitude <=
			Mathf.Epsilon)
		{
			return;
		}

		RequestShootServerRpc(
			direction.normalized);
	}

	/// <summary>
	/// Host上でこのプレイヤーが所持するボールを取得し、
	/// 指定方向へのシュートを試みます。
	/// </summary>
	/// <param name="shootDirection">
	/// ボールを射出する方向です。
	/// </param>
	[ServerRpc]
	private void RequestShootServerRpc(
		Vector3 shootDirection)
	{
		if (shootDirection.sqrMagnitude <=
			Mathf.Epsilon)
		{
			return;
		}

		shootDirection.Normalize();

		if (!TryGetOwnedBallServer(
			out BallController ballController))
		{
			Debug.LogWarning(
				"[PlayerBallCarrier] " +
				"シュート対象のボールが見つかりません。");

			return;
		}

		if (!ballController.TryShoot(
			shootDirection))
		{
			Debug.LogWarning(
				"[PlayerBallCarrier] " +
				"ボールのシュート処理に失敗しました。");
		}
	}

	/// <summary>
	/// タックルなどの外部処理から、
	/// このプレイヤーが所持するボールを強制的に解放します。
	/// </summary>
	public void ForceReleaseBall()
	{
		if (!IsSpawned)
		{
			return;
		}

		// Host自身から呼ばれた場合は、
		// ServerRpcを経由せず直接解放します。
		if (IsServer)
		{
			ReleaseOwnedBallServer();
			return;
		}

		// Clientからの解放要求は、
		// 対象プレイヤー本人のOwnerだけが送信できます。
		if (IsOwner)
		{
			RequestReleaseBallServerRpc();
		}
	}

	/// <summary>
	/// OwnerからHostへボールの強制解放を要求します。
	/// </summary>
	[ServerRpc]
	private void RequestReleaseBallServerRpc()
	{
		ReleaseOwnedBallServer();
	}

	/// <summary>
	/// Host上でこのプレイヤーが所持しているボールを探し、
	/// 見つかった場合は解放します。
	/// </summary>
	private void ReleaseOwnedBallServer()
	{
		if (!IsServer)
		{
			return;
		}

		if (!TryGetOwnedBallServer(
			out BallController ballController))
		{
			return;
		}

		ballController.ReleaseBall();
	}

	/// <summary>
	/// Host上で、このプレイヤーが現在所持している
	/// ボールを取得します。
	/// </summary>
	/// <param name="ballController">
	/// 所持中のボールです。
	/// 見つからない場合はnullになります。
	/// </param>
	/// <returns>
	/// 所持中のボールが見つかった場合はtrue、
	/// 見つからなかった場合はfalseです。
	/// </returns>
	private bool TryGetOwnedBallServer(
		out BallController ballController)
	{
		ballController = null;

		if (!IsServer)
		{
			return false;
		}

		// Spawn済みオブジェクトから、
		// このプレイヤーを所持者としているボールを探します。
		foreach (NetworkObject networkObject in
			NetworkManager.SpawnManager.SpawnedObjectsList)
		{
			BallController candidate =
				networkObject.GetComponent<BallController>();

			if (candidate == null)
			{
				continue;
			}

			if (!candidate.HasHolder)
			{
				continue;
			}

			if (candidate.CurrentHolderId !=
				OwnerClientId)
			{
				continue;
			}

			ballController = candidate;

			return true;
		}

		return false;
	}

	/// <summary>
	/// このプレイヤーのボール所持状態を更新し、
	/// Ownerの場合はPlayerControllerへ状態変化を通知します。
	/// </summary>
	/// <param name="hasBall">
	/// ボールを所持している場合はtrue、
	/// 所持していない場合はfalseです。
	/// </param>
	public void SetBallState(
		bool hasBall)
	{
		if (m_HasBall == hasBall)
		{
			return;
		}

		m_HasBall = hasBall;

		// 移動速度などプレイヤー固有の変更は、
		// そのプレイヤーを操作するOwner側だけに通知します。
		if (!IsOwner)
		{
			return;
		}

		if (m_PlayerController == null)
		{
			return;
		}

		m_PlayerController.NotifyBallStateChanged(
			hasBall);
	}
}
