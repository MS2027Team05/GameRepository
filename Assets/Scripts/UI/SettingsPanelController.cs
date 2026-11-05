using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 設定画面(音量・描画設定等)のUI表示および開閉制御を担当するコンポーネント。
/// タイトル画面やゲーム内ポーズメニューから共通して再利用可能です。
/// </summary>
public class SettingsPanelController : MonoBehaviour
{
	[Header("閉じるボタン参照")]
	[SerializeField] private Button m_CloseButton;

	/// <summary>設定画面が閉じられた際に発火するイベント</summary>
	public event Action OnClosed;

	private void Awake()
	{
		if (m_CloseButton != null)
		{
			m_CloseButton.onClick.AddListener(Close);
		}
	}

	/// <summary>
	/// 設定画面を表示します。
	/// </summary>
	public void Open()
	{
		gameObject.SetActive(true);
	}

	/// <summary>
	/// 設定画面を閉じます。
	/// </summary>
	public void Close()
	{
		gameObject.SetActive(false);
		OnClosed?.Invoke();
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
