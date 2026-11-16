using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 試合終了後のリザルト画面を制御するUIコントローラー。
/// チーム得点・勝敗結果の表示、各参加プレイヤーのモデル・カラー・名前の表示、
/// およびタイトル画面への復帰を担当します。
/// </summary>
public class ResultUIController : MonoBehaviour
{
	[Header("スコア・勝敗UI")]
	[SerializeField] private TextMeshProUGUI m_RedScoreText;
	[SerializeField] private TextMeshProUGUI m_BlueScoreText;
	[SerializeField] private TextMeshProUGUI m_WinnerResultText;

	[Header("参加プレイヤー表示(3Dキャラクター台座)")]
	[Tooltip("リザルトシーン上に配置されたLobbyPlayerDisplayの配列(任意)")]
	[SerializeField] private LobbyPlayerDisplay[] m_PlayerDisplays;
	[Tooltip("プレイヤー外見カタログ参照(未設定時はResourcesやインスタンスから取得を試みます)")]
	[SerializeField] private PlayerAppearanceCatalog m_AppearanceCatalog;

	[Header("参加プレイヤー表示(UIテキストリスト)")]
	[Tooltip("各プレイヤーの所属チーム・名前・外見設定・個人表彰を表示するUIテキスト(任意)")]
	[SerializeField] private TextMeshProUGUI m_PlayerListText;

	[Header("個人表彰・展示統計UI(任意)")]
	[Tooltip("ローカルプレイヤー自身の称号(1〜3個)を表示する専用テキスト(任意)")]
	[SerializeField] private TextMeshProUGUI m_LocalAwardsText;
	[Tooltip("3D台座(m_PlayerDisplays)の各スロットに対応する称号テキスト配列(任意)")]
	[SerializeField] private TextMeshProUGUI[] m_PedestalAwardTexts;
	[Tooltip("展示用の累計統計を表示する専用テキスト(任意。未設定時は一覧末尾へ追記)")]
	[SerializeField] private TextMeshProUGUI m_ExhibitionStatsText;

	[Header("タイトル・ロビー復帰")]
	[SerializeField] private Button m_ReturnToTitleButton;
	[SerializeField] private string m_TitleSceneName;
	[Tooltip("LobbySceneへの同期復帰を待つクライアント向け案内テキスト(任意)")]
	[SerializeField] private TextMeshProUGUI m_ClientWaitNoticeText;

	private readonly StringBuilder m_Builder = new StringBuilder();
	private bool m_IsTransitioning;

	private void Awake()
	{
		if (m_ReturnToTitleButton != null)
		{
			m_ReturnToTitleButton.onClick.AddListener(OnReturnToTitleClicked);
		}
	}

	private void Start()
	{
		// カーソルと入力をUI操作向けに開放
		PlayerInputReceiver.SetAllPaused(false);
		PlayerInputReceiver.SetCursorLocked(false);

		ConfigureReturnControlForRole();
		DisplayResult();
	}

	/// <summary>
	/// セッションデータストアから試合結果とプレイヤー情報を取得して画面に反映します。
	/// </summary>
	private void DisplayResult()
	{
		int redScore = 0;
		int blueScore = 0;
		ETeamType winnerTeam = ETeamType.None;

		if (SessionPlayerDataStore.Instance != null && SessionPlayerDataStore.Instance.LatestMatchResult.m_HasResult)
		{
			var matchResult = SessionPlayerDataStore.Instance.LatestMatchResult;
			redScore = matchResult.m_RedScore;
			blueScore = matchResult.m_BlueScore;
			winnerTeam = matchResult.m_WinnerTeam;
		}
		else if (MatchManager.Instance != null)
		{
			redScore = MatchManager.Instance.RedScore;
			blueScore = MatchManager.Instance.BlueScore;
			winnerTeam = MatchManager.Instance.WinnerTeam;
		}

		// スコア表示
		if (m_RedScoreText != null)
		{
			m_RedScoreText.text = redScore.ToString();
		}

		if (m_BlueScoreText != null)
		{
			m_BlueScoreText.text = blueScore.ToString();
		}

		// 勝敗判定表示
		if (m_WinnerResultText != null)
		{
			switch (winnerTeam)
			{
				case ETeamType.Red:
					m_WinnerResultText.text = "RED TEAM VICTORY!";
					m_WinnerResultText.color = new Color(1.0f, 0.3f, 0.3f);
					break;
				case ETeamType.Blue:
					m_WinnerResultText.text = "BLUE TEAM VICTORY!";
					m_WinnerResultText.color = new Color(0.3f, 0.6f, 1.0f);
					break;
				default:
					m_WinnerResultText.text = "DRAW MATCH";
					m_WinnerResultText.color = Color.white;
					break;
			}
		}

		// 参加プレイヤーの表示
		DisplayPlayers();
	}

	/// <summary>
	/// 参加全プレイヤーのモデル・カラー・名前・個人表彰および展示統計をUIに反映します。
	/// </summary>
	private void DisplayPlayers()
	{
		SessionPlayerDataStore store = SessionPlayerDataStore.Instance;
		IReadOnlyDictionary<ulong, LobbyPlayerData> allPlayers = store != null
			? store.GetAllPlayerData()
			: null;

		if (allPlayers == null || allPlayers.Count == 0)
		{
			if (m_PlayerListText != null)
			{
				m_Builder.Clear();
				m_Builder.AppendLine("プレイヤー情報なし(オフライン検証)");
				if (m_ExhibitionStatsText == null)
				{
					m_Builder.AppendLine();
					ExhibitionStatsStore.AppendSummaryText(m_Builder, GetExhibitionSnapshot(store));
				}
				m_PlayerListText.text = m_Builder.ToString();
			}

			DisplayLocalPlayerAwards(store);
			DisplayExhibitionStats(store);
			return;
		}

		// 3D台座および台座別称号テキストへの反映
		int displayIndex = 0;
		if (m_PlayerDisplays != null && m_PlayerDisplays.Length > 0)
		{
			foreach (var kvp in allPlayers)
			{
				if (displayIndex >= m_PlayerDisplays.Length)
				{
					break;
				}

				var playerData = kvp.Value;
				var display = m_PlayerDisplays[displayIndex];
				if (display != null)
				{
					display.gameObject.SetActive(true);
					Color nameColor = GetTeamColor(playerData.Team);
					display.ApplyPlayerData(playerData, nameColor);
				}

				UpdatePedestalAwardText(displayIndex, playerData.ClientId, store);
				displayIndex++;
			}

			// 余った台座は非表示
			for (int i = displayIndex; i < m_PlayerDisplays.Length; i++)
			{
				if (m_PlayerDisplays[i] != null)
				{
					m_PlayerDisplays[i].gameObject.SetActive(false);
				}
			}
		}

		if (m_PedestalAwardTexts != null)
		{
			for (int i = displayIndex; i < m_PedestalAwardTexts.Length; i++)
			{
				if (m_PedestalAwardTexts[i] != null)
				{
					m_PedestalAwardTexts[i].text = string.Empty;
				}
			}
		}

		DisplayLocalPlayerAwards(store);
		DisplayExhibitionStats(store);

		// UIテキスト一覧への反映
		if (m_PlayerListText != null)
		{
			m_Builder.Clear();
			m_Builder.AppendLine("=== 参加プレイヤー・個人表彰 ===");
			foreach (var kvp in allPlayers)
			{
				var p = kvp.Value;
				string teamTag = p.Team == ETeamType.Red ? "[RED]" : (p.Team == ETeamType.Blue ? "[BLUE]" : "[NONE]");
				m_Builder.Append($"{teamTag} {p.PlayerName}\n");
				AppendPlayerAwards(m_Builder, p.ClientId, store, "  ");
			}

			if (m_ExhibitionStatsText == null)
			{
				m_Builder.AppendLine();
				ExhibitionStatsStore.AppendSummaryText(m_Builder, GetExhibitionSnapshot(store));
			}

			m_PlayerListText.text = m_Builder.ToString();
		}
	}

	private void DisplayLocalPlayerAwards(SessionPlayerDataStore store)
	{
		if (m_LocalAwardsText == null)
		{
			return;
		}

		ulong localClientId = (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
			? NetworkManager.Singleton.LocalClientId
			: 0;

		m_Builder.Clear();
		m_Builder.AppendLine("【あなたの個人表彰】");
		if (store != null && store.TryGetPlayerSummary(localClientId, out PlayerMatchSummary summary) &&
			summary.m_Awards != null && summary.m_Awards.Length > 0)
		{
			AppendAwardEntries(m_Builder, summary.m_Awards, string.Empty);
		}
		else
		{
			m_Builder.AppendLine("表彰データなし(観戦 / オフライン検証)");
		}

		m_LocalAwardsText.text = m_Builder.ToString();
	}

	private void UpdatePedestalAwardText(int displayIndex, ulong clientId, SessionPlayerDataStore store)
	{
		if (m_PedestalAwardTexts == null || displayIndex < 0 || displayIndex >= m_PedestalAwardTexts.Length)
		{
			return;
		}

		TextMeshProUGUI targetText = m_PedestalAwardTexts[displayIndex];
		if (targetText == null)
		{
			return;
		}

		m_Builder.Clear();
		AppendPlayerAwards(m_Builder, clientId, store, string.Empty);
		targetText.text = m_Builder.ToString();
	}

	private void DisplayExhibitionStats(SessionPlayerDataStore store)
	{
		if (m_ExhibitionStatsText == null)
		{
			return;
		}

		m_Builder.Clear();
		ExhibitionStatsStore.AppendSummaryText(m_Builder, GetExhibitionSnapshot(store));
		m_ExhibitionStatsText.text = m_Builder.ToString();
	}

	private static ExhibitionStatsSnapshot GetExhibitionSnapshot(SessionPlayerDataStore store)
	{
		return store != null && store.HasExhibitionStats
			? store.LatestExhibitionStats
			: ExhibitionStatsStore.ToSnapshot();
	}

	private static void AppendPlayerAwards(
		StringBuilder builder,
		ulong clientId,
		SessionPlayerDataStore store,
		string prefix)
	{
		if (store == null || !store.TryGetPlayerSummary(clientId, out PlayerMatchSummary summary) ||
			summary.m_Awards == null || summary.m_Awards.Length == 0)
		{
			builder.AppendLine($"{prefix}- 表彰なし");
			return;
		}

		AppendAwardEntries(builder, summary.m_Awards, prefix);
	}

	private static void AppendAwardEntries(StringBuilder builder, PlayerAwardEntry[] awards, string prefix)
	{
		for (int i = 0; i < awards.Length; i++)
		{
			builder.AppendLine($"{prefix}{MatchAwardEvaluator.FormatAwardRichText(awards[i])}");
		}
	}

	private Color GetTeamColor(ETeamType team)
	{
		if (m_AppearanceCatalog != null)
		{
			return m_AppearanceCatalog.GetTeamDisplayColor(team);
		}

		return team switch
		{
			ETeamType.Red => new Color(1.0f, 0.4f, 0.4f),
			ETeamType.Blue => new Color(0.4f, 0.6f, 1.0f),
			_ => Color.white,
		};
	}

	private void ConfigureReturnControlForRole()
	{
		bool waitForHost = IsWaitingForHostLobbyTransition();

		if (m_ReturnToTitleButton != null)
		{
			m_ReturnToTitleButton.gameObject.SetActive(!waitForHost);
		}

		if (m_ClientWaitNoticeText != null)
		{
			m_ClientWaitNoticeText.gameObject.SetActive(waitForHost);
		}
	}

	private string GetTargetReturnSceneName()
	{
		return !string.IsNullOrEmpty(m_TitleSceneName) ? m_TitleSceneName : "TitleScene";
	}

	private bool ShouldPreserveConnectionForReturn(string targetScene)
	{
		return !string.Equals(targetScene, "TitleScene", System.StringComparison.OrdinalIgnoreCase);
	}

	private bool IsWaitingForHostLobbyTransition()
	{
		if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
		{
			return false;
		}

		string targetScene = GetTargetReturnSceneName();
		return ShouldPreserveConnectionForReturn(targetScene) && !NetworkManager.Singleton.IsServer;
	}

	/// <summary>
	/// 遷移先設定および接続ロールに応じてロビーへ同期復帰、または切断してタイトル画面へ戻ります。
	/// </summary>
	private void OnReturnToTitleClicked()
	{
		if (m_IsTransitioning)
		{
			return;
		}

		string targetScene = GetTargetReturnSceneName();
		bool isConnected = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

		if (isConnected && ShouldPreserveConnectionForReturn(targetScene))
		{
			// 接続維持でのLobbyScene復帰はホスト(サーバー)のみから全員一括で実行します。
			if (!NetworkManager.Singleton.IsServer)
			{
				return;
			}

			if (NetworkManager.Singleton.SceneManager == null)
			{
				Debug.LogWarning("[ResultUIController] NetworkSceneManagerが見つからないためロビー復帰を実行できません。");
				return;
			}

			m_IsTransitioning = true;
			SessionPlayerDataStore.Instance?.ResetAllReadyStates();
			PlayerInputReceiver.SetAllPaused(false);
			PlayerInputReceiver.SetCursorLocked(false);
			NetworkManager.Singleton.SceneManager.LoadScene(targetScene, LoadSceneMode.Single);
			return;
		}

		m_IsTransitioning = true;
		if (isConnected)
		{
			NetworkManager.Singleton.Shutdown();
		}

		SceneManager.LoadScene(targetScene);
	}
}
