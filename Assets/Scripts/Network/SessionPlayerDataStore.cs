using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// ロビーからゲームシーンを跨いでプレイヤープロファイル情報を保持・管理する永続ストアクラス。
/// サーバー(Host)側で各クライアントの最新設定をキャッシュし、席外し時の設定復旧およびゲームシーンへのデータ引き継ぎを担当します。
/// </summary>
public class SessionPlayerDataStore : MonoBehaviour
{
	public static SessionPlayerDataStore Instance { get; private set; }

	// サーバー上で各ClientIdの設定データを保持するキャッシュ
	private readonly Dictionary<ulong, LobbyPlayerData> m_PlayerDataCache = new Dictionary<ulong, LobbyPlayerData>();

	private bool IsServer => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;

	private void Awake()
	{
		if (Instance != null && Instance != this)
		{
			Destroy(gameObject);
			return;
		}

		Instance = this;
		DontDestroyOnLoad(gameObject);
	}

	private void Start()
	{
		if (IsServer)
		{
			NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
		}
	}

	private void OnDestroy()
	{
		if (Instance == this)
		{
			Instance = null;
		}

		if (IsServer)
		{
			NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
		}
	}

	private void HandleClientDisconnected(ulong clientId)
	{
		if (!IsServer) return;

		m_PlayerDataCache.Remove(clientId);
	}

	/// <summary>
	/// プレイヤーの設定データをキャッシュへ保存または更新します。
	/// </summary>
	public void SavePlayerData(LobbyPlayerData data)
	{
		if (!IsServer) return;

		m_PlayerDataCache[data.ClientId] = data;
	}

	/// <summary>
	/// 指定したClientIdのプレイヤー設定データを取得します。
	/// キャッシュが存在しない場合はデフォルト値を生成して返却します。
	/// </summary>
	public LobbyPlayerData GetOrCreatePlayerData(ulong clientId)
	{
		if (m_PlayerDataCache.TryGetValue(clientId, out var existingData))
		{
			return existingData;
		}

		// デフォルト生成: Player {clientId}
		FixedString32Bytes defaultName = $"Player {clientId}";
		var defaultData = new LobbyPlayerData(
			clientId,
			defaultName,
			ETeamType.None,
			ECharacterType.ModelA,
			EHighlightColor.Default,
			false);

		if (IsServer)
		{
			m_PlayerDataCache[clientId] = defaultData;
		}

		return defaultData;
	}

	/// <summary>
	/// 席外し(Team=None)時に、既存のキャラメイク設定を維持したままチームのみを解除します。
	/// </summary>
	public LobbyPlayerData UnseatPlayer(ulong clientId)
	{
		var data = GetOrCreatePlayerData(clientId);
		data.Team = ETeamType.None;
		data.IsReady = false;

		if (IsServer)
		{
			m_PlayerDataCache[clientId] = data;
		}

		return data;
	}

	/// <summary>
	/// キャッシュされている全プレイヤーのデータを取得します。
	/// </summary>
	public IReadOnlyDictionary<ulong, LobbyPlayerData> GetAllPlayerData()
	{
		return m_PlayerDataCache;
	}

	/// <summary>
	/// セッション終了時やロビー解散時に全キャッシュをクリアします。
	/// </summary>
	public void ClearAll()
	{
		m_PlayerDataCache.Clear();
	}
}
