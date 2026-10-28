using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using DG.Tweening;

/// <summary>
/// プレイヤーのカメラ演出および視点回転操作を担当するモジュール。
/// FOV変更やカメラシェイク、接地面法線に追従した三人称視点カメラの旋回を管理します。
/// </summary>
public class PlayerCameraEffect : MonoBehaviour, ICameraEffect
{
	/// <summary>
	/// 接地面法線に対するカメラの追従補正モード。
	/// </summary>
	public enum ECameraNormalAlignMode
	{
		/// <summary>法線の変化に合わせてカメラの向き全体(視線含む)を回転させる従来方式</summary>
		FullAlign,
		/// <summary>現在のワールド視線方向を維持したままカメラの天頂軸(Up)のみを法線へ合わせる方式</summary>
		PreserveLookDirection,
		/// <summary>基準Upから接地面法線へ一定の割合・最大角度まで部分的に傾ける方式</summary>
		PartialAlign,
		/// <summary>自動での法線追従を行わず、視点リセット(Rキー)時のみ法線へ合わせる方式</summary>
		None
	}

	[Header("Cinemachine Camera")]
	[SerializeField] private CinemachineCamera m_CinemachineCamera;

	[Header("カメラピボット(未指定時はCinemachineCameraの親オブジェクト)")]
	[SerializeField] private Transform m_CameraPivot;

	[Header("カメラシェイク")]
	[SerializeField] private CinemachineImpulseSource m_ImpulseSource;

	[Header("通常時FOV")]
	[SerializeField] private float m_NormalFOV;

	[Header("高速移動時FOV")]
	[SerializeField] private float m_FallFOV;

	[Header("FOV変更時間")]
	[SerializeField] private float m_FOVChangeDuration;

	[Header("視点操作感度・角度制限")]
	[SerializeField] private float m_MouseSensitivity;
	[SerializeField] private float m_MinPitchAngle;
	[SerializeField] private float m_MaxPitchAngle;

	[Header("接地面法線への追従モード設定")]
	// 想定設定値: PreserveLookDirection (視線維持) 等を切り替えて検証
	[SerializeField] private ECameraNormalAlignMode m_NormalAlignMode;

	// 想定設定値: false (落下・飛行移動中はカメラの法線追従を止め、着地後に追従させる場合はfalse)
	[SerializeField] private bool m_AlignWhileFalling;

	// 想定設定値: false (地上移動入力を行っている間のみ法線追従させる場合はtrue)
	[SerializeField] private bool m_AlignOnlyWhenMoving;

	[Header("接地面法線への姿勢追従速度")]
	[SerializeField] private float m_NormalAlignSpeed;

	[Header("カメラ法線追従の最大角速度(度/秒: 初速スパイク防止)")]
	[SerializeField] private float m_MaxNormalDegreesPerSecond;

	[Header("部分補正(PartialAlign)設定")]
	// 想定設定値: 0.5f (0.0〜1.0: ワールド上方向から接地面法線へ寄せる割合)
	[SerializeField, Range(0f, 1f)] private float m_PartialAlignRatio;

	// 想定設定値: 45.0f (ワールド上方向からカメラを傾ける最大角度)
	[SerializeField] private float m_MaxPartialTiltAngle;

	[Header("カメラピボット位置スムース時間(衝撃緩和SmoothDamp)")]
	[SerializeField] private float m_PositionSmoothTime;

	[Header("FOV変更")]
	[SerializeField]
	private AnimationCurve m_FOVCurve;

	[Header("視点リセット")]
	// 想定設定値: 0.4f
	[SerializeField] private float m_ViewResetDuration;

	private Coroutine m_FOVCoroutine;
	private PlayerInputReceiver m_InputReceiver;
	private IGroundDetector m_GroundDetector;
	private IGravityMover m_GravityMover;
	private NetworkObject m_NetworkObject;
	private Vector3 m_PositionVelocity;
	private Vector3 m_CurrentUp = Vector3.up;
	private Vector3 m_PlanarForward = Vector3.forward;
	private float m_Pitch = 12.0f;
	private bool m_IsInitialized;

	// 視点リセット中かどうか
	private bool m_IsResettingView = false;

	// 視点リセット用Tween
	private Sequence m_ViewResetTween;

	private void Awake()
	{
		m_InputReceiver = GetComponent<PlayerInputReceiver>();
		m_GroundDetector = GetComponent<IGroundDetector>();
		m_GravityMover = GetComponent<IGravityMover>();
		m_NetworkObject = GetComponent<NetworkObject>();

		if (m_CameraPivot == null && m_CinemachineCamera != null)
		{
			m_CameraPivot = m_CinemachineCamera.transform.parent;
		}
	}

	private void Start()
	{
		// マルチプレイ環境下では自端末(IsOwner)のみカメラピボットを独立・回転制御
		if (m_NetworkObject != null && !m_NetworkObject.IsOwner)
		{
			return;
		}

		if (m_CameraPivot != null)
		{
			// 親(Player)の回転に引きずられないよう階層を独立化
			m_CameraPivot.SetParent(null);

			m_CurrentUp = (m_GroundDetector != null && m_GroundDetector.CurrentGroundNormal.sqrMagnitude > 0.001f)
				? m_GroundDetector.CurrentGroundNormal
				: transform.up;

			m_PlanarForward = Vector3.ProjectOnPlane(transform.forward, m_CurrentUp);
			if (m_PlanarForward.sqrMagnitude < 0.001f)
			{
				m_PlanarForward = Vector3.ProjectOnPlane(Vector3.forward, m_CurrentUp);
				if (m_PlanarForward.sqrMagnitude < 0.001f)
				{
					m_PlanarForward = Vector3.Cross(m_CurrentUp, Vector3.right);
				}
			}
			m_PlanarForward.Normalize();

			m_Pitch = 12.0f;
			m_IsInitialized = true;
		}
	}

	private void LateUpdate()
	{
		if (!m_IsInitialized || m_CameraPivot == null)
		{
			return;
		}

		// プレイヤーの位置へ追従: SmoothDampによる滑らかなスプリングダンピングで角の衝撃を完全吸収
		float smoothTime = m_PositionSmoothTime > 0f ? m_PositionSmoothTime : 0.08f;
		m_CameraPivot.position = Vector3.SmoothDamp(m_CameraPivot.position, transform.position, ref m_PositionVelocity, smoothTime);

		// 視点リセット中でなければ、通常のカメラ制御を行う
		if (!m_IsResettingView)
		{
			if (ShouldAlignCameraNormal())
			{
				UpdateCameraNormalAlignment();
			}

			// マウス・スティック操作によるYaw(水平旋回)・Pitch(垂直仰角)の更新
			if (m_InputReceiver != null && Cursor.lockState == CursorLockMode.Locked)
			{
				Vector2 look = m_InputReceiver.LookInput;

				// 水平旋回: 現在のUp(法線)を軸として接平面上の前方ベクトルを回転
				float yawAngle = look.x * m_MouseSensitivity;
				m_PlanarForward = (Quaternion.AngleAxis(yawAngle, m_CurrentUp) * m_PlanarForward).normalized;

				// 垂直仰角: 接平面に対するチルト角を更新
				m_Pitch -= look.y * m_MouseSensitivity;
				m_Pitch = Mathf.Clamp(m_Pitch, m_MinPitchAngle, m_MaxPitchAngle);
			}
		}

		// 水平基底姿勢の決定
		Quaternion baseRotation =
			Quaternion.LookRotation(
				m_PlanarForward,
				m_CurrentUp);

		// Pitch角を合成してカメラを回転
		m_CameraPivot.rotation =
			baseRotation * Quaternion.Euler(m_Pitch, 0.0f, 0.0f);
	}

	/// <summary>
	/// 現フレームで接地面法線へのカメラ追従を実行すべきかを判定します。
	/// </summary>
	private bool ShouldAlignCameraNormal()
	{
		if (m_NormalAlignMode == ECameraNormalAlignMode.None)
		{
			return false;
		}

		if (!m_AlignWhileFalling)
		{
			bool isFalling = m_GravityMover != null && m_GravityMover.IsFalling;
			bool isGrounded = m_GroundDetector == null || m_GroundDetector.IsGrounded;
			if (isFalling || !isGrounded)
			{
				return false;
			}
		}

		if (m_AlignOnlyWhenMoving)
		{
			if (m_InputReceiver == null || m_InputReceiver.MoveInput.sqrMagnitude < 0.001f)
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// 選択された追従モードに応じてカメラの天頂軸(m_CurrentUp)および前方基準(m_PlanarForward)・仰角(m_Pitch)を更新します。
	/// </summary>
	private void UpdateCameraNormalAlignment()
	{
		Vector3 rawTargetUp = (m_GroundDetector != null && m_GroundDetector.CurrentGroundNormal.sqrMagnitude > 0.001f)
			? m_GroundDetector.CurrentGroundNormal.normalized
			: transform.up;

		Vector3 targetUp = rawTargetUp;

		if (m_NormalAlignMode == ECameraNormalAlignMode.PartialAlign)
		{
			float ratio = m_PartialAlignRatio > 0f ? m_PartialAlignRatio : 0.5f;
			float maxTilt = m_MaxPartialTiltAngle > 0f ? m_MaxPartialTiltAngle : 45.0f;
			Vector3 blendedUp = Vector3.Slerp(Vector3.up, rawTargetUp, ratio).normalized;
			targetUp = Vector3.RotateTowards(Vector3.up, blendedUp, maxTilt * Mathf.Deg2Rad, 0f).normalized;
		}

		// カメラのUpベクトル追従: RotateTowardsによる角速度上限で初速スパイクを排除しふわりと追従
		float maxDegPerSec = m_MaxNormalDegreesPerSecond > 0f ? m_MaxNormalDegreesPerSecond : 90.0f;
		float alignSpeed = m_NormalAlignSpeed > 0f ? m_NormalAlignSpeed : 5.0f;
		Vector3 slerpedUp = Vector3.Slerp(m_CurrentUp, targetUp, alignSpeed * Time.deltaTime);
		Vector3 newUp = Vector3.RotateTowards(m_CurrentUp, slerpedUp, maxDegPerSec * Mathf.Deg2Rad * Time.deltaTime, 0f).normalized;

		if (m_NormalAlignMode == ECameraNormalAlignMode.PreserveLookDirection)
		{
			// 現在のカメラのワールド視線ベクトルを算出
			Quaternion currentBaseRotation = Quaternion.LookRotation(m_PlanarForward, m_CurrentUp);
			Vector3 currentWorldLookDir = (currentBaseRotation * Quaternion.Euler(m_Pitch, 0.0f, 0.0f)) * Vector3.forward;

			// ワールド視線ベクトルを新しい法線の接平面へ投影し、見ている方向を維持
			Vector3 projectedForward = Vector3.ProjectOnPlane(currentWorldLookDir, newUp);
			if (projectedForward.sqrMagnitude > 0.001f)
			{
				m_PlanarForward = projectedForward.normalized;

				// 新しい接平面に対するワールド視線ベクトルの仰角(Pitch)を逆算して引き継ぐ
				float sinPitch = Mathf.Clamp(Vector3.Dot(currentWorldLookDir, newUp), -1.0f, 1.0f);
				float newPitch = -Mathf.Asin(sinPitch) * Mathf.Rad2Deg;
				m_Pitch = Mathf.Clamp(newPitch, m_MinPitchAngle, m_MaxPitchAngle);
			}
			else
			{
				// 視線と新しい法線がほぼ平行(真上・真下)の特異点ではロール回転で前方基準を補間
				Quaternion upRotation = Quaternion.FromToRotation(m_CurrentUp, newUp);
				m_PlanarForward = (upRotation * m_PlanarForward).normalized;
			}

			m_CurrentUp = newUp;
		}
		else
		{
			// FullAlign / PartialAlign: 法線の変化に合わせて水平視線ベクトル(m_PlanarForward)を回転追従
			Quaternion upRotation = Quaternion.FromToRotation(m_CurrentUp, newUp);
			m_PlanarForward = (upRotation * m_PlanarForward).normalized;
			m_CurrentUp = newUp;
		}
	}

	private void OnDestroy()
	{
		// 独立させたピボットオブジェクトのクリーンアップ
		if (m_CameraPivot != null && m_CameraPivot.parent == null)
		{
			Destroy(m_CameraPivot.gameObject);
		}
	}

	/// <summary>
	/// 高速移動開始時のカメラ演出。
	/// FOVを広げ、カメラシェイクを発生させます。
	/// </summary>
	public void PlayFallEffect()
	{
		// FOVを高速移動用へ変更
		StartFOVChange(m_FallFOV);

		// カメラシェイク
		PlayCameraShake();
	}

	/// <summary>
	/// 外部から呼び出されたとき、
	/// 高速移動時のFOVを通常値へ戻します。
	/// </summary>
	public void RestoreFOV()
	{
		StartFOVChange(m_NormalFOV);
	}

	/// <summary>
	/// カメラシェイクを発生させます。
	/// </summary>
	private void PlayCameraShake()
	{
		if (m_ImpulseSource == null)
		{
			return;
		}

		m_ImpulseSource.GenerateImpulse();
	}

	/// <summary>
	/// 指定したFOVまで徐々に変更します。
	/// </summary>
	private void StartFOVChange(float targetFOV)
	{
		if (m_CinemachineCamera == null)
		{
			return;
		}

		if (m_FOVCoroutine != null)
		{
			StopCoroutine(m_FOVCoroutine);
		}

		m_FOVCoroutine =
			StartCoroutine(ChangeFOVRoutine(targetFOV));
	}

	private System.Collections.IEnumerator ChangeFOVRoutine(
		float targetFOV)
	{
		float startFOV =
			m_CinemachineCamera.Lens.FieldOfView;

		float elapsedTime = 0.0f;

		while (elapsedTime < m_FOVChangeDuration)
		{
			elapsedTime += Time.deltaTime;

			float normalizedTime =
				Mathf.Clamp01(
					elapsedTime / m_FOVChangeDuration);

			float curveValue =
				m_FOVCurve.Evaluate(normalizedTime);

			m_CinemachineCamera.Lens.FieldOfView =
				Mathf.Lerp(
					startFOV,
					targetFOV,
					curveValue);

			yield return null;
		}

		m_CinemachineCamera.Lens.FieldOfView = targetFOV;
		m_FOVCoroutine = null;
	}

	/// <summary>
	/// プレイヤーの前方向に視点を滑らかにリセットします。
	/// </summary>
	public void ResetView()
	{
		// 初期化前なら何もしない
		if (!m_IsInitialized)
		{
			return;
		}

		// 接地面の法線を取得
		Vector3 upDirection =
			(m_GroundDetector != null &&
			 m_GroundDetector.CurrentGroundNormal.sqrMagnitude > 0.001f)
			? m_GroundDetector.CurrentGroundNormal.normalized
			: transform.up;

		// プレイヤーの前方向を接地面に沿わせる
		Vector3 forwardDirection =
			Vector3.ProjectOnPlane(
				transform.forward,
				upDirection);

		// 前方向が取得できない場合は現在の視線方向を利用
		if (forwardDirection.sqrMagnitude < 0.001f)
		{
			forwardDirection =
				Vector3.ProjectOnPlane(
					m_PlanarForward,
					upDirection);
		}

		if (forwardDirection.sqrMagnitude < 0.001f)
		{
			return;
		}

		forwardDirection.Normalize();

		// 以前のリセット処理が残っていれば停止
		if (m_ViewResetTween != null &&
			m_ViewResetTween.IsActive())
		{
			m_ViewResetTween.Kill();
		}

		// リセット中は通常のカメラ制御を停止
		m_IsResettingView = true;

		float resetDuration = m_ViewResetDuration > 0f ? m_ViewResetDuration : 0.4f;

		// 現在の値から目標値まで滑らかに補間する
		m_ViewResetTween = DOTween.Sequence();

		m_ViewResetTween.Join(
			DOTween.To(
				() => m_CurrentUp,
				value => m_CurrentUp = value,
				upDirection,
				resetDuration));

		m_ViewResetTween.Join(
			DOTween.To(
				() => m_PlanarForward,
				value => m_PlanarForward = value,
				forwardDirection,
				resetDuration));

		m_ViewResetTween.Join(
			DOTween.To(
				() => m_Pitch,
				value => m_Pitch = value,
				12.0f,
				resetDuration));

		// 終わりに向けてゆっくり減速する
		m_ViewResetTween.SetEase(Ease.OutCubic);

		m_ViewResetTween.OnComplete(() =>
		{
			// 最終的な値を正確に設定
			m_CurrentUp = upDirection;
			m_PlanarForward = forwardDirection;
			m_Pitch = 12.0f;

			// 通常のカメラ制御を再開
			m_IsResettingView = false;
		});
	}
}
