using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// GameScene開始時に各プレイヤーキャラクターを適切な陣営(Red/Blue)にスポーンさせるクラス。
/// SessionPlayerDataStoreに保持されたロビーデータをもとに、各プレイヤーの所属陣営位置へ生成しプロファイルを初期化します。
/// </summary>
public class PlayerSpawner : NetworkBehaviour
{
	[Header("プレイヤーPrefab")]
	[FormerlySerializedAs("playerPrefab")]
	[SerializeField] private GameObject m_PlayerPrefab;

	[Header("自陣ゴール付近のスポーン地点(チーム内の配置番号順)")]
	[FormerlySerializedAs("redSpawnPoints")]
	[SerializeField] private Transform[] m_RedSpawnPoints;
	[FormerlySerializedAs("blueSpawnPoints")]
	[SerializeField] private Transform[] m_BlueSpawnPoints;
	[Header("中央近くのスポーン地点(チーム内の配置番号順)")]
	[SerializeField] private Transform[] m_RedCenterSpawnPoints;
	[SerializeField] private Transform[] m_BlueCenterSpawnPoints;

	private readonly List<GameObject> m_SpawnedPlayerObjects = new List<GameObject>();
	private readonly List<(PlayerController Controller, ETeamType Team, int SlotIndex)> m_RestartPlayers =
		new List<(PlayerController, ETeamType, int)>();

	public bool IsReady { get; private set; }

	/// <summary>
	/// 接続中の全プレイヤーがOwner側での再配置を完了したか確認します。
	/// </summary>
	public bool ArePlayersReadyForRestart
	{
		get
		{
			if (!IsServer || !IsSpawned || !IsReady) return false;
			foreach (var player in m_RestartPlayers)
			{
				if (player.Controller != null && player.Controller.IsSpawned &&
					NetworkManager.ConnectedClients.ContainsKey(player.Controller.OwnerClientId) &&
					!player.Controller.IsReadyForRestart)
				{
					return false;
				}
			}
			return true;
		}
	}

	public override void OnNetworkSpawn()
	{
		// サーバー(Host)側のみがスポーン処理を実行
		if (!IsServer) return;

		SpawnAllPlayers();
	}

	/// <summary>
	/// 接続されている全クライアントに対してキャラクターをスポーンさせます。
	/// </summary>
	private void SpawnAllPlayers()
	{
		if (m_PlayerPrefab == null)
		{
			Debug.LogError("[PlayerSpawner] playerPrefab が設定されていません。");
			return;
		}

		bool isPlayerHost = NetworkConnectManager.IsPlayerHost;

		int redSlotIndex = 0;
		int blueSlotIndex = 0;

		foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
		{
			ulong clientId = client.ClientId;

			// ロビーデータストアからプレイヤー情報を取得(未登録時はデフォルト生成)
			LobbyPlayerData playerData;
			if (SessionPlayerDataStore.Instance != null)
			{
				playerData = SessionPlayerDataStore.Instance.GetOrCreatePlayerData(clientId);
			}
			else
			{
				playerData = new LobbyPlayerData(clientId, $"Player {clientId}");
			}

			// 観戦専用ホスト(本番モード)または観戦クライアントの場合、キャラはスポーンさせない
			if ((clientId == NetworkManager.ServerClientId && !isPlayerHost) || playerData.Team == ETeamType.Spectator)
			{
				continue;
			}

			// 未所属時のフォールバック(Red優先で空いている方へ)
			if (playerData.Team == ETeamType.None)
			{
				playerData.Team = (redSlotIndex <= blueSlotIndex) ? ETeamType.Red : ETeamType.Blue;
			}

			// 所属チームに応じたスポーン地点と回転の決定
			bool isRedTeam = (playerData.Team == ETeamType.Red);
			int targetSlot = isRedTeam ? redSlotIndex++ : blueSlotIndex++;
			if (!TryGetSpawnTransform(playerData.Team, targetSlot, false, out Vector3 spawnPosition, out Quaternion spawnRotation) ||
				!TryGetSpawnTransform(playerData.Team, targetSlot, true, out _, out _))
			{
				return;
			}

			GameObject playerInstance = Instantiate(m_PlayerPrefab, spawnPosition, spawnRotation);
			NetworkObject networkObject = playerInstance.GetComponent<NetworkObject>();

			if (networkObject != null)
			{
				// 各クライアントのPlayerObjectとしてネットワーク生成
				networkObject.SpawnAsPlayerObject(clientId);
				m_SpawnedPlayerObjects.Add(playerInstance);

				// プレイヤープロファイルの初期化
				var profile = playerInstance.GetComponent<PlayerNetworkProfile>();
				if (profile != null)
				{
					profile.InitializeProfile(playerData);
				}

				PlayerController controller = playerInstance.GetComponent<PlayerController>();
				if (controller == null)
				{
					Debug.LogError("[PlayerSpawner] PlayerPrefabにPlayerControllerを設定してください。", this);
					return;
				}
				m_RestartPlayers.Add((controller, playerData.Team, targetSlot));
				controller.SetMatchActionEnabledOwnerRpc(false);
			}
			else
			{
				Destroy(playerInstance);
				Debug.LogError("[PlayerSpawner] PlayerPrefabにNetworkObjectを設定してください。", this);
				return;
			}
		}
		IsReady = true;
	}

	/// <summary>
	/// 初期配置では両チームを自陣ゴール付近へ、得点後は失点側だけを中央近くへ再配置します。
	/// </summary>
	/// <param name="scoringTeam">得点したチーム。初期配置ではNoneを指定します。</param>
	/// <returns>配置地点が設定され、全プレイヤーへ再配置を要求できた場合はtrue。</returns>
	public bool ResetPlayersForRestart(ETeamType scoringTeam)
	{
		if (!IsServer || !IsSpawned) return false;
		bool isAfterGoal = scoringTeam == ETeamType.Red || scoringTeam == ETeamType.Blue;
		// 一部のプレイヤーだけが再配置されないよう、要求前に全員分の設定を確認します。
		foreach (var player in m_RestartPlayers)
		{
			if (player.Controller != null && player.Controller.IsSpawned &&
				!TryGetSpawnTransform(player.Team, player.SlotIndex, isAfterGoal && player.Team != scoringTeam, out _, out _))
			{
				return false;
			}
		}
		foreach (var player in m_RestartPlayers)
		{
			if (player.Controller != null && player.Controller.IsSpawned)
			{
				TryGetSpawnTransform(player.Team, player.SlotIndex, isAfterGoal && player.Team != scoringTeam,
					out Vector3 position, out Quaternion rotation);
				player.Controller.PrepareForRestart(position, rotation);
			}
		}
		return true;
	}

	/// <summary>
	/// 各Ownerへ試合進行に応じた操作の許可状態を通知します。
	/// </summary>
	public void SetMatchActionsEnabled(bool enabled)
	{
		if (!IsServer || !IsSpawned) return;
		foreach (var player in m_RestartPlayers)
		{
			if (player.Controller != null && player.Controller.IsSpawned)
			{
				player.Controller.SetMatchActionEnabledOwnerRpc(enabled);
			}
		}
	}

	private bool TryGetSpawnTransform(ETeamType team, int slotIndex, bool useCenter, out Vector3 position, out Quaternion rotation)
	{
		Transform[] targetPoints = useCenter
			? (team == ETeamType.Red ? m_RedCenterSpawnPoints : m_BlueCenterSpawnPoints)
			: (team == ETeamType.Red ? m_RedSpawnPoints : m_BlueSpawnPoints);

		if (targetPoints != null && slotIndex >= 0 && slotIndex < targetPoints.Length)
		{
			Transform point = targetPoints[slotIndex];
			if (point != null)
			{
				position = point.position;
				rotation = point.rotation;
				return true;
			}
		}

		// 配列の循環や固定座標への代替で同じ地点に重なることを避けます。
		Debug.LogError($"[PlayerSpawner] {team}の{(useCenter ? "中央近く" : "自陣ゴール付近")}の配置番号{slotIndex}にスポーン地点を設定してください。", this);
		position = default;
		rotation = Quaternion.identity;
		return false;
	}

	/// <summary>
	/// スポーンされた全プレイヤーキャラクターのNetworkObjectを明示的にDespawnし破棄します。
	/// </summary>
	public void DespawnAllPlayers()
	{
		if (!IsServer) return;

		for (int i = m_SpawnedPlayerObjects.Count - 1; i >= 0; i--)
		{
			GameObject player = m_SpawnedPlayerObjects[i];
			if (player != null)
			{
				NetworkObject netObj = player.GetComponent<NetworkObject>();
				if (netObj != null && netObj.IsSpawned)
				{
					netObj.Despawn(true);
				}
				else
				{
					Destroy(player);
				}
			}
		}
		m_SpawnedPlayerObjects.Clear();
	}

	public override void OnNetworkDespawn()
	{
		if (IsServer)
		{
			// シーン終了時のクリーンアップ
			foreach (var player in m_SpawnedPlayerObjects)
			{
				if (player != null)
				{
					Destroy(player);
				}
			}
			m_SpawnedPlayerObjects.Clear();
			m_RestartPlayers.Clear();
			IsReady = false;
		}
	}
}

