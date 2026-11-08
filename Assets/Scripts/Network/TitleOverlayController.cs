using System;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// LobbySceneの最前面に重畳され、体験者向けのタイトル演出(START/設定/QUIT)を管理するクラス。
/// STARTボタン押下やUIInputReceiver等からの入力により、自端末ローカルのみタイトル表示を解除してキャラクリ画面へ遷移させます。
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

	public event Action OnTitleDismissed;
	public event Action OnTitleShown;

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

		OnTitleDismissed?.Invoke();
	}

	/// <summary>
	/// タイトル表示をアクティブにします(キャラクリ画面から戻る時やゲーム終了後リセット等で利用)。
	/// </summary>
	public void ShowTitle()
	{
		m_IsTitleActive = true;
		if (m_OverlayRoot != null)
		{
			m_OverlayRoot.SetActive(true);
		}

		OnTitleShown?.Invoke();
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
