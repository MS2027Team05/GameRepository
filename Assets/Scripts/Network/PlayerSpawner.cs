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

	[Header("スポーン地点(Red陣営 / Blue陣営)")]
	[FormerlySerializedAs("redSpawnPoints")]
	[SerializeField] private Transform[] m_RedSpawnPoints;
	[FormerlySerializedAs("blueSpawnPoints")]
	[SerializeField] private Transform[] m_BlueSpawnPoints;

	private readonly List<GameObject> m_SpawnedPlayerObjects = new List<GameObject>();

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

			// 観戦専用ホスト(本番モード)の場合、サーバー(ClientId=0)のキャラはスポーンさせない
			if (clientId == NetworkManager.ServerClientId && !isPlayerHost)
			{
				continue;
			}

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

			// 未所属時のフォールバック(Red優先で空いている方へ)
			if (playerData.Team == ETeamType.None)
			{
				playerData.Team = (redSlotIndex <= blueSlotIndex) ? ETeamType.Red : ETeamType.Blue;
			}

			// 所属チームに応じたスポーン地点と回転の決定
			bool isRedTeam = (playerData.Team == ETeamType.Red);
			int targetSlot = isRedTeam ? redSlotIndex++ : blueSlotIndex++;
			GetSpawnTransform(isRedTeam, targetSlot, out Vector3 spawnPosition, out Quaternion spawnRotation);

			GameObject playerInstance = Instantiate(m_PlayerPrefab, spawnPosition, spawnRotation);
			NetworkObject networkObject = playerInstance.GetComponent<NetworkObject>();

			if (networkObject != null)
			{
				// 各クライアントに所有権(Ownership)を付与してネットワーク生成
				networkObject.SpawnWithOwnership(clientId);
				m_SpawnedPlayerObjects.Add(playerInstance);

				// プレイヤープロファイルの初期化
				var profile = playerInstance.GetComponent<PlayerNetworkProfile>();
				if (profile != null)
				{
					profile.InitializeProfile(playerData);
				}
			}
		}
	}

	private void GetSpawnTransform(bool isRedTeam, int slotIndex, out Vector3 position, out Quaternion rotation)
	{
		Transform[] targetPoints = isRedTeam ? m_RedSpawnPoints : m_BlueSpawnPoints;

		if (targetPoints != null && targetPoints.Length > 0)
		{
			Transform point = targetPoints[slotIndex % targetPoints.Length];
			if (point != null)
			{
				position = point.position;
				rotation = point.rotation;
				return;
			}
		}

		// スポーン地点が未設定の場合のフォールバック座標
		position = isRedTeam
			? new Vector3(-5f - (slotIndex * 2f), 1f, 0f)
			: new Vector3(5f + (slotIndex * 2f), 1f, 0f);
		rotation = isRedTeam ? Quaternion.LookRotation(Vector3.right) : Quaternion.LookRotation(Vector3.left);
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
		}
	}
}

