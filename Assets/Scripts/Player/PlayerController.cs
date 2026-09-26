using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// プレイヤー全体の「状態管理(ステート)」と「各専門モジュールへの命令ルーティング」を担当するクラス。
/// 自ら物理計算や描画は行わず、インターフェース経由で各モジュールに委託します。
/// </summary>
public class PlayerController : NetworkBehaviour
{
    [Header("自端末専用オブジェクト")]
    [SerializeField] private GameObject playerCameraObject;
    [SerializeField] private PlayerInputReceiver inputReceiver;

    // 各担当者が実装するインターフェース・コンポーネント参照
    private IGravityMover gravityMover;
    private IAimGuide aimGuide;
    private IBallCarrier ballCarrier;
    private PlayerCameraEffect cameraEffect;

    // プレイヤーの内部状態
    public enum PlayerState { Idle, Aiming, Falling, Stunned }
    public PlayerState CurrentState { get; private set; } = PlayerState.Idle;

    private Coroutine stunCoroutine;

    private void Awake()
    {
        // 各インターフェースおよび演出コンポーネントの取得
        gravityMover = GetComponent<IGravityMover>();
        aimGuide = GetComponent<IAimGuide>();
        ballCarrier = GetComponent<IBallCarrier>();
        cameraEffect = GetComponent<PlayerCameraEffect>();

        if (inputReceiver == null)
        {
            inputReceiver = GetComponent<PlayerInputReceiver>();
        }
    }

    public override void OnNetworkSpawn()
    {
        // 他人の画面なら、自機カメラ・入力を切って処理を終える
        if (!IsOwner)
        {
            if (playerCameraObject != null) playerCameraObject.SetActive(false);
            if (inputReceiver != null) inputReceiver.enabled = false;
            aimGuide?.ShowAimGuide(false);
            return;
        }

        // 自分のキャラならカメラを有効化し、入力をバインドする
        if (playerCameraObject != null) playerCameraObject.SetActive(true);
        BindInputEvents();
    }

    private void BindInputEvents()
    {
        if (inputReceiver == null) return;

        inputReceiver.OnAimTogglePressed += HandleAimToggle;
        inputReceiver.OnConfirmPressed += HandleConfirm;
        inputReceiver.OnBrakePressed += HandleBrake;
    }

    private void UnbindInputEvents()
    {
        if (inputReceiver == null) return;

        inputReceiver.OnAimTogglePressed -= HandleAimToggle;
        inputReceiver.OnConfirmPressed -= HandleConfirm;
        inputReceiver.OnBrakePressed -= HandleBrake;
    }

    private void Update()
    {
        if (!IsOwner) return;

        // エイム中のみ、毎フレームカメラの正面ベクトルを照準・予測線へ渡す
        if (CurrentState == PlayerState.Aiming && aimGuide != null)
        {
            Vector3 aimDirection = playerCameraObject != null
                ? playerCameraObject.transform.forward
                : transform.forward;

            aimGuide.UpdateAimDirection(aimDirection);
        }
    }

    // --- 状態遷移ロジック ---

    private void HandleAimToggle()
    {
        if (CurrentState == PlayerState.Stunned) return;

        if (CurrentState != PlayerState.Aiming)
        {
            CurrentState = PlayerState.Aiming;
            gravityMover?.StopFalling();
            aimGuide?.ShowAimGuide(true);
        }
        else
        {
            CurrentState = PlayerState.Idle;
            aimGuide?.ShowAimGuide(false);
        }
    }

    private void HandleConfirm()
    {
        if (CurrentState != PlayerState.Aiming) return;

        CurrentState = PlayerState.Falling;
        aimGuide?.ShowAimGuide(false);

        Vector3 direction = playerCameraObject != null
            ? playerCameraObject.transform.forward
            : transform.forward;

        cameraEffect?.PlayFallEffect();
        gravityMover?.StartFalling(direction);
    }

    private void HandleBrake()
    {
        if (CurrentState != PlayerState.Falling) return;

        CurrentState = PlayerState.Idle;
        gravityMover?.StopFalling();
    }

    // --- 外部モジュール連携用メソッド ---

    /// <summary>
    /// ボール所持状態の変化通知を受け取り、速度補正を適用します。
    /// </summary>
    /// <param name="hasBall">ボールを所持している場合はtrue</param>
    public void NotifyBallStateChanged(bool hasBall)
    {
        float speedMultiplier = hasBall ? 0.7f : 1.0f;
        gravityMover?.SetSpeedMultiplier(speedMultiplier);
    }

    /// <summary>
    /// 被タックル・衝突によるスタン処理を開始します。
    /// </summary>
    /// <param name="duration">スタン持続時間(秒)</param>
    public void ApplyStun(float duration)
    {
        if (stunCoroutine != null)
        {
            StopCoroutine(stunCoroutine);
        }
        stunCoroutine = StartCoroutine(StunRoutine(duration));
    }

    private IEnumerator StunRoutine(float duration)
    {
        CurrentState = PlayerState.Stunned;
        aimGuide?.ShowAimGuide(false);
        gravityMover?.StopFalling();

        yield return new WaitForSeconds(duration);

        CurrentState = PlayerState.Idle;
        stunCoroutine = null;
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner)
        {
            UnbindInputEvents();
        }
    }
}
