//PlayerController.cs
using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// プレイヤー全体の「状態管理(ステート)」と「各専門モジュールへの命令ルーティング」を担当するクラス。
/// 自ら物理計算や描画は行わず、インターフェース経由で各モジュールに委託します。
/// </summary>
public class PlayerController : NetworkBehaviour, IPlayerRespawnHandler
{
	[Header("自端末専用オブジェクト")]
	[FormerlySerializedAs("playerCameraObject")]
	[SerializeField] private GameObject m_PlayerCameraObject;
	[FormerlySerializedAs("inputReceiver")]
	[SerializeField] private PlayerInputReceiver m_InputReceiver;
	[FormerlySerializedAs("CinemachineCameraTransform")]
	[SerializeField] private Transform m_CinemachineCameraTransform;

	// ========================================================================
	// 【重要: 移行完了後に削除すること】
	// 新アクションフロー(常時レティクル・R1即時移動・L1単押し/長押し・R1キャンセル)の有効化フラグ。
	// 各PGの実装および新フローへの移行作業が完了し次第、本フラグと旧フロー分岐コードを削除してください。
	// ========================================================================



	[Header("新アクションフロー設定")]
	[SerializeField]
	private bool m_UseReworkedActionFlow = true;

	[Tooltip("この時間以内に離すと単押し扱い")]
	[SerializeField]
	private float m_ShortTapMaxDuration = 0.2f;


	/// <summary>
	/// 新フロー：移動待機中かどうか。
	/// 右クリックでtrue、左クリック確定またはキャンセルでfalse。
	/// </summary>
	private bool m_IsMoveStandby;


	// 各担当者が実装するインターフェース・コンポーネント参照
	private IGravityMover m_GravityMover;
	private ISurfaceMover m_SurfaceMover;
	private IGroundDetector m_GroundDetector;
	private IAimGuide m_AimGuide;
	private IBallCarrier m_BallCarrier;
	private ICameraEffect m_CameraEffect;
	private ILockOnProvider m_LockOnProvider;
	private IPlayerResource m_PlayerResource;
	private IElectricAction m_ElectricAction;

	
	// プレイヤーの内部状態
	public enum EPlayerState
	{
		Idle,
		Aiming,
		Falling,
		Stunned,
		ChargingSubAction
	}

	public EPlayerState CurrentState { get; private set; } = EPlayerState.Idle;

	private Coroutine m_StunCoroutine;

	// 今回の移動中に空中再移動(旧: 移動中エイム)を使用したか
	private bool m_UsedMidFallAim;

	// 現在、移動中エイムを行っているか(旧フロー互換用)
	private bool m_IsMidFallAiming;

	// 試合進行によるアクション許可フラグ(カウントダウン中・演出中はfalse)
	private bool m_IsMatchActionEnabled = true;

	// 新フロー: L1サブアクション押下・チャージ管理用フラグ
	private bool m_IsSubActionHeld;
	private bool m_IsChargingElectric;
	private bool m_HasStartedPassCharge;
	private float m_SubActionHoldTimer;

	private void Awake()
	{
		// 各インターフェースおよび演出コンポーネントの取得
		m_GravityMover = GetComponent<IGravityMover>();
		m_SurfaceMover = GetComponent<ISurfaceMover>();
		m_GroundDetector = GetComponent<IGroundDetector>();
		m_AimGuide = GetComponent<IAimGuide>();
		m_BallCarrier = GetComponent<IBallCarrier>();
		m_CameraEffect = GetComponent<ICameraEffect>();
		m_LockOnProvider = GetComponent<ILockOnProvider>();
		m_PlayerResource = GetComponent<IPlayerResource>();
		m_ElectricAction = GetComponent<IElectricAction>();

		if (m_GravityMover == null)
		{
			Debug.LogError("[PlayerController] IGravityMover (GravityMover) が見つかりません。PlayerオブジェクトにGravityMoverをアタッチしてください。");
		}
		if (m_SurfaceMover == null)
		{
			Debug.LogWarning("[PlayerController] ISurfaceMover (PlayerSurfaceMover) が見つかりません。");
		}
		if (m_GroundDetector == null)
		{
			Debug.LogWarning("[PlayerController] IGroundDetector (PlayerGroundDetector) が見つかりません。");
		}
		if (m_AimGuide == null)
		{
			Debug.LogWarning("[PlayerController] IAimGuide (PlayerAimGuide) が見つかりません。");
		}

		if (m_InputReceiver == null)
		{
			m_InputReceiver = GetComponent<PlayerInputReceiver>();
		}

		Debug.Log(
	$"[Action Debug] LockOnProvider = {m_LockOnProvider}");

		Debug.Log(
			$"[Action Debug] ElectricAction = {m_ElectricAction}");
	}

	public override void OnNetworkSpawn()
	{
		Debug.Log(
		$"[PlayerController] OnNetworkSpawn: IsOwner = {IsOwner}");

		// Inspectorで未設定の場合も、Player自身から取得する
		if (m_InputReceiver == null)
		{
			m_InputReceiver = GetComponent<PlayerInputReceiver>();
		}

		if (m_InputReceiver == null)
		{
			Debug.LogError(
				"[PlayerController] PlayerInputReceiverが見つかりません。"
				+ " Playerにアタッチされているか確認してください。"
			);
		}

		// 他人のプレイヤーなら、自機カメラ・入力を無効にする
		if (!IsOwner)
		{
			if (m_PlayerCameraObject != null)
			{
				m_PlayerCameraObject.SetActive(false);
			}

			// コンポーネント自体は無効にせず、入力だけを禁止する
			m_InputReceiver?.SetInputAllowed(false);

			m_AimGuide?.ShowAimGuide(false);
			m_AimGuide?.SetConstantReticleActive(false);
			return;
		}

		// 自分のプレイヤーなら、カメラと入力を有効にする
		if (m_PlayerCameraObject != null)
		{
			m_PlayerCameraObject.SetActive(true);
		}

		// コンポーネント自体を有効化するのではなく、入力を許可する
		m_InputReceiver?.SetInputAllowed(true);

		BindInputEvents();
		PlayerInputReceiver.SetCursorLocked(true);

		if (m_UseReworkedActionFlow)
		{
			m_AimGuide?.SetConstantReticleActive(true);
		}

		if (m_GravityMover != null)
		{
			m_GravityMover.OnLanded += HandleLanded;
		}
	}

	private void HandleLanded()
	{
		if (CurrentState == EPlayerState.Falling)
		{
			CurrentState = EPlayerState.Idle;
		}

		if (m_UseReworkedActionFlow)
		{
			m_AimGuide?.EndLandingTracking();
			m_UsedMidFallAim = false;
		}
	}

	private void BindInputEvents()
	{
		if (m_InputReceiver == null)
		{
			Debug.LogError(
				"[PlayerController] InputReceiver is NULL");
			return;
		}

		m_InputReceiver.OnAimTogglePressed += HandleAimToggle;
		m_InputReceiver.OnConfirmPressed += HandleConfirm;
		m_InputReceiver.OnBrakePressed += HandleLegacyBrakeInput;
		m_InputReceiver.OnShootPressed += HandleLegacyShootInput;
		m_InputReceiver.OnViewResetPressed += HandleLegacyViewResetInput;

		Debug.Log(
	"[PlayerController] Binding Input Events");

		m_InputReceiver.OnMoveActionPressed += HandleMoveActionPressed;
		m_InputReceiver.OnSubActionStarted += HandleSubActionStarted;
		m_InputReceiver.OnSubActionReleased += HandleSubActionReleased;

		Debug.Log(
			"[PlayerController] Input Events Bound");
	}

	private void UnbindInputEvents()
	{
		if (m_InputReceiver == null)
		{
			return;
		}

		m_InputReceiver.OnAimTogglePressed -= HandleAimToggle;
		m_InputReceiver.OnConfirmPressed -= HandleConfirm;
		m_InputReceiver.OnBrakePressed -= HandleLegacyBrakeInput;
		m_InputReceiver.OnShootPressed -= HandleLegacyShootInput;
		m_InputReceiver.OnViewResetPressed -= HandleLegacyViewResetInput;

		m_InputReceiver.OnMoveActionPressed -= HandleMoveActionPressed;
		m_InputReceiver.OnSubActionStarted -= HandleSubActionStarted;
		m_InputReceiver.OnSubActionReleased -= HandleSubActionReleased;
	}

	private void Update()
	{
		if (!IsOwner)
		{
			return;
		}

		// 着地した場合、EPlayerStateをIdleに戻す
		if (CurrentState == EPlayerState.Falling &&
			m_GroundDetector != null &&
			m_GroundDetector.IsGrounded &&
			(m_GravityMover == null || !m_GravityMover.IsFalling))
		{
			CurrentState = EPlayerState.Idle;
			if (m_UseReworkedActionFlow)
			{
				m_AimGuide?.EndLandingTracking();
			}
		}

		// 着地などで移動が終了したら使用回数をリセット
		if (CurrentState != EPlayerState.Aiming &&
			(m_GravityMover == null || !m_GravityMover.IsFalling))
		{
			m_UsedMidFallAim = false;
			m_IsMidFallAiming = false;
		}

		if (m_UseReworkedActionFlow)
		{
			UpdateReworkedFlow();
		}
		else
		{
			// 旧フロー: エイム中のみ、照準方向を更新する
			if (CurrentState == EPlayerState.Aiming && m_AimGuide != null)
			{
				Vector3 aimDirection = GetAimDirection();

				// 照準・予測線を更新
				m_AimGuide.UpdateAimDirection(aimDirection);

				// 現在の立ち面に合わせてプレイヤーを回転させる
				RotatePlayerTowardAim(aimDirection);
			}
		}

		// 地上移動入力のルーティング(スタン中・エイム中・予備浮上中・試合待機中でない場合に伝達)
		if (m_SurfaceMover != null && m_InputReceiver != null)
		{
			bool suppressSurfaceMove =
				!m_IsMatchActionEnabled ||
				CurrentState == EPlayerState.Aiming ||
				CurrentState == EPlayerState.Stunned ||
				(m_AimGuide != null && m_AimGuide.IsTakingOff);

			Vector2 moveInput = suppressSurfaceMove
				? Vector2.zero
				: m_InputReceiver.MoveInput;

			Transform camTransform = Camera.main != null
				? Camera.main.transform
				: (m_PlayerCameraObject != null ? m_PlayerCameraObject.transform : transform);

			m_SurfaceMover.SetMoveInput(moveInput, camTransform);
		}
	}

	private void FixedUpdate()
	{
		if (!IsOwner)
		{
			return;
		}

		bool isGroundedLocomotionState =
			CurrentState == EPlayerState.Idle ||
			(CurrentState == EPlayerState.ChargingSubAction && (m_GravityMover == null || !m_GravityMover.IsFalling));

		// 地上待機・歩行中は接地検知および地上移動物理処理を実行(予備浮上中は除外)
		if (isGroundedLocomotionState && (m_AimGuide == null || !m_AimGuide.IsTakingOff))
		{
			if (m_GroundDetector != null && m_SurfaceMover != null)
			{
				m_GroundDetector.DetectGround(m_SurfaceMover.CurrentMoveDirection, m_SurfaceMover.CurrentPlaneSpeed);
				m_SurfaceMover.UpdateSurfaceMovement();
			}
		}
	}

	/// <summary>
	/// 新アクションフロー有効時の毎フレーム更新処理(常時レティクル・サブアクション長押し・飛行中着地点シフト)。
	/// </summary>
	private void UpdateReworkedFlow()
	{
		if (m_AimGuide != null)
		{
			bool canShowReticle =
				m_IsMatchActionEnabled &&
				CurrentState != EPlayerState.Stunned;

			m_AimGuide.SetConstantReticleActive(canShowReticle);

			if (canShowReticle && !m_AimGuide.IsTakingOff)
			{
				Vector3 aimDirection = GetAimDirection();

				if (m_IsMoveStandby)
				{
					// 右クリック移動待機中：
					// 着地点マーカーと予測線を更新
					m_AimGuide.UpdateAimDirection(aimDirection);
				}
				else
				{
					// 通常時：常時レティクルを更新
					m_AimGuide.UpdateConstantReticle(aimDirection);
				}
			}
		}

		UpdateReworkedSubAction();

		// 飛行中の空中WASD操作による着地点マーカーのシフト更新
		if (CurrentState == EPlayerState.Falling &&
			m_GravityMover != null &&
			m_GravityMover.IsFalling &&
			m_AimGuide != null &&
			m_InputReceiver != null &&
			m_IsMatchActionEnabled)
		{
			Vector2 airMoveInput = m_InputReceiver.MoveInput;
			if (airMoveInput.sqrMagnitude > 0.0001f)
			{
				Transform camTransform = Camera.main != null
					? Camera.main.transform
					: (m_PlayerCameraObject != null ? m_PlayerCameraObject.transform : transform);

				if (m_AimGuide.TryShiftLandingTarget(
					airMoveInput,
					camTransform,
					out Vector3 shiftedPoint,
					out Vector3 shiftedNormal))
				{
					m_GravityMover.SetFallingTarget(shiftedPoint, shiftedNormal);
				}
			}
		}
	}

	/// <summary>
	/// 新フロー: L1長押し中の電撃チャージおよび追尾パスチャージを更新します。
	/// </summary>
	private void UpdateReworkedSubAction()
	{
		if (!m_IsSubActionHeld)
			return;

		if (!m_IsMatchActionEnabled ||
			CurrentState == EPlayerState.Stunned)
		{
			CancelActiveSubAction();
			return;
		}

		// 電撃チャージ
		if (m_IsChargingElectric)
		{
			m_LockOnProvider?.UpdateTargetSearch();
			m_ElectricAction?.UpdateCharge(Time.deltaTime);
			return;
		}

		// ボールを持っていない場合
		if (m_BallCarrier == null || !m_BallCarrier.HasBall)
		{
			CancelActiveSubAction();
			return;
		}

		m_SubActionHoldTimer += Time.deltaTime;

		float threshold = m_ShortTapMaxDuration > 0
			? m_ShortTapMaxDuration
			: 0.2f;

		// 一定時間経過したら追尾パスモードへ
		if (!m_HasStartedPassCharge &&
			m_SubActionHoldTimer >= threshold)
		{
			m_HasStartedPassCharge = true;

			m_LockOnProvider?.SetSearchMode(
				ELockOnSearchMode.BallHolder);

			m_BallCarrier.BeginPassCharge();
		}

		// 追尾対象の検索とチャージ
		if (m_HasStartedPassCharge)
		{
			m_LockOnProvider?.UpdateTargetSearch();
			m_BallCarrier.UpdatePassCharge(Time.deltaTime);
		}
	}

	/// <summary>
	/// 新フロー：右クリック(R1)押下時の処理。
	/// 通常時は移動待機、待機中はキャンセル。
	/// 移動中は従来どおり移動キャンセル。
	/// </summary>
	private void HandleMoveActionPressed()
	{
		if (!m_UseReworkedActionFlow)
		{
			return;
		}

		// サブアクション中は、右クリックでキャンセル
		if (m_IsSubActionHeld ||
			CurrentState == EPlayerState.ChargingSubAction)
		{
			CancelActiveSubAction();
			return;
		}

		if (!m_IsMatchActionEnabled ||
			CurrentState == EPlayerState.Stunned)
		{
			return;
		}

		// すでに移動待機中ならキャンセル
		if (m_IsMoveStandby)
		{
			CancelMoveStandby();
			return;
		}

		// 飛行中は、移動待機ではなく従来の空中操作を行う
		if (m_GravityMover != null &&
			m_GravityMover.IsFalling)
		{
			if (m_UsedMidFallAim)
			{
				return;
			}

			m_UsedMidFallAim = true;

			if (m_AimGuide is PlayerAimGuide groundAimGuide)
			{
				groundAimGuide.StartMidFallAimGuide();
			}

			m_IsMidFallAiming = true;
			return;
		}

		// 着地点が有効か確認
		if (m_AimGuide == null ||
			!m_AimGuide.CanFall)
		{
			Debug.Log(
				"[Move] 着地点がないため移動待機を開始できません。");

			return;
		}
		// 移動待機状態にする
		m_IsMoveStandby = true;

		// 予備浮上と着地点マーカーの表示を開始
		if (m_AimGuide is PlayerAimGuide playerAimGuide)
		{
			playerAimGuide.StartMoveStandbyGuide();
		}

		Debug.Log(
			"[Move] 移動待機開始。左クリックで確定してください。");
	}


	/// <summary>
	/// 移動待機をキャンセルする。
	/// 予備浮上を解除し、プレイヤーを元の位置へ戻す。
	/// </summary>
	private void CancelMoveStandby()
	{
		if (!m_IsMoveStandby)
		{
			return;
		}

		m_IsMoveStandby = false;

		// 予備浮上と着地点マーカーを解除
		if (m_AimGuide is PlayerAimGuide playerAimGuide)
		{
			playerAimGuide.CancelAimGuide();
		}

		Debug.Log("[Move] 移動待機をキャンセルしました。");
	}

	/// <summary>
	/// 新フロー: L1(左クリック)押下開始時の処理。
	/// ボール所持時は単押し/長押し判定を開始し、非所持時は電撃チャージを開始します。
	/// </summary>
	private void HandleSubActionStarted()
	{
		Debug.Log("[PlayerController] SubAction Started");

		// 移動待機中に左クリックされた場合は移動確定
		if (m_UseReworkedActionFlow && m_IsMoveStandby)
		{
			ConfirmMoveStandby();
			return;
		}

		if (!m_UseReworkedActionFlow)
		{
			Debug.LogWarning(
				"[PlayerController] 再設計されたアクションフローは無効です");
			return;
		}

		if (!m_IsMatchActionEnabled ||
			CurrentState == EPlayerState.Stunned ||
			(m_AimGuide != null && m_AimGuide.IsTakingOff))
			return;

		if (m_IsSubActionHeld)
			return;

		bool hasBall = m_BallCarrier != null && m_BallCarrier.HasBall;

		if (hasBall)
		{
			// ボール所持時：パス操作の判定開始
			m_IsSubActionHeld = true;
			m_IsChargingElectric = false;
			m_HasStartedPassCharge = false;
			m_SubActionHoldTimer = 0.0f;

			CurrentState = EPlayerState.ChargingSubAction;
			return;
		}

		// ボール非所持時：電撃チャージ
		if (m_ElectricAction != null && m_PlayerResource != null)
		{
			if (!m_PlayerResource.CanAfford(
				m_ElectricAction.RequiredCost))
				return;
		}

		m_IsSubActionHeld = true;
		m_IsChargingElectric = true;
		m_HasStartedPassCharge = false;
		m_SubActionHoldTimer = 0.0f;

		CurrentState = EPlayerState.ChargingSubAction;

		// 敵・ボールの自動ロックオン
		m_LockOnProvider?.SetSearchMode(
			ELockOnSearchMode.NonBallHolder);

		m_ElectricAction?.BeginCharge();
	}


	/// <summary>
	/// 移動待機中の左クリックによる移動確定処理。
	/// </summary>
	private void ConfirmMoveStandby()
	{
		if (!m_IsMoveStandby)
		{
			return;
		}

		if (!m_IsMatchActionEnabled ||
			CurrentState == EPlayerState.Stunned)
		{
			CancelMoveStandby();
			return;
		}

		// 着地点が無効になっていた場合は移動しない
		if (m_AimGuide == null ||
			!m_AimGuide.CanFall)
		{
			Debug.LogWarning(
				"[Move] 着地点が無効になったため移動をキャンセルしました。");

			CancelMoveStandby();
			return;
		}

		// 確定時点の着地点と法線を保存
		Vector3 landingPoint = m_AimGuide is PlayerAimGuide playerAimGuide
			? playerAimGuide.TargetLandingPoint
			: transform.position + m_AimGuide.AimMoveDirection;

		Vector3 landingNormal = m_AimGuide is PlayerAimGuide guide
			? guide.TargetLandingNormal
			: Vector3.up;

		// 待機状態を解除
		m_IsMoveStandby = false;

		Debug.Log(
			$"[Move] 移動確定。着地点: {landingPoint}");

		// 地上からの移動
		if (m_AimGuide is PlayerAimGuide groundAimGuide)
		{
			// 右クリック待機中の予備浮上を完了させる。
			// Playerの位置は浮上した位置のまま維持する。
			groundAimGuide.EndAimGuideForMovement();

			// 移動状態へ変更
			CurrentState = EPlayerState.Falling;

			// 着地点マーカーの追跡を開始
			m_AimGuide.BeginLandingTracking(
				landingPoint,
				landingNormal);

			// カメラ演出
			m_CameraEffect?.PlayFallEffect();

			// 移動先を設定
			m_GravityMover?.SetFallingTarget(
				landingPoint,
				landingNormal);

			// ここで初めて移動を開始
			m_GravityMover?.StartFalling(
				groundAimGuide.AimMoveDirection);

			Debug.Log("[Move] 移動確定。移動開始。");
		}
		else
		{
			// PlayerAimGuide以外を使用している場合
			CurrentState = EPlayerState.Falling;

			m_CameraEffect?.PlayFallEffect();

			m_GravityMover?.StartFalling(
				m_AimGuide.AimMoveDirection);
		}
	}

	/// <summary>
	/// 新フロー: L1(左クリック)離し時の処理。
	/// 単押しパス、長押し追尾パス発射、または電撃発射を確定します。
	/// </summary>
	private void HandleSubActionReleased()
	{

		Debug.Log("[PlayerController] SubAction Released");

		if (!m_UseReworkedActionFlow || !m_IsSubActionHeld)
			return;

		bool wasChargingElectric = m_IsChargingElectric;
		bool hadStartedPassCharge = m_HasStartedPassCharge;

		m_IsSubActionHeld = false;
		m_IsChargingElectric = false;
		m_HasStartedPassCharge = false;
		m_SubActionHoldTimer = 0.0f;

		RestoreStateAfterSubAction();

		// 電撃発射
		if (wasChargingElectric)
		{
			Debug.Log("[Electric Debug] 発射処理開始");

			if (m_ElectricAction == null)
			{
				Debug.LogError(
					"[Electric Debug] ElectricAction が未設定です");
			}
			else
			{
				Debug.Log(
					"[Electric Debug] TryReleaseAndFire 呼び出し");

				bool fired =
					m_ElectricAction.TryReleaseAndFire();

				Debug.Log(
					$"[Electric Debug] 発射結果: {fired}");

				if (fired)
				{
					float cost =
						m_ElectricAction.RequiredCost;

					m_PlayerResource?.TryConsume(cost);
					m_PlayerResource?.NotifyActionPerformed();
				}
				else
				{
					Debug.LogWarning(
						"[Electric Debug] 発射条件を満たしていません");

					m_ElectricAction.CancelCharge();
				}
			}

			m_LockOnProvider?.SetSearchMode(
				ELockOnSearchMode.None);

			return;
		}

		// 短押し：通常シュート
		if (!hadStartedPassCharge)
		{
			HandleShoot();
			return;
		}

		// 長押し：追尾パス
		if (m_BallCarrier != null &&
			m_BallCarrier.IsPassChargeFull &&
			m_LockOnProvider != null &&
			m_LockOnProvider.HasValidTarget)
		{
			m_BallCarrier.ShootHoming(
				m_LockOnProvider.CurrentTargetObject,
				m_LockOnProvider.CurrentTargetType);
		}
		else
		{
			m_BallCarrier?.CancelPassCharge();
		}

		m_LockOnProvider?.SetSearchMode(
			ELockOnSearchMode.None);
	}

	/// <summary>
	/// 進行中のサブアクション(電撃チャージ・追尾パスチャージ)をキャンセルし状態を復元します。
	/// </summary>
	private void CancelActiveSubAction()
	{
		// 移動待機中なら、サブアクションとは別にキャンセルする
		if (m_IsMoveStandby)
		{
			CancelMoveStandby();
			return;
		}


		Debug.Log("[PlayerController] SubAction Cancelled");

		if (!m_IsSubActionHeld &&
			CurrentState != EPlayerState.ChargingSubAction)
			return;

		m_IsSubActionHeld = false;
		m_IsChargingElectric = false;
		m_HasStartedPassCharge = false;
		m_SubActionHoldTimer = 0.0f;

		m_ElectricAction?.CancelCharge();
		m_BallCarrier?.CancelPassCharge();

		m_LockOnProvider?.SetSearchMode(
			ELockOnSearchMode.None);

		RestoreStateAfterSubAction();
	}

	/// <summary>
	/// サブアクション終了・キャンセル後に地上(Idle)または飛行中(Falling)へステートを戻します。
	/// </summary>
	private void RestoreStateAfterSubAction()
	{
		if (CurrentState == EPlayerState.Stunned)
		{
			return;
		}

		CurrentState = (m_GravityMover != null && m_GravityMover.IsFalling)
			? EPlayerState.Falling
			: EPlayerState.Idle;
	}

	/// <summary>
	/// 現在の立ち面に沿って、プレイヤーを照準方向へ回転させます。
	/// 床・壁・斜面のいずれでも、立ち面の法線を軸に回転します。
	/// </summary>
	private void RotatePlayerTowardAim(Vector3 aimDirection)
	{
		// 現在立っている面の法線を取得
		Vector3 surfaceNormal = (m_GroundDetector != null && m_GroundDetector.CurrentGroundNormal.sqrMagnitude > 0.001f)
			? m_GroundDetector.CurrentGroundNormal
			: transform.up;

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

	// --- 旧フロー互換および状態遷移ロジック ---

	private void HandleLegacyBrakeInput()
	{
		// 新フローではブレーキのキー割当を除外(関数自体は保持)
		if (m_UseReworkedActionFlow)
		{
			return;
		}

		HandleBrake();
	}

	private void HandleLegacyShootInput()
	{
		if (m_UseReworkedActionFlow)
		{
			return;
		}

		HandleShoot();
	}

	private void HandleLegacyViewResetInput()
	{
		// 新フローでは視点リセットのキー割当を除外(関数自体は保持)
		if (m_UseReworkedActionFlow)
		{
			return;
		}

		HandleViewReset();
	}

	private void HandleAimToggle()
	{
		if (m_UseReworkedActionFlow)
		{
			return;
		}

		if (!m_IsMatchActionEnabled || CurrentState == EPlayerState.Stunned)
		{
			return;
		}

		// エイム開始
		if (CurrentState != EPlayerState.Aiming)
		{
			// 移動中の場合
			if (m_GravityMover != null && m_GravityMover.IsFalling)
			{
				// 今回の移動中にすでに使っていたら開始しない
				if (m_UsedMidFallAim)
				{
					return;
				}

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
		if (m_UseReworkedActionFlow)
		{
			return;
		}

		if (CurrentState != EPlayerState.Aiming)
		{
			return;
		}

		// 射程外の場合は移動しない
		if (m_AimGuide == null || !m_AimGuide.CanFall)
		{
			Debug.Log("[Move] 射程外、または着地点がないため移動を中止しました。");
			return;
		}

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
		if (CurrentState != EPlayerState.Falling)
		{
			return;
		}

		CurrentState = EPlayerState.Idle;
		m_GravityMover?.StopFalling();
		m_AimGuide?.EndLandingTracking();

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

		if (!m_UseReworkedActionFlow && m_GravityMover != null && m_GravityMover.IsFalling && !m_IsMidFallAiming)
		{
			return;
		}

		Vector3 shootDirection = GetAimDirection();

		m_BallCarrier.Shoot(shootDirection);
	}

	/// <summary>
	/// Rキーによる視点リセット処理(キー割当除外後もメソッドとして保持)。
	/// </summary>
	private void HandleViewReset()
	{
		// [デバッグ用]
		// 現在のEPlayerStateの状態を確認
		Debug.Log($"[ViewReset] 現在のEPlayerState: {CurrentState}");

		// GravityMoverがなければ処理しない
		if (m_GravityMover == null)
		{
			return;
		}

		// Idle状態は通常どおりリセット可能
		if (CurrentState == EPlayerState.Idle)
		{
			// Idle状態でも、実際に接地しているか確認
			if (m_GroundDetector == null || !m_GroundDetector.IsGrounded)
			{
				Debug.Log("[ViewReset] 未接地のためリセットを中止しました。");
				return;
			}
		}
		// Falling状態は、接地していて移動も終了している場合のみ許可
		else if (CurrentState == EPlayerState.Falling)
		{
			if (m_GravityMover.IsFalling || m_GroundDetector == null || !m_GroundDetector.IsGrounded)
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

	// --- 外部モジュール・通信連携用メソッド ---

	/// <summary>
	/// Host側から対象プレイヤーのOwnerへスタン適用を指示するRPC。
	/// </summary>
	/// <param name="duration">スタン持続時間(秒)</param>
	[Rpc(SendTo.Owner)]
	public void ApplyStunOwnerRpc(float duration)
	{
		ApplyStun(duration);
	}

	/// <summary>
	/// Host側から対象プレイヤーのOwnerへ指定座標への復帰・再配置を指示するRPC。
	/// </summary>
	/// <param name="position">復帰先のワールド座標</param>
	/// <param name="rotation">復帰先のワールド回転</param>
	[Rpc(SendTo.Owner)]
	public void RespawnOwnerRpc(Vector3 position, Quaternion rotation)
	{
		Respawn(position, rotation);
	}

	/// <summary>
	/// Host側の試合進行管理(GameManager等)からプレイヤーの行動許可・制限を切り替えるRPC。
	/// </summary>
	/// <param name="actionEnabled">行動を許可する場合はtrue</param>
	[Rpc(SendTo.Owner)]
	public void SetMatchActionEnabledOwnerRpc(bool actionEnabled)
	{
		m_IsMatchActionEnabled = actionEnabled;

		if (!actionEnabled)
		{
			CancelActiveSubAction();
			m_AimGuide?.CancelGroundTakeoff();
		}
	}

	/// <summary>
	/// 場外へ出たプレイヤーまたは得点後再開時のプレイヤーを指定位置へ復帰させます。
	/// </summary>
	/// <param name="position">復帰先のワールド座標</param>
	/// <param name="rotation">復帰先のワールド回転</param>
	public void Respawn(Vector3 position, Quaternion rotation)
	{
		if (!IsOwner)
		{
			return;
		}

		if (m_StunCoroutine != null)
		{
			StopCoroutine(m_StunCoroutine);
			m_StunCoroutine = null;
		}

		CancelActiveSubAction();
		m_AimGuide?.CancelGroundTakeoff();
		m_AimGuide?.EndLandingTracking();

		if (m_AimGuide is PlayerAimGuide playerAimGuide)
		{
			playerAimGuide.ResetAimGuideForRespawn();
		}

		if (m_UseReworkedActionFlow)
		{
			m_AimGuide?.SetConstantReticleActive(m_IsMatchActionEnabled);
		}

		m_GravityMover?.ResetMovement(position, rotation);
		m_CameraEffect?.RestoreFOV();

		CurrentState = EPlayerState.Idle;
		m_UsedMidFallAim = false;
		m_IsMidFallAiming = false;
	}

	/// <summary>
	/// ボール所持状態の変化通知を受け取り、速度補正を適用します。
	/// </summary>
	/// <param name="hasBall">ボールを所持している場合はtrue</param>
	public void NotifyBallStateChanged(bool hasBall)
	{
		if (m_UseReworkedActionFlow && m_IsSubActionHeld)
		{
			CancelActiveSubAction();
		}

		float speedMultiplier = hasBall ? 0.7f : 1.0f;
		m_GravityMover?.SetSpeedMultiplier(speedMultiplier);
		m_SurfaceMover?.SetSpeedMultiplier(speedMultiplier);
	}

	/// <summary>
	/// 被タックル・電撃被弾等によるスタン処理を開始します。
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
		CancelActiveSubAction();
		m_AimGuide?.CancelGroundTakeoff();
		m_AimGuide?.EndLandingTracking();

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
			if (m_GravityMover != null)
			{
				m_GravityMover.OnLanded -= HandleLanded;
			}
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
