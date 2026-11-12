using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 全シーンでF1キーにより開閉可能なスタッフ用緊急管理・診断メニュークラス。
/// ネットワーク状態の診断、チームの強制変更、必要人数の変更、強制リセット、緊急切断、
/// インゲームでのリソース消費無制限化、リスポーン、任意シーン遷移を担当します。
/// </summary>
public class StaffInspectionMenuController : MonoBehaviour
{
	[Header("パネルルート参照")]
	[SerializeField] private GameObject m_MenuRoot;

	[Header("診断情報テキストUI")]
	[SerializeField] private TextMeshProUGUI m_ConnectionInfoText;
	[SerializeField] private TextMeshProUGUI m_MemberListText;

	[Header("ロビー専用操作UI")]
	[Tooltip("ロビー用UIの親オブジェクト(任意)。未設定時は各ボタンが個別に自動制御されるためNone(空欄)でも問題ありません")]
	[SerializeField] private GameObject m_LobbyControlsRoot;
	[SerializeField] private Button m_SetRedTeamButton;
	[SerializeField] private Button m_SetBlueTeamButton;
	[SerializeField] private Button m_SetSpectatorTeamButton;
	[SerializeField] private Button m_LeaveTeamButton;
	[SerializeField] private Button m_CycleMinPlayersButton;
	[SerializeField] private Button m_ForceStartButton;
	[SerializeField] private Toggle m_SkipTutorialToggle;

	[Header("インゲーム専用操作UI")]
	[Tooltip("インゲーム用操作の親オブジェクト。ロビーシーンでは自動で非表示化されます")]
	[SerializeField] private GameObject m_InGameControlsRoot;
	[SerializeField] private Toggle m_InfiniteEnergyToggle;
	[SerializeField] private Button m_RespawnPlayerButton;
	[SerializeField] private TMP_Dropdown m_SceneTransitionDropdown;
	[SerializeField] private Button m_LoadSelectedSceneButton;

	[Header("共通緊急操作ボタンUI")]
	[SerializeField] private Button m_ForceResetButton;
	[SerializeField] private Button m_EmergencyDisconnectButton;
	[SerializeField] private Button m_CloseButton;

	[Header("遷移先シーン名")]
	[SerializeField] private string m_BootstrapSceneName;
	[Tooltip("任意シーン遷移で選択可能なシーン一覧")]
	private string[] m_AvailableSceneNames;

	private readonly StringBuilder m_InfoBuilder = new StringBuilder();
	private LobbyManager m_CachedLobbyManager;

	private void Awake()
	{
		SetupButtonListeners();
		SetupSceneTransitionDropdown();
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
	/// インゲーム時はF1押下時にマウスカーソルを開放し、プレイヤー操作を一時停止します。
	/// </summary>
	public void ToggleMenu()
	{
		if (m_MenuRoot == null) return;

		bool nextState = !m_MenuRoot.activeSelf;
		m_MenuRoot.SetActive(nextState);

		bool isLobby = FindLobbyManager() != null;

		if (nextState)
		{
			UpdateContextVisibility();
			RefreshDiagnostics();

			// インゲーム時: マウスカーソルを開放し、背後のプレイヤー入力を一時停止
			if (!isLobby)
			{
				PlayerInputReceiver.SetAllPaused(true);
				PlayerInputReceiver.SetCursorLocked(false);
			}
		}
		else
		{
			// メニューを閉じた際、インゲームかつポーズメニューが開いていなければカーソルを再ロック・入力復旧
			if (!isLobby)
			{
				var pauseMenu = FindFirstObjectByType<InGamePauseMenuController>();
				bool isPauseOpen = pauseMenu != null && pauseMenu.IsMenuOpen;

				if (!isPauseOpen)
				{
					PlayerInputReceiver.SetAllPaused(false);
					PlayerInputReceiver.SetCursorLocked(true);
				}
			}
		}
	}

	/// <summary>
	/// 現在のシーン状態(ロビーかインゲームか)に応じて操作UIの表示/非表示を切り替えます。
	/// </summary>
	private void UpdateContextVisibility()
	{
		bool isLobby = FindLobbyManager() != null;

		if (m_LobbyControlsRoot != null)
		{
			m_LobbyControlsRoot.SetActive(isLobby);
		}
		else
		{
			// 個別制御
			if (m_SetRedTeamButton != null) m_SetRedTeamButton.gameObject.SetActive(isLobby);
			if (m_SetBlueTeamButton != null) m_SetBlueTeamButton.gameObject.SetActive(isLobby);
			if (m_SetSpectatorTeamButton != null) m_SetSpectatorTeamButton.gameObject.SetActive(isLobby);
			if (m_LeaveTeamButton != null) m_LeaveTeamButton.gameObject.SetActive(isLobby);
			if (m_CycleMinPlayersButton != null) m_CycleMinPlayersButton.gameObject.SetActive(isLobby);
			if (m_ForceStartButton != null) m_ForceStartButton.gameObject.SetActive(isLobby);
			if (m_SkipTutorialToggle != null) m_SkipTutorialToggle.gameObject.SetActive(isLobby);
		}

		if (m_InGameControlsRoot != null)
		{
			m_InGameControlsRoot.SetActive(!isLobby);
		}

		// エネルギー無制限トグルの状態を反映
		if (m_InfiniteEnergyToggle != null)
		{
			m_InfiniteEnergyToggle.SetIsOnWithoutNotify(PlayerResourceController.IsInfiniteEnergyDebugEnabled);
		}
	}

	private void SetupButtonListeners()
	{
		if (m_CloseButton != null)
		{
			m_CloseButton.onClick.AddListener(ToggleMenu);
		}

		// ロビー操作
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

		if (m_SetSpectatorTeamButton != null)
		{
			m_SetSpectatorTeamButton.onClick.AddListener(() =>
			{
				SetLocalToSpectator();
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

		// インゲーム専用操作
		if (m_InfiniteEnergyToggle != null)
		{
			m_InfiniteEnergyToggle.onValueChanged.AddListener(isOn =>
			{
				PlayerResourceController.IsInfiniteEnergyDebugEnabled = isOn;
			});
		}

		if (m_RespawnPlayerButton != null)
		{
			m_RespawnPlayerButton.onClick.AddListener(RespawnLocalPlayer);
		}

		if (m_LoadSelectedSceneButton != null)
		{
			m_LoadSelectedSceneButton.onClick.AddListener(TransitionToSelectedScene);
		}

		// 共通操作
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
	}

	private void SetupSceneTransitionDropdown()
	{
		if (m_SceneTransitionDropdown == null) return;

		m_SceneTransitionDropdown.ClearOptions();
		var options = new List<string>();

		// 1. Build Settings に登録されている全シーンから自動取得
		int buildSceneCount = SceneManager.sceneCountInBuildSettings;
		for (int i = 0; i < buildSceneCount; i++)
		{
			string scenePath = SceneUtility.GetScenePathByBuildIndex(i);
			if (!string.IsNullOrEmpty(scenePath))
			{
				string sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);
				if (!string.IsNullOrEmpty(sceneName) && !options.Contains(sceneName))
				{
					options.Add(sceneName);
				}
			}
		}

		// 2. Build Settings から取得できなかった場合のフォールバック(Inspector指定値またはデフォルト値)
		if (options.Count == 0)
		{
			if (m_AvailableSceneNames != null && m_AvailableSceneNames.Length > 0)
			{
				options.AddRange(m_AvailableSceneNames);
			}
			else
			{
				options.Add("TitleScene");
				options.Add("LobbyScene");
				options.Add("GameScene");
				options.Add("ResultScene");
			}
		}

		m_SceneTransitionDropdown.AddOptions(options);

		// 現在のアクティブシーンに初期選択を合わせる
		string currentSceneName = SceneManager.GetActiveScene().name;
		int currentIndex = options.IndexOf(currentSceneName);
		if (currentIndex >= 0)
		{
			m_SceneTransitionDropdown.SetValueWithoutNotify(currentIndex);
		}
	}

	/// <summary>
	/// ドロップダウンで選択されているシーンへ遷移します。
	/// </summary>
	private void TransitionToSelectedScene()
	{
		if (m_SceneTransitionDropdown == null || m_SceneTransitionDropdown.options.Count == 0)
		{
			return;
		}

		string targetScene = m_SceneTransitionDropdown.options[m_SceneTransitionDropdown.value].text;
		TransitionToScene(targetScene);
	}

	/// <summary>
	/// 指定したシーンへ安全に遷移します。
	/// </summary>
	public void TransitionToScene(string sceneName)
	{
		if (string.IsNullOrEmpty(sceneName)) return;

		// ネットワーク接続中の場合
		if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
		{
			// ホストであり、かつタイトルシーン以外へ同期遷移する場合
			if (NetworkManager.Singleton.IsServer && NetworkManager.Singleton.SceneManager != null && sceneName != "TitleScene")
			{
				NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
				return;
			}

			// タイトル戻りまたはクライアント時は安全のため切断して遷移
			NetworkManager.Singleton.Shutdown();
		}

		SceneManager.LoadScene(sceneName);
	}

	/// <summary>
	/// 自端末のプレイヤーキャラクターを初期スポーン位置へリスポーンさせます。
	/// </summary>
	private void RespawnLocalPlayer()
	{
		// =========================================================================
		// 【将来の空中足場リスポーンへの拡張・移行用メモ】
		// 将来的に初期スポーン位置ではなく、「空中に動的生成した足場(空中リスポーンプラットフォーム)」へ
		// リスポーンさせるシステムへ移行する場合は、以下の処理を置き換えてください。
		// 1. 空中足場の生成または取得(例: FloatingPlatformManager.Instance.GetOrCreateRespawnPlatform(...) など)
		// 2. その足場の上面ワールド座標および向きを取得
		// 3. 下記の playerController.Respawn(targetPosition, targetRotation) にその座標と向きを渡す
		// このように実装することで、他のリスポーン判定(場外復帰等)と競合することなく安全に移行可能です。
		// =========================================================================

		PlayerController localPlayer = null;

		if (NetworkManager.Singleton != null &&
			NetworkManager.Singleton.LocalClient != null &&
			NetworkManager.Singleton.LocalClient.PlayerObject != null)
		{
			localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerController>();
		}

		if (localPlayer == null)
		{
			// PlayerObject未紐づけ時のフォールバック探索
			foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
			{
				if (pc.IsOwner)
				{
					localPlayer = pc;
					break;
				}
			}
		}

		if (localPlayer == null)
		{
			Debug.LogWarning("[StaffMenu] 自プレイヤー(PlayerController)が見つからないためリスポーンできません。");
			return;
		}

		Vector3 targetPosition = localPlayer.transform.position;
		Quaternion targetRotation = localPlayer.transform.rotation;

		// 初期スポーン位置が保存されている場合はそちらを優先
		var outOfBounds = localPlayer.GetComponent<PlayerOutOfBoundsRespawner>();
		if (outOfBounds != null && outOfBounds.RespawnPosition != Vector3.zero)
		{
			targetPosition = outOfBounds.RespawnPosition;
			targetRotation = outOfBounds.RespawnRotation;
		}

		localPlayer.Respawn(targetPosition, targetRotation);
		Debug.Log($"[StaffMenu] 自プレイヤーをリスポーンさせました: Pos={targetPosition}");
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
			else
			{
				m_InfoBuilder.Append($"\nActiveScene: {SceneManager.GetActiveScene().name}");
				if (MatchManager.Instance != null)
				{
					m_InfoBuilder.Append($" | MatchState: {MatchManager.Instance.MatchState}");
				}
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
			else if (SessionPlayerDataStore.Instance != null)
			{
				var allPlayers = SessionPlayerDataStore.Instance.GetAllPlayerData();
				m_InfoBuilder.Append($"参加者 ({allPlayers.Count}名):\n");
				foreach (var kvp in allPlayers)
				{
					var p = kvp.Value;
					m_InfoBuilder.Append($"#{p.ClientId} {p.PlayerName} [{p.Team}] ({p.CharacterType}/{p.HighlightColor})\n");
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

	/// <summary>
	/// 自端末を観戦モードに切り替えます(ロビーまたはインゲーム)。
	/// </summary>
	private void SetLocalToSpectator()
	{
		LocalPlayerProfilePreferences.SetAssignedTeam(ETeamType.Spectator);

		var lobby = FindLobbyManager();
		if (lobby != null)
		{
			lobby.JoinTeam(ETeamType.Spectator);
		}

		if (SpectatorDisplayManager.Instance != null)
		{
			SpectatorDisplayManager.Instance.InitializeSpectatorDisplay();
		}

		PlayerNamePlate.RefreshAll();
		PlayerSilhouette.RefreshAll();
	}
}

