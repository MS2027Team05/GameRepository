using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// ゲームパッド選択(Select)およびマウスホバー(PointerEnter)を検知し、
/// ボタン外周に発光感のある黄色枠線を表示する汎用UIフォーカスコンポーネント。
/// 任意のSelectable(Button、Toggle等)にアタッチして利用できます。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Selectable))]
public sealed class UIFocusGlowOutline : MonoBehaviour,
	ISelectHandler,
	IDeselectHandler,
	IPointerEnterHandler,
	IPointerExitHandler
{
	[Header("発光枠線カラー設定")]
	[Tooltip("フォーカス時の基本枠線カラー(鮮やかな黄色)")]
	[SerializeField] private Color m_GlowColor = new Color(1.0f, 0.90f, 0.05f, 1.0f);

	[Tooltip("非フォーカス時の枠線カラー(通常は非表示のため透明)")]
	[SerializeField] private Color m_IdleColor = Color.clear;

	[Header("枠線サイズ・演出設定")]
	[Tooltip("枠線の太さ(ピクセル単位)")]
	[SerializeField] private Vector2 m_OutlineDistance = new Vector2(3.5f, -3.5f);

	[Tooltip("フォーカス中に微小な呼吸パルス(明滅・発光感)を付与するか")]
	[SerializeField] private bool m_EnablePulse = true;

	[Tooltip("パルスの速さ")]
	[SerializeField] private float m_PulseSpeed = 5.0f;

	[Tooltip("パルスによるアルファ・明度の揺らぎ幅(0〜0.4)")]
	[Range(0f, 0.4f)]
	[SerializeField] private float m_PulseIntensity = 0.18f;

	private Outline m_OutlineComponent;
	private Selectable m_Selectable;
	private bool m_IsSelected;
	private bool m_IsPointerHovered;

	/// <summary>現在フォーカス(Pad選択またはマウスホバー)中であるか</summary>
	public bool IsFocused => m_IsSelected || m_IsPointerHovered;

	public Color GlowColor
	{
		get => m_GlowColor;
		set
		{
			m_GlowColor = value;
			UpdateVisualState();
		}
	}

	public Vector2 OutlineDistance
	{
		get => m_OutlineDistance;
		set
		{
			m_OutlineDistance = value;
			if (m_OutlineComponent != null)
			{
				m_OutlineComponent.effectDistance = value;
			}
		}
	}

	private void Awake()
	{
		m_Selectable = GetComponent<Selectable>();
		EnsureOutlineComponent();
	}

	private void OnEnable()
	{
		EnsureOutlineComponent();
		UpdateVisualState();
	}

	private void OnDisable()
	{
		m_IsSelected = false;
		m_IsPointerHovered = false;
		UpdateVisualState();
	}

	private void Update()
	{
		if (!m_EnablePulse || !IsFocused || m_OutlineComponent == null || !m_OutlineComponent.enabled)
		{
			return;
		}

		// unscaledTimeを用いてポーズ中でも一定周期で発光明滅
		float wave = (Mathf.Sin(Time.unscaledTime * m_PulseSpeed) + 1.0f) * 0.5f;
		float alphaFactor = 1.0f - (wave * m_PulseIntensity);

		Color activeColor = m_GlowColor;
		activeColor.a *= alphaFactor;
		m_OutlineComponent.effectColor = activeColor;
	}

	public void OnSelect(BaseEventData eventData)
	{
		m_IsSelected = true;
		UpdateVisualState();
	}

	public void OnDeselect(BaseEventData eventData)
	{
		m_IsSelected = false;
		UpdateVisualState();
	}

	public void OnPointerEnter(PointerEventData eventData)
	{
		m_IsPointerHovered = true;
		UpdateVisualState();
	}

	public void OnPointerExit(PointerEventData eventData)
	{
		m_IsPointerHovered = false;
		UpdateVisualState();
	}

	/// <summary>
	/// 外部から明示的にフォーカス状態を強制設定します(初期選択時など)。
	/// </summary>
	public void SetFocusState(bool focused)
	{
		m_IsSelected = focused;
		UpdateVisualState();
	}

	private void EnsureOutlineComponent()
	{
		if (m_OutlineComponent == null)
		{
			m_OutlineComponent = GetComponent<Outline>();
			if (m_OutlineComponent == null)
			{
				m_OutlineComponent = gameObject.AddComponent<Outline>();
			}
		}

		if (m_OutlineComponent != null)
		{
			m_OutlineComponent.effectDistance = m_OutlineDistance;
			m_OutlineComponent.useGraphicAlpha = true;
		}
	}

	private void UpdateVisualState()
	{
		if (m_OutlineComponent == null)
		{
			return;
		}

		bool show = IsFocused && (m_Selectable == null || m_Selectable.interactable);
		m_OutlineComponent.enabled = show;
		m_OutlineComponent.effectColor = show ? m_GlowColor : m_IdleColor;
		m_OutlineComponent.effectDistance = m_OutlineDistance;
	}
}

