
using System;
using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
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
	[Tooltip("Owner本人が使用するカメラ用GameObjectです。")]
	[SerializeField] private GameObject m_PlayerCameraObject;
	[FormerlySerializedAs("inputReceiver")]
	[Tooltip("プレイヤーの入力を受け取るコンポーネントです。未設定時は同じオブジェクトから取得します。")]
	[SerializeField] private PlayerInputReceiver m_InputReceiver;
	[FormerlySerializedAs("CinemachineCameraTransform")]
	[Tooltip("視点補正に利用するCinemachineカメラ用のTransformです。")]
	[SerializeField] private Transform m_CinemachineCameraTransform;

	[Header("新アクションフロー設定")]
	[Tooltip("L1の短押しと長押しを区別する時間です(秒)。0以下ではコード側の代替値を使用します。")]
	[SerializeField] private float m_ShortTapMaxDuration;

	/// <summary>
	/// 現在の入力デバイスと行動状態に応じた操作補助UIコンポーネント
	/// </summary>
	[Header("操作補助UI")]
	[SerializeField]
	private PlayerActionHelpUI m_ActionHelpUI;

	[Header("移動時の電撃エフェクト")]
	[SerializeField] private GameObject m_MovingEffect;
	[SerializeField] private Transform m_StartPoint;
	[SerializeField] private Transform m_EndPoint;

	// 他端末・観戦視点へ移動時エフェクトの稼働状態と終点座標を同期するNetworkVariable
	private readonly NetworkVariable<bool> m_NetIsMovingEffectActive = new NetworkVariable<bool>(
		false,
		NetworkVariableReadPermission.Everyone,
		NetworkVariableWritePermission.Owner);

	private readonly NetworkVariable<Vector3> m_NetMovingEffectEndPoint = new NetworkVariable<Vector3>(
		Vector3.zero,
		NetworkVariableReadPermission.Everyone,
		NetworkVariableWritePermission.Owner);

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
	private Rigidbody m_MatchRigidbody;
	private NetworkTransform m_NetworkTransform;
	private uint m_RestartSequence;
	private PlayerAnimationController m_AnimationController;
	public bool IsReadyForRestart { get; private set; }

	private PlayerChargeProgress m_ChargeProgressUI;

	// プレイヤーの内部状態
	public enum EPlayerState
	{
		Idle,
		Falling,
		Stunned,
		ChargingSubAction
	}

	public EPlayerState CurrentState { get; private set; } = EPlayerState.Idle;
	public bool IsMatchActionEnabled => m_IsMatchActionEnabled;

	/// <summary>
	/// Owner端末で空中移動(離陸または空中追加移動)が開始された際にClientIdを通知します。
	/// </summary>
	public static event Action<ulong> OnLocalPlayerAirMoveStarted;

	private Coroutine m_StunCoroutine;

	// 今回の移動中に空中再移動(旧: 移動中エイム)を使用したか
	private bool m_UsedAimMove;

	// 試合進行によるアクション許可フラグ(カウントダウン中・演出中はfalse)
	private bool m_IsMatchActionEnabled = true;

	// 新フロー: L1サブアクション押下・チャージ管理用フラグ
	private bool m_IsSubActionHeld;
	// R1移動アクションの押下状態
	private bool m_IsMoveActionHeld;
	// Aim終了前の解放を記録し、終了後に一度だけ移動を実行する。
	private bool m_IsMoveActionPending;
	private Vector3 m_ReleasedMoveAimDirection;

	private bool m_IsChargingElectric;
	private bool m_HasStartedPassCharge;
	private float m_SubActionHoldTimer;

	// パスモード管理
	private bool m_IsPassModeActive = false;

	// パスモード中の2回目の短押しかどうか
	private bool m_IsPassModeTapHeld = false;

	// パスモードの経過時間
	private float m_PassModeTimer = 0.0f;

	// パスモードの制限時間(Inspectorから変更可能)
	[SerializeField] private float m_PassModeDuration = 5.0f;

	// 最後にログを出したロックオン対象
	private GameObject m_LastLoggedPassTarget;

	//飛んでるときにスタン食らったか
	private bool m_IsStunnedOnFlying = false;
	private float m_StunDuration = 0.0f;

	private void Awake()
	{
		m_MatchRigidbody = GetComponent<Rigidbody>();
		m_NetworkTransform = GetComponent<NetworkTransform>();
		m_AnimationController = GetComponent<PlayerAnimationController>();
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
		m_ChargeProgressUI =
			GetComponentInChildren<PlayerChargeProgress>(true);
		if (m_ActionHelpUI == null)
		{
			m_ActionHelpUI = GetComponentInChildren<PlayerActionHelpUI>(true);
		}

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
			m_InputReceiver?.SetLookInputAllowed(false);

			// 他プレイヤーのレティクルは表示しない
			m_AimGuide?.SetConstantReticleActive(false);

			// 非Owner(観戦視点・他クライアント)向けに同期済み移動エフェクト状態を購読・初期反映
			m_NetIsMovingEffectActive.OnValueChanged += HandleNetMovingEffectActiveChanged;
			m_NetMovingEffectEndPoint.OnValueChanged += HandleNetMovingEffectEndPointChanged;
			ApplyRemoteMovingEffectState();
			return;
		}

		if (m_PlayerCameraObject != null)
		{
			m_PlayerCameraObject.SetActive(true);
		}

		// 待機中も周囲を見渡せるよう視点入力はOwner生成時から許可し、移動・アクション入力のみ試合状態に合わせる
		m_InputReceiver?.SetLookInputAllowed(true);
		// コンポーネント自体を有効化するのではなく、入力を許可する
		SetMatchActionEnabled(MatchManager.Instance == null || MatchManager.Instance.MatchState == MatchManager.EMatchState.Playing);

		BindInputEvents();
		PlayerInputReceiver.SetCursorLocked(true);

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
			m_IsMoveActionHeld = false;
			m_IsMoveActionPending = false;
			m_AnimationController?.PlaySpecialMoveLanding();
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
		m_InputReceiver.OnMoveActionReleased += HandleMoveActionReleased;
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
		m_InputReceiver.OnMoveActionReleased -= HandleMoveActionReleased;
		m_InputReceiver.OnSubActionStarted -= HandleSubActionStarted;
		m_InputReceiver.OnSubActionReleased -= HandleSubActionReleased;
	}

	private void Update()
	{
		if (!IsOwner)
		{
			if (IsSpawned && m_NetIsMovingEffectActive.Value)
			{
				UpdateRemoteMovingEffectPositions();
			}
			return;
		}

		if (!m_IsMatchActionEnabled)
		{
			m_ActionHelpUI?.Hide();
			return;
		}

		UpdatePendingMoveAction();

		// 飛行中は移動中エフェクトの始点と終点を現在の座標に更新
		if (CurrentState == EPlayerState.Falling && m_AimGuide != null)
		{
			UpdateMovingEffectPositions(m_AimGuide.TargetLandingPoint);
		}

		// 飛行中に電撃受けていたら着地したときにスタン
		if (CurrentState == EPlayerState.Idle && m_IsStunnedOnFlying)
		{
			m_StunCoroutine = StartCoroutine(StunRoutine(m_StunDuration));
			m_IsStunnedOnFlying = false;
		}

		// 着地した場合、EPlayerStateをIdleに戻してエフェクト非表示
		if (CurrentState == EPlayerState.Falling &&
			m_GroundDetector != null &&
			m_GroundDetector.IsGrounded &&
			(m_GravityMover == null || !m_GravityMover.IsFalling))
		{
			HandleLanded();
		}

		// 通常状態のとき、移動中エフェクトを非表示にする
		if (CurrentState == EPlayerState.Idle)
		{
			DeactivateMovingEffect();
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
		if (!IsOwner || !m_IsMatchActionEnabled)
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
		UpdateActionHelpUI();

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
		// 電撃チャージを毎フレーム更新
		m_ElectricAction?.UpdateCharge(Time.deltaTime);

		if (!m_IsSubActionHeld)
		{
			return;
		}

		if (!m_IsMatchActionEnabled ||
			CurrentState == EPlayerState.Stunned)
		{
			CancelActiveSubAction();
			return;
		}

		// ボール非所持：電撃チャージ・ロックオン更新
		if (m_IsChargingElectric)
		{
			m_LockOnProvider?.UpdateTargetSearch();
			return;
		}

		// ボール所持確認
		if (m_BallCarrier == null || !m_BallCarrier.HasBall)
		{
			CancelActiveSubAction();
			return;
		}

		// 長押し時間を加算
		m_SubActionHoldTimer += Time.deltaTime;

		float tapThreshold =
			m_ShortTapMaxDuration > 0.0f
			? m_ShortTapMaxDuration
			: 0.2f;

		// 一定時間を超えたら追尾パスチャージ開始
		if (!m_HasStartedPassCharge &&
			m_SubActionHoldTimer >= tapThreshold)
		{
			m_HasStartedPassCharge = true;

			m_LockOnProvider?.SetSearchMode(
				ELockOnSearchMode.BallHolder);

			m_BallCarrier.BeginPassCharge();

			// 追尾パス用のゲージ表示に切り替え
			m_ChargeProgressUI?.SetPassChargeMode(true);
			m_ChargeProgressUI?.SetPassChargeProgress(0.0f);

			Debug.Log("[PlayerController] 追尾パスチャージ開始");
		}

		// 追尾パスチャージ中
		if (m_HasStartedPassCharge)
		{
			m_LockOnProvider?.UpdateTargetSearch();

			m_BallCarrier.UpdatePassCharge(Time.deltaTime);

			// 追尾パスの進捗率を円形ゲージへ反映
			m_ChargeProgressUI?.SetPassChargeProgress(
				m_BallCarrier.PassChargeRatio);

			LogPassLockOnTarget();

			// チャージ完了時のログ
			if (m_BallCarrier.IsPassChargeFull)
			{
				Debug.Log("[PlayerController] 追尾パスチャージ完了！");
			}
		}
	}

	/// <summary>
	/// Aim終了とボタン解放の両方を満たした後、確定した照準方向へ移動します。
	/// 移動先・リソースを検証し、予備浮上または空中追加移動を開始した場合に成功を返します。
	/// </summary>
	private bool ExecuteMoveAction(Vector3 aimDirection)
	{
		if (!m_IsMatchActionEnabled || CurrentState == EPlayerState.Stunned)
		{
			return false;
		}

		// L1長押しチャージ中のR1入力はキャンセル専用として消費し、移動を発火させない
		if (m_IsSubActionHeld || CurrentState == EPlayerState.ChargingSubAction)
		{
			CancelActiveSubAction();
			return false;
		}

		if (m_AimGuide == null || m_AimGuide.IsTakingOff)
		{
			return false;
		}

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
				return false;
			}

			// 空中追加移動専用の照準判定を行う。
			// 現在追跡中の着地点は、この時点では変更しない。
			if (!m_AimGuide.TryUpdateAdditionalMoveTarget(
				aimDirection,
				out Vector3 airTargetPoint,
				out Vector3 airTargetNormal,
				out Vector3 airMoveDirection))
			{
				return false;
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
				return false;
			}

			// 新しい着地点をGravityMoverに設定
			m_GravityMover.SetFallingTarget(
				airTargetPoint,
				airTargetNormal);

			// 空中追加移動を開始
			if (!m_GravityMover.StartFalling(airMoveDirection))
			{
				// 開始に失敗した場合は使用回数を消費しない
				return false;
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

			m_AnimationController?.PlaySpecialMoveJump();
			CompleteFallStart(airTargetPoint, airTargetNormal);

			return true;
		}

		// =====================================================
		// 地上からの通常移動
		// =====================================================

		// 地上では通常のレティクルによる着地点評価を行う
		m_AimGuide.UpdateConstantReticle(aimDirection);

		if (!m_AimGuide.CanFall)
		{
			return false;
		}

		float moveCost = m_PlayerResource != null
			? m_PlayerResource.CalculateMoveCost(m_AimGuide.TargetDistance)
			: 0.0f;

		if (m_PlayerResource != null && !m_PlayerResource.CanAfford(moveCost))
		{
			return false;
		}

		if (m_GravityMover == null || m_AnimationController == null) return false;

		// 着地点は解放時に確定し、飛行方向は浮上完了後の位置から計算します。
		Vector3 groundTargetPoint = m_AimGuide.TargetLandingPoint;
		Vector3 groundTargetNormal = m_AimGuide.TargetLandingNormal;

		RotatePlayerTowardAim(aimDirection);
		m_AnimationController.PlaySpecialMoveJump();

		m_AimGuide.StartGroundTakeoff(() =>
			CompleteGroundTakeoff(groundTargetPoint, groundTargetNormal, moveCost));
		return true;
	}

	/// <summary>
	/// Jump中の予備浮上を終えた位置から飛行を開始し、表示をFallへ切り替えます。
	/// </summary>
	private void CompleteGroundTakeoff(Vector3 targetPoint, Vector3 targetNormal, float moveCost)
	{
		if (!m_IsMatchActionEnabled || CurrentState == EPlayerState.Stunned ||
			m_GravityMover == null || m_AimGuide == null ||
			(m_PlayerResource != null && !m_PlayerResource.CanAfford(moveCost)))
		{
			CancelGroundMoveAction();
			return;
		}

		Vector3 toTarget = targetPoint - transform.position;
		Vector3 launchDirection = toTarget.sqrMagnitude > 0.001f
			? toTarget.normalized
			: m_AimGuide.AimMoveDirection;

		if (launchDirection.sqrMagnitude <= 0.0001f)
		{
			CancelGroundMoveAction();
			return;
		}
		m_GravityMover.SetFallingTarget(targetPoint, targetNormal);
		if (!m_GravityMover.StartFalling(launchDirection))
		{
			CancelGroundMoveAction();
			return;
		}

		m_PlayerResource?.TryConsume(moveCost);
		m_UsedAimMove = false;
		m_AimGuide.SetAdditionalMoveAimVisible(true);
		m_AnimationController?.PlaySpecialMoveFall();
		CompleteFallStart(targetPoint, targetNormal);
	}

	private void CancelGroundMoveAction()
	{
		m_AimGuide?.CancelGroundTakeoff();
		CancelPendingMoveAction(false);
	}

	/// <summary>
	/// R1押下時の処理。
	/// この時点では移動を発動せず、プレイヤーとテンタクルのAimを開始します。
	/// </summary>
	private void HandleMoveActionPressed()
	{
		if (!m_IsMatchActionEnabled ||
			CurrentState == EPlayerState.Stunned)
		{
			return;
		}

		// L1チャージ中のR1入力はキャンセル専用
		if (m_IsSubActionHeld ||
			CurrentState == EPlayerState.ChargingSubAction)
		{
			CancelActiveSubAction();
			return;
		}

		if (m_AimGuide == null || m_AimGuide.IsTakingOff || m_IsMoveActionHeld || m_IsMoveActionPending)
		{
			return;
		}

		if (m_GravityMover != null && m_GravityMover.IsFalling && m_UsedAimMove) return;
		if (m_AnimationController == null || !m_AnimationController.BeginSpecialMoveAim()) return;
		m_IsMoveActionHeld = true;
	}

	/// <summary>
	/// R1解放時の処理。
	/// Aim終了前なら解放を予約し、完了後に移動処理を実行します。
	/// </summary>
	private void HandleMoveActionReleased()
	{
		if (!m_IsMoveActionHeld)
		{
			return;
		}

		m_IsMoveActionHeld = false;

		if (!m_IsMatchActionEnabled ||
			CurrentState == EPlayerState.Stunned)
		{
			m_AnimationController?.CancelSpecialMoveAnimation();
			return;
		}

		if (m_IsSubActionHeld ||
			CurrentState == EPlayerState.ChargingSubAction)
		{
			m_AnimationController?.CancelSpecialMoveAnimation();
			return;
		}

		m_ReleasedMoveAimDirection = GetAimDirection();
		m_IsMoveActionPending = true;
		UpdatePendingMoveAction();
	}

	private void UpdatePendingMoveAction()
	{
		if (!m_IsMoveActionPending) return;
		if (!m_IsMatchActionEnabled || CurrentState == EPlayerState.Stunned || m_IsSubActionHeld)
		{
			CancelPendingMoveAction();
			return;
		}
		if (m_AnimationController != null && !m_AnimationController.IsSpecialMoveAimComplete) return;

		m_IsMoveActionPending = false;
		if (!ExecuteMoveAction(m_ReleasedMoveAimDirection))
		{
			CancelPendingMoveAction();
		}
	}

	private void CancelPendingMoveAction(bool resumeFlight = true)
	{
		m_IsMoveActionHeld = false;
		m_IsMoveActionPending = false;
		if (resumeFlight && m_GravityMover != null && m_GravityMover.IsFalling)
		{
			m_AnimationController?.PlaySpecialMoveFall();
		}
		else
		{
			m_AnimationController?.CancelSpecialMoveAnimation();
		}
	}

	private void CompleteFallStart(Vector3 targetPoint, Vector3 targetNormal)
	{
		CurrentState = EPlayerState.Falling;
		ActivateMovingEffect(targetPoint);
		m_AimGuide?.BeginLandingTracking(targetPoint, targetNormal);
		m_CameraEffect?.PlayFallEffect();
		if (IsOwner)
		{
			OnLocalPlayerAirMoveStarted?.Invoke(OwnerClientId);
		}
	}

	private void ActivateMovingEffect(Vector3 targetPoint)
	{
		if (m_MovingEffect != null)
		{
			m_MovingEffect.SetActive(true);
		}
		UpdateMovingEffectPositions(targetPoint);

		if (IsSpawned && IsOwner)
		{
			m_NetMovingEffectEndPoint.Value = targetPoint;
			if (!m_NetIsMovingEffectActive.Value)
			{
				m_NetIsMovingEffectActive.Value = true;
			}
		}
	}

	private void UpdateMovingEffectPositions(Vector3 targetPoint)
	{
		if (m_StartPoint != null)
		{
			m_StartPoint.position = transform.position;
		}
		if (m_EndPoint != null)
		{
			m_EndPoint.position = targetPoint;
		}

		if (IsSpawned && IsOwner && (m_NetMovingEffectEndPoint.Value - targetPoint).sqrMagnitude > 0.0001f)
		{
			m_NetMovingEffectEndPoint.Value = targetPoint;
		}
	}

	private void DeactivateMovingEffect()
	{
		if (m_MovingEffect != null)
		{
			m_MovingEffect.SetActive(false);
		}

		if (IsSpawned && IsOwner && m_NetIsMovingEffectActive.Value)
		{
			m_NetIsMovingEffectActive.Value = false;
		}
	}

	private void HandleNetMovingEffectActiveChanged(bool previousValue, bool newValue)
	{
		if (IsOwner)
		{
			return;
		}

		ApplyRemoteMovingEffectState();
	}

	private void HandleNetMovingEffectEndPointChanged(Vector3 previousValue, Vector3 newValue)
	{
		if (IsOwner || !m_NetIsMovingEffectActive.Value)
		{
			return;
		}

		UpdateRemoteMovingEffectPositions();
	}

	private void ApplyRemoteMovingEffectState()
	{
		bool isActive = m_NetIsMovingEffectActive.Value;
		if (m_MovingEffect != null && m_MovingEffect.activeSelf != isActive)
		{
			m_MovingEffect.SetActive(isActive);
		}

		if (isActive)
		{
			UpdateRemoteMovingEffectPositions();
		}
	}

	private void UpdateRemoteMovingEffectPositions()
	{
		if (m_StartPoint != null)
		{
			m_StartPoint.position = transform.position;
		}
		if (m_EndPoint != null)
		{
			m_EndPoint.position = m_NetMovingEffectEndPoint.Value;
		}
	}

	private void CancelAllActiveActionsAndEffects()
	{
		CancelPendingMoveAction(false);

		if (m_IsPassModeActive)
		{
			CancelPassMode();
		}

		CancelActiveSubAction();
		m_AimGuide?.CancelGroundTakeoff();
		m_AimGuide?.EndLandingTracking();
		DeactivateMovingEffect();
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

		if (m_IsMoveActionHeld || m_IsMoveActionPending) CancelPendingMoveAction();

		// パスモード中の左クリックは、パス実行用の短押しとして扱う
		if (m_IsPassModeActive)
		{
			m_IsSubActionHeld = true;
			m_IsPassModeTapHeld = true;

			Debug.Log("[PassMode] 2回目の左クリック押下");

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

		// 非所持時: 電撃コンポーネントがない間はチャージ状態へ移行しない。
		if (m_ElectricAction == null || m_PlayerResource == null)
		{
			return;
		}

		if (!m_PlayerResource.CanAfford(m_ElectricAction.RequiredCost))
		{
			return;
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
		if (!m_IsSubActionHeld) return;

		bool wasChargingElectric = m_IsChargingElectric;
		bool hadStartedPassCharge = m_HasStartedPassCharge;

		bool passChargeFull =
			m_BallCarrier != null &&
			m_BallCarrier.IsPassChargeFull;

		bool isAllyTarget =
			m_LockOnProvider != null &&
			m_LockOnProvider.HasValidTarget &&
			m_LockOnProvider.CurrentTargetType ==
			ELockOnTargetType.AllyPlayer;

		GameObject passTarget =
			isAllyTarget
			? m_LockOnProvider.CurrentTargetObject
			: null;

		// チャージ状態を解除
		m_IsSubActionHeld = false;
		m_IsChargingElectric = false;
		m_HasStartedPassCharge = false;
		m_SubActionHoldTimer = 0.0f;

		m_LastLoggedPassTarget = null;

		RestoreStateAfterSubAction();

		// 電撃アクション
		if (wasChargingElectric)
		{
			float cost =
				m_ElectricAction != null
				? m_ElectricAction.RequiredCost
				: float.PositiveInfinity;

			if (m_PlayerResource != null &&
				m_PlayerResource.CanAfford(cost) &&
				m_ElectricAction != null &&
				m_ElectricAction.TryReleaseAndFire())
			{
				m_PlayerResource.TryConsume(cost);
			}
			else
			{
				m_ElectricAction?.CancelCharge();
			}

			m_LockOnProvider?.SetSearchMode(
				ELockOnSearchMode.None);

			UpdateActionHelpUI();
			return;
		}

		// 追尾パス発射条件
		if (hadStartedPassCharge &&
			passChargeFull &&
			isAllyTarget &&
			passTarget != null &&
			m_BallCarrier != null &&
			m_BallCarrier.HasBall)
		{
			Debug.Log(
				"[PlayerController] チャージ満タン・味方ロックオン。追尾パス発射");

			m_BallCarrier.ShootHoming(
				passTarget,
				ELockOnTargetType.AllyPlayer);
		}
		// 上記以外は通常投げ
		else if (m_BallCarrier != null &&
				 m_BallCarrier.HasBall)
		{
			Debug.Log(
				"[PlayerController] 通常投げ実行 (ロックオンなし/チャージ不足)");

			HandleShoot();
		}

		// チャージ・ロックオン状態の後処理
		m_BallCarrier?.CancelPassCharge();

		// 追尾パス表示を終了し、電撃ゲージ表示へ戻す
		m_ChargeProgressUI?.SetPassChargeMode(false);

		m_LockOnProvider?.SetSearchMode(
			ELockOnSearchMode.None);

		UpdateActionHelpUI();
	}

	/// <summary>
	/// パスモードとロックオンを解除します。
	/// </summary>
	private void CancelPassMode()
	{
		m_IsPassModeActive = false;
		m_IsPassModeTapHeld = false;
		m_PassModeTimer = 0.0f;

		m_HasStartedPassCharge = false;

		m_BallCarrier?.CancelPassCharge();

		// 追尾パスゲージ表示を解除
		m_ChargeProgressUI?.SetPassChargeMode(false);

		m_LastLoggedPassTarget = null;

		// ロックオン解除
		m_LockOnProvider?.SetSearchMode(ELockOnSearchMode.None);

		Debug.Log("[PlayerController] パスモード解除");
	}

	/// <summary>
	/// 進行中のサブアクション(電撃チャージ・追尾パスチャージ)をキャンセルし状態を復元します。
	/// </summary>
	private void CancelActiveSubAction()
	{
		if (!m_IsSubActionHeld &&
			CurrentState != EPlayerState.ChargingSubAction)
		{
			return;
		}

		m_IsSubActionHeld = false;
		m_IsChargingElectric = false;
		m_HasStartedPassCharge = false;
		m_SubActionHoldTimer = 0.0f;

		m_ElectricAction?.CancelCharge();
		m_BallCarrier?.CancelPassCharge();
		m_ChargeProgressUI?.SetPassChargeMode(false);

		m_LockOnProvider?.SetSearchMode(
			ELockOnSearchMode.None);

		RestoreStateAfterSubAction();

		UpdateActionHelpUI();
	}

	/// <summary>
	/// パスモード中のロックオン対象を確認し、対象が変わった場合にログを出します。
	/// </summary>
	private void LogPassLockOnTarget()
	{
		if (m_LockOnProvider == null ||
			!m_LockOnProvider.HasValidTarget)
		{
			return;
		}

		GameObject target = m_LockOnProvider.CurrentTargetObject;

		if (target == null)
		{
			return;
		}

		// 同じ対象へのログの連続出力を防止
		if (m_LastLoggedPassTarget == target)
		{
			return;
		}

		m_LastLoggedPassTarget = target;

		Debug.Log(
			$"[PassMode] ロックオン成功！ 対象：{target.name} / 種類：{m_LockOnProvider.CurrentTargetType}",
			this
		);
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
	/// 所持中のボールを中央レティクルの照準座標へ射出します。
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
		if (m_AimGuide != null && m_AimGuide.TryGetShotAim(shootDirection,
			out Vector3 targetPoint, out Vector3 aimDirection))
		{
			m_BallCarrier.ShootAt(targetPoint, aimDirection);
			return;
		}

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
	/// 再配置要求を識別し、対象Ownerからの完了応答を待ちます。
	/// </summary>
	public void PrepareForRestart(Vector3 position, Quaternion rotation, bool preserveCameraOrientation = false)
	{
		if (!IsServer || !IsSpawned) return;
		IsReadyForRestart = false;
		m_RestartSequence++;
		PrepareForRestartOwnerRpc(position, rotation, m_RestartSequence, preserveCameraOrientation);
	}

	[Rpc(SendTo.Owner)]
	private void PrepareForRestartOwnerRpc(Vector3 position, Quaternion rotation, uint sequence, bool preserveCameraOrientation)
	{
		SetMatchActionEnabled(false);
		Respawn(position, rotation, preserveCameraOrientation);
		m_PlayerResource?.ResetToFull();
		ConfirmRestartServerRpc(sequence);
	}

	[Rpc(SendTo.Server)]
	private void ConfirmRestartServerRpc(uint sequence, RpcParams rpcParams = default)
	{
		// 過去の要求の応答や、別プレイヤーからの通知を採用しません。
		if (rpcParams.Receive.SenderClientId == OwnerClientId && sequence == m_RestartSequence)
		{
			IsReadyForRestart = true;
		}
	}

	/// <summary>
	/// Host側の試合進行管理(GameManager等)からプレイヤーの行動許可・制限を切り替えるRPC。
	/// </summary>
	/// <param name="actionEnabled">行動を許可する場合はtrue</param>
	[Rpc(SendTo.Owner)]
	public void SetMatchActionEnabledOwnerRpc(bool actionEnabled)
	{
		SetMatchActionEnabled(actionEnabled);
	}

	private void SetMatchActionEnabled(bool actionEnabled)
	{
		if (!IsOwner) return;
		m_IsMatchActionEnabled = actionEnabled;
		m_InputReceiver?.SetInputAllowed(actionEnabled);

		if (!actionEnabled)
		{
			CancelAllActiveActionsAndEffects();
			m_AimGuide?.SetConstantReticleActive(false);
			// 停止処理が速度を書き込めるよう、一時的に物理制御を戻してから最後に固定します。
			if (m_MatchRigidbody != null)
			{
				m_MatchRigidbody.isKinematic = false;
			}
			m_GravityMover?.StopFalling();
			m_SurfaceMover?.SetMoveInput(Vector2.zero, transform);
		}
		m_AimGuide?.SetConstantReticleActive(actionEnabled && CurrentState != EPlayerState.Stunned);
		if (m_MatchRigidbody != null)
		{
			if (!m_MatchRigidbody.isKinematic)
			{
				m_MatchRigidbody.linearVelocity = Vector3.zero;
				m_MatchRigidbody.angularVelocity = Vector3.zero;
			}
			m_MatchRigidbody.isKinematic = !actionEnabled;
		}
	}

	/// <summary>
	/// 場外へ出たプレイヤーまたは得点後再開時のプレイヤーを指定位置へ復帰させます。
	/// </summary>
	/// <param name="position">復帰先のワールド座標</param>
	/// <param name="rotation">復帰先のワールド回転</param>
	public void Respawn(Vector3 position, Quaternion rotation)
	{
		Respawn(position, rotation, false);
	}

	private void Respawn(Vector3 position, Quaternion rotation, bool preserveCameraOrientation)
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

		m_IsStunnedOnFlying = false;
		m_StunDuration = 0.0f;

		CancelAllActiveActionsAndEffects();
		(m_BallCarrier as PlayerBallCarrier)?.ResetActionState();

		if (m_AimGuide is PlayerAimGuide playerAimGuide)
		{
			playerAimGuide.ResetAimGuideForRespawn();
		}

		// 復帰後のレティクル表示
		m_AimGuide?.SetConstantReticleActive(m_IsMatchActionEnabled);

		m_GravityMover?.ResetMovement(position, rotation);
		// Rigidbodyの補間と固定解除を待たず、画面上と通信上の位置を即時に反映します。
		transform.SetPositionAndRotation(position, rotation);
		if (m_NetworkTransform != null && m_NetworkTransform.CanCommitToTransform)
		{
			m_NetworkTransform.Teleport(position, rotation, transform.localScale);
		}
		CurrentState = EPlayerState.Idle;
		m_UsedAimMove = false;
		m_SurfaceMover?.ResetMovement();
		m_GroundDetector?.DetectGround(Vector3.zero, 0f);
		m_CameraEffect?.ResetForRespawn(position, rotation, preserveCameraOrientation);
		m_AnimationController?.ResetForRespawn();
		if (!m_IsMatchActionEnabled && m_MatchRigidbody != null)
		{
			m_MatchRigidbody.isKinematic = true;
		}
	}

	/// <summary>
	/// ボール所持状態の変化通知を受け取り、速度補正を適用します。
	/// </summary>
	/// <param name="hasBall">ボールを所持している場合はtrue</param>
	public void NotifyBallStateChanged(bool hasBall)
	{
		// サブアクション中にボール状態が変化した場合はチャージをキャンセルした上で速度補正を更新する
		if (m_IsSubActionHeld ||
			CurrentState == EPlayerState.ChargingSubAction)
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

		if (CurrentState == EPlayerState.Falling)
		{
			m_IsStunnedOnFlying = true;
			m_StunDuration = duration;
		}
		else
		{
			m_StunCoroutine = StartCoroutine(StunRoutine(duration));
		}
	}

	private IEnumerator StunRoutine(float duration)
	{
		CancelAllActiveActionsAndEffects();

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
		if (!IsOwner)
		{
			m_NetIsMovingEffectActive.OnValueChanged -= HandleNetMovingEffectActiveChanged;
			m_NetMovingEffectEndPoint.OnValueChanged -= HandleNetMovingEffectEndPointChanged;
			if (m_MovingEffect != null)
			{
				m_MovingEffect.SetActive(false);
			}
			return;
		}

		if (IsOwner)
		{
			if (m_GravityMover != null)
			{
				m_GravityMover.OnLanded -= HandleLanded;
			}
			m_InputReceiver?.SetLookInputAllowed(false);
			m_ActionHelpUI?.Hide();
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

	/// <summary>
	/// 現在の入力デバイスと行動状態に応じて操作補助UIを更新します。
	/// </summary>
	private void UpdateActionHelpUI()
	{
		if (m_ActionHelpUI == null)
		{
			return;
		}

		bool canShow =
			IsOwner &&
			m_IsMatchActionEnabled &&
			CurrentState != EPlayerState.Stunned;

		bool hasBall = m_BallCarrier != null && m_BallCarrier.HasBall;
		bool isPassChargeFull =
			hasBall &&
			m_HasStartedPassCharge &&
			m_BallCarrier.IsPassChargeFull;
		bool isAllyTarget =
			m_LockOnProvider != null &&
			m_LockOnProvider.HasValidTarget &&
			m_LockOnProvider.CurrentTargetType == ELockOnTargetType.AllyPlayer;

		bool validElectricTarget =
			m_LockOnProvider != null &&
			m_LockOnProvider.HasValidTarget &&
			(m_LockOnProvider.CurrentTargetType == ELockOnTargetType.Ball ||
			m_LockOnProvider.CurrentTargetType == ELockOnTargetType.EnemyPlayer);
		bool isElectricReady =
			validElectricTarget &&
			m_ElectricAction != null &&
			m_ElectricAction.ChargeRatio >= 1.0f;

		bool isUsingGamepad = m_InputReceiver != null && m_InputReceiver.IsUsingGamepad;

		m_ActionHelpUI.UpdateHelpState(
			canShow,
			m_IsSubActionHeld,
			hasBall,
			isPassChargeFull,
			isAllyTarget,
			m_IsChargingElectric,
			isElectricReady,
			isUsingGamepad);
	}
}
