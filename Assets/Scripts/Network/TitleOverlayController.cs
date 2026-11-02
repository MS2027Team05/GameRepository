using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// LobbySceneの最前面に重畳され、体験者向けのタイトル演出(START/設定/QUIT)を管理するクラス。
/// STARTボタン押下やコントローラー入力により、自端末ローカルのみタイトル表示を解除してキャラクリ画面へ遷移させます。
/// </summary>
public class TitleOverlayController : MonoBehaviour
{
	[Header("パネルルート参照")]
	[SerializeField] private GameObject m_OverlayRoot;
	[SerializeField] private SettingsPanelController m_SettingsPanel;

	[Header("ボタン・テキストUI参照")]
	[SerializeField] private Button m_StartButton;
	[SerializeField] private Button m_SettingsButton;
	[SerializeField] private Button m_QuitButton;
	[SerializeField] private TextMeshProUGUI m_PressPromptText;

	private bool m_IsTitleActive;

	public bool IsTitleActive { get => m_IsTitleActive; }

	private void Awake()
	{
		SetupButtonListeners();
	}

	private void Start()
	{
		ShowTitle();

		if (m_SettingsPanel != null)
		{
			m_SettingsPanel.Close();
			m_SettingsPanel.OnClosed += HandleSettingsClosed;
		}
	}

	private void OnDestroy()
	{
		if (m_SettingsPanel != null)
		{
			m_SettingsPanel.OnClosed -= HandleSettingsClosed;
		}
	}

	private void Update()
	{
		HandleDismissInput();
	}

	private void SetupButtonListeners()
	{
		if (m_StartButton != null)
		{
			m_StartButton.onClick.AddListener(DismissTitle);
		}

		if (m_SettingsButton != null)
		{
			m_SettingsButton.onClick.AddListener(OpenSettings);
		}

		if (m_QuitButton != null)
		{
			m_QuitButton.onClick.AddListener(QuitGame);
		}
	}

	private void HandleDismissInput()
	{
		if (!m_IsTitleActive) return;

		// 設定パネルが開いている場合はタイトル解除入力を無視
		if (m_SettingsPanel != null && m_SettingsPanel.gameObject.activeSelf)
		{
			return;
		}

		// 観戦専用ホスト端末の場合は体験者ボタンでタイトル画面を解除しない
		bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
		if (isServer && !NetworkConnectManager.IsPlayerHost)
		{
			return;
		}

		bool shouldDismiss = false;

		// // ゲームパッド入力検知
		// if (UnityEngine.InputSystem.Gamepad.current != null)
		// {
		// 	var gamepad = UnityEngine.InputSystem.Gamepad.current;
		// 	if (gamepad.buttonSouth.wasPressedThisFrame ||
		// 		gamepad.startButton.wasPressedThisFrame ||
		// 		gamepad.buttonEast.wasPressedThisFrame)
		// 	{
		// 		shouldDismiss = true;
		// 	}
		// }

		// // キーボード入力検知
		// if (UnityEngine.InputSystem.Keyboard.current != null)
		// {
		// 	var keyboard = UnityEngine.InputSystem.Keyboard.current;
		// 	if (keyboard.spaceKey.wasPressedThisFrame ||
		// 		keyboard.enterKey.wasPressedThisFrame ||
		// 		keyboard.numpadEnterKey.wasPressedThisFrame)
		// 	{
		// 		shouldDismiss = true;
		// 	}
		// }

		if (shouldDismiss)
		{
			DismissTitle();
		}
	}

	/// <summary>
	/// タイトル表示を解除し、キャラクリ画面へ移行します。
	/// </summary>
	public void DismissTitle()
	{
		m_IsTitleActive = false;
		if (m_OverlayRoot != null)
		{
			m_OverlayRoot.SetActive(false);
		}
	}

	/// <summary>
	/// タイトル表示をアクティブにします(ゲーム終了後リセット等で利用)。
	/// </summary>
	public void ShowTitle()
	{
		m_IsTitleActive = true;
		if (m_OverlayRoot != null)
		{
			m_OverlayRoot.SetActive(true);
		}
	}

	/// <summary>
	/// 設定パネルを開きます。
	/// </summary>
	public void OpenSettings()
	{
		if (m_SettingsPanel != null)
		{
			m_SettingsPanel.Open();
		}
	}

	private void HandleSettingsClosed()
	{
		// 設定パネルが閉じられた後は何もしない(タイトルアクティブ状態を維持)
	}

	/// <summary>
	/// ゲームを終了します。
	/// </summary>
	public void QuitGame()
	{
#if UNITY_EDITOR
		UnityEditor.EditorApplication.isPlaying = false;
#else
		Application.Quit();
#endif
	}
}

