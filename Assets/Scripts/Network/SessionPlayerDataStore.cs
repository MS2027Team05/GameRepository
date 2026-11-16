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
	private readonly List<PlayerMatchSummary> m_LatestMatchSummaries = new List<PlayerMatchSummary>();
	private MatchResultData m_LatestMatchResult;
	private ExhibitionStatsSnapshot m_LatestExhibitionStats;
	private bool m_HasExhibitionStats;

	public MatchResultData LatestMatchResult => m_LatestMatchResult;
	public ExhibitionStatsSnapshot LatestExhibitionStats => m_LatestExhibitionStats;
	public bool HasExhibitionStats => m_HasExhibitionStats;
	public IReadOnlyList<PlayerMatchSummary> LatestMatchSummaries => m_LatestMatchSummaries;

	/// <summary>
	/// 必要に応じてクライアント側にも永続ストアのインスタンスを確保します。
	/// </summary>
	public static SessionPlayerDataStore EnsureInstance()
	{
		if (Instance != null)
		{
			return Instance;
		}

		var storeObj = new GameObject("SessionPlayerDataStore");
		return storeObj.AddComponent<SessionPlayerDataStore>();
	}

	private bool IsServer { get => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer; }

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
		if (!IsServer)
		{
			return;
		}

		m_PlayerDataCache.Remove(clientId);
	}

	/// <summary>
	/// プレイヤーの設定データをキャッシュへ保存または更新します。
	/// </summary>
	public void SavePlayerData(LobbyPlayerData data)
	{
		if (!IsServer)
		{
			return;
		}

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
		FixedString64Bytes defaultName = $"Player {clientId}";
		var defaultData = new LobbyPlayerData(
			clientId,
			defaultName,
			ETeamType.None,
			ECharacterType.Man,
			EHighlightColor.Black,
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
	/// 全プレイヤーの準備完了(IsReady)状態を解除します(ゲーム終了後のホットロード用)。
	/// </summary>
	public void ResetAllReadyStates()
	{
		if (!IsServer)
		{
			return;
		}

		var keys = new List<ulong>(m_PlayerDataCache.Keys);
		foreach (var clientId in keys)
		{
			var data = m_PlayerDataCache[clientId];
			data.IsReady = false;
			m_PlayerDataCache[clientId] = data;
		}
	}

	/// <summary>
	/// 試合結果(スコア・勝敗)を保存します。
	/// </summary>
	public void SetMatchResult(int redScore, int blueScore, ETeamType winnerTeam)
	{
		m_LatestMatchResult = new MatchResultData
		{
			m_RedScore = redScore,
			m_BlueScore = blueScore,
			m_WinnerTeam = winnerTeam,
			m_HasResult = true,
		};
	}

	/// <summary>
	/// 試合終了時にサーバーから同期されたプレイヤーサマリー(称号含む)および展示統計を保存します。
	/// </summary>
	public void SetMatchAwardsAndStats(
		IReadOnlyList<PlayerMatchSummary> summaries,
		in ExhibitionStatsSnapshot exhibitionStats)
	{
		m_LatestMatchSummaries.Clear();
		if (summaries != null)
		{
			for (int i = 0; i < summaries.Count; i++)
			{
				PlayerMatchSummary summary = summaries[i];
				if (summary == null)
				{
					continue;
				}

				m_LatestMatchSummaries.Add(summary);
				m_PlayerDataCache[summary.m_PlayerData.ClientId] = summary.m_PlayerData;
			}
		}

		m_LatestExhibitionStats = exhibitionStats;
		m_HasExhibitionStats = true;
	}

	/// <summary>
	/// 指定ClientIdの確定サマリー(個人表彰含む)を取得します。
	/// </summary>
	public bool TryGetPlayerSummary(ulong clientId, out PlayerMatchSummary summary)
	{
		for (int i = 0; i < m_LatestMatchSummaries.Count; i++)
		{
			if (m_LatestMatchSummaries[i] != null && m_LatestMatchSummaries[i].m_PlayerData.ClientId == clientId)
			{
				summary = m_LatestMatchSummaries[i];
				return true;
			}
		}

		summary = null;
		return false;
	}

	/// <summary>
	/// 新規試合開始時に前回試合の表彰キャッシュのみをクリアします。
	/// </summary>
	public void ClearLatestMatchAwards()
	{
		m_LatestMatchSummaries.Clear();
	}

	/// <summary>
	/// セッション終了時やロビー解散時に全キャッシュをクリアします。
	/// </summary>
	public void ClearAll()
	{
		m_PlayerDataCache.Clear();
		m_LatestMatchSummaries.Clear();
		m_LatestMatchResult = default;
		m_LatestExhibitionStats = default;
		m_HasExhibitionStats = false;
	}
}

/// <summary>
/// 試合終了時のスコアおよび勝敗結果を保持する構造体。
/// </summary>
public struct MatchResultData
{
	public int m_RedScore;
	public int m_BlueScore;
	public ETeamType m_WinnerTeam;
	public bool m_HasResult;
}
