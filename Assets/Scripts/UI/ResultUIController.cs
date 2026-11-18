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
	[Tooltip("各プレイヤーの所属チーム・名前を表示するUIテキスト(任意)")]
	[SerializeField] private TextMeshProUGUI m_PlayerListText;

	[Header("ローカル個人表彰UI(任意)")]
	[Tooltip("ローカルプレイヤーの個人表彰全体を覆うパネル(背景を含む親オブジェクト)")]
	[SerializeField] private GameObject m_LocalAwardsRoot;
	[Tooltip("ローカルプレイヤーの個人表彰を表示するスロット配列(最大3個)")]
	[SerializeField] private ResultAwardItemUI[] m_LocalAwardItems;
	[Tooltip("個人表彰が存在しない場合(観戦時等)に表示する代替テキスト(任意)")]
	[SerializeField] private TextMeshProUGUI m_NoLocalAwardsText;

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
	/// 参加全プレイヤーのモデル・カラー・名前およびローカルプレイヤーの個人表彰をUIに反映します。
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
				m_PlayerListText.text = "プレイヤー情報なし(オフライン検証)";
			}

			DisplayLocalPlayerAwards(store);
			return;
		}

		// 3D台座への反映(試合に参加したRed/Blueプレイヤーのみ対象)
		int displayIndex = 0;
		if (m_PlayerDisplays != null && m_PlayerDisplays.Length > 0)
		{
			foreach (var kvp in allPlayers)
			{
				var playerData = kvp.Value;
				if (playerData.Team != ETeamType.Red && playerData.Team != ETeamType.Blue)
				{
					continue;
				}

				if (displayIndex >= m_PlayerDisplays.Length)
				{
					break;
				}

				var display = m_PlayerDisplays[displayIndex];
				if (display != null)
				{
					display.gameObject.SetActive(true);
					Color nameColor = GetTeamColor(playerData.Team);
					display.ApplyPlayerData(playerData, nameColor);
				}

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

		DisplayLocalPlayerAwards(store);

		// UIテキスト一覧への反映(観戦者・未所属は除外)
		if (m_PlayerListText != null)
		{
			m_Builder.Clear();
			m_Builder.AppendLine("=== 参加プレイヤー ===");
			foreach (var kvp in allPlayers)
			{
				var p = kvp.Value;
				if (p.Team != ETeamType.Red && p.Team != ETeamType.Blue)
				{
					continue;
				}

				string teamTag = p.Team == ETeamType.Red ? "[RED]" : "[BLUE]";
				m_Builder.Append($"{teamTag} {p.PlayerName}\n");
			}

			m_PlayerListText.text = m_Builder.ToString();
		}
	}

	private void DisplayLocalPlayerAwards(SessionPlayerDataStore store)
	{
		if (m_LocalAwardItems == null || m_LocalAwardItems.Length == 0)
		{
			if (m_LocalAwardsRoot != null)
			{
				m_LocalAwardsRoot.SetActive(false);
			}

			return;
		}

		ulong localClientId = (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
			? NetworkManager.Singleton.LocalClientId
			: 0;

		PlayerMatchSummary summary = null;
		bool hasAwards = store != null
			&& store.TryGetPlayerSummary(localClientId, out summary)
			&& summary.m_Awards != null
			&& summary.m_Awards.Length > 0;

		if (!hasAwards)
		{
			HideAllLocalAwardItems();
			bool showEmptyNotice = m_NoLocalAwardsText != null;
			if (m_LocalAwardsRoot != null)
			{
				m_LocalAwardsRoot.SetActive(showEmptyNotice);
			}

			if (m_NoLocalAwardsText != null)
			{
				m_NoLocalAwardsText.gameObject.SetActive(true);
			}

			return;
		}

		if (m_LocalAwardsRoot != null)
		{
			m_LocalAwardsRoot.SetActive(true);
		}

		if (m_NoLocalAwardsText != null)
		{
			m_NoLocalAwardsText.gameObject.SetActive(false);
		}

		PlayerAwardEntry[] awards = summary.m_Awards;
		for (int i = 0; i < m_LocalAwardItems.Length; i++)
		{
			ResultAwardItemUI item = m_LocalAwardItems[i];
			if (item == null)
			{
				continue;
			}

			if (i < awards.Length)
			{
				item.SetAward(awards[i]);
			}
			else
			{
				item.Hide();
			}
		}
	}

	private void HideAllLocalAwardItems()
	{
		if (m_LocalAwardItems == null)
		{
			return;
		}

		for (int i = 0; i < m_LocalAwardItems.Length; i++)
		{
			if (m_LocalAwardItems[i] != null)
			{
				m_LocalAwardItems[i].Hide();
			}
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
