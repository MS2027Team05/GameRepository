using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// プレイヤー全体の「状態管理(ステート)」と「各専門モジュールへの命令ルーティング」を担当するクラス。
/// 自ら物理計算や描画は行わず、インターフェース経由で各モジュールに委託します。
/// </summary>
public class PlayerController : NetworkBehaviour
{
	[Header("自端末専用オブジェクト")]
	[FormerlySerializedAs("playerCameraObject")]
	[SerializeField] private GameObject m_PlayerCameraObject;
	[FormerlySerializedAs("inputReceiver")]
	[SerializeField] private PlayerInputReceiver m_InputReceiver;
	[FormerlySerializedAs("CinemachineCameraTransform")]
	[SerializeField] private Transform m_CinemachineCameraTransform;

	// 各担当者が実装するインターフェース・コンポーネント参照
	private IGravityMover m_GravityMover;
	private IAimGuide m_AimGuide;
	private IBallCarrier m_BallCarrier;
	private ICameraEffect m_CameraEffect;

	// プレイヤーの内部状態
	public enum EPlayerState { Idle, Aiming, Falling, Stunned }
	public EPlayerState CurrentState { get; private set; } = EPlayerState.Idle;

	private Coroutine m_StunCoroutine;

	// 今回の移動中にエイムを使用したか
	private bool m_UsedMidFallAim;

	// 現在、移動中エイムを行っているか
	private bool m_IsMidFallAiming;

	private void Awake()
	{
		// 各インターフェースおよび演出コンポーネントの取得
		m_GravityMover = GetComponent<IGravityMover>();
		m_AimGuide = GetComponent<IAimGuide>();
		m_BallCarrier = GetComponent<IBallCarrier>();
		m_CameraEffect = GetComponent<ICameraEffect>();

		if (m_GravityMover == null)
		{
			Debug.LogError("[PlayerController] IGravityMover (GravityMover) が見つかりません。PlayerオブジェクトにGravityMoverをアタッチしてください。");
		}
		if (m_AimGuide == null)
		{
			Debug.LogWarning("[PlayerController] IAimGuide (PlayerAimGuide) が見つかりません。");
		}

		if (m_InputReceiver == null)
		{
			m_InputReceiver = GetComponent<PlayerInputReceiver>();
		}
	}

	public override void OnNetworkSpawn()
	{
		// 他人の画面なら、自機カメラ・入力を切って処理を終える
		if (!IsOwner)
		{
			if (m_PlayerCameraObject != null) m_PlayerCameraObject.SetActive(false);
			if (m_InputReceiver != null) m_InputReceiver.enabled = false;
			m_AimGuide?.ShowAimGuide(false);
			return;
		}

		// 自分のキャラならカメラを有効化し、入力をバインドする
		if (m_PlayerCameraObject != null) m_PlayerCameraObject.SetActive(true);
		BindInputEvents();
		PlayerInputReceiver.SetCursorLocked(true);
	}

	private void BindInputEvents()
	{
		if (m_InputReceiver == null) return;

		m_InputReceiver.OnAimTogglePressed += HandleAimToggle;
		m_InputReceiver.OnConfirmPressed += HandleConfirm;
		m_InputReceiver.OnBrakePressed += HandleBrake;
		m_InputReceiver.OnShootPressed += HandleShoot;
		m_InputReceiver.OnViewResetPressed += HandleViewReset;
	}

	private void UnbindInputEvents()
	{
		if (m_InputReceiver == null) return;

		m_InputReceiver.OnAimTogglePressed -= HandleAimToggle;
		m_InputReceiver.OnConfirmPressed -= HandleConfirm;
		m_InputReceiver.OnBrakePressed -= HandleBrake;
		m_InputReceiver.OnShootPressed -= HandleShoot;
		m_InputReceiver.OnViewResetPressed -= HandleViewReset;
	}

	private void Update()
	{
		if (!IsOwner) return;

		// 着地などで移動が終了したら使用回数をリセット
		if (CurrentState != EPlayerState.Aiming &&
			(m_GravityMover == null || !m_GravityMover.IsFalling))
		{
			m_UsedMidFallAim = false;
			m_IsMidFallAiming = false;
		}

		// エイム中のみ、照準方向を更新する
		if (CurrentState == EPlayerState.Aiming && m_AimGuide != null)
		{
			Vector3 aimDirection = GetAimDirection();

			// 照準・予測線を更新
			m_AimGuide.UpdateAimDirection(aimDirection);

			// 【変更】現在の立ち面に合わせてプレイヤーを回転させる
			RotatePlayerTowardAim(aimDirection);
		}

		// 地上移動入力のルーティング(スタン中やエイム中でない場合に伝達)
		if (m_GravityMover != null && m_InputReceiver != null)
		{
			Vector2 moveInput = (CurrentState == EPlayerState.Aiming || CurrentState == EPlayerState.Stunned)
				? Vector2.zero
				: m_InputReceiver.MoveInput;

			Transform camTransform = Camera.main != null
				? Camera.main.transform
				: (m_PlayerCameraObject != null ? m_PlayerCameraObject.transform : transform);

			m_GravityMover.SetMoveInput(moveInput, camTransform);
		}
	}

	/// <summary>
	/// 現在の立ち面に沿って、プレイヤーを照準方向へ回転させます。
	/// 床・壁・斜面のいずれでも、立ち面の法線を軸に回転します。
	/// </summary>
	private void RotatePlayerTowardAim(Vector3 aimDirection)
	{
		// 現在立っている面の法線を取得
		Vector3 surfaceNormal = m_GravityMover.CurrentGroundNormal;

		// 法線が無効な場合は、プレイヤーの上方向を使用
		if (surfaceNormal.sqrMagnitude < 0.001f)
		{
			surfaceNormal = transform.up;
		}

		surfaceNormal.Normalize();

		// 照準方向を立ち面に沿う平面へ投影
		Vector3 projectedAim = Vector3.ProjectOnPlane(
			aimDirection,
			surfaceNormal
		);

		// 照準が立ち面に対してほぼ垂直なら回転しない
		if (projectedAim.sqrMagnitude < 0.001f)
		{
			return;
		}

		// 現在のプレイヤーの正面方向も、同じ平面へ投影
		Vector3 projectedForward = Vector3.ProjectOnPlane(
			transform.forward,
			surfaceNormal
		);

		// 現在の正面方向が取得できない場合は回転しない
		if (projectedForward.sqrMagnitude < 0.001f)
		{
			return;
		}

		projectedAim.Normalize();
		projectedForward.Normalize();

		// 立ち面の法線を軸に、現在の正面から照準方向までの角度を取得
		float angle = Vector3.SignedAngle(
			projectedForward,
			projectedAim,
			surfaceNormal
		);

		// 立ち面の法線を軸として回転
		Quaternion targetRotation =
			Quaternion.AngleAxis(angle, surfaceNormal) * transform.rotation;

		// 滑らかに回転
		transform.rotation = Quaternion.Slerp(
			transform.rotation,
			targetRotation,
			Time.deltaTime * 100.0f
		);
	}

	/// <summary>
	/// 照準・落下・シュートの基準となるカメラの正面向きを取得します。
	/// </summary>
	private Vector3 GetAimDirection()
	{
		if (Camera.main != null)
		{
			return Camera.main.transform.forward;
		}

		if (m_PlayerCameraObject != null)
		{
			return m_PlayerCameraObject.transform.forward;
		}

		return transform.forward;
	}

	// --- 状態遷移ロジック ---

	private void HandleAimToggle()
	{
		if (CurrentState == EPlayerState.Stunned) return;

		// エイム開始
		if (CurrentState != EPlayerState.Aiming)
		{
			// 移動中の場合
			if (m_GravityMover != null && m_GravityMover.IsFalling)
			{
				// 今回の移動中にすでに使っていたら開始しない
				if (m_UsedMidFallAim) return;

				m_UsedMidFallAim = true;
				m_IsMidFallAiming = true;
				CurrentState = EPlayerState.Aiming;

				// 移動中専用のエイムを開始
				if (m_AimGuide is PlayerAimGuide playerAimGuide)
				{
					playerAimGuide.StartMidFallAimGuide();
				}

				return;
			}

			// 通常時のエイム開始
			m_IsMidFallAiming = false;
			CurrentState = EPlayerState.Aiming;
			m_AimGuide?.ShowAimGuide(true);
		}
		else
		{
			// 移動中エイムのキャンセル
			if (m_IsMidFallAiming)
			{
				if (m_AimGuide is PlayerAimGuide playerAimGuide)
				{
					playerAimGuide.EndMidFallAimGuide();
				}

				m_IsMidFallAiming = false;
				CurrentState = EPlayerState.Falling;
				return;
			}

			// 通常エイムのキャンセル
			CurrentState = EPlayerState.Idle;
			m_AimGuide?.CancelAimGuide();
		}
	}

	private void HandleConfirm()
	{
		if (CurrentState != EPlayerState.Aiming) return;

		// エイム終了前に移動方向を取得
		Vector3 direction = transform.forward;

		if (m_AimGuide is PlayerAimGuide playerAimGuide)
		{
			direction = playerAimGuide.AimMoveDirection;

			if (m_IsMidFallAiming)
			{
				// 移動中専用のエイムを終了
				playerAimGuide.EndMidFallAimGuide();
			}
			else
			{
				// 通常エイムの終了
				m_AimGuide?.EndAimGuideForMovement();
			}
		}
		else
		{
			m_AimGuide?.EndAimGuideForMovement();
		}

		CurrentState = EPlayerState.Falling;
		m_IsMidFallAiming = false;

		m_CameraEffect?.PlayFallEffect();

		// 予測線で指定した方向・着地点へ移動
		m_GravityMover?.StartFalling(direction);
	}

	private void HandleBrake()
	{
		if (CurrentState != EPlayerState.Falling) return;

		CurrentState = EPlayerState.Idle;
		m_GravityMover?.StopFalling();

		// 移動終了時に使用回数をリセット
		m_UsedMidFallAim = false;
		m_IsMidFallAiming = false;
	}

	/// <summary>
	/// 所持中のボールをカメラ正面方向へ射出します。
	/// </summary>
	private void HandleShoot()
	{
		if (m_BallCarrier == null)
		{
			return;
		}

		if (!m_BallCarrier.HasBall)
		{
			return;
		}

		if (m_GravityMover.IsFalling && !m_IsMidFallAiming)

		{
			return;
		}

		Vector3 shootDirection = GetAimDirection();

		m_BallCarrier.Shoot(shootDirection);
	}


	/// <summary>
	/// Rキーによる視点リセット処理。
	/// </summary>
	private void HandleViewReset()
	{
		// GravityMoverがなければ処理しない
		if (m_GravityMover == null)
		{
			return;
		}

		// Idle状態は通常どおりリセット可能
		if (CurrentState == EPlayerState.Idle)
		{
			// Idle状態でも、実際に接地しているか確認
			if (!m_GravityMover.IsGrounded)
			{
				Debug.Log("[ViewReset] 未接地のためリセットを中止しました。");
				return;
			}
		}
		// Falling状態は、接地していて移動も終了している場合のみ許可
		else if (CurrentState == EPlayerState.Falling)
		{
			if (m_GravityMover.IsFalling || !m_GravityMover.IsGrounded)
			{
				Debug.Log("[ViewReset] 移動中または未接地のためリセットを中止しました。");
				return;
			}
		}
		// Aiming・Stunned状態ではリセットしない
		else
		{
			return;
		}

		// カメラ制御スクリプトを取得
		PlayerCameraEffect cameraEffect =
			GetComponent<PlayerCameraEffect>();

		if (cameraEffect == null)
		{
			Debug.LogWarning("[ViewReset] PlayerCameraEffect が見つかりません。");
			return;
		}

		// 視点をプレイヤーの前方向にリセット
		cameraEffect.ResetView();

		Debug.Log("[ViewReset] 視点リセットを実行しました。");
	}

	// --- 外部モジュール連携用メソッド ---

	/// <summary>
	/// ボール所持状態の変化通知を受け取り、速度補正を適用します。
	/// </summary>
	/// <param name="hasBall">ボールを所持している場合はtrue</param>
	public void NotifyBallStateChanged(bool hasBall)
	{
		float speedMultiplier = hasBall ? 0.7f : 1.0f;
		m_GravityMover?.SetSpeedMultiplier(speedMultiplier);
	}

	/// <summary>
	/// 被タックル・衝突によるスタン処理を開始します。
	/// </summary>
	/// <param name="duration">スタン持続時間(秒)</param>
	public void ApplyStun(float duration)
	{
		if (m_StunCoroutine != null)
		{
			StopCoroutine(m_StunCoroutine);
		}
		m_StunCoroutine = StartCoroutine(StunRoutine(duration));
	}

	private IEnumerator StunRoutine(float duration)
	{
		CurrentState = EPlayerState.Stunned;
		m_AimGuide?.ShowAimGuide(false);
		m_GravityMover?.StopFalling();

		yield return new WaitForSeconds(duration);

		CurrentState = EPlayerState.Idle;
		m_StunCoroutine = null;
	}

	public override void OnNetworkDespawn()
	{
		if (IsOwner)
		{
			UnbindInputEvents();
			PlayerInputReceiver.SetCursorLocked(false);
		}
	}

	public override void OnDestroy()
	{
		if (IsOwner)
		{
			PlayerInputReceiver.SetCursorLocked(false);
		}
		base.OnDestroy();
	}
}
