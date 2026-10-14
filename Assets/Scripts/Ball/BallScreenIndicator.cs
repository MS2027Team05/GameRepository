using Unity.Netcode;
using UnityEngine;

/// <summary>
/// ボールの画面上での位置を示すインジケーターを管理します。
/// </summary>
/// <remarks>
/// 主な処理の流れ:
/// 1. ボールが近距離か確認します。
/// 2. 遠距離かつ画面内なら画面内用Indicatorを表示します。
/// 3. 画面外なら画面外用Indicatorを画面端へ表示します。
/// 4. ボールの所持状態に応じて表示するIndicatorを切り替えます。
/// 5. IndicatorはGameObject単位で切り替え、
///    MainとGlowをまとめて表示・非表示にします。
/// </remarks>
public class BallScreenIndicator : MonoBehaviour
{
	private enum EIndicatorState
	{
		Free,
		HeldByOther,
		HeldBySelf
	}

	[Header("追跡対象")]
	[Tooltip("追跡するボールのBallControllerです。")]
	[SerializeField] private BallController m_BallController;

	[Header("画面内・遠距離インジケーター")]
	[Tooltip("誰にも所持されていない場合の画面内用Indicatorです。")]
	[SerializeField] private RectTransform m_OnScreenFreeIndicator;

	[Tooltip("他プレイヤーが所持している場合の画面内用Indicatorです。")]
	[SerializeField] private RectTransform m_OnScreenHeldByOtherIndicator;

	[Tooltip("自分が所持している場合の画面内用Indicatorです。")]
	[SerializeField] private RectTransform m_OnScreenHeldBySelfIndicator;

	[Header("画面外インジケーター")]
	[Tooltip("誰にも所持されていない場合の画面外用Indicatorです。")]
	[SerializeField] private RectTransform m_OffscreenFreeIndicator;

	[Tooltip("他プレイヤーが所持している場合の画面外用Indicatorです。")]
	[SerializeField] private RectTransform m_OffscreenHeldByOtherIndicator;

	[Tooltip("自分が所持している場合の画面外用Indicatorです。")]
	[SerializeField] private RectTransform m_OffscreenHeldBySelfIndicator;

	[Header("距離設定")]
	[Tooltip("この距離以上離れた場合、画面内でもインジケーターを表示します。")]
	[SerializeField] private float m_DisplayDistance;

	[Header("画面端設定")]
	[Tooltip("画面端からインジケーターを離す余白です。")]
	[SerializeField] private Vector2 m_ScreenMargin;

	[Tooltip("画面外インジケーターの向きを補正する角度です。")]
	[SerializeField] private float m_ArrowAngleOffset;

	private Camera m_TargetCamera;
	private Canvas m_Canvas;
	private RectTransform m_CanvasRectTransform;

	/// <summary>
	/// Canvasの参照を取得します。
	/// </summary>
	private void Awake()
	{
		m_Canvas =
			GetComponentInParent<Canvas>();

		if (m_Canvas != null)
		{
			m_CanvasRectTransform =
				m_Canvas.transform as RectTransform;
		}
	}

	/// <summary>
	/// 使用するCameraを取得し、
	/// 全Indicatorを非表示にします。
	/// </summary>
	private void Start()
	{
		TrySetTargetCamera();
		HideAllIndicators();
	}

	/// <summary>
	/// ボールの画面位置、距離、所持状態に応じて
	/// 表示するIndicatorを更新します。
	/// </summary>
	private void LateUpdate()
	{
		if (m_BallController == null ||
			m_Canvas == null ||
			m_CanvasRectTransform == null)
		{
			HideAllIndicators();
			return;
		}

		if (m_TargetCamera == null ||
			!m_TargetCamera.isActiveAndEnabled)
		{
			TrySetTargetCamera();

			if (m_TargetCamera == null)
			{
				HideAllIndicators();
				return;
			}
		}

		Vector3 ballPosition =
			m_BallController.transform.position;

		Vector3 viewportPosition =
			m_TargetCamera.WorldToViewportPoint(
				ballPosition);

		bool isOnScreen =
			IsBallOnScreen(
				viewportPosition);

		float distance =
			Vector3.Distance(
				m_TargetCamera.transform.position,
				ballPosition);

		if (isOnScreen &&
			distance < m_DisplayDistance)
		{
			HideAllIndicators();
			return;
		}

		EIndicatorState indicatorState =
			GetIndicatorState();

		if (isOnScreen)
		{
			UpdateOnScreenState(
				indicatorState,
				ballPosition);

			return;
		}

		UpdateOffscreenState(
			indicatorState,
			viewportPosition);
	}

	/// <summary>
	/// 現在使用されているメインカメラを取得します。
	/// </summary>
	private void TrySetTargetCamera()
	{
		m_TargetCamera =
			Camera.main;
	}

	/// <summary>
	/// ボールが画面内に存在するか確認します。
	/// </summary>
	/// <param name="viewportPosition">
	/// ボールのViewport座標です。
	/// </param>
	/// <returns>
	/// ボールがCamera前方かつ画面内ならtrueです。
	/// </returns>
	private bool IsBallOnScreen(
		Vector3 viewportPosition)
	{
		return viewportPosition.z > 0.0f &&
			viewportPosition.x >= 0.0f &&
			viewportPosition.x <= 1.0f &&
			viewportPosition.y >= 0.0f &&
			viewportPosition.y <= 1.0f;
	}

	/// <summary>
	/// 現在のボール所持状態を取得します。
	/// </summary>
	/// <returns>
	/// Free、HeldByOther、HeldBySelfのいずれかです。
	/// </returns>
	private EIndicatorState GetIndicatorState()
	{
		if (!m_BallController.HasHolder)
		{
			return EIndicatorState.Free;
		}

		if (NetworkManager.Singleton != null &&
			NetworkManager.Singleton.IsListening &&
			m_BallController.CurrentHolderId ==
			NetworkManager.Singleton.LocalClientId)
		{
			return EIndicatorState.HeldBySelf;
		}

		return EIndicatorState.HeldByOther;
	}

	/// <summary>
	/// 遠距離かつ画面内の場合のIndicatorを更新します。
	/// </summary>
	private void UpdateOnScreenState(
		EIndicatorState indicatorState,
		Vector3 ballPosition)
	{
		RectTransform activeIndicator =
			GetOnScreenIndicator(
				indicatorState);

		if (activeIndicator == null)
		{
			HideAllIndicators();
			return;
		}

		ShowOnlyIndicator(
			activeIndicator);

		UpdateOnScreenIndicatorTransform(
			activeIndicator,
			ballPosition);
	}

	/// <summary>
	/// 画面外の場合のIndicatorを更新します。
	/// </summary>
	private void UpdateOffscreenState(
		EIndicatorState indicatorState,
		Vector3 viewportPosition)
	{
		RectTransform activeIndicator =
			GetOffscreenIndicator(
				indicatorState);

		if (activeIndicator == null)
		{
			HideAllIndicators();
			return;
		}

		ShowOnlyIndicator(
			activeIndicator);

		UpdateOffscreenIndicatorTransform(
			activeIndicator,
			viewportPosition);
	}

	/// <summary>
	/// 状態に対応する画面内Indicatorを取得します。
	/// </summary>
	private RectTransform GetOnScreenIndicator(
		EIndicatorState indicatorState)
	{
		switch (indicatorState)
		{
			case EIndicatorState.Free:
				return m_OnScreenFreeIndicator;

			case EIndicatorState.HeldByOther:
				return m_OnScreenHeldByOtherIndicator;

			case EIndicatorState.HeldBySelf:
				return m_OnScreenHeldBySelfIndicator;
		}

		return null;
	}

	/// <summary>
	/// 状態に対応する画面外Indicatorを取得します。
	/// </summary>
	private RectTransform GetOffscreenIndicator(
		EIndicatorState indicatorState)
	{
		switch (indicatorState)
		{
			case EIndicatorState.Free:
				return m_OffscreenFreeIndicator;

			case EIndicatorState.HeldByOther:
				return m_OffscreenHeldByOtherIndicator;

			case EIndicatorState.HeldBySelf:
				return m_OffscreenHeldBySelfIndicator;
		}

		return null;
	}

	/// <summary>
	/// 画面内にあるボール位置へIndicatorを配置します。
	/// </summary>
	private void UpdateOnScreenIndicatorTransform(
		RectTransform indicatorTransform,
		Vector3 ballPosition)
	{
		Vector3 screenPosition =
			m_TargetCamera.WorldToScreenPoint(
				ballPosition);

		if (!TryConvertScreenToCanvasPosition(
			screenPosition,
			out Vector2 canvasPosition))
		{
			return;
		}

		indicatorTransform.anchoredPosition =
			canvasPosition;

		indicatorTransform.localRotation =
			Quaternion.identity;
	}

	/// <summary>
	/// 画面外にあるボール方向へIndicatorを配置します。
	/// </summary>
	private void UpdateOffscreenIndicatorTransform(
		RectTransform indicatorTransform,
		Vector3 viewportPosition)
	{
		Vector2 direction =
			new Vector2(
				viewportPosition.x - 0.5f,
				viewportPosition.y - 0.5f);

		if (viewportPosition.z < 0.0f)
		{
			direction *=
				-1.0f;
		}

		if (direction.sqrMagnitude <=
			Mathf.Epsilon)
		{
			direction =
				Vector2.up;
		}

		direction.Normalize();

		Vector2 halfCanvasSize =
			m_CanvasRectTransform.rect.size *
			0.5f;

		float horizontalLimit =
			halfCanvasSize.x -
			m_ScreenMargin.x;

		float verticalLimit =
			halfCanvasSize.y -
			m_ScreenMargin.y;

		float horizontalScale =
			float.MaxValue;

		if (Mathf.Abs(direction.x) >
			Mathf.Epsilon)
		{
			horizontalScale =
				horizontalLimit /
				Mathf.Abs(direction.x);
		}

		float verticalScale =
			float.MaxValue;

		if (Mathf.Abs(direction.y) >
			Mathf.Epsilon)
		{
			verticalScale =
				verticalLimit /
				Mathf.Abs(direction.y);
		}

		float edgeScale =
			Mathf.Min(
				horizontalScale,
				verticalScale);

		indicatorTransform.anchoredPosition =
			direction *
			edgeScale;

		float angle =
			Mathf.Atan2(
				direction.y,
				direction.x) *
			Mathf.Rad2Deg;

		indicatorTransform.localRotation =
			Quaternion.Euler(
				0.0f,
				0.0f,
				angle +
				m_ArrowAngleOffset);
	}

	/// <summary>
	/// Screen座標をCanvas内の座標へ変換します。
	/// </summary>
	private bool TryConvertScreenToCanvasPosition(
		Vector2 screenPosition,
		out Vector2 canvasPosition)
	{
		Camera canvasCamera =
			null;

		if (m_Canvas.renderMode !=
			RenderMode.ScreenSpaceOverlay)
		{
			canvasCamera =
				m_Canvas.worldCamera;
		}

		return RectTransformUtility.ScreenPointToLocalPointInRectangle(
			m_CanvasRectTransform,
			screenPosition,
			canvasCamera,
			out canvasPosition);
	}

	/// <summary>
	/// 指定されたIndicatorだけを表示します。
	/// </summary>
	private void ShowOnlyIndicator(
		RectTransform activeIndicator)
	{
		SetIndicatorVisible(
			m_OnScreenFreeIndicator,
			m_OnScreenFreeIndicator ==
			activeIndicator);

		SetIndicatorVisible(
			m_OnScreenHeldByOtherIndicator,
			m_OnScreenHeldByOtherIndicator ==
			activeIndicator);

		SetIndicatorVisible(
			m_OnScreenHeldBySelfIndicator,
			m_OnScreenHeldBySelfIndicator ==
			activeIndicator);

		SetIndicatorVisible(
			m_OffscreenFreeIndicator,
			m_OffscreenFreeIndicator ==
			activeIndicator);

		SetIndicatorVisible(
			m_OffscreenHeldByOtherIndicator,
			m_OffscreenHeldByOtherIndicator ==
			activeIndicator);

		SetIndicatorVisible(
			m_OffscreenHeldBySelfIndicator,
			m_OffscreenHeldBySelfIndicator ==
			activeIndicator);
	}

	/// <summary>
	/// 全Indicatorを非表示にします。
	/// </summary>
	private void HideAllIndicators()
	{
		SetIndicatorVisible(
			m_OnScreenFreeIndicator,
			false);

		SetIndicatorVisible(
			m_OnScreenHeldByOtherIndicator,
			false);

		SetIndicatorVisible(
			m_OnScreenHeldBySelfIndicator,
			false);

		SetIndicatorVisible(
			m_OffscreenFreeIndicator,
			false);

		SetIndicatorVisible(
			m_OffscreenHeldByOtherIndicator,
			false);

		SetIndicatorVisible(
			m_OffscreenHeldBySelfIndicator,
			false);
	}

	/// <summary>
	/// Indicator全体の表示状態を変更します。
	/// MainとGlowを含むGameObject単位で切り替えます。
	/// </summary>
	private void SetIndicatorVisible(
		RectTransform indicator,
		bool isVisible)
	{
		if (indicator == null)
		{
			return;
		}

		if (indicator.gameObject.activeSelf ==
			isVisible)
		{
			return;
		}

		indicator.gameObject.SetActive(
			isVisible);
	}
}
