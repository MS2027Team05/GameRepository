using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ボールが画面外にある場合、
/// ボールの所持状態に対応したインジケーターを画面端へ表示します。
/// </summary>
public class BallOffscreenIndicator : MonoBehaviour
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

	[Header("状態別インジケーター")]
	[Tooltip("誰にも所持されていない場合に表示するImageです。")]
	[SerializeField] private Image m_FreeIndicator;

	[Tooltip("他プレイヤーが所持している場合に表示するImageです。")]
	[SerializeField] private Image m_HeldByOtherIndicator;

	[Tooltip("自分が所持している場合に表示するImageです。")]
	[SerializeField] private Image m_HeldBySelfIndicator;

	[Header("画面端設定")]
	[Tooltip("画面端からインジケーターを離すピクセル数です。")]
	[SerializeField] private Vector2 m_ScreenMargin;

	[Tooltip("矢印画像の向きに合わせるための回転角度補正です。")]
	[SerializeField] private float m_ArrowAngleOffset;

	private Camera m_TargetCamera;

	private void Start()
	{
		TrySetTargetCamera();
		HideAllIndicators();
	}

	private void LateUpdate()
	{
		if (m_BallController == null)
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

		Vector3 viewportPosition =
			m_TargetCamera.WorldToViewportPoint(
				m_BallController.transform.position);

		if (IsBallOnScreen(viewportPosition))
		{
			HideAllIndicators();
			return;
		}

		EIndicatorState indicatorState =
			GetIndicatorState();

		Image activeIndicator =
			GetIndicatorImage(indicatorState);

		if (activeIndicator == null)
		{
			HideAllIndicators();
			return;
		}

		ShowOnlyIndicator(activeIndicator);

		UpdateIndicatorTransform(
			activeIndicator.rectTransform,
			viewportPosition);
	}

	/// <summary>
	/// 現在の端末で使用されているメインカメラを取得します。
	/// </summary>
	private void TrySetTargetCamera()
	{
		m_TargetCamera = Camera.main;
	}

	/// <summary>
	/// ボールが現在画面内に存在するか判定します。
	/// </summary>
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
	/// BallControllerの所持情報から、
	/// インジケーターの表示状態を決定します。
	/// </summary>
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
	/// 状態に対応するImageを取得します。
	/// </summary>
	private Image GetIndicatorImage(
		EIndicatorState indicatorState)
	{
		switch (indicatorState)
		{
			case EIndicatorState.Free:
				return m_FreeIndicator;

			case EIndicatorState.HeldByOther:
				return m_HeldByOtherIndicator;

			case EIndicatorState.HeldBySelf:
				return m_HeldBySelfIndicator;
		}

		return null;
	}

	/// <summary>
	/// 指定したインジケーターだけを表示します。
	/// </summary>
	private void ShowOnlyIndicator(
		Image activeIndicator)
	{
		SetIndicatorVisible(
			m_FreeIndicator,
			m_FreeIndicator == activeIndicator);

		SetIndicatorVisible(
			m_HeldByOtherIndicator,
			m_HeldByOtherIndicator == activeIndicator);

		SetIndicatorVisible(
			m_HeldBySelfIndicator,
			m_HeldBySelfIndicator == activeIndicator);
	}

	/// <summary>
	/// すべてのインジケーターを非表示にします。
	/// </summary>
	private void HideAllIndicators()
	{
		SetIndicatorVisible(
			m_FreeIndicator,
			false);

		SetIndicatorVisible(
			m_HeldByOtherIndicator,
			false);

		SetIndicatorVisible(
			m_HeldBySelfIndicator,
			false);
	}

	/// <summary>
	/// Imageの表示状態を変更します。
	/// </summary>
	private void SetIndicatorVisible(
		Image indicator,
		bool isVisible)
	{
		if (indicator == null)
		{
			return;
		}

		indicator.enabled = isVisible;
	}

	/// <summary>
	/// ボールの方向に合わせて、
	/// インジケーターを画面端へ配置・回転させます。
	/// </summary>
	private void UpdateIndicatorTransform(
		RectTransform indicatorTransform,
		Vector3 viewportPosition)
	{
		Vector2 screenDirection =
			new Vector2(
				(viewportPosition.x - 0.5f) *
				Screen.width,
				(viewportPosition.y - 0.5f) *
				Screen.height);

		// ボールがカメラ後方にある場合は、
		// 画面上で正しい方向になるよう反転します。
		if (viewportPosition.z < 0.0f)
		{
			screenDirection *= -1.0f;
		}

		if (screenDirection.sqrMagnitude <=
			Mathf.Epsilon)
		{
			screenDirection = Vector2.up;
		}

		screenDirection.Normalize();

		float halfWidth =
			(Screen.width * 0.5f) -
			m_ScreenMargin.x;

		float halfHeight =
			(Screen.height * 0.5f) -
			m_ScreenMargin.y;

		float horizontalScale =
			float.MaxValue;

		if (Mathf.Abs(screenDirection.x) >
			Mathf.Epsilon)
		{
			horizontalScale =
				halfWidth /
				Mathf.Abs(screenDirection.x);
		}

		float verticalScale =
			float.MaxValue;

		if (Mathf.Abs(screenDirection.y) >
			Mathf.Epsilon)
		{
			verticalScale =
				halfHeight /
				Mathf.Abs(screenDirection.y);
		}

		float edgeScale =
			Mathf.Min(
				horizontalScale,
				verticalScale);

		Vector2 screenCenter =
			new Vector2(
				Screen.width * 0.5f,
				Screen.height * 0.5f);

		Vector2 indicatorPosition =
			screenCenter +
			(screenDirection * edgeScale);

		indicatorTransform.position =
			indicatorPosition;

		float angle =
			Mathf.Atan2(
				screenDirection.y,
				screenDirection.x) *
			Mathf.Rad2Deg;

		indicatorTransform.rotation =
			Quaternion.Euler(
				0.0f,
				0.0f,
				angle + m_ArrowAngleOffset);
	}
}
