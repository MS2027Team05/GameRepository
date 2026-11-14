using System;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// 常時表示レティクルによる移動先の判定と、
/// 地上からの予備浮上・飛行中の着地点調整を担当する。
/// </summary>
public class PlayerAimGuide : MonoBehaviour, IAimGuide
{
	[Header("レティクル設定")]
	[SerializeField] private GameObject m_AimReticle;

	[Header("予測線設定")]
	[SerializeField] private LineRenderer m_AimPredictionLine;
	[SerializeField] private Transform m_AimLineStart;
	[SerializeField] private Vector3 m_AimLineStartOffset = new Vector3(0f, 0f, 0.2f);

	[Header("着地点マーカー設定")]
	[SerializeField] private GameObject m_LandingMarker;
	[SerializeField] private LayerMask m_AimTargetLayers;
	[SerializeField] private float m_MaxAimDistance = 100f;
	[SerializeField] private float m_LandingMarkerOffset = 0.05f;

	[Header("地上からの予備浮上設定")]
	[SerializeField] private float m_AimFloatHeight = 1f;
	[SerializeField] private float m_AimFloatDuration = 0.5f;

	[Header("飛行中の着地点調整設定")]
	[SerializeField] private float m_AirTargetMoveSpeed = 2f;
	[SerializeField] private float m_MaxNormalAngle = 85f;
	[SerializeField] private float m_SurfaceCheckHeight = 2f;
	[SerializeField] private float m_SurfaceCheckDistance = 4f;

	[Header("カメラ設定")]
	[SerializeField] private Transform m_CinemachineCameraTransform;

	private IGravityMover m_GravityMover;
	private Rigidbody m_Rigidbody;

	private Vector3 m_AimTargetPoint;
	private Vector3 m_AimTargetNormal = Vector3.up;
	private Vector3 m_AimMoveDirection;

	// 現在カメラが狙っている地点。
	// 飛行中の実際の着地点とは分けて管理する。
	private Vector3 m_AimPreviewPoint;
	private Vector3 m_AimPreviewNormal = Vector3.up;
	private bool m_HasAimPreviewTarget;

	private Vector3 m_InitialTrackingNormal;

	private Vector3 m_LandingMoveUp;
	private Vector3 m_LandingMoveRight;

	private Vector3 m_OriginalCameraLocalPosition;

	private Tween m_AimFloatTween;

	private bool m_CanFall;
	private bool m_IsTakingOff;
	private bool m_IsTrackingLanding;
	private bool m_IsAdditionalMoveAimAvailable = true;

	public bool CanFall => m_CanFall;
	public bool IsTakingOff => m_IsTakingOff;

	public Vector3 TargetLandingPoint => m_AimTargetPoint;
	public Vector3 TargetLandingNormal => m_AimTargetNormal;

	public float TargetDistance =>
		Vector3.Distance(transform.position, m_AimTargetPoint);

	public Vector3 AimMoveDirection => m_AimMoveDirection;

	private void Awake()
	{
		m_GravityMover = GetComponent<IGravityMover>();
		m_Rigidbody = GetComponent<Rigidbody>();

		if (m_CinemachineCameraTransform != null)
		{
			m_OriginalCameraLocalPosition =
				m_CinemachineCameraTransform.localPosition;
		}

		// 着地点マーカーをプレイヤーの親子関係から切り離す。
		// ワールド空間に配置し、プレイヤーの移動に追従させない。
		if (m_LandingMarker != null)
		{
			m_LandingMarker.transform.SetParent(null, true);
		}

		// レティクルは初期状態では非表示。
		// PlayerControllerから自分のプレイヤーだけ有効化する。
		SetConstantReticleActive(false);

		// 着地点マーカーは飛行開始まで非表示。
		HideLandingMarker();
	}

	private void OnDestroy()
	{
		m_AimFloatTween?.Kill();
	}

	// =========================================================
	// 常時表示レティクル
	// =========================================================

	/// <summary>
	/// レティクルの表示・非表示を切り替える。
	/// </summary>
	public void SetConstantReticleActive(bool active)
	{
		// 空中移動回数を使い切っている間は、
		// 飛行中のレティクルを表示しない。
		if (m_IsTrackingLanding && !m_IsAdditionalMoveAimAvailable)
		{
			active = false;
		}

		if (m_AimReticle != null)
		{
			m_AimReticle.SetActive(active);
		}
	}
	/// <summary>
	/// レティクルの方向にレイを飛ばし、移動可能地点を判定する。
	/// 飛行中はエイム表示だけを更新し、実際の着地点は上書きしない。
	/// </summary>
	public void UpdateConstantReticle(Vector3 aimDirection)
	{
		// 予備浮上中は通常のエイム判定を行わない。
		if (m_IsTakingOff)
		{
			HideAimPredictionLine();
			return;
		}

		// エイム方向が無効なら非表示。
		if (aimDirection.sqrMagnitude <= 0.001f)
		{
			m_HasAimPreviewTarget = false;

			if (!m_IsTrackingLanding)
			{
				m_CanFall = false;
			}

			HideAimPredictionLine();
			return;
		}

		// 現在のカメラ方向から移動先を判定する。
		if (!TryEvaluateAimTarget(
			aimDirection,
			out Vector3 targetPoint,
			out Vector3 targetNormal))
		{
			m_HasAimPreviewTarget = false;

			if (!m_IsTrackingLanding)
			{
				m_CanFall = false;
			}

			HideAimPredictionLine();
			return;
		}

		// 現在カメラが狙っている地点を保存する。
		m_AimPreviewPoint = targetPoint;
		m_AimPreviewNormal = targetNormal;
		m_HasAimPreviewTarget = true;

		// 飛行中でなければ、通常の移動先としても更新する。
		if (!m_IsTrackingLanding)
		{
			m_AimTargetPoint = targetPoint;
			m_AimTargetNormal = targetNormal;

			m_CanFall = true;

			m_AimMoveDirection =
				(m_AimTargetPoint - transform.position).normalized;
		}

		// 通常時・飛行中のどちらでも予測線を更新する。
		UpdateAimPredictionLine();
	}

	/// <summary>
	/// 空中追加移動用に、現在のカメラ照準方向から着地点を評価する。
	/// </summary>
	public bool TryUpdateAdditionalMoveTarget(
		Vector3 aimDirection,
		out Vector3 targetPoint,
		out Vector3 targetNormal,
		out Vector3 moveDirection)
	{
		targetPoint = Vector3.zero;
		targetNormal = Vector3.up;
		moveDirection = Vector3.zero;

		if (!m_IsTrackingLanding || m_IsTakingOff)
		{
			return false;
		}

		if (!TryEvaluateAimTarget(
			aimDirection,
			out targetPoint,
			out targetNormal))
		{
			return false;
		}

		Vector3 direction = targetPoint - transform.position;

		if (direction.sqrMagnitude <= 0.0001f)
		{
			return false;
		}

		moveDirection = direction.normalized;

		return true;
	}

	/// <summary>
	/// レティクルの方向にある移動先を判定する。
	/// </summary>
	private bool TryEvaluateAimTarget(
		Vector3 aimDirection,
		out Vector3 targetPoint,
		out Vector3 targetNormal)
	{
		targetPoint = Vector3.zero;
		targetNormal = Vector3.up;

		Transform cameraTransform = GetAimCameraTransform();

		if (cameraTransform == null ||
			aimDirection.sqrMagnitude <= 0.001f)
		{
			return false;
		}

		Ray ray = new Ray(
			cameraTransform.position,
			aimDirection.normalized);

		if (!Physics.Raycast(
			ray,
			out RaycastHit hit,
			m_MaxAimDistance,
			m_AimTargetLayers))
		{
			return false;
		}

		targetPoint = hit.point;
		targetNormal = hit.normal.normalized;

		return true;
	}

	/// <summary>
	/// 移動先までの予測線を更新する。
	/// </summary>
	/// <summary>
	/// 移動先までの予測線を更新する。
	/// </summary>
	private void UpdateAimPredictionLine()
	{
		if (m_AimPredictionLine == null)
		{
			return;
		}

		// 移動可能な地点を捉えていない場合は非表示。
		if (!m_CanFall ||
			m_IsTakingOff ||
			m_AimLineStart == null ||
			(m_IsTrackingLanding && !m_IsAdditionalMoveAimAvailable))
		{
			HideAimPredictionLine();
			return;
		}

		// 飛行中は現在のエイム地点を使用する。
		// 通常時は通常の移動先を使用する。
		Vector3 targetPoint = m_IsTrackingLanding
			? m_AimPreviewPoint
			: m_AimTargetPoint;

		if (m_IsTrackingLanding && !m_HasAimPreviewTarget)
		{
			HideAimPredictionLine();
			return;
		}

		// プレイヤーからエイム先までの距離を取得。
		float aimDistance =
			Vector3.Distance(transform.position, targetPoint);

		// 最大射程以上なら非表示。
		if (aimDistance >= m_MaxAimDistance)
		{
			HideAimPredictionLine();
			return;
		}

		// カメラの向きを基準にオフセットをワールド座標へ変換する。
		Transform cameraTransform = GetAimCameraTransform();

		if (cameraTransform == null)
		{
			HideAimPredictionLine();
			return;
		}

		Vector3 startPosition =
			m_AimLineStart.position +
			cameraTransform.right * m_AimLineStartOffset.x +
			cameraTransform.up * m_AimLineStartOffset.y +
			cameraTransform.forward * m_AimLineStartOffset.z;

		// エイム先を終点にする。
		m_AimPredictionLine.positionCount = 2;
		m_AimPredictionLine.SetPosition(0, startPosition);
		m_AimPredictionLine.SetPosition(1, targetPoint);
		m_AimPredictionLine.enabled = true;
	}

	/// <summary>
	/// 飛行中の現在の着地点に合わせて予測線を更新する。
	/// カメラの照準方向による着地点の再判定は行わない。
	/// </summary>
	private void UpdateLandingPredictionLine()
	{
		if (m_AimPredictionLine == null)
		{
			return;
		}

		// 飛行中の着地点追跡が無効なら非表示。
		if (!m_IsTrackingLanding ||
			!m_IsAdditionalMoveAimAvailable ||
			m_AimLineStart == null)
		{
			HideAimPredictionLine();
			return;
		}

		Transform cameraTransform = GetAimCameraTransform();

		if (cameraTransform == null)
		{
			HideAimPredictionLine();
			return;
		}

		// 予測線の開始位置を取得する。
		Vector3 startPosition =
			m_AimLineStart.position +
			cameraTransform.right * m_AimLineStartOffset.x +
			cameraTransform.up * m_AimLineStartOffset.y +
			cameraTransform.forward * m_AimLineStartOffset.z;

		// 終点には現在の着地点を使用する。
		// カメラの向きから着地点を再計算することはしない。
		m_AimPredictionLine.positionCount = 2;
		m_AimPredictionLine.SetPosition(0, startPosition);
		m_AimPredictionLine.SetPosition(1, m_AimTargetPoint);
		m_AimPredictionLine.enabled = true;
	}

	/// <summary>
	/// 予測線を非表示にする。
	/// </summary>
	private void HideAimPredictionLine()
	{
		if (m_AimPredictionLine != null)
		{
			m_AimPredictionLine.enabled = false;
		}
	}

	/// <summary>
	/// エイム判定に使用するカメラを取得する。
	/// </summary>
	private Transform GetAimCameraTransform()
	{
		if (m_CinemachineCameraTransform != null)
		{
			return m_CinemachineCameraTransform;
		}

		if (Camera.main != null)
		{
			return Camera.main.transform;
		}

		return null;
	}
	/// <summary>
	/// 空中追加移動用のエイムレティクルと予測線の表示を切り替える。
	/// </summary>
	public void SetAdditionalMoveAimVisible(bool visible)
	{
		m_IsAdditionalMoveAimAvailable = visible;

		if (m_AimReticle != null)
		{
			m_AimReticle.SetActive(visible);
		}

		if (visible)
		{
			UpdateAimPredictionLine();
		}
		else
		{
			HideAimPredictionLine();
		}
	}

	// =========================================================
	// 地上からの予備浮上
	// =========================================================

	/// <summary>
	/// 地上から移動するときの予備浮上を開始する。
	/// 浮上完了後にコールバックを呼び出す。
	/// </summary>
	public void StartGroundTakeoff(Action onTakeoffCompleted)
	{
		if (m_IsTakingOff)
		{
			return;
		}

		m_IsTakingOff = true;

		m_AimFloatTween?.Kill();

		// プレイヤー自身の上方向を取得する。
		Vector3 playerUp = transform.up.normalized;

		Vector3 startPosition = transform.position;
		Vector3 targetPosition =
			startPosition + playerUp * m_AimFloatHeight;

		// 予備浮上中はGravityMoverのホバー状態にする。
		m_GravityMover?.StartAimHover();

		Sequence sequence = DOTween.Sequence();

		// プレイヤーを地面の法線方向へ浮かせる。
		sequence.Append(
			transform.DOMove(targetPosition, m_AimFloatDuration)
				.SetEase(Ease.OutQuad));

		// カメラは浮上中に少し下げる。
		if (m_CinemachineCameraTransform != null)
		{
			Vector3 loweredCameraPosition =
				m_OriginalCameraLocalPosition +
				Vector3.down * m_AimFloatHeight;

			sequence.Join(
				m_CinemachineCameraTransform
					.DOLocalMove(loweredCameraPosition, m_AimFloatDuration)
					.SetEase(Ease.OutQuad));
		}

		sequence.OnComplete(() =>
		{
			if (m_Rigidbody != null)
			{
				m_Rigidbody.position = transform.position;
			}

			// カメラの位置を元に戻す。
			if (m_CinemachineCameraTransform != null)
			{
				m_CinemachineCameraTransform.localPosition =
					m_OriginalCameraLocalPosition;
			}

			m_IsTakingOff = false;

			// PlayerController側で飛行開始処理を行う。
			onTakeoffCompleted?.Invoke();
		});

		m_AimFloatTween = sequence;
	}

	/// <summary>
	/// 予備浮上をキャンセルする。
	/// </summary>
	public void CancelGroundTakeoff()
	{
		m_AimFloatTween?.Kill();
		m_AimFloatTween = null;

		m_IsTakingOff = false;

		if (m_CinemachineCameraTransform != null)
		{
			m_CinemachineCameraTransform.localPosition =
				m_OriginalCameraLocalPosition;
		}

		m_GravityMover?.StopAimHover();
		HideAimPredictionLine();
	}

	// =========================================================
	// 飛行中の着地点追跡
	// =========================================================

	/// <summary>
	/// 飛行開始時の着地点と法線を記録する。
	/// 再移動時は最初の法線と移動方向を維持する。
	/// </summary>
	public void BeginLandingTracking(
		Vector3 initialPoint,
		Vector3 initialNormal)
	{
		// 初回の飛行開始時だけ、基準となる方向を記録する。
		if (!m_IsTrackingLanding)
		{
			m_InitialTrackingNormal = initialNormal.normalized;

			Transform cameraTransform = GetAimCameraTransform();

			// カメラの上方向を着地点の面に沿わせる。
			Vector3 up = Vector3.ProjectOnPlane(
				cameraTransform != null
					? cameraTransform.up
					: transform.up,
				m_InitialTrackingNormal);

			// 上方向が使えない場合はカメラの前方向を使用する。
			if (up.sqrMagnitude <= 0.001f)
			{
				up = Vector3.ProjectOnPlane(
					cameraTransform != null
						? cameraTransform.forward
						: transform.forward,
					m_InitialTrackingNormal);
			}

			if (up.sqrMagnitude > 0.001f)
			{
				up.Normalize();
			}
			else
			{
				up = Vector3.Cross(
					m_InitialTrackingNormal,
					Vector3.right).normalized;
			}

			// カメラの右方向を着地点の面に沿わせる。
			Vector3 right = Vector3.ProjectOnPlane(
				cameraTransform != null
					? cameraTransform.right
					: transform.right,
				m_InitialTrackingNormal);

			if (right.sqrMagnitude <= 0.001f)
			{
				right = Vector3.Cross(
					m_InitialTrackingNormal,
					up);
			}

			right = Vector3.ProjectOnPlane(right, up);

			if (right.sqrMagnitude > 0.001f)
			{
				right.Normalize();
			}
			else
			{
				right = Vector3.Cross(
					up,
					m_InitialTrackingNormal).normalized;
			}

			// 飛行中に使い続ける方向を保存する。
			m_LandingMoveUp = up;
			m_LandingMoveRight = right;
		}

		m_IsTrackingLanding = true;

		m_AimTargetPoint = initialPoint;
		m_AimTargetNormal = initialNormal.normalized;

		// 飛行開始時点では、実際の着地点をエイム表示にも使用する。
		m_AimPreviewPoint = initialPoint;
		m_AimPreviewNormal = initialNormal.normalized;
		m_HasAimPreviewTarget = true;

		m_CanFall = true;

		UpdateLandingMarker();
		UpdateAimPredictionLine();
		UpdateLandingPredictionLine();
	}

	/// <summary>
	/// 飛行中の移動入力に応じて着地点をずらす。
	/// 元の着地点と同じ面の範囲内でのみ移動できる。
	/// </summary>
	public bool TryShiftLandingTarget(
		Vector2 moveInput,
		Transform cameraTransform,
		out Vector3 shiftedPoint,
		out Vector3 shiftedNormal)
	{
		shiftedPoint = m_AimTargetPoint;
		shiftedNormal = m_AimTargetNormal;

		if (!m_IsTrackingLanding)
		{
			return false;
		}

		if (moveInput.sqrMagnitude <= 0.001f)
		{
			return false;
		}

		Vector3 currentNormal = m_AimTargetNormal.normalized;

		// 飛行開始時に記録した方向だけを使用する。
		// 飛行中のカメラ回転やプレイヤー回転の影響を受けない。
		Vector3 up = m_LandingMoveUp;
		Vector3 right = m_LandingMoveRight;

		Vector3 moveDirection =
			right * moveInput.x + up * moveInput.y;

		if (moveDirection.sqrMagnitude <= 0.001f)
		{
			return false;
		}

		moveDirection.Normalize();

		Vector3 candidatePoint =
			m_AimTargetPoint +
			moveDirection * m_AirTargetMoveSpeed * Time.deltaTime;

		// 現在の面に沿って候補地点を再判定する。
		Vector3 rayStart =
			candidatePoint + currentNormal * m_SurfaceCheckHeight;

		if (!Physics.Raycast(
			rayStart,
			-currentNormal,
			out RaycastHit hit,
			m_SurfaceCheckDistance,
			m_AimTargetLayers))
		{
			// 面の外に出た場合は移動させない。
			return false;
		}

		Vector3 candidateNormal = hit.normal.normalized;

		// 最初の着地点の法線から一定角度以上変わる面には移動できない。
		float normalAngle =
			Vector3.Angle(m_InitialTrackingNormal, candidateNormal);

		if (normalAngle > m_MaxNormalAngle)
		{
			return false;
		}

		m_AimTargetPoint = hit.point;
		m_AimTargetNormal = candidateNormal;

		shiftedPoint = m_AimTargetPoint;
		shiftedNormal = m_AimTargetNormal;

		UpdateLandingMarker();

		// 予測線は実際の着地点ではなく、現在カメラが狙っている地点へ向ける。
		UpdateAimPredictionLine();

		return true;
	}

	/// <summary>
	/// 着地点マーカーの位置と向きを更新する。
	/// </summary>
	private void UpdateLandingMarker()
	{
		if (m_LandingMarker == null)
		{
			return;
		}

		m_LandingMarker.SetActive(true);

		m_LandingMarker.transform.position =
			m_AimTargetPoint +
			m_AimTargetNormal * m_LandingMarkerOffset;

		m_LandingMarker.transform.rotation =
			Quaternion.FromToRotation(
				Vector3.up,
				m_AimTargetNormal);
	}

	/// <summary>
	/// 着地点の追跡を終了する。
	/// </summary>
	public void EndLandingTracking()
	{
		m_IsTrackingLanding = false;
		HideLandingMarker();
		HideAimPredictionLine();
	}

	/// <summary>
	/// 着地点マーカーを非表示にする。
	/// </summary>
	private void HideLandingMarker()
	{
		if (m_LandingMarker != null)
		{
			m_LandingMarker.SetActive(false);
		}
	}

	/// <summary>
	/// リスポーン時にエイムガイドの状態を初期化する。
	/// </summary>
	public void ResetAimGuideForRespawn()
	{
		// 予備浮上中ならTweenを停止して元の状態に戻す。
		CancelGroundTakeoff();

		// 飛行中の着地点追跡を終了する。
		EndLandingTracking();

		// 移動先の判定状態を初期化する。
		m_CanFall = false;
		m_AimTargetPoint = Vector3.zero;
		m_AimTargetNormal = Vector3.up;
		m_AimMoveDirection = Vector3.zero;

		m_AimPreviewPoint = Vector3.zero;
		m_AimPreviewNormal = Vector3.up;
		m_HasAimPreviewTarget = false;

		// 着地点追跡用の法線も初期化する。
		m_InitialTrackingNormal = Vector3.up;

		// 移動方向を初期化する。
		m_LandingMoveUp = Vector3.up;
		m_LandingMoveRight = Vector3.right;

		// リスポーン直後はレティクルを非表示にする。
		SetConstantReticleActive(false);
	}
}
