using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ロビー画面のUI表示およびパネル切り替え制御を担当するクラス。
/// 本番専用ホスト・クライアント・デバッグ兼任ホストの各端末ロールに応じてパネルの表示/非表示を一元管理します。
/// </summary>
public class LobbyUIController : MonoBehaviour
{
	[Header("マネージャー参照")]
	[SerializeField] private LobbyManager m_LobbyManager;

	[Header("パネルルート参照")]
	[SerializeField] private GameObject m_SharedInfoPanel;
	[SerializeField] private GameObject m_HostControlPanel;
	[SerializeField] private GameObject m_PlayerControlPanel;
	[SerializeField] private GameObject m_DebugPanel;

	[Header("全体情報UI参照")]
	[SerializeField] private TextMeshProUGUI m_PlayerCountText;
	[SerializeField] private TextMeshProUGUI m_MemberListText;
	[SerializeField] private TextMeshProUGUI m_LobbyStatusText;
	[SerializeField] private TextMeshProUGUI m_HostIpText;

	[Header("ホスト操作UI参照")]
	[SerializeField] private Button m_StartGameButton;
	[SerializeField] private Button m_LeaveLobbyButton;

	[Header("プレイヤー操作UI参照(自端末用)")]
	[SerializeField] private TMP_InputField m_PlayerNameInput;
	[SerializeField] private Button m_JoinRedTeamButton;
	[SerializeField] private Button m_JoinBlueTeamButton;
	[SerializeField] private Button m_LeaveTeamButton;
	[SerializeField] private Button m_ToggleCharacterButton;
	[SerializeField] private TextMeshProUGUI m_CharacterText;
	[SerializeField] private Button m_CycleColorButton;
	[SerializeField] private TextMeshProUGUI m_ColorText;
	[SerializeField] private Button m_ReadyButton;
	[SerializeField] private TextMeshProUGUI m_ReadyButtonText;
	[SerializeField] private Button m_ClientLeaveLobbyButton;

	[Header("デバッグ操作UI参照")]
	[SerializeField] private Button m_DebugCycleMinPlayersButton;
	[SerializeField] private TextMeshProUGUI m_DebugMinPlayersText;
	[SerializeField] private Button m_DebugToggleSceneButton;
	[SerializeField] private TextMeshProUGUI m_DebugTargetSceneText;
	[SerializeField] private Button m_DebugForceStartButton;

	private readonly StringBuilder m_InfoStringBuilder = new StringBuilder();

	private void Awake()
	{
		if (m_LobbyManager == null)
		{
			m_LobbyManager = FindFirstObjectByType<LobbyManager>();
		}

		SetupUIListeners();
	}

	private void Start()
	{
		SetupPanelVisibility();

		if (m_LobbyManager != null)
		{
			m_LobbyManager.OnLobbyDataChanged += RefreshAllUI;
		}

		RefreshAllUI();
	}

	private void OnDestroy()
	{
		if (m_LobbyManager != null)
		{
			m_LobbyManager.OnLobbyDataChanged -= RefreshAllUI;
		}
	}

	/// <summary>
	/// 端末ロール(本番専用ホスト / クライアント / デバッグ兼任ホスト)に応じて各パネルの表示状態を決定します。
	/// </summary>
	private void SetupPanelVisibility()
	{
		bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
		bool isPlayerHost = NetworkConnectManager.IsPlayerHost;
		bool isDebugBuild = Application.isEditor || Debug.isDebugBuild;

		// ① 全体情報パネル: 全員表示
		if (m_SharedInfoPanel != null)
		{
			m_SharedInfoPanel.SetActive(true);
		}

		// ② ホスト操作パネル: サーバー(Host)のみ表示
		if (m_HostControlPanel != null)
		{
			m_HostControlPanel.SetActive(isServer);
		}

		// ③ プレイヤー操作パネル: クライアント端末およびデバッグ兼任ホストで表示(本番専用ホストでは非表示)
		bool showPlayerControls = !isServer || isPlayerHost;
		if (m_PlayerControlPanel != null)
		{
			m_PlayerControlPanel.SetActive(showPlayerControls);
		}

		// ④ デバッグパネル: サーバーかつデバッグホストかつデバッグビルド/Editorのみ表示
		bool showDebugPanel = isServer && isPlayerHost && isDebugBuild;
		if (m_DebugPanel != null)
		{
			m_DebugPanel.SetActive(showDebugPanel);
		}
	}

	private void SetupUIListeners()
	{
		// ホスト操作
		if (m_StartGameButton != null)
		{
			m_StartGameButton.onClick.AddListener(() => m_LobbyManager?.StartGame());
		}

		if (m_LeaveLobbyButton != null)
		{
			m_LeaveLobbyButton.onClick.AddListener(() => m_LobbyManager?.LeaveLobby());
		}

		// プレイヤー操作
		if (m_PlayerNameInput != null)
		{
			m_PlayerNameInput.onEndEdit.AddListener(name => m_LobbyManager?.SetPlayerName(name));
		}

		if (m_JoinRedTeamButton != null)
		{
			m_JoinRedTeamButton.onClick.AddListener(() => m_LobbyManager?.JoinTeam(ETeamType.Red));
		}

		if (m_JoinBlueTeamButton != null)
		{
			m_JoinBlueTeamButton.onClick.AddListener(() => m_LobbyManager?.JoinTeam(ETeamType.Blue));
		}

		if (m_LeaveTeamButton != null)
		{
			m_LeaveTeamButton.onClick.AddListener(() => m_LobbyManager?.LeaveTeam());
		}

		if (m_ToggleCharacterButton != null)
		{
			m_ToggleCharacterButton.onClick.AddListener(() => m_LobbyManager?.ToggleCharacter());
		}

		if (m_CycleColorButton != null)
		{
			m_CycleColorButton.onClick.AddListener(() => m_LobbyManager?.CycleHighlightColor());
		}

		if (m_ReadyButton != null)
		{
			m_ReadyButton.onClick.AddListener(() => m_LobbyManager?.ToggleReady());
		}

		if (m_ClientLeaveLobbyButton != null)
		{
			m_ClientLeaveLobbyButton.onClick.AddListener(() => m_LobbyManager?.LeaveLobby());
		}

		// デバッグ操作
		if (m_DebugCycleMinPlayersButton != null)
		{
			m_DebugCycleMinPlayersButton.onClick.AddListener(() => m_LobbyManager?.CycleMinPlayersDebug());
		}

		if (m_DebugToggleSceneButton != null)
		{
			m_DebugToggleSceneButton.onClick.AddListener(() => m_LobbyManager?.ToggleTargetSceneDebug());
		}

		if (m_DebugForceStartButton != null)
		{
			m_DebugForceStartButton.onClick.AddListener(() => m_LobbyManager?.ForceStartGameDebug());
		}
	}

	private void RefreshAllUI()
	{
		if (m_LobbyManager == null || NetworkManager.Singleton == null) return;

		UpdateSharedInfo();
		UpdatePlayerControls();
		UpdateHostControls();
		UpdateDebugControls();
	}

	private void UpdateSharedInfo()
	{
		int count = m_LobbyManager.ConnectedPlayerCount;
		int required = m_LobbyManager.RequiredPlayerCount;

		if (m_PlayerCountText != null)
		{
			m_InfoStringBuilder.Clear();
			m_InfoStringBuilder.Append("参加人数: ").Append(count).Append(" / ").Append(required);
			m_PlayerCountText.text = m_InfoStringBuilder.ToString();
		}

		if (m_HostIpText != null)
		{
			bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
			if (isServer)
			{
				m_HostIpText.text = $"接続先IP: {m_LobbyManager.HostIpAddress}";
				m_HostIpText.gameObject.SetActive(true);
			}
			else
			{
				m_HostIpText.gameObject.SetActive(false);
			}
		}

		if (m_MemberListText != null)
		{
			UpdateMemberListText();
		}

		if (m_LobbyStatusText != null)
		{
			m_LobbyManager.CheckCanStartGame(out string statusMessage);
			m_LobbyStatusText.text = statusMessage;
		}
	}

	private void UpdateMemberListText()
	{
		var players = m_LobbyManager.LobbyPlayers;
		m_InfoStringBuilder.Clear();

		// Team Red
		m_InfoStringBuilder.AppendLine("【Team Red】 (最大2名)");
		int redCount = 0;
		for (int i = 0; i < players.Count; i++)
		{
			var p = players[i];
			if (p.Team == ETeamType.Red)
			{
				AppendPlayerLine(p);
				redCount++;
			}
		}
		if (redCount == 0) m_InfoStringBuilder.AppendLine("  (空席)");

		// Team Blue
		m_InfoStringBuilder.AppendLine("\n【Team Blue】 (最大2名)");
		int blueCount = 0;
		for (int i = 0; i < players.Count; i++)
		{
			var p = players[i];
			if (p.Team == ETeamType.Blue)
			{
				AppendPlayerLine(p);
				blueCount++;
			}
		}
		if (blueCount == 0) m_InfoStringBuilder.AppendLine("  (空席)");

		// 未所属
		int unassignedCount = 0;
		for (int i = 0; i < players.Count; i++)
		{
			if (players[i].Team == ETeamType.None) unassignedCount++;
		}
		if (unassignedCount > 0)
		{
			m_InfoStringBuilder.AppendLine("\n【未所属】");
			for (int i = 0; i < players.Count; i++)
			{
				var p = players[i];
				if (p.Team == ETeamType.None)
				{
					AppendPlayerLine(p);
				}
			}
		}

		m_MemberListText.text = m_InfoStringBuilder.ToString();
	}

	private void AppendPlayerLine(LobbyPlayerData p)
	{
		string hostTag = p.ClientId == NetworkManager.ServerClientId ? " [Host]" : "";
		string localTag = p.ClientId == NetworkManager.Singleton.LocalClientId ? " [You]" : "";
		string readyTag = p.IsReady ? " <color=green>[READY]</color>" : " <color=yellow>[WAITING]</color>";

		m_InfoStringBuilder.Append("  - ")
			.Append(p.PlayerName.ToString())
			.Append(" (").Append(p.CharacterType).Append(", ").Append(p.HighlightColor).Append(")")
			.Append(readyTag)
			.Append(hostTag)
			.Append(localTag)
			.AppendLine();
	}

	private void UpdatePlayerControls()
	{
		if (!m_LobbyManager.TryGetLocalPlayerData(out var localPlayer)) return;

		bool isSeated = localPlayer.Team != ETeamType.None;
		bool isReady = localPlayer.IsReady;

		if (m_PlayerNameInput != null)
		{
			m_PlayerNameInput.interactable = !isReady;
			if (!m_PlayerNameInput.isFocused && m_PlayerNameInput.text != localPlayer.PlayerName.ToString())
			{
				m_PlayerNameInput.text = localPlayer.PlayerName.ToString();
			}
		}

		if (m_JoinRedTeamButton != null)
		{
			m_JoinRedTeamButton.interactable = !isReady && localPlayer.Team != ETeamType.Red;
		}

		if (m_JoinBlueTeamButton != null)
		{
			m_JoinBlueTeamButton.interactable = !isReady && localPlayer.Team != ETeamType.Blue;
		}

		if (m_LeaveTeamButton != null)
		{
			m_LeaveTeamButton.interactable = !isReady && isSeated;
		}

		if (m_ToggleCharacterButton != null)
		{
			m_ToggleCharacterButton.interactable = !isReady && isSeated;
		}

		if (m_CharacterText != null)
		{
			m_CharacterText.text = $"モデル: {localPlayer.CharacterType}";
		}

		if (m_CycleColorButton != null)
		{
			m_CycleColorButton.interactable = !isReady && isSeated;
		}

		if (m_ColorText != null)
		{
			m_ColorText.text = $"カラー: {localPlayer.HighlightColor}";
		}

		if (m_ReadyButton != null)
		{
			m_ReadyButton.interactable = isSeated;
		}

		if (m_ReadyButtonText != null)
		{
			m_ReadyButtonText.text = isReady ? "準備完了解除" : "準備完了!";
		}
	}

	private void UpdateHostControls()
	{
		bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
		if (!isServer) return;

		bool canStart = m_LobbyManager.CheckCanStartGame(out _);

		if (m_StartGameButton != null)
		{
			m_StartGameButton.interactable = canStart;
		}
	}

	private void UpdateDebugControls()
	{
		if (m_DebugMinPlayersText != null)
		{
			m_DebugMinPlayersText.text = $"[Debug] 開始人数: {m_LobbyManager.RequiredPlayerCount}人";
		}

		if (m_DebugTargetSceneText != null)
		{
			m_DebugTargetSceneText.text = $"[Debug] 遷移先: {m_LobbyManager.TargetSceneName}";
		}
	}
}
