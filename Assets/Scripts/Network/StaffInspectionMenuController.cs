using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 全シーンでF1キーにより開閉可能なスタッフ用緊急管理・診断メニュークラス。
/// ネットワーク状態の診断、チームの強制変更、必要人数の変更、強制リセット、緊急切断を担当します。
/// </summary>
public class StaffInspectionMenuController : MonoBehaviour
{
	[Header("パネルルート参照")]
	[SerializeField] private GameObject m_MenuRoot;

	[Header("診断情報テキストUI")]
	[SerializeField] private TextMeshProUGUI m_ConnectionInfoText;
	[SerializeField] private TextMeshProUGUI m_MemberListText;

	[Header("緊急操作ボタンUI")]
	[SerializeField] private Button m_SetRedTeamButton;
	[SerializeField] private Button m_SetBlueTeamButton;
	[SerializeField] private Button m_LeaveTeamButton;
	[SerializeField] private Button m_CycleMinPlayersButton;
	[SerializeField] private Button m_ForceStartButton;
	[SerializeField] private Button m_ForceResetButton;
	[SerializeField] private Button m_EmergencyDisconnectButton;
	[SerializeField] private Button m_CloseButton;
	[SerializeField] private Toggle m_SkipTutorialToggle;

	[Header("遷移先シーン名")]
	[SerializeField] private string m_BootstrapSceneName;

	private readonly StringBuilder m_InfoBuilder = new StringBuilder();
	private LobbyManager m_CachedLobbyManager;

	private void Awake()
	{
		SetupButtonListeners();
	}

	private void Start()
	{
		if (m_MenuRoot != null)
		{
			m_MenuRoot.SetActive(false);
		}
	}

	private void Update()
	{
		HandleF1Input();

		if (m_MenuRoot != null && m_MenuRoot.activeSelf)
		{
			RefreshDiagnostics();
		}
	}

	private void HandleF1Input()
	{
		if (UnityEngine.InputSystem.Keyboard.current != null &&
			UnityEngine.InputSystem.Keyboard.current.f1Key.wasPressedThisFrame)
		{
			ToggleMenu();
		}
	}

	/// <summary>
	/// スタッフメニューの表示/非表示をトグル切り替えします。
	/// </summary>
	public void ToggleMenu()
	{
		if (m_MenuRoot == null) return;

		bool nextState = !m_MenuRoot.activeSelf;
		m_MenuRoot.SetActive(nextState);

		if (nextState)
		{
			RefreshDiagnostics();
		}
	}

	private void SetupButtonListeners()
	{
		if (m_CloseButton != null)
		{
			m_CloseButton.onClick.AddListener(ToggleMenu);
		}

		if (m_SetRedTeamButton != null)
		{
			m_SetRedTeamButton.onClick.AddListener(() =>
			{
				FindLobbyManager()?.JoinTeam(ETeamType.Red);
			});
		}

		if (m_SetBlueTeamButton != null)
		{
			m_SetBlueTeamButton.onClick.AddListener(() =>
			{
				FindLobbyManager()?.JoinTeam(ETeamType.Blue);
			});
		}

		if (m_LeaveTeamButton != null)
		{
			m_LeaveTeamButton.onClick.AddListener(() =>
			{
				FindLobbyManager()?.LeaveTeam();
			});
		}

		if (m_CycleMinPlayersButton != null)
		{
			m_CycleMinPlayersButton.onClick.AddListener(() =>
			{
				FindLobbyManager()?.CycleMinPlayersDebug();
			});
		}

		if (m_ForceStartButton != null)
		{
			m_ForceStartButton.onClick.AddListener(() =>
			{
				FindLobbyManager()?.ForceStartGameDebug();
			});
		}

		if (m_ForceResetButton != null)
		{
			m_ForceResetButton.onClick.AddListener(() =>
			{
				if (GameSessionResetCoordinator.Instance != null)
				{
					GameSessionResetCoordinator.Instance.EndGameAndReturnToLobby();
				}
			});
		}

		if (m_EmergencyDisconnectButton != null)
		{
			m_EmergencyDisconnectButton.onClick.AddListener(EmergencyDisconnect);
		}

		if (m_SkipTutorialToggle != null)
		{
			m_SkipTutorialToggle.onValueChanged.AddListener(isOn =>
			{
				var lobby = FindLobbyManager();
				if (lobby != null)
				{
					lobby.SkipTutorial = isOn;
				}
			});
		}
	}

	private LobbyManager FindLobbyManager()
	{
		if (m_CachedLobbyManager == null)
		{
			m_CachedLobbyManager = FindFirstObjectByType<LobbyManager>();
		}
		return m_CachedLobbyManager;
	}

	private void RefreshDiagnostics()
	{
		if (NetworkManager.Singleton == null)
		{
			if (m_ConnectionInfoText != null) m_ConnectionInfoText.text = "NetworkManager: 未起動";
			if (m_MemberListText != null) m_MemberListText.text = string.Empty;
			return;
		}

		// 接続情報
		if (m_ConnectionInfoText != null)
		{
			m_InfoBuilder.Clear();
			bool isServer = NetworkManager.Singleton.IsServer;
			m_InfoBuilder.Append(isServer ? "[HOST]" : "[CLIENT]");
			m_InfoBuilder.Append($" LocalClientId: {NetworkManager.Singleton.LocalClientId}");
			m_InfoBuilder.Append($" | IsListening: {NetworkManager.Singleton.IsListening}");

			var lobby = FindLobbyManager();
			if (lobby != null)
			{
				m_InfoBuilder.Append($"\nHostIP: {lobby.HostIpAddress} | 必要人数: {lobby.RequiredPlayerCount}");
			}
			m_ConnectionInfoText.text = m_InfoBuilder.ToString();
		}

		// メンバー一覧
		if (m_MemberListText != null)
		{
			m_InfoBuilder.Clear();
			var lobby = FindLobbyManager();
			if (lobby != null)
			{
				var players = lobby.LobbyPlayers;
				m_InfoBuilder.Append($"参加者 ({players.Count}名):\n");
				for (int i = 0; i < players.Count; i++)
				{
					var p = players[i];
					string readyMark = p.IsReady ? "[READY]" : "[WAIT]";
					m_InfoBuilder.Append($"#{p.ClientId} {p.PlayerName} [{p.Team}] {readyMark}\n");
				}
			}
			m_MemberListText.text = m_InfoBuilder.ToString();
		}
	}

	private void EmergencyDisconnect()
	{
		if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
		{
			NetworkManager.Singleton.Shutdown();
		}

		string targetScene = !string.IsNullOrEmpty(m_BootstrapSceneName) ? m_BootstrapSceneName : "TitleScene";
		SceneManager.LoadScene(targetScene);
	}
}

