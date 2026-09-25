using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// GameScene開始時に各プレイヤーキャラクターを適切な陣営(Red/Blue)にスポーンさせるクラス。
/// サーバー(Host)側でのみ実行され、専用ホスト(観戦)とプレイヤー兼任ホストの判定を行います。
/// </summary>
public class PlayerSpawner : NetworkBehaviour
{
    [Header("プレイヤーPrefab")]
    [SerializeField] private GameObject playerPrefab;

    [Header("スポーン地点(Red陣営 / Blue陣営)")]
    [SerializeField] private Transform[] redSpawnPoints;
    [SerializeField] private Transform[] blueSpawnPoints;

    private readonly List<GameObject> spawnedPlayerObjects = new List<GameObject>();

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
        if (playerPrefab == null)
        {
            Debug.LogError("[PlayerSpawner] playerPrefab が設定されていません。");
            return;
        }

        bool isPlayerHost = NetworkConnectManager.IsPlayerHost;
        int playerIndex = 0;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            ulong clientId = client.ClientId;

            // 観戦専用ホスト(本番モード)の場合、サーバー(ClientId=0)のキャラはスポーンさせない
            if (clientId == NetworkManager.ServerClientId && !isPlayerHost)
            {
                continue;
            }

            // スポーン位置と回転の決定(Red陣営とBlue陣営に交互に振り分け)
            GetSpawnTransform(playerIndex, out Vector3 spawnPosition, out Quaternion spawnRotation);

            GameObject playerInstance = Instantiate(playerPrefab, spawnPosition, spawnRotation);
            NetworkObject networkObject = playerInstance.GetComponent<NetworkObject>();

            if (networkObject != null)
            {
                // 各クライアントに所有権(Ownership)を付与してネットワーク生成
                networkObject.SpawnWithOwnership(clientId);
                spawnedPlayerObjects.Add(playerInstance);
            }

            playerIndex++;
        }
    }

    private void GetSpawnTransform(int index, out Vector3 position, out Quaternion rotation)
    {
        // 偶数インデックスはRed陣営、奇数インデックスはBlue陣営
        bool isRedTeam = (index % 2 == 0);
        Transform[] targetPoints = isRedTeam ? redSpawnPoints : blueSpawnPoints;
        int pointIndex = (index / 2);

        if (targetPoints != null && targetPoints.Length > 0)
        {
            Transform point = targetPoints[pointIndex % targetPoints.Length];
            if (point != null)
            {
                position = point.position;
                rotation = point.rotation;
                return;
            }
        }

        // スポーン地点が未設定の場合のフォールバック座標
        position = isRedTeam ? new Vector3(-5f + (index * 2f), 1f, 0f) : new Vector3(5f + (index * 2f), 1f, 0f);
        rotation = isRedTeam ? Quaternion.LookRotation(Vector3.right) : Quaternion.LookRotation(Vector3.left);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            // シーン終了時のクリーンアップ
            foreach (var player in spawnedPlayerObjects)
            {
                if (player != null)
                {
                    Destroy(player);
                }
            }
            spawnedPlayerObjects.Clear();
        }
    }
}
