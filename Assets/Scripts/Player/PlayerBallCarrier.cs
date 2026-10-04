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

		Debug.Log(
			$"[PlayerBallCarrier] Awake: " +
			$"PlayerController={(m_PlayerController != null ? "取得成功" : "取得失敗")}, " +
			$"BallHoldPoint={(m_BallHoldPoint != null ? m_BallHoldPoint.name : "未設定")}");
	}

	public override void OnNetworkSpawn()
	{
		Debug.Log(
			$"[PlayerBallCarrier] OnNetworkSpawn: " +
			$"Object={gameObject.name}, " +
			$"IsOwner={IsOwner}, " +
			$"IsServer={IsServer}, " +
			$"OwnerClientId={OwnerClientId}, " +
			$"NetworkObjectId={NetworkObjectId}");
	}

	/// <summary>
	/// ボールに接触した時にHostへ取得を要求します。
	/// </summary>
	private void OnTriggerEnter(Collider other)
	{
		Debug.Log(
			$"[PlayerBallCarrier] OnTriggerEnter: " +
			$"Player={gameObject.name}, " +
			$"Other={other.gameObject.name}, " +
			$"IsOwner={IsOwner}, " +
			$"HasBall={m_HasBall}");

		// 自分が操作しているプレイヤーだけが取得要求を送ります。
		if (!IsOwner)
		{
			Debug.Log(
				"[PlayerBallCarrier] キャッチ処理中断: " +
				"このプレイヤーはOwnerではありません。");

			return;
		}

		// 既にボールを持っている場合は何もしません。
		if (m_HasBall)
		{
			Debug.Log(
				"[PlayerBallCarrier] キャッチ処理中断: " +
				"既にボールを所持しています。");

			return;
		}

		BallController ballController =
			other.GetComponentInParent<BallController>();

		if (ballController == null)
		{
			Debug.Log(
				$"[PlayerBallCarrier] キャッチ処理中断: " +
				$"{other.gameObject.name}からBallControllerを取得できませんでした。");

			return;
		}

		Debug.Log(
			$"[PlayerBallCarrier] BallController取得成功: " +
			$"Ball={ballController.gameObject.name}, " +
			$"IsSpawned={ballController.IsSpawned}, " +
			$"NetworkObjectId={ballController.NetworkObjectId}");

		if (!ballController.IsSpawned)
		{
			Debug.Log(
				"[PlayerBallCarrier] キャッチ処理中断: " +
				"ボールのNetworkObjectがSpawnされていません。");

			return;
		}

		Debug.Log(
			$"[PlayerBallCarrier] Hostへキャッチ要求を送信: " +
			$"BallNetworkObjectId={ballController.NetworkObjectId}");

		RequestPickupServerRpc(ballController.NetworkObjectId);
	}

	/// <summary>
	/// ボールに接触したことをHostへ通知します。
	/// </summary>
	/// <param name="ballNetworkObjectId">接触したボールのNetworkObjectId</param>
	[ServerRpc]
	private void RequestPickupServerRpc(ulong ballNetworkObjectId)
	{
		Debug.Log(
			$"[PlayerBallCarrier] Hostがキャッチ要求を受信: " +
			$"PlayerClientId={OwnerClientId}, " +
			$"BallNetworkObjectId={ballNetworkObjectId}");

		if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(
			ballNetworkObjectId,
			out NetworkObject ballNetworkObject))
		{
			Debug.LogError(
				$"[PlayerBallCarrier] キャッチ失敗: " +
				$"NetworkObjectId={ballNetworkObjectId}のボールが見つかりません。");

			return;
		}

		BallController ballController =
			ballNetworkObject.GetComponent<BallController>();

		if (ballController == null)
		{
			Debug.LogError(
				"[PlayerBallCarrier] キャッチ失敗: " +
				"NetworkObjectにBallControllerがありません。");

			return;
		}

		// Host側でボールがフリーか確認し、所持者を確定します。
		bool pickupSucceeded =
			ballController.TrySetHolder(OwnerClientId);

		Debug.Log(
			$"[PlayerBallCarrier] TrySetHolder結果: " +
			$"Success={pickupSucceeded}, " +
			$"PlayerClientId={OwnerClientId}");
	}

	/// <summary>
	/// タックル等によってボールを強制的に手放します。
	/// </summary>
	public void ForceReleaseBall()
	{
		Debug.Log(
			$"[PlayerBallCarrier] ForceReleaseBall: " +
			$"IsSpawned={IsSpawned}, " +
			$"IsServer={IsServer}, " +
			$"IsOwner={IsOwner}, " +
			$"HasBall={m_HasBall}");

		if (!IsSpawned)
		{
			Debug.Log(
				"[PlayerBallCarrier] ボール解放中断: " +
				"NetworkObjectがSpawnされていません。");

			return;
		}

		// Host側から呼ばれた場合は、そのまま解放処理を行います。
		if (IsServer)
		{
			Debug.Log(
				"[PlayerBallCarrier] Host側でボール解放処理を実行します。");

			ReleaseOwnedBallServer();
			return;
		}

		// 所持者本人から呼ばれた場合はHostへ要求します。
		if (IsOwner)
		{
			Debug.Log(
				"[PlayerBallCarrier] Hostへボール解放要求を送信します。");

			RequestReleaseBallServerRpc();
		}
	}

	/// <summary>
	/// 所持者本人からHostへボール解放を要求します。
	/// </summary>
	[ServerRpc]
	private void RequestReleaseBallServerRpc()
	{
		Debug.Log(
			$"[PlayerBallCarrier] Hostがボール解放要求を受信: " +
			$"PlayerClientId={OwnerClientId}");

		ReleaseOwnedBallServer();
	}

	/// <summary>
	/// このプレイヤーが持っているボールをHost側で解放します。
	/// </summary>
	private void ReleaseOwnedBallServer()
	{
		if (!IsServer)
		{
			Debug.LogWarning(
				"[PlayerBallCarrier] ReleaseOwnedBallServer中断: " +
				"サーバーではありません。");

			return;
		}

		foreach (NetworkObject networkObject in
			NetworkManager.SpawnManager.SpawnedObjectsList)
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

			Debug.Log(
				$"[PlayerBallCarrier] 所持中のボールを発見: " +
				$"BallNetworkObjectId={networkObject.NetworkObjectId}");

			ballController.ReleaseBall();
			return;
		}

		Debug.LogWarning(
			$"[PlayerBallCarrier] 解放対象のボールが見つかりませんでした。 " +
			$"PlayerClientId={OwnerClientId}");
	}

	/// <summary>
	/// BallControllerから所持状態の変化を受け取ります。
	/// </summary>
	/// <param name="hasBall">ボールを所持している場合はtrue</param>
	public void SetBallState(bool hasBall)
	{
		Debug.Log(
			$"[PlayerBallCarrier] SetBallState: " +
			$"PlayerClientId={OwnerClientId}, " +
			$"現在={m_HasBall}, " +
			$"変更後={hasBall}, " +
			$"IsOwner={IsOwner}");

		// 同じ状態なら再通知しません。
		if (m_HasBall == hasBall)
		{
			Debug.Log(
				"[PlayerBallCarrier] SetBallState中断: " +
				"所持状態に変化がありません。");

			return;
		}

		m_HasBall = hasBall;

		// 移動速度を変更するのは所持者本人のPCだけです。
		if (!IsOwner)
		{
			Debug.Log(
				"[PlayerBallCarrier] 所持状態のみ更新。 " +
				"OwnerではないためPlayerControllerへの通知は行いません。");

			return;
		}

		if (m_PlayerController == null)
		{
			Debug.LogError(
				"[PlayerBallCarrier] " +
				"PlayerControllerが取得できていないため速度変更できません。");

			return;
		}

		Debug.Log(
			$"[PlayerBallCarrier] NotifyBallStateChangedを実行: " +
			$"hasBall={hasBall}");

		m_PlayerController.NotifyBallStateChanged(hasBall);
	}
}
