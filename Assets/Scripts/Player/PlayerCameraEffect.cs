using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using DG.Tweening;

/// <summary>
/// プレイヤーのカメラ演出および視点回転操作を担当するモジュール。
/// FOV変更やカメラシェイク、着地時の視線維持法線補正および地上・エッジ歩行時の法線追従旋回を管理します。
/// </summary>
public class PlayerCameraEffect : MonoBehaviour, ICameraEffect
{
	private const float m_SteepBlendStartDot = 0.85f;
	private const float m_SteepBlendEndDot = 0.98f;

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

	[Header("着地時のカメラ法線追従設定")]
	// 想定設定値: true (true: 着地後に移動入力が入るまで法線追従を待機 / false: 着地直後から法線追従を開始)
	[SerializeField] private bool m_AlignOnlyWhenMoving;

	[Header("接地面法線への姿勢追従速度")]
	[SerializeField] private float m_NormalAlignSpeed;

	[Header("カメラ法線追従の最大角速度(度/秒: 初速スパイク防止)")]
	[SerializeField] private float m_MaxNormalDegreesPerSecond;

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
	private Vector3 m_LandedSurfaceNormal = Vector3.up;
	private float m_Pitch = 12.0f;
	private float m_CameraLocalPitchOffset;
	private bool m_IsInitialized;

	// 着地後の視線維持法線補正フェーズ中かどうか
	private bool m_IsLandingAlignPending;

	// 着地後の法線補正が開始済みかどうか(m_AlignOnlyWhenMoving有効時の入力トリガー管理用)
	private bool m_IsLandingAlignStarted;

	// 直前まで落下(飛行)移動中だったかどうか
	private bool m_WasFalling;

	// 視点リセット中かどうか
	private bool m_IsResettingView;

	// 視点リセット用Tween
	private Sequence m_ViewResetTween;

	private void Awake()
	{
		m_InputReceiver = GetComponent<PlayerInputReceiver>();
		m_GroundDetector = GetComponent<IGroundDetector>();
		m_GravityMover = GetComponent<IGravityMover>();
		m_NetworkObject = GetComponent<NetworkObject>();

		if (m_CinemachineCamera != null)
		{
			m_CameraLocalPitchOffset = Mathf.DeltaAngle(0.0f, m_CinemachineCamera.transform.localEulerAngles.x);
			if (m_CameraPivot == null)
			{
				m_CameraPivot = m_CinemachineCamera.transform.parent;
			}
		}
	}

	private void Start()
	{
		// マルチプレイ環境下では自端末(IsOwner)のみカメラピボットを独立・回転制御
		if (m_NetworkObject != null && !m_NetworkObject.IsOwner)
		{
			return;
		}

		if (m_GravityMover != null)
		{
			m_GravityMover.OnLanded += HandleLanded;
		}

		if (m_CameraPivot != null)
		{
			// 親(Player)の回転に引きずられないよう階層を独立化
			m_CameraPivot.SetParent(null);

			m_CurrentUp = GetCurrentGroundOrTransformUp();

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

			m_Pitch = ClampPitchWithOffset(12.0f);
			m_IsInitialized = true;
		}
	}

	private void LateUpdate()
	{
		if (!m_IsInitialized || m_CameraPivot == null)
		{
			return;
		}

		// プレイヤーの位置へ追従: SmoothDampによる滑らかなスプリングダンピングで角の衝撃を吸収
		float smoothTime = m_PositionSmoothTime > 0f ? m_PositionSmoothTime : 0.08f;
		m_CameraPivot.position = Vector3.SmoothDamp(m_CameraPivot.position, transform.position, ref m_PositionVelocity, smoothTime);

		// 視点リセット中でなければ、通常のカメラ制御を行う
		if (!m_IsResettingView)
		{
			bool isFalling = m_GravityMover != null && m_GravityMover.IsFalling;
			bool isGrounded = m_GroundDetector == null || m_GroundDetector.IsGrounded;

			if (isFalling)
			{
				// 飛行移動中はカメラの法線追従を行わず視界を安定化
				m_WasFalling = true;
				m_IsLandingAlignPending = false;
				m_IsLandingAlignStarted = false;
			}
			else if (isGrounded)
			{
				// 空中ブレーキ後などOnLandedを経由せず接地した場合のフォールバック検知
				if (m_WasFalling)
				{
					BeginLandingAlign(GetCurrentGroundOrTransformUp());
				}

				UpdateCameraNormalAlignment();
			}

			// マウス・スティック操作によるYaw(水平旋回)・Pitch(垂直仰角)の更新
			if (m_InputReceiver != null && Cursor.lockState == CursorLockMode.Locked)
			{
				Vector2 look = m_InputReceiver.LookInput;

				// 水平旋回: 現在のUp(法線)を軸として接平面上の前方ベクトルを回転
				float yawAngle = look.x * m_MouseSensitivity;
				m_PlanarForward = (Quaternion.AngleAxis(yawAngle, m_CurrentUp) * m_PlanarForward).normalized;

				// 垂直仰角: 子カメラのローカルオフセット角を加味して実効仰角を設定範囲内へクランプ
				m_Pitch -= look.y * m_MouseSensitivity;
				m_Pitch = ClampPitchWithOffset(m_Pitch);
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
	/// 指定された接地法線に対して、上下角の特異点や反転を含まない移動用の前方・右方単位ベクトルを取得します。
	/// </summary>
	/// <param name="groundNormal">現在の接地法線ベクトル</param>
	/// <param name="forwardOnPlane">接平面上の前方単位ベクトル</param>
	/// <param name="rightOnPlane">接平面上の右方単位ベクトル</param>
	/// <returns>有効な移動基底を算出できた場合はtrue</returns>
	public bool TryGetMovementBasis(Vector3 groundNormal, out Vector3 forwardOnPlane, out Vector3 rightOnPlane)
	{
		forwardOnPlane = Vector3.zero;
		rightOnPlane = Vector3.zero;

		if (!m_IsInitialized || groundNormal.sqrMagnitude < 0.0001f)
		{
			return false;
		}

		Vector3 normal = groundNormal.normalized;

		if (!m_IsLandingAlignPending)
		{
			// 通常地上・エッジ歩行フェーズ: Pitchを含まない水平前方ベクトル(m_PlanarForward)を現在の接地法線へ回転投影
			Quaternion upRotation = CreateNormalAlignmentRotation(m_CurrentUp, normal);
			forwardOnPlane = Vector3.ProjectOnPlane(upRotation * m_PlanarForward, normal);
			if (forwardOnPlane.sqrMagnitude < 0.0001f)
			{
				return false;
			}

			forwardOnPlane.Normalize();
			rightOnPlane = Vector3.Cross(normal, forwardOnPlane).normalized;
			return true;
		}

		// 着地後の視線維持補正フェーズ: 実カメラの視線方向をベースにしつつ、垂直着地等の特異点ではカメラ上下軸へ連続ブレンド
		Quaternion currentBaseRotation = Quaternion.LookRotation(m_PlanarForward, m_CurrentUp);
		Quaternion currentEffectiveRotation = currentBaseRotation * Quaternion.Euler(m_Pitch + m_CameraLocalPitchOffset, 0.0f, 0.0f);
		Vector3 camForward = currentEffectiveRotation * Vector3.forward;
		Vector3 camUp = currentEffectiveRotation * Vector3.up;
		Vector3 camRight = currentEffectiveRotation * Vector3.right;

		forwardOnPlane = ComputeBlendedForwardOnPlane(camForward, camUp, normal);
		if (forwardOnPlane.sqrMagnitude < 0.0001f)
		{
			return false;
		}

		forwardOnPlane.Normalize();

		rightOnPlane = Vector3.ProjectOnPlane(camRight, normal);
		if (rightOnPlane.sqrMagnitude < 0.01f)
		{
			float upSign = Vector3.Dot(camUp, normal) < 0.0f ? -1.0f : 1.0f;
			rightOnPlane = Vector3.Cross(normal, forwardOnPlane) * upSign;
		}

		if (rightOnPlane.sqrMagnitude < 0.0001f)
		{
			return false;
		}

		rightOnPlane.Normalize();
		return true;
	}

	private void HandleLanded()
	{
		if (!m_IsInitialized)
		{
			return;
		}

		BeginLandingAlign(GetCurrentGroundOrTransformUp());
	}

	/// <summary>
	/// 着地後の視線維持法線補正フェーズを開始します。
	/// </summary>
	/// <param name="landedNormal">着地した面の法線ベクトル</param>
	private void BeginLandingAlign(Vector3 landedNormal)
	{
		m_WasFalling = false;
		m_LandedSurfaceNormal = landedNormal;

		// すでに現在のカメラUpと着地面法線がほぼ一致している場合は着地補正を行わず通常エッジ追従へ移行
		if (Vector3.Angle(m_CurrentUp, landedNormal) <= 0.5f)
		{
			Quaternion snapRotation = CreateNormalAlignmentRotation(m_CurrentUp, landedNormal);
			m_PlanarForward = Vector3.ProjectOnPlane(snapRotation * m_PlanarForward, landedNormal).normalized;
			m_CurrentUp = landedNormal;
			m_IsLandingAlignPending = false;
			m_IsLandingAlignStarted = false;
			return;
		}

		m_IsLandingAlignPending = true;
		m_IsLandingAlignStarted = !m_AlignOnlyWhenMoving;
	}

	/// <summary>
	/// 着地後フェーズ(視線維持投影)および地上・エッジ歩行フェーズ(法線一体回転)に応じてカメラ姿勢を更新します。
	/// </summary>
	private void UpdateCameraNormalAlignment()
	{
		Vector3 targetUp = GetCurrentGroundOrTransformUp();

		if (m_IsLandingAlignPending)
		{
			// 移動入力待機オプションが有効な場合、移動入力が入るまで法線追従を待機
			if (!m_IsLandingAlignStarted)
			{
				if (m_InputReceiver != null && m_InputReceiver.MoveInput.sqrMagnitude > 0.001f)
				{
					m_IsLandingAlignStarted = true;
				}
				else
				{
					return;
				}
			}

			// 着地補正の途中で歩いて角・エッジを跨ぎ、着地した面から大きく法線が変化した場合はエッジ追従へ即時移行
			if (Vector3.Angle(m_LandedSurfaceNormal, targetUp) > 30.0f)
			{
				m_IsLandingAlignPending = false;
				m_IsLandingAlignStarted = false;
			}
		}

		// カメラのUpベクトル追従: RotateTowardsによる角速度上限で初速スパイクを排除し滑らかに追従
		float maxDegPerSec = m_MaxNormalDegreesPerSecond > 0f ? m_MaxNormalDegreesPerSecond : 90.0f;
		float alignSpeed = m_NormalAlignSpeed > 0f ? m_NormalAlignSpeed : 5.0f;
		Vector3 slerpedUp = Vector3.Slerp(m_CurrentUp, targetUp, alignSpeed * Time.deltaTime);
		Vector3 newUp = Vector3.RotateTowards(m_CurrentUp, slerpedUp, maxDegPerSec * Mathf.Deg2Rad * Time.deltaTime, 0f).normalized;

		if (m_IsLandingAlignPending)
		{
			// 着地後の法線補正フェーズ: 実カメラのワールド視線ベクトルを維持したまま天頂軸(Up)のみを着地面法線へ合わせる
			Quaternion currentBaseRotation = Quaternion.LookRotation(m_PlanarForward, m_CurrentUp);
			Quaternion currentEffectiveRotation = currentBaseRotation * Quaternion.Euler(m_Pitch + m_CameraLocalPitchOffset, 0.0f, 0.0f);
			Vector3 currentWorldLookDir = currentEffectiveRotation * Vector3.forward;
			Vector3 currentWorldUpDir = currentEffectiveRotation * Vector3.up;

			Vector3 blendedForward = ComputeBlendedForwardOnPlane(currentWorldLookDir, currentWorldUpDir, newUp);
			if (blendedForward.sqrMagnitude > 0.0001f)
			{
				m_PlanarForward = blendedForward.normalized;
			}
			else
			{
				Quaternion upRotation = CreateNormalAlignmentRotation(m_CurrentUp, newUp);
				m_PlanarForward = Vector3.ProjectOnPlane(upRotation * m_PlanarForward, newUp).normalized;
			}

			// 新しい接平面に対する実カメラ視線ベクトルの仰角(Pitch)を逆算し、オフセットを差し引いて引き継ぐ
			float sinPitch = Mathf.Clamp(Vector3.Dot(currentWorldLookDir, newUp), -1.0f, 1.0f);
			float newEffectivePitch = -Mathf.Asin(sinPitch) * Mathf.Rad2Deg;
			m_Pitch = ClampPitchWithOffset(newEffectivePitch - m_CameraLocalPitchOffset);

			m_CurrentUp = newUp;

			// 着地面法線への補正が完了したら地上・エッジ歩行フェーズへ移行
			if (Vector3.Angle(m_CurrentUp, targetUp) <= 0.5f)
			{
				m_IsLandingAlignPending = false;
				m_IsLandingAlignStarted = false;
			}
		}
		else
		{
			// 地上・エッジ歩行フェーズ: 法線の変化に合わせて水平視線ベクトル(m_PlanarForward)を一体として回転追従
			Quaternion upRotation = CreateNormalAlignmentRotation(m_CurrentUp, newUp);
			m_PlanarForward = Vector3.ProjectOnPlane(upRotation * m_PlanarForward, newUp).normalized;
			m_CurrentUp = newUp;
		}
	}

	/// <summary>
	/// 子カメラのローカルPitchオフセットを加味し、実カメラの仰角が設定範囲に収まるようクランプします。
	/// </summary>
	private float ClampPitchWithOffset(float pitch)
	{
		bool hasConfiguredRange = Mathf.Abs(m_MinPitchAngle) > 0.001f || Mathf.Abs(m_MaxPitchAngle) > 0.001f;
		float minEffectivePitch = hasConfiguredRange ? m_MinPitchAngle : -70.0f;
		float maxEffectivePitch = hasConfiguredRange ? m_MaxPitchAngle : 70.0f;

		return Mathf.Clamp(
			pitch,
			minEffectivePitch - m_CameraLocalPitchOffset,
			maxEffectivePitch - m_CameraLocalPitchOffset);
	}

	/// <summary>
	/// 180度逆向きの特異点を回避しながら、2つの法線間の回転クォータニオンを生成します。
	/// </summary>
	private Quaternion CreateNormalAlignmentRotation(Vector3 fromUp, Vector3 toUp)
	{
		if (Vector3.Dot(fromUp, toUp) < -0.999f)
		{
			Vector3 rightAxis = Vector3.Cross(fromUp, m_PlanarForward);
			if (rightAxis.sqrMagnitude < 0.0001f)
			{
				rightAxis = Vector3.Cross(fromUp, Vector3.right);
				if (rightAxis.sqrMagnitude < 0.0001f)
				{
					rightAxis = Vector3.Cross(fromUp, Vector3.forward);
				}
			}

			return Quaternion.AngleAxis(180.0f, rightAxis.normalized);
		}

		return Quaternion.FromToRotation(fromUp, toUp);
	}

	/// <summary>
	/// 視線ベクトルと法線が平行に近づく特異点領域において、カメラ上下軸の平面投影へ滑らかにブレンドした前方ベクトルを返します。
	/// </summary>
	private static Vector3 ComputeBlendedForwardOnPlane(Vector3 lookForward, Vector3 lookUp, Vector3 planeNormal)
	{
		float normalDot = Vector3.Dot(lookForward, planeNormal);
		float absDot = Mathf.Abs(normalDot);

		Vector3 forwardFromLook = Vector3.ProjectOnPlane(lookForward, planeNormal);
		Vector3 upReference = normalDot <= 0.0f ? lookUp : -lookUp;
		Vector3 forwardFromUp = Vector3.ProjectOnPlane(upReference, planeNormal);

		float blendWeight = Mathf.InverseLerp(m_SteepBlendStartDot, m_SteepBlendEndDot, absDot);
		if (blendWeight <= 0.0f && forwardFromLook.sqrMagnitude > 0.0001f)
		{
			return forwardFromLook.normalized;
		}

		if (forwardFromUp.sqrMagnitude < 0.0001f)
		{
			return forwardFromLook.sqrMagnitude > 0.0001f ? forwardFromLook.normalized : Vector3.zero;
		}

		Vector3 normalizedUpProj = forwardFromUp.normalized;
		if (blendWeight >= 1.0f || forwardFromLook.sqrMagnitude <= 0.0001f)
		{
			return normalizedUpProj;
		}

		Vector3 normalizedLookProj = forwardFromLook.normalized;
		float signedAngle = Vector3.SignedAngle(normalizedLookProj, normalizedUpProj, planeNormal);
		if (Mathf.Abs(signedAngle) > 179.0f)
		{
			signedAngle = 180.0f;
		}

		return (Quaternion.AngleAxis(signedAngle * blendWeight, planeNormal) * normalizedLookProj).normalized;
	}

	private Vector3 GetCurrentGroundOrTransformUp()
	{
		return (m_GroundDetector != null && m_GroundDetector.CurrentGroundNormal.sqrMagnitude > 0.001f)
			? m_GroundDetector.CurrentGroundNormal.normalized
			: transform.up;
	}

	private void OnDestroy()
	{
		if (m_GravityMover != null)
		{
			m_GravityMover.OnLanded -= HandleLanded;
		}

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
		Vector3 upDirection = GetCurrentGroundOrTransformUp();

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

		// 視点リセット実行時は着地補正待機状態を解除
		m_IsLandingAlignPending = false;
		m_IsLandingAlignStarted = false;

		// リセット中は通常のカメラ制御を停止
		m_IsResettingView = true;

		float resetDuration = m_ViewResetDuration > 0f ? m_ViewResetDuration : 0.4f;
		float targetResetPitch = ClampPitchWithOffset(12.0f);

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
				targetResetPitch,
				resetDuration));

		// 終わりに向けてゆっくり減速する
		m_ViewResetTween.SetEase(Ease.OutCubic);

		m_ViewResetTween.OnComplete(() =>
		{
			// 最終的な値を正確に設定
			m_CurrentUp = upDirection;
			m_PlanarForward = forwardDirection;
			m_Pitch = targetResetPitch;

			// 通常のカメラ制御を再開
			m_IsResettingView = false;
		});
	}
}
