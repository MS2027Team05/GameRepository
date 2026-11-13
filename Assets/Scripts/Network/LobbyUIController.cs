using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// ロビー画面のUI表示およびパネル切り替え制御を担当するクラス。
/// キャラクリUI(名前・READY・外見ピッカー連携)、タイトル復帰切り替え、
/// ホスト操作、デバッグ操作、および3D台座・モデルオブジェクトの表示/非表示を一元管理します。
/// </summary>
public class LobbyUIController : MonoBehaviour
{
	[Header("マネージャー・コントローラー参照")]
	[SerializeField] private LobbyManager m_LobbyManager;
	[SerializeField] private TitleOverlayController m_TitleOverlay;
	[SerializeField] private LobbyAppearancePickerUI m_AppearancePickerUI;

	[Header("タイトルボタン参照")]
	[SerializeField] private Button m_TitleStartButton;
	[SerializeField] private Button m_TitleSettingsButton;
	[SerializeField] private Button m_TitleQuitButton;

	[Header("パネルルート参照")]
	[SerializeField] private GameObject m_HostControlPanel;
	[SerializeField] private GameObject m_PlayerControlPanel;
	[SerializeField] private GameObject m_DebugPanel;
	[SerializeField] private GameObject m_LoadingPanel;
	[SerializeField] private GameObject m_DisconnectNoticePanel;

	[Header("キャラクリ用3D表示・台座オブジェクト参照")]
	[Tooltip("ロビー内の3Dキャラクターモデルおよび台座(円柱)の親オブジェクト")]
	[SerializeField] private GameObject m_CharacterDisplayRoot;

	[Header("プレイヤー操作UI参照")]
	[SerializeField] private TMP_InputField m_PlayerNameInput;
	[SerializeField] private Button m_ReadyButton;
	[SerializeField] private TextMeshProUGUI m_ReadyButtonText;
	[SerializeField] private TextMeshProUGUI m_TeamBadgeText;
	[SerializeField] private Button m_BackToTitleButton;

	[Header("ホスト操作UI参照")]
	[SerializeField] private Button m_StartGameButton;
	[SerializeField] private Button m_LeaveLobbyButton;
	[SerializeField] private Toggle m_SkipTutorialToggle;

	[Header("デバッグ操作UI参照(兼任ホスト時限定)")]
	[SerializeField] private Button m_DebugCycleMinPlayersButton;
	[SerializeField] private TextMeshProUGUI m_DebugMinPlayersText;
	[SerializeField] private Button m_DebugToggleSceneButton;
	[SerializeField] private TextMeshProUGUI m_DebugTargetSceneText;
	[SerializeField] private Button m_DebugForceStartButton;

	private void Awake()
	{
		if (m_LobbyManager == null)
		{
			m_LobbyManager = FindFirstObjectByType<LobbyManager>();
		}

		if (m_TitleOverlay == null)
		{
			m_TitleOverlay = FindFirstObjectByType<TitleOverlayController>();
		}

		if (m_AppearancePickerUI == null)
		{
			m_AppearancePickerUI = GetComponentInChildren<LobbyAppearancePickerUI>(true);
		}

		if (m_AppearancePickerUI != null)
		{
			m_AppearancePickerUI.Initialize(m_LobbyManager);
		}

		SetupUIListeners();
	}

	private void Start()
	{
		SetupPanelVisibility();

		if (m_LobbyManager != null)
		{
			m_LobbyManager.OnLobbyDataChanged += RefreshAllUI;
			m_LobbyManager.OnGameSceneLoadStarted += ShowLoadingPanel;
			m_LobbyManager.OnDisconnectedFromHost += ShowDisconnectNoticePanel;
		}

		if (m_TitleOverlay != null)
		{
			m_TitleOverlay.OnTitleDismissed += HandleTitleDismissed;
			m_TitleOverlay.OnTitleShown += HandleTitleShown;
		}

		RefreshAllUI();

		// タイトル画面が表示されていなければ初期選択
		if (m_TitleOverlay == null || !m_TitleOverlay.IsTitleActive)
		{
			SetLobbyInitialSelectedButton();
		}
	}

	private void OnDestroy()
	{
		if (m_LobbyManager != null)
		{
			m_LobbyManager.OnLobbyDataChanged -= RefreshAllUI;
			m_LobbyManager.OnGameSceneLoadStarted -= ShowLoadingPanel;
			m_LobbyManager.OnDisconnectedFromHost -= ShowDisconnectNoticePanel;
		}

		if (m_TitleOverlay != null)
		{
			m_TitleOverlay.OnTitleDismissed -= HandleTitleDismissed;
			m_TitleOverlay.OnTitleShown -= HandleTitleShown;
		}
	}

	/// <summary>
	/// 端末ロール(本番観戦ホスト / クライアント / デバッグ兼任ホスト)に応じて各パネルおよび3D表示状態を決定します。
	/// </summary>
	private void SetupPanelVisibility()
	{
		bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
		bool isPlayerHost = NetworkConnectManager.IsPlayerHost;
		bool isTitleActive = m_TitleOverlay != null && m_TitleOverlay.IsTitleActive;

		// 1. ホスト操作パネル: サーバー(Host)かつタイトル画面非表示時のみ表示
		SetPanelActive(
			m_HostControlPanel,
			isServer && !isTitleActive,
			m_StartGameButton);

		// 2. ロード中パネル・切断通知パネル: 初期状態では非表示
		SetPanelActive(m_LoadingPanel, false);
		SetPanelActive(m_DisconnectNoticePanel, false);

		// 3. プレイヤー操作・キャラクリ3D・デバッグの表示制御
		if (isServer && !isPlayerHost)
		{
			// 本番観戦ホスト: プレイヤー操作UIおよびデバッグUIは非表示、3D台座・モデルはタイトル非表示時に表示
			SetPanelActive(m_PlayerControlPanel, false);
			SetDebugControlsActive(false);

			if (m_CharacterDisplayRoot != null)
			{
				m_CharacterDisplayRoot.SetActive(!isTitleActive);
			}
		}
		else
		{
			// クライアントおよび兼任ホスト: タイトル表示中はキャラクリUI、3D表示、デバッグパネルをすべて非表示
			bool showControls = !isTitleActive;
			SetPanelActive(
				m_PlayerControlPanel,
				showControls,
				m_ReadyButton);

			if (m_CharacterDisplayRoot != null)
			{
				m_CharacterDisplayRoot.SetActive(showControls);
			}

			if (!showControls && m_AppearancePickerUI != null)
			{
				m_AppearancePickerUI.ClosePicker();
			}

			// デバッグパネルは兼任ホストかつタイトル解除時のみ表示
			bool showDebugControls = isServer && isPlayerHost && !isTitleActive;
			SetDebugControlsActive(showDebugControls);
		}
	}

	private void HandleTitleDismissed()
	{
		bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
		bool isPlayerHost = NetworkConnectManager.IsPlayerHost;

		// ホスト端末ならタイトル解除時にホスト用パネルを表示
		if (isServer)
		{
			SetPanelActive(
				m_HostControlPanel,
				true,
				m_StartGameButton);
		}

		// 3D台座・モデルを表示
		if (m_CharacterDisplayRoot != null)
		{
			m_CharacterDisplayRoot.SetActive(true);
		}

		if (!isServer || isPlayerHost)
		{
			// クライアントまたは兼任ホストならキャラクリUIを表示
			SetPanelActive(
				m_PlayerControlPanel,
				true,
				m_ReadyButton);

			// 兼任ホストの場合はデバッグパネルも合わせて表示
			if (isServer && isPlayerHost)
			{
				SetDebugControlsActive(true);
			}
		}

		RefreshAllUI();

		// ロビー画面の初期選択ボタンを設定
		SetLobbyInitialSelectedButton();
	}

	private void HandleTitleShown()
	{
		// ロビー側の選択状態を解除
		if (EventSystem.current != null)
		{
			EventSystem.current.SetSelectedGameObject(null);
		}

		bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
		bool isPlayerHost = NetworkConnectManager.IsPlayerHost;

		if (m_AppearancePickerUI != null)
		{
			m_AppearancePickerUI.ClosePicker();
		}

		// タイトルが表示されている間はホスト用パネルを非表示
		SetPanelActive(m_HostControlPanel, false);

		// 3Dオブジェクトを非表示
		if (m_CharacterDisplayRoot != null)
		{
			m_CharacterDisplayRoot.SetActive(false);
		}

		if (!isServer || isPlayerHost)
		{
			// キャラクリUI、デバッグパネルを非表示
			SetPanelActive(m_PlayerControlPanel, false);

			SetDebugControlsActive(false);
		}
	}

	private void SetDebugControlsActive(bool active)
	{
		SetPanelActive(m_DebugPanel, active);

		if (m_DebugToggleSceneButton != null)
		{
			m_DebugToggleSceneButton.gameObject.SetActive(active);
		}

		if (m_DebugCycleMinPlayersButton != null)
		{
			m_DebugCycleMinPlayersButton.gameObject.SetActive(active);
		}

		// デバッグパネル表示時も、兼任ホストの初期選択はREADYボタンにする
		if (active)
		{
			SetInitialSelectedButton(m_ReadyButton);
		}
	}

	/// <summary>
	/// キャラクリ画面からタイトルオーバーレイ画面へ戻ります。
	/// </summary>
	public void ReturnToTitleOverlay()
	{
		if (m_AppearancePickerUI != null)
		{
			m_AppearancePickerUI.ClosePicker();
		}

		SetPanelActive(m_HostControlPanel, false);

		SetPanelActive(m_PlayerControlPanel, false);

		if (m_CharacterDisplayRoot != null)
		{
			m_CharacterDisplayRoot.SetActive(false);
		}

		SetDebugControlsActive(false);

		if (m_TitleOverlay != null)
		{
			m_TitleOverlay.ShowTitle();
			SetInitialSelectedButton(m_TitleStartButton);
		}
	}

	private void ShowLoadingPanel()
	{
		if (m_AppearancePickerUI != null)
		{
			m_AppearancePickerUI.ClosePicker();
		}

		SetPanelActive(m_HostControlPanel, false);

		SetPanelActive(m_PlayerControlPanel, false);

		SetDebugControlsActive(false);

		if (m_CharacterDisplayRoot != null)
		{
			m_CharacterDisplayRoot.SetActive(false);
		}

		SetPanelActive(m_LoadingPanel, true);
	}

	private void ShowDisconnectNoticePanel()
	{
		if (m_AppearancePickerUI != null)
		{
			m_AppearancePickerUI.ClosePicker();
		}

		SetPanelActive(m_HostControlPanel, false);

		SetPanelActive(m_PlayerControlPanel, false);

		SetDebugControlsActive(false);

		SetPanelActive(m_LoadingPanel, false);

		if (m_CharacterDisplayRoot != null)
		{
			m_CharacterDisplayRoot.SetActive(false);
		}

		SetPanelActive(m_DisconnectNoticePanel, true);
	}

	/// <summary>
	/// 端末の役割に応じてロビー画面の初期選択ボタンを設定します。
	/// </summary>
	private void SetLobbyInitialSelectedButton()
	{
		bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
		bool isPlayerHost = NetworkConnectManager.IsPlayerHost;

		// 本番観戦ホストの場合
		if (isServer && !isPlayerHost)
		{
			SetInitialSelectedButton(m_StartGameButton);
		}
		// クライアントおよびデバッグ兼任ホストの場合
		else
		{
			SetInitialSelectedButton(m_ReadyButton);
		}
	}

	/// <summary>
	/// 指定したSelectableを初期選択状態にします。
	/// </summary>
	public void SetInitialSelectedButton(Selectable selectable)
	{
		if (EventSystem.current == null)
		{
			return;
		}

		// 以前の選択状態を解除
		EventSystem.current.SetSelectedGameObject(null);

		// 選択可能なUIだけを選択する
		if (selectable != null &&
			selectable.gameObject.activeInHierarchy &&
			selectable.IsInteractable())
		{
			EventSystem.current.SetSelectedGameObject(selectable.gameObject);
		}
	}


	/// <summary>
	/// UIパネルの表示を切り替え、表示した場合は初期選択ボタンを設定します。
	/// 非表示にするパネル内のUIが選択されている場合は、選択状態を解除します。
	/// </summary>
	private void SetPanelActive(
		GameObject panel,
		bool active,
		Selectable initialSelectedButton = null)
	{
		if (panel == null) return;

		// 表示状態が変わらない場合は何もしない
		if (panel.activeSelf == active) return;

		// 非表示にするパネル内のUIが選択されている場合は選択解除
		if (!active && EventSystem.current != null)
		{
			GameObject selectedObject = EventSystem.current.currentSelectedGameObject;

			if (selectedObject != null &&
				(selectedObject == panel ||
				 selectedObject.transform.IsChildOf(panel.transform)))
			{
				EventSystem.current.SetSelectedGameObject(null);
			}
		}

		panel.SetActive(active);

		// パネル表示時、初期選択ボタンが指定されていれば選択
		if (active && initialSelectedButton != null)
		{
			SetInitialSelectedButton(initialSelectedButton);
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

		if (m_SkipTutorialToggle != null)
		{
			m_SkipTutorialToggle.onValueChanged.AddListener(isOn =>
			{
				if (m_LobbyManager != null)
				{
					m_LobbyManager.SkipTutorial = isOn;
				}
			});
		}

		// プレイヤー操作
		if (m_PlayerNameInput != null)
		{
			m_PlayerNameInput.characterLimit = LobbyManager.MaxPlayerNameLength;
			m_PlayerNameInput.onEndEdit.AddListener(playerName => m_LobbyManager?.SetPlayerName(playerName));
		}

		if (m_ReadyButton != null)
		{
			m_ReadyButton.onClick.AddListener(() => m_LobbyManager?.ToggleReady());
		}

		if (m_BackToTitleButton != null)
		{
			m_BackToTitleButton.onClick.AddListener(ReturnToTitleOverlay);
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
		if (m_LobbyManager == null || NetworkManager.Singleton == null)
		{
			return;
		}

		UpdatePlayerControls();
		UpdateHostControls();
		UpdateDebugControls();
	}

	private void UpdatePlayerControls()
	{
		if (!m_LobbyManager.TryGetLocalPlayerData(out var localPlayer))
		{
			return;
		}

		bool isSeated = localPlayer.Team != ETeamType.None;
		bool isReady = localPlayer.IsReady;
		bool canCustomize = isSeated && !isReady;

		if (m_PlayerNameInput != null)
		{
			m_PlayerNameInput.interactable = !isReady;
			if (!m_PlayerNameInput.isFocused && m_PlayerNameInput.text != localPlayer.PlayerName.ToString())
			{
				m_PlayerNameInput.text = localPlayer.PlayerName.ToString();
			}
		}

		if (m_AppearancePickerUI != null)
		{
			m_AppearancePickerUI.RefreshState(localPlayer, canCustomize);
		}

		if (m_ReadyButton != null)
		{
			m_ReadyButton.interactable = isSeated;
		}

		if (m_ReadyButtonText != null)
		{
			m_ReadyButtonText.text = isReady ? "準備完了解除" : "準備完了!";
		}

		if (m_TeamBadgeText != null)
		{
			string teamStr = localPlayer.Team == ETeamType.Red
				? "<color=#FF5555>RED TEAM</color>"
				: localPlayer.Team == ETeamType.Blue
					? "<color=#5588FF>BLUE TEAM</color>"
					: "未所属";
			m_TeamBadgeText.text = $"所属チーム: {teamStr}";
		}

		if (m_BackToTitleButton != null)
		{
			// 準備完了状態ではタイトルに戻れないよう制御
			m_BackToTitleButton.interactable = !isReady;
		}
	}

	private void UpdateHostControls()
	{
		bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
		if (!isServer)
		{
			return;
		}

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
