using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// ロビー画面におけるキャラクターモデル選択(男/女)およびカラー選択(12色)の共通ピッカーウィンドウ制御クラス。
/// ChangeModelボタンとChangeColorボタンの押下に応じ、同一ウィンドウ内で表示内容を切り替えます。
/// </summary>
public sealed class LobbyAppearancePickerUI : MonoBehaviour
{
	private enum EPickerMode : byte
	{
		None = 0,
		Model = 1,
		Color = 2,
	}

	[Serializable]
	public struct ModelOptionItem
	{
		[Tooltip("対象のキャラクターモデル種別(Man / Woman)")]
		[SerializeField] private ECharacterType m_CharacterType;

		[Tooltip("選択用ボタン")]
		[SerializeField] private Button m_Button;

		[Tooltip("現在選択中であることを示す枠線やチェックマーク(任意)")]
		[SerializeField] private GameObject m_SelectedIndicator;

		public ECharacterType CharacterType { get => m_CharacterType; }
		public Button OptionButton { get => m_Button; }
		public GameObject SelectedIndicator { get => m_SelectedIndicator; }
	}

	[Serializable]
	public struct ColorOptionItem
	{
		[Tooltip("対象のカラー種別(12色)")]
		[SerializeField] private EHighlightColor m_ColorType;

		[Tooltip("選択用ボタン")]
		[SerializeField] private Button m_Button;

		[Tooltip("カタログのプレビュー色を自動反映するImage(任意)")]
		[SerializeField] private Image m_ColorPreviewImage;

		[Tooltip("現在選択中であることを示す枠線やチェックマーク(任意)")]
		[SerializeField] private GameObject m_SelectedIndicator;

		public EHighlightColor ColorType { get => m_ColorType; }
		public Button OptionButton { get => m_Button; }
		public Image ColorPreviewImage { get => m_ColorPreviewImage; }
		public GameObject SelectedIndicator { get => m_SelectedIndicator; }
	}

	[Header("参照")]
	[SerializeField] private LobbyManager m_LobbyManager;
	[SerializeField] private PlayerAppearanceCatalog m_AppearanceCatalog;
	[SerializeField] private LobbyCharacterDisplayManager m_CharacterDisplayManager;

	[Header("開閉トリガーボタン")]
	[Tooltip("モデル変更ウィンドウを開閉するボタン(ChangeModel)")]
	[SerializeField] private Button m_OpenModelPickerButton;
	[Tooltip("カラー変更ウィンドウを開閉するボタン(ChangeColor)")]
	[SerializeField] private Button m_OpenColorPickerButton;
	[Tooltip("ウィンドウを閉じるボタン(任意)")]
	[SerializeField] private Button m_ClosePickerButton;

	[Header("共通ピッカーウィンドウ構成")]
	[Tooltip("共通ウィンドウのルートオブジェクト(Picker)")]
	[SerializeField] private GameObject m_PickerWindowRoot;
	[Tooltip("モデル選択用の2択ボタン群を格納する親オブジェクト")]
	[SerializeField] private GameObject m_ModelSelectionPanel;
	[Tooltip("カラー選択用の12色ボタン群を格納する親オブジェクト")]
	[SerializeField] private GameObject m_ColorSelectionPanel;
	[Tooltip("有効時、項目を選択した直後に自動でピッカーウィンドウを閉じます")]
	[SerializeField] private bool m_CloseOnSelect;

	[Header("ローカルプレイヤー追従設定")]
	[Tooltip("自身の足元に追従移動させるUIルート(ChangeModel, ChangeColor, Pickerをまとめた親RectTransform)")]
	[SerializeField] private RectTransform m_FollowTargetRoot;
	[Tooltip("Canvas座標系での追従オフセット(X: 左右オフセット, Y: 上下オフセット)")]
	[SerializeField] private Vector2 m_FollowCanvasOffset;
	[Tooltip("Canvas枠外への見切れを防止するための左右余白パディング")]
	[SerializeField] private float m_CanvasMarginX = 20f;

	[Header("選択肢リスト")]
	[SerializeField] private ModelOptionItem[] m_ModelOptions;
	[SerializeField] private ColorOptionItem[] m_ColorOptions;

	private EPickerMode m_CurrentMode;
	private bool m_CanCustomize;
	private bool m_IsInitialized;
	private RectTransform m_CachedCanvasRect;
	private Camera m_CachedUiCamera;

	/// <summary>
	/// 初期化処理を行います。LobbyManager参照の注入とイベント登録を実行します。
	/// </summary>
	public void Initialize(LobbyManager lobbyManager)
	{
		if (lobbyManager != null)
		{
			m_LobbyManager = lobbyManager;
		}
		else if (m_LobbyManager == null)
		{
			m_LobbyManager = FindFirstObjectByType<LobbyManager>();
		}

		if (m_CharacterDisplayManager == null)
		{
			m_CharacterDisplayManager = FindFirstObjectByType<LobbyCharacterDisplayManager>();
		}

		ResolveCanvasReferences();

		if (m_IsInitialized)
		{
			return;
		}

		m_IsInitialized = true;
		SetupListeners();
		ApplyCatalogPreviewColors();
		SetPickerMode(EPickerMode.None);
	}

	private void Awake()
	{
		Initialize(m_LobbyManager);
	}

	/// <summary>
	/// ローカルプレイヤーの最新状態に合わせてボタンの有効状態および選択中インジケーターを更新します。
	/// </summary>
	public void RefreshState(LobbyPlayerData localPlayer, bool canCustomize)
	{
		m_CanCustomize = canCustomize;

		if (m_OpenModelPickerButton != null)
		{
			m_OpenModelPickerButton.interactable = canCustomize;
		}

		if (m_OpenColorPickerButton != null)
		{
			m_OpenColorPickerButton.interactable = canCustomize;
		}

		// Ready状態や未着席状態に移行した場合は開いているピッカーを自動で閉じる
		if (!canCustomize && m_CurrentMode != EPickerMode.None)
		{
			ClosePicker();
		}

		UpdateModelOptionsState(localPlayer.CharacterType, canCustomize);
		UpdateColorOptionsState(localPlayer.HighlightColor, canCustomize);
		UpdateFollowUIPosition(localPlayer);
	}

	/// <summary>
	/// ローカルプレイヤーが立っている台座のワールド座標からScreen座標・Canvasローカル座標へ変換し、UIのX座標を追従配置します。
	/// </summary>
	public void UpdateFollowUIPosition(LobbyPlayerData localPlayer)
	{
		if (m_FollowTargetRoot == null || localPlayer.Team == ETeamType.None)
		{
			return;
		}

		if (m_CharacterDisplayManager == null)
		{
			m_CharacterDisplayManager = FindFirstObjectByType<LobbyCharacterDisplayManager>();
			if (m_CharacterDisplayManager == null)
			{
				return;
			}
		}

		if (!m_CharacterDisplayManager.TryGetPlayerSlotWorldPosition(localPlayer.ClientId, out Vector3 worldPos))
		{
			return;
		}

		Camera mainCam = Camera.main;
		if (mainCam == null)
		{
			return;
		}

		ResolveCanvasReferences();
		if (m_CachedCanvasRect == null)
		{
			return;
		}

		Vector3 screenPoint = mainCam.WorldToScreenPoint(worldPos);
		if (screenPoint.z <= 0.001f)
		{
			return;
		}

		if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
			m_CachedCanvasRect,
			new Vector2(screenPoint.x, screenPoint.y),
			m_CachedUiCamera,
			out Vector2 canvasLocalPoint))
		{
			// 親RectTransform(m_FollowTargetRoot.parent)のローカル座標に変換するか、
			// m_FollowTargetRootの親がCanvasルートであればcanvasLocalPointをそのまま利用
			RectTransform parentRect = m_FollowTargetRoot.parent as RectTransform;
			Vector2 targetPos;

			if (parentRect != null && parentRect != m_CachedCanvasRect)
			{
				Vector3 worldCanvasPoint = m_CachedCanvasRect.TransformPoint(canvasLocalPoint);
				targetPos = parentRect.InverseTransformPoint(worldCanvasPoint);
			}
			else
			{
				targetPos = canvasLocalPoint;
			}

			// X座標を台座に合わせ、Y座標はオフセットまたは元の基準高さを維持
			float newX = targetPos.x + m_FollowCanvasOffset.x;
			float newY = m_FollowCanvasOffset.y != 0f ? targetPos.y + m_FollowCanvasOffset.y : m_FollowTargetRoot.anchoredPosition.y;

			// 画面左右端への見切れ防止クランプ
			float halfCanvasWidth = m_CachedCanvasRect.rect.width * 0.5f;
			float halfUIWidth = m_FollowTargetRoot.rect.width * 0.5f;
			float minX = -halfCanvasWidth + halfUIWidth + m_CanvasMarginX;
			float maxX = halfCanvasWidth - halfUIWidth - m_CanvasMarginX;

			if (minX <= maxX)
			{
				newX = Mathf.Clamp(newX, minX, maxX);
			}

			m_FollowTargetRoot.anchoredPosition = new Vector2(newX, newY);
		}
	}

	private void ResolveCanvasReferences()
	{
		if (m_CachedCanvasRect == null)
		{
			Canvas canvas = GetComponentInParent<Canvas>();
			if (canvas != null)
			{
				m_CachedCanvasRect = canvas.transform as RectTransform;
				m_CachedUiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
			}
		}
	}

	/// <summary>
	/// 共通ピッカーウィンドウを閉じます。
	/// </summary>
	public void ClosePicker()
	{
		EPickerMode previousMode = m_CurrentMode;
		SetPickerMode(EPickerMode.None);
		RestoreFocusAfterClose(previousMode);
	}

	private void SetupListeners()
	{
		if (m_OpenModelPickerButton != null)
		{
			m_OpenModelPickerButton.onClick.AddListener(HandleOpenModelPickerClicked);
		}

		if (m_OpenColorPickerButton != null)
		{
			m_OpenColorPickerButton.onClick.AddListener(HandleOpenColorPickerClicked);
		}

		if (m_ClosePickerButton != null)
		{
			m_ClosePickerButton.onClick.AddListener(ClosePicker);
		}

		if (m_ModelOptions != null)
		{
			for (int i = 0; i < m_ModelOptions.Length; i++)
			{
				ECharacterType targetType = m_ModelOptions[i].CharacterType;
				Button button = m_ModelOptions[i].OptionButton;
				if (button != null)
				{
					button.onClick.AddListener(() => HandleModelSelected(targetType));
				}
			}
		}

		if (m_ColorOptions != null)
		{
			for (int i = 0; i < m_ColorOptions.Length; i++)
			{
				EHighlightColor targetColor = m_ColorOptions[i].ColorType;
				Button button = m_ColorOptions[i].OptionButton;
				if (button != null)
				{
					button.onClick.AddListener(() => HandleColorSelected(targetColor));
				}
			}
		}
	}

	private void ApplyCatalogPreviewColors()
	{
		if (m_AppearanceCatalog == null || m_ColorOptions == null)
		{
			return;
		}

		for (int i = 0; i < m_ColorOptions.Length; i++)
		{
			Image previewImage = m_ColorOptions[i].ColorPreviewImage;
			if (previewImage == null)
			{
				continue;
			}

			if (m_AppearanceCatalog.TryGetPreviewColor(m_ColorOptions[i].ColorType, out Color previewColor) &&
				previewColor.a > 0.001f)
			{
				previewImage.color = previewColor;
			}
		}
	}

	private void HandleOpenModelPickerClicked()
	{
		if (!m_CanCustomize)
		{
			return;
		}

		if (m_CurrentMode == EPickerMode.Model)
		{
			ClosePicker();
			return;
		}

		SetPickerMode(EPickerMode.Model);
	}

	private void HandleOpenColorPickerClicked()
	{
		if (!m_CanCustomize)
		{
			return;
		}

		if (m_CurrentMode == EPickerMode.Color)
		{
			ClosePicker();
			return;
		}

		SetPickerMode(EPickerMode.Color);
	}

	private void HandleModelSelected(ECharacterType characterType)
	{
		if (!m_CanCustomize || m_LobbyManager == null)
		{
			return;
		}

		m_LobbyManager.SetCharacterType(characterType);

		if (m_CloseOnSelect)
		{
			ClosePicker();
		}
	}

	private void HandleColorSelected(EHighlightColor colorType)
	{
		if (!m_CanCustomize || m_LobbyManager == null)
		{
			return;
		}

		m_LobbyManager.SetHighlightColor(colorType);

		if (m_CloseOnSelect)
		{
			ClosePicker();
		}
	}

	private void SetPickerMode(EPickerMode mode)
	{
		m_CurrentMode = mode;

		bool showWindow = mode != EPickerMode.None;
		if (m_PickerWindowRoot != null)
		{
			m_PickerWindowRoot.SetActive(showWindow);
		}

		if (m_ModelSelectionPanel != null)
		{
			m_ModelSelectionPanel.SetActive(mode == EPickerMode.Model);
		}

		if (m_ColorSelectionPanel != null)
		{
			m_ColorSelectionPanel.SetActive(mode == EPickerMode.Color);
		}
	}

	private void UpdateModelOptionsState(ECharacterType selectedType, bool canCustomize)
	{
		if (m_ModelOptions == null)
		{
			return;
		}

		for (int i = 0; i < m_ModelOptions.Length; i++)
		{
			ModelOptionItem item = m_ModelOptions[i];
			if (item.OptionButton != null)
			{
				item.OptionButton.interactable = canCustomize;
			}

			if (item.SelectedIndicator != null)
			{
				item.SelectedIndicator.SetActive(item.CharacterType == selectedType);
			}
		}
	}

	private void UpdateColorOptionsState(EHighlightColor selectedColor, bool canCustomize)
	{
		if (m_ColorOptions == null)
		{
			return;
		}

		for (int i = 0; i < m_ColorOptions.Length; i++)
		{
			ColorOptionItem item = m_ColorOptions[i];
			if (item.OptionButton != null)
			{
				item.OptionButton.interactable = canCustomize;
			}

			if (item.SelectedIndicator != null)
			{
				item.SelectedIndicator.SetActive(item.ColorType == selectedColor);
			}
		}
	}

	private void RestoreFocusAfterClose(EPickerMode closedMode)
	{
		if (EventSystem.current == null || m_PickerWindowRoot == null)
		{
			return;
		}

		GameObject currentSelected = EventSystem.current.currentSelectedGameObject;
		if (currentSelected == null || !currentSelected.transform.IsChildOf(m_PickerWindowRoot.transform))
		{
			return;
		}

		if (closedMode == EPickerMode.Model && m_OpenModelPickerButton != null && m_OpenModelPickerButton.interactable)
		{
			EventSystem.current.SetSelectedGameObject(m_OpenModelPickerButton.gameObject);
		}
		else if (closedMode == EPickerMode.Color && m_OpenColorPickerButton != null && m_OpenColorPickerButton.interactable)
		{
			EventSystem.current.SetSelectedGameObject(m_OpenColorPickerButton.gameObject);
		}
		else
		{
			EventSystem.current.SetSelectedGameObject(null);
		}
	}
}

