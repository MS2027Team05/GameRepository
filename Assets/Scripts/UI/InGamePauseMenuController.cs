using System.Collections;
using System.Text;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// インゲーム時のポーズメニュー(Escメニュー)の開閉・設定パネル連携・IP表示・タイトル画面への退出を制御するクラス。
/// UIInputReceiverからのイベントを購読し、キーボードおよびゲームパッドでの操作に対応します。
/// </summary>
public class InGamePauseMenuController : MonoBehaviour
{
	[Header("UI入力レシーバー参照(未設定時は同一オブジェクトから自動取得)")]
	[SerializeField] private UIInputReceiver m_UIInputReceiver;

	[Header("パネル参照")]
	// ポーズメニュー全体のルートオブジェクト
	[SerializeField] private GameObject m_PauseMenuRoot;

	// メインボタン群の親オブジェクト(設定画面を開いている間に非表示にする対象。未設定時はPauseMenuRootを維持)
	[SerializeField] private GameObject m_MainMenuContainer;

	// シーン内に配置済みの設定パネル参照(未設定時は下のPrefabから動的生成)
	[SerializeField] private SettingsPanelController m_SettingsPanel;

	// 設定パネルをPrefabから動的生成する場合の参照(Assets/Prefabs/UI/SettingPanel.prefab)
	[SerializeField] private SettingsPanelController m_SettingsPanelPrefab;

	[Header("ボタン・テキスト参照")]
	[SerializeField] private UnityEngine.UI.Button m_ResumeButton;
	[SerializeField] private UnityEngine.UI.Button m_OpenSettingsButton;
	[SerializeField] private UnityEngine.UI.Button m_LeaveGameButton;
	[SerializeField] private TextMeshProUGUI m_IpAddressText;

	[Header("切断通知設定")]
	// ホスト切断時に表示する通知パネル(未設定時は即時タイトルへ遷移)
	[SerializeField] private GameObject m_DisconnectNoticePanel;

	// 想定設定値: 3.0f (切断通知を表示してから自動でタイトル画面へ遷移するまでの秒数)
	[SerializeField] private float m_DisconnectReturnDelay;

	[Header("遷移先シーン設定")]
	// 想定設定値: "TitleScene"
	[SerializeField] private string m_TitleSceneName;

	private readonly StringBuilder m_IpStringBuilder = new StringBuilder();
	private string m_CachedHostIp;
	private bool m_IsMenuOpen = false;
	private bool m_IsSettingsOpen = false;
	private bool m_IsHandlingDisconnect = false;
	private int m_LastProcessedFrame = -1;
	private Coroutine m_DisconnectCoroutine;

	private void Awake()
	{
		if (m_UIInputReceiver == null)
		{
			m_UIInputReceiver = GetComponent<UIInputReceiver>();
		}

		InitializeSettingsPanel();
		SetupButtonListeners();
	}

	private void OnEnable()
	{
		if (m_UIInputReceiver != null)
		{
			m_UIInputReceiver.OnPausePressed += HandlePausePressed;
			m_UIInputReceiver.OnCancelPressed += HandleCancelPressed;
		}
	}

	private void OnDisable()
	{
		if (m_UIInputReceiver != null)
		{
			m_UIInputReceiver.OnPausePressed -= HandlePausePressed;
			m_UIInputReceiver.OnCancelPressed -= HandleCancelPressed;
		}
	}

	private void Start()
	{
		if (NetworkManager.Singleton != null)
		{
			NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnect;
		}

		// 初期状態ではポーズメニュー・設定パネル・切断通知パネルを非表示にし、プレイヤー入力を許可
		m_IsMenuOpen = false;
		m_IsSettingsOpen = false;
		m_IsHandlingDisconnect = false;

		if (m_PauseMenuRoot != null)
		{
			m_PauseMenuRoot.SetActive(false);
		}

		if (m_SettingsPanel != null)
		{
			m_SettingsPanel.gameObject.SetActive(false);
		}

		if (m_DisconnectNoticePanel != null)
		{
			m_DisconnectNoticePanel.SetActive(false);
		}

		PlayerInputReceiver.SetAllPaused(false);
	}

	private void OnDestroy()
	{
		if (NetworkManager.Singleton != null)
		{
			NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnect;
		}

		if (m_SettingsPanel != null)
		{
			m_SettingsPanel.OnClosed -= HandleSettingsClosed;
		}

		if (m_DisconnectCoroutine != null)
		{
			StopCoroutine(m_DisconnectCoroutine);
			m_DisconnectCoroutine = null;
		}

		// シーン破棄時にポーズロックが残らないよう解除
		PlayerInputReceiver.SetAllPaused(false);
	}

	private void InitializeSettingsPanel()
	{
		if (m_SettingsPanel == null && m_SettingsPanelPrefab != null)
		{
			Transform parentTransform = m_PauseMenuRoot != null && m_PauseMenuRoot.transform.parent != null
				? m_PauseMenuRoot.transform.parent
				: transform;

			m_SettingsPanel = Instantiate(m_SettingsPanelPrefab, parentTransform);
		}

		if (m_SettingsPanel != null)
		{
			m_SettingsPanel.OnClosed += HandleSettingsClosed;
			m_SettingsPanel.gameObject.SetActive(false);
		}
	}

	private void SetupButtonListeners()
	{
		if (m_ResumeButton != null)
		{
			m_ResumeButton.onClick.AddListener(ClosePauseMenu);
		}

		if (m_OpenSettingsButton != null)
		{
			m_OpenSettingsButton.onClick.AddListener(OpenSettingsPanel);
		}

		if (m_LeaveGameButton != null)
		{
			m_LeaveGameButton.onClick.AddListener(LeaveGame);
		}
	}

	private void HandlePausePressed()
	{
		if (m_IsHandlingDisconnect)
		{
			return;
		}

		// 同一フレーム内でPauseとCancelが重複発火した場合のガード
		if (m_LastProcessedFrame == Time.frameCount)
		{
			return;
		}
		m_LastProcessedFrame = Time.frameCount;

		if (!m_IsMenuOpen)
		{
			OpenPauseMenu();
		}
		else if (m_IsSettingsOpen)
		{
			CloseSettingsPanel();
		}
		else
		{
			ClosePauseMenu();
		}
	}

	private void HandleCancelPressed()
	{
		if (m_IsHandlingDisconnect)
		{
			return;
		}

		if (m_LastProcessedFrame == Time.frameCount)
		{
			return;
		}

		if (!m_IsMenuOpen)
		{
			return;
		}

		m_LastProcessedFrame = Time.frameCount;

		if (m_IsSettingsOpen)
		{
			CloseSettingsPanel();
		}
		else
		{
			ClosePauseMenu();
		}
	}

	/// <summary>
	/// ポーズメニューを開き、マウスカーソルを表示してプレイヤー操作を一時停止します。
	/// </summary>
	public void OpenPauseMenu()
	{
		if (m_IsHandlingDisconnect)
		{
			return;
		}

		m_IsMenuOpen = true;
		m_IsSettingsOpen = false;

		if (m_PauseMenuRoot != null)
		{
			m_PauseMenuRoot.SetActive(true);
		}

		if (m_MainMenuContainer != null)
		{
			m_MainMenuContainer.SetActive(true);
		}

		if (m_SettingsPanel != null)
		{
			m_SettingsPanel.gameObject.SetActive(false);
		}

		UpdateIpAddressDisplay();

		PlayerInputReceiver.SetAllPaused(true);
		PlayerInputReceiver.SetCursorLocked(false);

		if (m_ResumeButton != null)
		{
			SetSelectedUIObject(m_ResumeButton.gameObject);
		}
	}

	/// <summary>
	/// ポーズメニューを閉じ、マウスカーソルを再ロックしてゲーム操作へ戻ります。
	/// </summary>
	public void ClosePauseMenu()
	{
		m_IsMenuOpen = false;
		m_IsSettingsOpen = false;

		if (m_SettingsPanel != null && m_SettingsPanel.gameObject.activeSelf)
		{
			m_SettingsPanel.gameObject.SetActive(false);
		}

		if (m_PauseMenuRoot != null)
		{
			m_PauseMenuRoot.SetActive(false);
		}

		SetSelectedUIObject(null);

		PlayerInputReceiver.SetAllPaused(false);
		PlayerInputReceiver.SetCursorLocked(true);
	}

	/// <summary>
	/// 設定パネルを開きます。
	/// </summary>
	public void OpenSettingsPanel()
	{
		if (m_SettingsPanel == null)
		{
			Debug.LogWarning("[InGamePauseMenuController] SettingsPanelが設定されていません。");
			return;
		}

		m_IsSettingsOpen = true;

		if (m_MainMenuContainer != null)
		{
			m_MainMenuContainer.SetActive(false);
		}

		m_SettingsPanel.Open();

		// ゲームパッド操作用に設定パネル内の最初の操作可能UI要素を選択
		Selectable firstSelectable = m_SettingsPanel.GetComponentInChildren<Selectable>();
		if (firstSelectable != null)
		{
			SetSelectedUIObject(firstSelectable.gameObject);
		}
	}

	/// <summary>
	/// 設定パネルを閉じてポーズメニューのメイン表示へ戻ります。
	/// </summary>
	public void CloseSettingsPanel()
	{
		if (m_SettingsPanel != null && m_SettingsPanel.gameObject.activeSelf)
		{
			m_SettingsPanel.Close();
		}
		else
		{
			HandleSettingsClosed();
		}
	}

	private void HandleSettingsClosed()
	{
		if (!m_IsMenuOpen)
		{
			return;
		}

		m_IsSettingsOpen = false;

		if (m_MainMenuContainer != null)
		{
			m_MainMenuContainer.SetActive(true);
		}

		if (m_OpenSettingsButton != null)
		{
			SetSelectedUIObject(m_OpenSettingsButton.gameObject);
		}
		else if (m_ResumeButton != null)
		{
			SetSelectedUIObject(m_ResumeButton.gameObject);
		}
	}

	/// <summary>
	/// 現在のネットワーク接続を切断し、タイトル画面へ戻ります。
	/// </summary>
	public void LeaveGame()
	{
		m_IsHandlingDisconnect = true;

		PlayerInputReceiver.SetAllPaused(false);
		PlayerInputReceiver.SetCursorLocked(false);

		if (NetworkManager.Singleton != null)
		{
			if (NetworkManager.Singleton.IsServer && SessionPlayerDataStore.Instance != null)
			{
				SessionPlayerDataStore.Instance.ClearAll();
			}

			NetworkManager.Singleton.Shutdown();
		}

		ReturnToTitleScene();
	}

	private void HandleClientDisconnect(ulong clientId)
	{
		if (m_IsHandlingDisconnect)
		{
			return;
		}

		// クライアント端末でホスト(サーバー)との接続が切断された場合、通知パネルを表示して一定時間後にタイトル画面へ戻る
		if (NetworkManager.Singleton != null &&
			!NetworkManager.Singleton.IsServer &&
			(clientId == NetworkManager.Singleton.LocalClientId || clientId == NetworkManager.ServerClientId))
		{
			m_IsHandlingDisconnect = true;
			m_IsMenuOpen = false;
			m_IsSettingsOpen = false;

			if (m_UIInputReceiver != null)
			{
				m_UIInputReceiver.SetInputAllowed(false);
			}

			if (m_SettingsPanel != null && m_SettingsPanel.gameObject.activeSelf)
			{
				m_SettingsPanel.gameObject.SetActive(false);
			}

			if (m_PauseMenuRoot != null)
			{
				m_PauseMenuRoot.SetActive(false);
			}

			SetSelectedUIObject(null);
			PlayerInputReceiver.SetAllPaused(false);
			PlayerInputReceiver.SetCursorLocked(false);

			if (m_DisconnectNoticePanel != null)
			{
				m_DisconnectNoticePanel.SetActive(true);
				m_DisconnectCoroutine = StartCoroutine(DisconnectReturnRoutine());
			}
			else
			{
				ReturnToTitleScene();
			}
		}
	}

	private IEnumerator DisconnectReturnRoutine()
	{
		float delay = m_DisconnectReturnDelay > 0f ? m_DisconnectReturnDelay : 3.0f;
		yield return new WaitForSecondsRealtime(delay);

		if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
		{
			NetworkManager.Singleton.Shutdown();
		}

		ReturnToTitleScene();
	}

	private void ReturnToTitleScene()
	{
		string targetScene = !string.IsNullOrEmpty(m_TitleSceneName) ? m_TitleSceneName : "TitleScene";
		SceneManager.LoadScene(targetScene);
	}

	private void UpdateIpAddressDisplay()
	{
		if (m_IpAddressText == null)
		{
			return;
		}

		m_IpStringBuilder.Clear();

		if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
		{
			if (NetworkManager.Singleton.IsServer)
			{
				if (string.IsNullOrEmpty(m_CachedHostIp))
				{
					m_CachedHostIp = LobbyManager.GetLocalIPv4Address();
				}

				m_IpStringBuilder.Append("ホストIP: ").Append(m_CachedHostIp);
			}
			else
			{
				UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
				string connectedIp = transport != null && !string.IsNullOrEmpty(transport.ConnectionData.Address)
					? transport.ConnectionData.Address
					: "127.0.0.1";

				m_IpStringBuilder.Append("接続先IP: ").Append(connectedIp);
			}
		}
		else
		{
			m_IpStringBuilder.Append("オフライン (127.0.0.1)");
		}

		m_IpAddressText.text = m_IpStringBuilder.ToString();
	}

	private void SetSelectedUIObject(GameObject target)
	{
		if (EventSystem.current != null)
		{
			EventSystem.current.SetSelectedGameObject(target);
		}
	}
}
