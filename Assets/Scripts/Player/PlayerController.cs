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
	[SerializeField] private float m_ShortTapMaxDuration;

	[Header("移動時の電撃エフェクト")]
	[SerializeField] private GameObject m_MovingEffect;
	[SerializeField] private Transform m_StartPoint;
	[SerializeField] private Transform m_EndPoint;



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
		Falling,
		Stunned,
		ChargingSubAction
	}

	public EPlayerState CurrentState { get; private set; } = EPlayerState.Idle;

	private Coroutine m_StunCoroutine;

	// 今回の移動中に空中再移動(旧: 移動中エイム)を使用したか
	private bool m_UsedAimMove;

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
	}

	public override void OnNetworkSpawn()
	{
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

		if (!IsOwner)
		{
			if (m_PlayerCameraObject != null)
			{
				m_PlayerCameraObject.SetActive(false);
			}

			m_InputReceiver?.SetInputAllowed(false);

			// 他プレイヤーのレティクルは表示しない
			m_AimGuide?.SetConstantReticleActive(false);
			return;
		}

		if (m_PlayerCameraObject != null)
		{
			m_PlayerCameraObject.SetActive(true);
		}

		m_InputReceiver?.SetInputAllowed(true);

		BindInputEvents();
		PlayerInputReceiver.SetCursorLocked(true);

		// 【変更】新システムでは自分のレティクルを常時有効にする
		m_AimGuide?.SetConstantReticleActive(m_IsMatchActionEnabled);

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

		m_AimGuide?.EndLandingTracking();
	}

	private void BindInputEvents()
	{
		if (m_InputReceiver == null)
		{
			return;
		}

		// 旧エイム開始・確定・ブレーキ・単独シュート入力は登録しない
		m_InputReceiver.OnViewResetPressed += HandleLegacyViewResetInput;

		// 新アクションフロー
		m_InputReceiver.OnMoveActionPressed += HandleMoveActionPressed;
		m_InputReceiver.OnSubActionStarted += HandleSubActionStarted;
		m_InputReceiver.OnSubActionReleased += HandleSubActionReleased;
	}

	private void UnbindInputEvents()
	{
		if (m_InputReceiver == null)
		{
			return;
		}

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

		// 飛行中は移動中エフェクトの始点と終点を現在の座標に更新
		if (CurrentState == EPlayerState.Falling && m_MovingEffect != null)
		{
			m_StartPoint.position = transform.position;
			Vector3 groundTargetPoint = m_AimGuide.TargetLandingPoint;
			m_EndPoint.position = groundTargetPoint;
		}

		// 着地した場合、EPlayerStateをIdleに戻してエフェクト非表示
		if (CurrentState == EPlayerState.Falling &&
			m_GroundDetector != null &&
			m_GroundDetector.IsGrounded &&
			(m_GravityMover == null || !m_GravityMover.IsFalling))
		{
			CurrentState = EPlayerState.Idle;
			m_AimGuide?.EndLandingTracking();
		}

		// 通常状態のとき、移動中エフェクトを非表示にする
		if (CurrentState == EPlayerState.Idle)
		{
			m_MovingEffect.SetActive(false);
		}

		// 新アクションフローのみを更新する
		UpdateReworkedFlow();

		// 地上移動入力のルーティング
		if (m_SurfaceMover != null && m_InputReceiver != null)
		{
			bool suppressSurfaceMove =
				!m_IsMatchActionEnabled ||
				CurrentState == EPlayerState.Stunned ||
				(m_AimGuide != null && m_AimGuide.IsTakingOff);

			Vector2 moveInput = suppressSurfaceMove
				? Vector2.zero
				: m_InputReceiver.MoveInput;

			Transform camTransform = Camera.main != null
				? Camera.main.transform
				: (m_PlayerCameraObject != null
					? m_PlayerCameraObject.transform
					: transform);

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
			bool canShowReticle = m_IsMatchActionEnabled && CurrentState != EPlayerState.Stunned;
			m_AimGuide.SetConstantReticleActive(canShowReticle);

			if (canShowReticle && !m_AimGuide.IsTakingOff)
			{
				m_AimGuide.UpdateConstantReticle(GetAimDirection());
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
				if (m_AimGuide.TryShiftLandingTarget(
					airMoveInput,
					null,
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
		{
			return;
		}

		if (!m_IsMatchActionEnabled || CurrentState == EPlayerState.Stunned)
		{
			CancelActiveSubAction();
			return;
		}

		if (m_IsChargingElectric)
		{
			m_LockOnProvider?.UpdateTargetSearch();
			m_ElectricAction?.UpdateCharge(Time.deltaTime);
			return;
		}

		if (m_BallCarrier == null || !m_BallCarrier.HasBall)
		{
			CancelActiveSubAction();
			return;
		}

		m_SubActionHoldTimer += Time.deltaTime;
		float tapThreshold = m_ShortTapMaxDuration > 0.0f ? m_ShortTapMaxDuration : 0.2f;

		if (!m_HasStartedPassCharge && m_SubActionHoldTimer >= tapThreshold)
		{
			m_HasStartedPassCharge = true;
			m_LockOnProvider?.SetSearchMode(ELockOnSearchMode.BallHolder);
			m_BallCarrier.BeginPassCharge();
		}

		if (m_HasStartedPassCharge)
		{
			m_LockOnProvider?.UpdateTargetSearch();
			m_BallCarrier.UpdatePassCharge(Time.deltaTime);
		}
	}

	/// <summary>
	/// 新フロー: R1(右クリック)押下時の処理。
	/// L1チャージ中であればキャンセルのみを実行して移動暴発を防ぎ、
	/// 通常時は地上予備浮上移動または空中即時再移動を実行します。
	/// </summary>
	private void HandleMoveActionPressed()
	{
		if (!m_IsMatchActionEnabled || CurrentState == EPlayerState.Stunned)
		{
			return;
		}

		// L1長押しチャージ中のR1入力はキャンセル専用として消費し、移動を発火させない
		if (m_IsSubActionHeld || CurrentState == EPlayerState.ChargingSubAction)
		{
			CancelActiveSubAction();
			return;
		}

		if (m_AimGuide == null || m_AimGuide.IsTakingOff)
		{
			return;
		}

		// 押下フレームの最新照準方向を取得
		Vector3 aimDirection = GetAimDirection();

		// GravityMover.IsFallingで空中か地上かを判定
		bool isAirborne = m_GravityMover != null && m_GravityMover.IsFalling;

		// =====================================================
		// 空中追加移動
		// =====================================================
		if (isAirborne)
		{
			// 既に1回使用済みなら不発
			if (m_UsedAimMove)
			{
				return;
			}

			// 空中追加移動専用の照準判定を行う。
			// 現在追跡中の着地点は、この時点では変更しない。
			if (!m_AimGuide.TryUpdateAdditionalMoveTarget(
				aimDirection,
				out Vector3 airTargetPoint,
				out Vector3 airTargetNormal,
				out Vector3 airMoveDirection))
			{
				return;
			}

			// 新しい着地点までの距離からリソース消費量を計算
			float airMoveDistance =
				Vector3.Distance(transform.position, airTargetPoint);

			float airMoveCost = m_PlayerResource != null
				? m_PlayerResource.CalculateMoveCost(airMoveDistance)
				: 0.0f;

			if (m_PlayerResource != null &&
				!m_PlayerResource.CanAfford(airMoveCost))
			{
				return;
			}

			// 新しい着地点をGravityMoverに設定
			m_GravityMover.SetFallingTarget(
				airTargetPoint,
				airTargetNormal);

			// 空中追加移動を開始
			if (!m_GravityMover.StartFalling(airMoveDirection))
			{
				// 開始に失敗した場合は使用回数を消費しない
				return;
			}

			// 移動開始に成功した場合のみ着地点マーカーを更新
			// BeginLandingTracking内では最初の法線が維持される。
			m_AimGuide.BeginLandingTracking(
				airTargetPoint,
				airTargetNormal);

			// 実際に空中追加移動が開始された場合のみ使用済みにする
			m_UsedAimMove = true;

			// 空中移動回数を使い切ったので、エイムを非表示にする。
			m_AimGuide.SetAdditionalMoveAimVisible(false);

			// 移動開始に成功したのでリソースを消費
			m_PlayerResource?.TryConsume(airMoveCost);
			m_PlayerResource?.NotifyActionPerformed();

			// 自分と着地点との間に電撃エフェクトを発生させる
			m_MovingEffect.SetActive(true);
			m_StartPoint.position = transform.position;
			m_EndPoint.position = airTargetPoint;

			m_CameraEffect?.PlayFallEffect();

			return;
		}

		// =====================================================
		// 地上からの通常移動
		// =====================================================

		// 地上では通常のレティクルによる着地点評価を行う
		m_AimGuide.UpdateConstantReticle(aimDirection);

		if (!m_AimGuide.CanFall)
		{
			return;
		}

		float moveCost = m_PlayerResource != null
			? m_PlayerResource.CalculateMoveCost(m_AimGuide.TargetDistance)
			: 0.0f;

		if (m_PlayerResource != null && !m_PlayerResource.CanAfford(moveCost))
		{
			return;
		}

		// 地上移動: リソースを消費し、立ち面法線方向へ少し浮上してから飛行を開始
		if (m_PlayerResource != null && !m_PlayerResource.TryConsume(moveCost))
		{
			return;
		}

		m_PlayerResource?.NotifyActionPerformed();

		Vector3 groundTargetPoint = m_AimGuide.TargetLandingPoint;
		Vector3 groundTargetNormal = m_AimGuide.TargetLandingNormal;

		RotatePlayerTowardAim(aimDirection);

		m_AimGuide.StartGroundTakeoff(() =>
		{
			if (!m_IsMatchActionEnabled || CurrentState == EPlayerState.Stunned)
			{
				return;
			}

			Vector3 toTarget = groundTargetPoint - transform.position;

			Vector3 launchDirection = toTarget.sqrMagnitude > 0.001f
				? toTarget.normalized
				: m_AimGuide.AimMoveDirection;

			m_GravityMover?.SetFallingTarget(
				groundTargetPoint,
				groundTargetNormal);

			// 通常飛行を開始
			if (m_GravityMover == null ||
				!m_GravityMover.StartFalling(launchDirection))
			{
				return;
			}

			// 通常飛行が実際に始まったので、空中追加移動の使用回数をリセット
			m_UsedAimMove = false;

			// 次の飛行が始まったので、空中移動用エイムを表示する。
			m_AimGuide.SetAdditionalMoveAimVisible(true);

			// 自分と着地点との間に電撃エフェクトを発生させる
			m_MovingEffect.SetActive(true);
			m_StartPoint.position = transform.position;
			m_EndPoint.position = groundTargetPoint;

			CurrentState = EPlayerState.Falling;

			m_AimGuide.BeginLandingTracking(
				groundTargetPoint,
				groundTargetNormal);

			m_CameraEffect?.PlayFallEffect();
		});
	}

	/// <summary>
	/// 新フロー: L1(左クリック)押下開始時の処理。
	/// ボール所持時は単押し/長押し判定を開始し、非所持時は電撃チャージを開始します。
	/// </summary>
	private void HandleSubActionStarted()
	{
		if (!m_IsMatchActionEnabled ||
			CurrentState == EPlayerState.Stunned ||
			(m_AimGuide != null && m_AimGuide.IsTakingOff))
		{
			return;
		}

		if (m_IsSubActionHeld)
		{
			return;
		}

		bool hasBall = m_BallCarrier != null && m_BallCarrier.HasBall;
		if (hasBall)
		{
			m_IsSubActionHeld = true;
			m_IsChargingElectric = false;
			m_HasStartedPassCharge = false;
			m_SubActionHoldTimer = 0.0f;
			CurrentState = EPlayerState.ChargingSubAction;
			return;
		}

		// 非所持時: 電撃チャージ開始(リソース残量を事前確認)
		if (m_ElectricAction != null && m_PlayerResource != null)
		{
			if (!m_PlayerResource.CanAfford(m_ElectricAction.RequiredCost))
			{
				return;
			}
		}

		m_IsSubActionHeld = true;
		m_IsChargingElectric = true;
		m_HasStartedPassCharge = false;
		m_SubActionHoldTimer = 0.0f;
		CurrentState = EPlayerState.ChargingSubAction;

		m_LockOnProvider?.SetSearchMode(ELockOnSearchMode.NonBallHolder);
		m_ElectricAction?.BeginCharge();
	}

	/// <summary>
	/// 新フロー: L1(左クリック)離し時の処理。
	/// 単押しパス、長押し追尾パス発射、または電撃発射を確定します。
	/// </summary>
	private void HandleSubActionReleased()
	{
		if (!m_IsSubActionHeld)
		{
			return;
		}

		bool wasChargingElectric = m_IsChargingElectric;
		bool hadStartedPassCharge = m_HasStartedPassCharge;

		m_IsSubActionHeld = false;
		m_IsChargingElectric = false;
		m_HasStartedPassCharge = false;
		m_SubActionHoldTimer = 0.0f;

		RestoreStateAfterSubAction();

		if (wasChargingElectric)
		{
			if (m_ElectricAction != null && m_ElectricAction.TryReleaseAndFire())
			{
				float cost = m_ElectricAction.RequiredCost;
				m_PlayerResource?.TryConsume(cost);
				m_PlayerResource?.NotifyActionPerformed();
			}
			else
			{
				m_ElectricAction?.CancelCharge();
			}

			m_LockOnProvider?.SetSearchMode(ELockOnSearchMode.None);
			return;
		}

		// ボール所持時の離し処理
		if (!hadStartedPassCharge)
		{
			// 短押し(タップ): カメラ正面方向へ即座に通常パス・シュート
			HandleShoot();
			return;
		}

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

		m_LockOnProvider?.SetSearchMode(ELockOnSearchMode.None);
	}

	/// <summary>
	/// 進行中のサブアクション(電撃チャージ・追尾パスチャージ)をキャンセルし状態を復元します。
	/// </summary>
	private void CancelActiveSubAction()
	{
		if (!m_IsSubActionHeld && CurrentState != EPlayerState.ChargingSubAction)
		{
			return;
		}

		m_IsSubActionHeld = false;
		m_IsChargingElectric = false;
		m_HasStartedPassCharge = false;
		m_SubActionHoldTimer = 0.0f;

		m_ElectricAction?.CancelCharge();
		m_BallCarrier?.CancelPassCharge();
		m_LockOnProvider?.SetSearchMode(ELockOnSearchMode.None);

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

	private void HandleLegacyViewResetInput()
	{
		HandleViewReset();
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

			// 着地点マーカーも終了する。
			m_AimGuide?.EndLandingTracking();

			// 行動禁止中はレティクルを非表示
			m_AimGuide?.SetConstantReticleActive(false);
		}
		else
		{
			// 行動再開時はレティクルを再表示
			if (CurrentState != EPlayerState.Stunned)
			{
				m_AimGuide?.SetConstantReticleActive(true);
			}
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

		// 復帰後のレティクル表示
		m_AimGuide?.SetConstantReticleActive(m_IsMatchActionEnabled);

		m_GravityMover?.ResetMovement(position, rotation);
		m_CameraEffect?.RestoreFOV();

		CurrentState = EPlayerState.Idle;
		m_UsedAimMove = false;
	}

	/// <summary>
	/// ボール所持状態の変化通知を受け取り、速度補正を適用します。
	/// </summary>
	/// <param name="hasBall">ボールを所持している場合はtrue</param>
	public void NotifyBallStateChanged(bool hasBall)
	{
		// 新システムでは、サブアクション中にボール状態が変化したらキャンセル
		if (m_IsSubActionHeld)
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

		// スタン中は常時レティクルを非表示
		m_AimGuide?.SetConstantReticleActive(false);

		m_GravityMover?.StopFalling();

		yield return new WaitForSeconds(duration);

		CurrentState = EPlayerState.Idle;
		m_StunCoroutine = null;

		// 試合中であればレティクルを再表示
		if (m_IsMatchActionEnabled)
		{
			m_AimGuide?.SetConstantReticleActive(true);
		}
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
