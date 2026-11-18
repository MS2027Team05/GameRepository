using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 設定画面(視点感度・音量・描画設定等)のUI表示および開閉制御を担当するコンポーネント。
/// タイトル画面やゲーム内ポーズメニューから共通して再利用可能です。
/// </summary>
public class SettingsPanelController : MonoBehaviour
{
	private const float m_FallbackMinSensitivity = 0.1f;
	private const float m_FallbackMaxSensitivity = 3.0f;
	private const float m_FallbackSensitivityStep = 0.1f;
	private const float m_FallbackDefaultSensitivity = 1.0f;

	[Header("ボタン初期選択用参照")]
	[SerializeField] private LobbyUIController m_LobbyUIController;
	[SerializeField] private Button m_InitialSelectedButton;

	[Header("閉じるボタン参照")]
	[SerializeField] private Button m_CloseButton;

	[Header("視点感度設定UI(マウス / ゲームパッド共通)")]
	[SerializeField] private Slider m_LookSensitivitySlider;
	[SerializeField] private TextMeshProUGUI m_LookSensitivityValueText;
	[SerializeField] private float m_MinLookSensitivity;
	[SerializeField] private float m_MaxLookSensitivity;
	[SerializeField] private float m_LookSensitivityStep;
	[SerializeField] private float m_DefaultLookSensitivity;

	/// <summary>設定画面が閉じられた際に発火するイベント</summary>
	public event Action OnClosed;

	private void Awake()
	{
		if (m_CloseButton != null)
		{
			m_CloseButton.onClick.AddListener(Close);
		}

		ConfigureLookSensitivitySlider();
		SyncLookSensitivityUI();
	}

	/// <summary>
	/// 設定画面を表示します。
	/// </summary>
	public void Open()
	{
		gameObject.SetActive(true);
		SyncLookSensitivityUI();

		if (m_LookSensitivitySlider != null && EventSystem.current != null)
		{
			EventSystem.current.SetSelectedGameObject(m_LookSensitivitySlider.gameObject);
		}
	}

	/// <summary>
	/// 設定画面を閉じます。
	/// </summary>
	public void Close()
	{
		gameObject.SetActive(false);
		OnClosed?.Invoke();
		if (m_LobbyUIController != null)
		{
			m_LobbyUIController.SetInitialSelectedButton(m_InitialSelectedButton);
		}
	}

	private void ConfigureLookSensitivitySlider()
	{
		if (m_LookSensitivitySlider == null)
		{
			return;
		}

		GetSensitivityRange(out float min, out float max, out float step, out _);
		int stepCount = Mathf.Max(1, Mathf.RoundToInt((max - min) / step));

		m_LookSensitivitySlider.wholeNumbers = true;
		m_LookSensitivitySlider.minValue = 0;
		m_LookSensitivitySlider.maxValue = stepCount;
		m_LookSensitivitySlider.onValueChanged.AddListener(HandleLookSensitivitySliderChanged);
	}

	private void SyncLookSensitivityUI()
	{
		GetSensitivityRange(out float min, out float max, out float step, out float defaultVal);
		float current = Mathf.Clamp(LocalPlayerProfilePreferences.GetLookSensitivity(defaultVal), min, max);
		int stepCount = Mathf.Max(1, Mathf.RoundToInt((max - min) / step));
		int index = Mathf.Clamp(Mathf.RoundToInt((current - min) / step), 0, stepCount);
		float snapped = ConvertIndexToSensitivity(index, min, max, step);

		if (m_LookSensitivitySlider != null)
		{
			m_LookSensitivitySlider.SetValueWithoutNotify(index);
		}

		UpdateLookSensitivityValueText(snapped);
	}

	private void HandleLookSensitivitySliderChanged(float sliderValue)
	{
		GetSensitivityRange(out float min, out float max, out float step, out _);
		int stepCount = Mathf.Max(1, Mathf.RoundToInt((max - min) / step));
		int index = Mathf.Clamp(Mathf.RoundToInt(sliderValue), 0, stepCount);
		float sensitivity = ConvertIndexToSensitivity(index, min, max, step);

		LocalPlayerProfilePreferences.SetLookSensitivity(sensitivity);
		UpdateLookSensitivityValueText(sensitivity);
	}

	private void UpdateLookSensitivityValueText(float sensitivity)
	{
		if (m_LookSensitivityValueText != null)
		{
			m_LookSensitivityValueText.SetText("{0:0.0}", sensitivity);
		}
	}

	private void GetSensitivityRange(out float min, out float max, out float step, out float defaultValue)
	{
		step = m_LookSensitivityStep > 0.001f ? m_LookSensitivityStep : m_FallbackSensitivityStep;
		min = m_MinLookSensitivity > 0.001f ? m_MinLookSensitivity : m_FallbackMinSensitivity;
		max = m_MaxLookSensitivity > min ? m_MaxLookSensitivity : Mathf.Max(min + step, m_FallbackMaxSensitivity);
		defaultValue = m_DefaultLookSensitivity > 0.001f
			? Mathf.Clamp(m_DefaultLookSensitivity, min, max)
			: Mathf.Clamp(m_FallbackDefaultSensitivity, min, max);
	}

	private static float ConvertIndexToSensitivity(int index, float min, float max, float step)
	{
		float value = Mathf.Clamp(min + (index * step), min, max);
		return Mathf.Round(value * 100.0f) / 100.0f;
	}

	// --- 将来的な設定項目のプレースホルダー/拡張ポイント ---

	/// <summary>
	/// マスター音量を変更します(将来的なAudioMixer連携用)。
	/// </summary>
	public void SetMasterVolume(float volume)
	{
		// 将来実装: AudioMixerへの反映およびPlayerPrefs保存
	}

	/// <summary>
	/// BGM音量を変更します(将来的なAudioMixer連携用)。
	/// </summary>
	public void SetBgmVolume(float volume)
	{
		// 将来実装: AudioMixerへの反映およびPlayerPrefs保存
	}

	/// <summary>
	/// SE音量を変更します(将来的なAudioMixer連携用)。
	/// </summary>
	public void SetSeVolume(float volume)
	{
		// 将来実装: AudioMixerへの反映およびPlayerPrefs保存
	}

	/// <summary>
	/// フルスクリーン表示を切り替えます(将来的なScreen連携用)。
	/// </summary>
	public void SetFullscreen(bool isFullscreen)
	{
		// 将来実装: Screen.fullScreenへの反映およびPlayerPrefs保存
	}
}
