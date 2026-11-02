using System;
using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// ロビー画面のUI表示およびパネル切り替え制御を担当するクラス。
/// キャラクリUI(モデル・カラー・名前・READY)、タイトル復帰切り替え、
/// ホスト操作、デバッグ操作、および3D台座・モデルオブジェクトの表示/非表示を一元管理します。
/// </summary>
public class LobbyUIController : MonoBehaviour
{
	[Header("マネージャー・コントローラー参照")]
	[SerializeField] private LobbyManager m_LobbyManager;
	[SerializeField] private TitleOverlayController m_TitleOverlay;

	[Header("パネルルート参照")]
	[SerializeField] private GameObject m_HostControlPanel;
	[SerializeField] private GameObject m_PlayerControlPanel;
	[SerializeField] private GameObject m_DebugPanel;
	[SerializeField] private GameObject m_LoadingPanel;
	[SerializeField] private GameObject m_DisconnectNoticePanel;

	[Header("キャラクリ用3D表示・台座オブジェクト参照")]
	[Tooltip("ロビー内の3Dキャラクターモデルおよび台座(円柱)の親オブジェクト")]
	[SerializeField] private GameObject m_CharacterDisplayRoot;

	[Header("プレイヤー操作UI参照(キャラクリ特化)")]
	[SerializeField] private TMP_InputField m_PlayerNameInput;
	[SerializeField] private Button m_ToggleCharacterButton;
	[SerializeField] private TextMeshProUGUI m_CharacterText;
	[SerializeField] private Button m_CycleColorButton;
	[SerializeField] private TextMeshProUGUI m_ColorText;
	[SerializeField] private Button m_ReadyButton;
	[SerializeField] private TextMeshProUGUI m_ReadyButtonText;
	[SerializeField] private TextMeshProUGUI m_TeamBadgeText;
	[SerializeField] private Button m_BackToTitleButton;

	[Serializable]
	public struct ColorPaletteItem
	{
		[Tooltip("対象のハイライトカラー種別")]
		[FormerlySerializedAs("ColorType")]
		public EHighlightColor m_ColorType;
		[Tooltip("クリック可能なボタン")]
		[FormerlySerializedAs("Button")]
		public Button m_Button;
		[Tooltip("カラープレビュー用のImage(任意)")]
		[FormerlySerializedAs("ColorImage")]
		public Image m_ColorImage;
		[Tooltip("現在選択中を示す枠線やアイコン(任意)")]
		[FormerlySerializedAs("SelectedIndicator")]
		public GameObject m_SelectedIndicator;
	}

	[Header("サブカラーパレットUI参照")]
	[SerializeField] private ColorPaletteItem[] m_ColorPaletteItems;

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

	private readonly StringBuilder m_InfoStringBuilder = new StringBuilder();

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

		// ① ホスト操作パネル: サーバー(Host)かつタイトル画面非表示時のみ表示
		if (m_HostControlPanel != null)
		{
			m_HostControlPanel.SetActive(isServer && !isTitleActive);
		}

		// ② ロード中パネル・切断通知パネル: 初期状態では非表示
		if (m_LoadingPanel != null) m_LoadingPanel.SetActive(false);
		if (m_DisconnectNoticePanel != null) m_DisconnectNoticePanel.SetActive(false);

		// ③ プレイヤー操作・キャラクリ3D・デバッグの表示制御
		if (isServer && !isPlayerHost)
		{
			// 本番観戦ホスト: プレイヤー操作UIおよびデバッグUIは非表示、3D台座・モデルはタイトル非表示時に表示
			if (m_PlayerControlPanel != null) m_PlayerControlPanel.SetActive(false);
			if (m_DebugPanel != null) m_DebugPanel.SetActive(false);
			if (m_CharacterDisplayRoot != null) m_CharacterDisplayRoot.SetActive(!isTitleActive);
		}
		else
		{
			// クライアントおよび兼任ホスト: タイトル表示中はキャラクリUI、3D表示、デバッグパネルをすべて非表示
			bool showControls = !isTitleActive;
			if (m_PlayerControlPanel != null) m_PlayerControlPanel.SetActive(showControls);
			if (m_CharacterDisplayRoot != null) m_CharacterDisplayRoot.SetActive(showControls);

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
		if (isServer && m_HostControlPanel != null)
		{
			m_HostControlPanel.SetActive(true);
		}

		// 3D台座・モデルを表示
		if (m_CharacterDisplayRoot != null)
		{
			m_CharacterDisplayRoot.SetActive(true);
		}

		if (!isServer || isPlayerHost)
		{
			// クライアントまたは兼任ホストならキャラクリUIを表示
			if (m_PlayerControlPanel != null) m_PlayerControlPanel.SetActive(true);

			// 兼任ホストの場合はデバッグパネルも合わせて表示
			if (isServer && isPlayerHost)
			{
				SetDebugControlsActive(true);
			}
		}
	}

	private void HandleTitleShown()
	{
		bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
		bool isPlayerHost = NetworkConnectManager.IsPlayerHost;

		// タイトルが表示されている間はホスト用パネルを非表示
		if (m_HostControlPanel != null)
		{
			m_HostControlPanel.SetActive(false);
		}

		// 3Dオブジェクトを非表示
		if (m_CharacterDisplayRoot != null)
		{
			m_CharacterDisplayRoot.SetActive(false);
		}

		if (!isServer || isPlayerHost)
		{
			// キャラクリUI、デバッグパネルを非表示
			if (m_PlayerControlPanel != null) m_PlayerControlPanel.SetActive(false);
			SetDebugControlsActive(false);
		}
	}

	private void SetDebugControlsActive(bool active)
	{
		if (m_DebugPanel != null) m_DebugPanel.SetActive(active);
		if (m_DebugToggleSceneButton != null) m_DebugToggleSceneButton.gameObject.SetActive(active);
		if (m_DebugCycleMinPlayersButton != null) m_DebugCycleMinPlayersButton.gameObject.SetActive(active);
	}

	/// <summary>
	/// キャラクリ画面からタイトルオーバーレイ画面へ戻ります。
	/// </summary>
	public void ReturnToTitleOverlay()
	{
		if (m_HostControlPanel != null)
		{
			m_HostControlPanel.SetActive(false);
		}
		if (m_PlayerControlPanel != null)
		{
			m_PlayerControlPanel.SetActive(false);
		}
		if (m_CharacterDisplayRoot != null)
		{
			m_CharacterDisplayRoot.SetActive(false);
		}
		SetDebugControlsActive(false);

		if (m_TitleOverlay != null)
		{
			m_TitleOverlay.ShowTitle();
		}
	}

	private void ShowLoadingPanel()
	{
		if (m_HostControlPanel != null) m_HostControlPanel.SetActive(false);
		if (m_PlayerControlPanel != null) m_PlayerControlPanel.SetActive(false);
		if (m_DebugPanel != null) m_DebugPanel.SetActive(false);
		if (m_CharacterDisplayRoot != null) m_CharacterDisplayRoot.SetActive(false);

		if (m_LoadingPanel != null)
		{
			m_LoadingPanel.SetActive(true);
		}
	}

	private void ShowDisconnectNoticePanel()
	{
		if (m_HostControlPanel != null) m_HostControlPanel.SetActive(false);
		if (m_PlayerControlPanel != null) m_PlayerControlPanel.SetActive(false);
		if (m_DebugPanel != null) m_DebugPanel.SetActive(false);
		if (m_LoadingPanel != null) m_LoadingPanel.SetActive(false);
		if (m_CharacterDisplayRoot != null) m_CharacterDisplayRoot.SetActive(false);

		if (m_DisconnectNoticePanel != null)
		{
			m_DisconnectNoticePanel.SetActive(true);
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

		// プレイヤー操作(キャラクリ特化)
		if (m_PlayerNameInput != null)
		{
			m_PlayerNameInput.characterLimit = 12;
			m_PlayerNameInput.onEndEdit.AddListener(name => m_LobbyManager?.SetPlayerName(name));
		}

		if (m_ToggleCharacterButton != null)
		{
			m_ToggleCharacterButton.onClick.AddListener(() => m_LobbyManager?.ToggleCharacter());
		}

		if (m_CycleColorButton != null)
		{
			m_CycleColorButton.onClick.AddListener(() => m_LobbyManager?.CycleHighlightColor());
		}

		if (m_ColorPaletteItems != null)
		{
			for (int i = 0; i < m_ColorPaletteItems.Length; i++)
			{
				var item = m_ColorPaletteItems[i];
				if (item.m_Button != null)
				{
					item.m_Button.onClick.AddListener(() => m_LobbyManager?.SetHighlightColor(item.m_ColorType));
				}
			}
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
		if (m_LobbyManager == null || NetworkManager.Singleton == null) return;

		UpdatePlayerControls();
		UpdateHostControls();
		UpdateDebugControls();
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

		if (m_ColorPaletteItems != null)
		{
			for (int i = 0; i < m_ColorPaletteItems.Length; i++)
			{
				var item = m_ColorPaletteItems[i];
				if (item.m_Button != null)
				{
					item.m_Button.interactable = !isReady && isSeated;
				}
				if (item.m_SelectedIndicator != null)
				{
					item.m_SelectedIndicator.SetActive(isSeated && item.m_ColorType == localPlayer.HighlightColor);
				}
			}
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
