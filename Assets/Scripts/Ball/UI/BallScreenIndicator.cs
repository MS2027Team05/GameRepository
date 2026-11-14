using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

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
		HeldByAlly,
		HeldByEnemy,
		HeldBySelf,
		HeldByOther
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

	private ulong m_ColorHolderClientId = ulong.MaxValue;
	private PlayerNetworkProfile m_ColorHolderProfile;
	private bool m_HasResolvedColorHolderProfile;

	private PlayerNetworkProfile m_LocalPlayerProfile;
	private PlayerNetworkProfile m_HolderPlayerProfile;
	private ulong m_CachedHolderClientId = ulong.MaxValue;

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
	/// 現在使用されているカメラ(観戦時は観戦カメラ、プレイヤー時はメインカメラ)を取得します。
	/// </summary>
	private void TrySetTargetCamera()
	{
		if (SpectatorDisplayManager.IsLocalSpectator && SpectatorDisplayManager.ActiveSpectatorCamera != null)
		{
			m_TargetCamera = SpectatorDisplayManager.ActiveSpectatorCamera;
			return;
		}

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
	/// Free、HeldByAlly、HeldByEnemy、HeldBySelfのいずれかです。
	/// </returns>
	private EIndicatorState GetIndicatorState()
	{
		NetworkManager networkManager =
			NetworkManager.Singleton;

		if (!m_BallController.HasHolder)
		{
			ResetHolderProfileCache();
			return EIndicatorState.Free;
		}

		if (networkManager == null ||
			!networkManager.IsListening)
		{
			return EIndicatorState.HeldByEnemy;
		}

		ulong holderClientId =
			m_BallController.CurrentHolderId;

		if (SpectatorDisplayManager.IsLocalSpectator)
		{
			// 観戦者視点では保持者のチーム色をそのまま表示
			return EIndicatorState.HeldByOther;
		}

		if (holderClientId ==
			networkManager.LocalClientId)
		{
			return EIndicatorState.HeldBySelf;
		}

		if (!TryGetLocalPlayerProfile(
			networkManager,
			out PlayerNetworkProfile localProfile) ||
			!TryGetHolderPlayerProfile(
				networkManager,
				holderClientId,
				out PlayerNetworkProfile holderProfile))
		{
			return EIndicatorState.HeldByEnemy;
		}

		if (localProfile.Team !=
				ETeamType.None &&
			localProfile.Team ==
				holderProfile.Team)
		{
			return EIndicatorState.HeldByAlly;
		}

		return EIndicatorState.HeldByEnemy;
	}

	/// <summary>
	/// ローカルPlayerのProfileを取得します。
	/// 一度取得できた参照は再利用します。
	/// </summary>
	private bool TryGetLocalPlayerProfile(
		NetworkManager networkManager,
		out PlayerNetworkProfile playerProfile)
	{
		if (m_LocalPlayerProfile != null)
		{
			playerProfile =
				m_LocalPlayerProfile;

			return true;
		}

		NetworkObject localPlayerObject =
			networkManager.LocalClient != null
				? networkManager.LocalClient.PlayerObject
				: null;

		if (localPlayerObject == null)
		{
			playerProfile =
				null;

			return false;
		}

		m_LocalPlayerProfile =
			localPlayerObject.GetComponent<PlayerNetworkProfile>();

		playerProfile =
			m_LocalPlayerProfile;

		return playerProfile !=
			null;
	}

	/// <summary>
	/// 現在のボール所持者のProfileを取得します。
	/// 所持者が変わった場合だけ参照を取り直します。
	/// </summary>
	private bool TryGetHolderPlayerProfile(
		NetworkManager networkManager,
		ulong holderClientId,
		out PlayerNetworkProfile playerProfile)
	{
		if (m_CachedHolderClientId !=
			holderClientId)
		{
			m_CachedHolderClientId =
				holderClientId;

			m_HolderPlayerProfile =
				null;
		}

		if (m_HolderPlayerProfile != null)
		{
			playerProfile =
				m_HolderPlayerProfile;

			return true;
		}

		if (!networkManager.ConnectedClients.TryGetValue(
			holderClientId,
			out NetworkClient holderClient) ||
			holderClient.PlayerObject == null)
		{
			playerProfile =
				null;

			return false;
		}

		m_HolderPlayerProfile =
			holderClient.PlayerObject.GetComponent<PlayerNetworkProfile>();

		playerProfile =
			m_HolderPlayerProfile;

		return playerProfile !=
			null;
	}

	/// <summary>
	/// ボールがFreeになった場合の所持者Profileキャッシュを破棄します。
	/// </summary>
	private void ResetHolderProfileCache()
	{
		m_CachedHolderClientId =
			ulong.MaxValue;

		m_HolderPlayerProfile =
			null;
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

		ApplyHolderTeamColor(
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

		ApplyHolderTeamColor(
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

			case EIndicatorState.HeldByAlly:
			case EIndicatorState.HeldBySelf:
				return m_OnScreenHeldBySelfIndicator;

			case EIndicatorState.HeldByEnemy:
			case EIndicatorState.HeldByOther:
				return m_OnScreenHeldByOtherIndicator;
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

			case EIndicatorState.HeldByAlly:
			case EIndicatorState.HeldBySelf:
				return m_OffscreenHeldBySelfIndicator;

			case EIndicatorState.HeldByEnemy:
			case EIndicatorState.HeldByOther:
				return m_OffscreenHeldByOtherIndicator;
		}

		return null;
	}

	/// <summary>
	/// ボール所持者のTeamに合わせて、
	/// 現在表示するIndicator全体をTeam Colorへ変更します。
	/// Freeの場合は元のIndicator色を変更しません。
	/// </summary>
	private void ApplyHolderTeamColor(
		RectTransform indicatorTransform)
	{
		if (indicatorTransform == null ||
			m_BallController == null ||
			!m_BallController.HasHolder)
		{
			return;
		}

		ETeamType holderTeam =
			GetHolderTeamForIndicator();

		Color teamColor;

		switch (holderTeam)
		{
			case ETeamType.Red:
				teamColor =
					Color.red;
				break;

			case ETeamType.Blue:
				teamColor =
					Color.blue;
				break;

			default:
				return;
		}

		Graphic[] graphics =
			indicatorTransform.GetComponentsInChildren<Graphic>(
				true);

		for (int graphicIndex = 0;
			graphicIndex < graphics.Length;
			graphicIndex++)
		{
			Graphic graphic =
				graphics[graphicIndex];

			if (graphic == null)
			{
				continue;
			}

			Color currentColor =
				graphic.color;

			graphic.color =
				new Color(
					teamColor.r,
					teamColor.g,
					teamColor.b,
					currentColor.a);
		}
	}

	/// <summary>
	/// 現在のボール所持者のTeamを取得します。
	/// 所持者が変わった場合だけPlayerNetworkProfileを再取得します。
	/// </summary>
	private ETeamType GetHolderTeamForIndicator()
	{
		NetworkManager networkManager =
			NetworkManager.Singleton;

		if (networkManager == null ||
			!networkManager.IsListening ||
			m_BallController == null ||
			!m_BallController.HasHolder)
		{
			ResetHolderTeamColorCache();
			return ETeamType.None;
		}

		ulong holderClientId =
			m_BallController.CurrentHolderId;

		if (m_ColorHolderClientId !=
			holderClientId)
		{
			m_ColorHolderClientId =
				holderClientId;

			m_ColorHolderProfile =
				null;

			m_HasResolvedColorHolderProfile =
				false;
		}

		if (!m_HasResolvedColorHolderProfile)
		{
			m_HasResolvedColorHolderProfile =
				true;

			if (networkManager.ConnectedClients.TryGetValue(
				holderClientId,
				out NetworkClient holderClient) &&
				holderClient.PlayerObject != null)
			{
				m_ColorHolderProfile =
					holderClient.PlayerObject.GetComponent<PlayerNetworkProfile>();
			}
		}

		if (m_ColorHolderProfile == null)
		{
			return ETeamType.None;
		}

		return m_ColorHolderProfile.Team;
	}

	/// <summary>
	/// Indicator用の所持者Teamキャッシュを初期化します。
	/// </summary>
	private void ResetHolderTeamColorCache()
	{
		m_ColorHolderClientId =
			ulong.MaxValue;

		m_ColorHolderProfile =
			null;

		m_HasResolvedColorHolderProfile =
			false;
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
