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

	// 各担当者が実装するインターフェース・コンポーネント参照
	private IGravityMover m_GravityMover;
	private IAimGuide m_AimGuide;
	private IBallCarrier m_BallCarrier;
	private ICameraEffect m_CameraEffect;

	// プレイヤーの内部状態
	public enum EPlayerState { Idle, Aiming, Falling, Stunned }
	public EPlayerState CurrentState { get; private set; } = EPlayerState.Idle;

	private Coroutine m_StunCoroutine;

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
	}

	private void UnbindInputEvents()
	{
		if (m_InputReceiver == null) return;

		m_InputReceiver.OnAimTogglePressed -= HandleAimToggle;
		m_InputReceiver.OnConfirmPressed -= HandleConfirm;
		m_InputReceiver.OnBrakePressed -= HandleBrake;
		m_InputReceiver.OnShootPressed -= HandleShoot;
	}

	private void Update()
	{
		if (!IsOwner) return;

		// エイム中のみ、実際に画面を描画しているカメラの
		// 正面ベクトルを照準・予測線へ渡します。
		if (CurrentState == EPlayerState.Aiming && m_AimGuide != null)
		{
			Vector3 aimDirection = GetAimDirection();
			m_AimGuide.UpdateAimDirection(aimDirection);
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

		if (CurrentState != EPlayerState.Aiming)
		{
			CurrentState = EPlayerState.Aiming;
			m_GravityMover?.StopFalling();
			m_AimGuide?.ShowAimGuide(true);
		}
		else
		{
			CurrentState = EPlayerState.Idle;
			m_AimGuide?.ShowAimGuide(false);
		}
	}

	private void HandleConfirm()
	{
		if (CurrentState != EPlayerState.Aiming) return;

		CurrentState = EPlayerState.Falling;
		m_AimGuide?.ShowAimGuide(false);

		Vector3 direction = GetAimDirection();

		m_CameraEffect?.PlayFallEffect();
		m_GravityMover?.StartFalling(direction);
	}

	private void HandleBrake()
	{
		if (CurrentState != EPlayerState.Falling) return;

		CurrentState = EPlayerState.Idle;
		m_GravityMover?.StopFalling();
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
