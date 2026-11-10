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
	[Tooltip("各プレイヤーの所属チーム・名前・外見設定を表示するUIテキスト(任意)")]
	[SerializeField] private TextMeshProUGUI m_PlayerListText;

	[Header("タイトル復帰")]
	[SerializeField] private Button m_ReturnToTitleButton;
	[SerializeField] private string m_TitleSceneName;

	private readonly StringBuilder m_Builder = new StringBuilder();

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

		if (SessionPlayerDataStore.Instance != null && SessionPlayerDataStore.Instance.LatestMatchResult.HasResult)
		{
			var matchResult = SessionPlayerDataStore.Instance.LatestMatchResult;
			redScore = matchResult.RedScore;
			blueScore = matchResult.BlueScore;
			winnerTeam = matchResult.WinnerTeam;
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
	/// 参加全プレイヤーのモデル・カラー・名前を3D台座およびUIテキストに反映します。
	/// </summary>
	private void DisplayPlayers()
	{
		IReadOnlyDictionary<ulong, LobbyPlayerData> allPlayers = null;
		if (SessionPlayerDataStore.Instance != null)
		{
			allPlayers = SessionPlayerDataStore.Instance.GetAllPlayerData();
		}

		if (allPlayers == null || allPlayers.Count == 0)
		{
			if (m_PlayerListText != null)
			{
				m_PlayerListText.text = "プレイヤー情報なし(オフライン検証)";
			}
			return;
		}

		// 3D台座への反映
		if (m_PlayerDisplays != null && m_PlayerDisplays.Length > 0)
		{
			int displayIndex = 0;
			foreach (var kvp in allPlayers)
			{
				if (displayIndex >= m_PlayerDisplays.Length) break;

				var playerData = kvp.Value;
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

		// UIテキスト一覧への反映
		if (m_PlayerListText != null)
		{
			m_Builder.Clear();
			m_Builder.AppendLine("=== 参加プレイヤー ===");
			foreach (var kvp in allPlayers)
			{
				var p = kvp.Value;
				string teamTag = p.Team == ETeamType.Red ? "[RED]" : (p.Team == ETeamType.Blue ? "[BLUE]" : "[NONE]");
				m_Builder.Append($"{teamTag} {p.PlayerName} (タイプ: {p.CharacterType}, カラー: {p.HighlightColor})\n");
			}
			m_PlayerListText.text = m_Builder.ToString();
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

	/// <summary>
	/// タイトル画面へ戻ります。ネットワーク接続中の場合は安全に切断します。
	/// </summary>
	private void OnReturnToTitleClicked()
	{
		if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
		{
			NetworkManager.Singleton.Shutdown();
		}

		string targetScene = !string.IsNullOrEmpty(m_TitleSceneName) ? m_TitleSceneName : "TitleScene";
		SceneManager.LoadScene(targetScene);
	}
}
