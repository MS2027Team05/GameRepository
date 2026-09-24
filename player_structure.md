チームメンバーやAIエージェントにそのままタスク指示書として渡せるよう、モジュール（担当）別の仕様・連携手順書を作成しました。

マルチプレイ（Netcode for GameObjects: NGO）における**「誰のPCで動くのか（IsOwner / Host / 全員）」**と**「モジュール間の連携フロー」**を明確に記載しています。

---

# コア設計担当：司令塔・調停役

## 1. `PlayerController.cs` 実装仕様
このクラスは `NetworkBehaviour` を継承し、プレイヤー全体の「状態管理（ステート）」と「各専門モジュールへの命令のルーティング（交通整理）」を担当します。自ら物理計算や描画は行わず、**すべてインターフェース経由で各モジュールに委託**します。

* **ネットワーク上の動作:** **全員の端末にインスタンスが存在**するが、**入力と状態遷移の判定は自分（IsOwner == true）**が行う。
* **依存・保持するもの:**
  * 入力: `PlayerInputReceiver`
  * 移動: `IGravityMover`
  * 照準: `IAimGuide`
  * ボール: `IBallCarrier`
  * 演出: `PlayerCameraEffect`
  * カメラ: `GameObject playerCamera` (Cinemachine等のカメラルート)

(シーン上にNetworkManager componentを持ったObjを出さないとダメかな)
->検証でだした、後はロビー画面作ってホストのip取得して手打ちするウィンドウの用意が必要かな

フロー事態は
title->接続画面(Editorなら出さない。exeの場合はホスト立てた時にip表示してそれを打ってもらう?)->ロビー->ゲームとなってて、ゲーム事態が終わったらロビーに戻す。

---

### ① ネットワーク生成と初期化（スポーン時）
* **トリガー:** ネットワーク上に自他キャラが生成された時（`OnNetworkSpawn`）
* **処理フロー:**
  ```text
  └─ 各インターフェースを取得 (GetComponent<IGravityMover> 等)
  ├─ [IsOwner == false (他人の画面)]
  │    └─ playerCamera を非アクティブ化（画面が他人のカメラに奪われるのを防止）
  │    └─ inputReceiver を無効化（他人のキー入力を遮断）
  │    └─ aimGuide.ShowAimGuide(false)（他人の照準UIを非表示）
  │    └─ 処理を終了（他人のキャラは同期コンポーネントの指示通りに動くだけ）
  └─ [IsOwner == true (自分の画面)]
       └─ playerCamera をアクティブ化
       └─ inputReceiver の各C#イベントを購読 (Subscribe)
       └─ 初期状態を「通常・ホバリング中」に設定
  ```

---

### ② エイムモードの開始・キャンセル（方向指定）
* **トリガー:** `inputReceiver.OnAimButtonPressed`（エイムキー押下）
* **処理フロー:**
  ```text
  └─ スタン中（isStunned）なら処理を弾く
  ├─ [現在エイム中ではない場合]
  │    └─ 状態を「エイム中」に更新
  │    └─ gravityMover.StopFalling() を呼び出し、キャラを空中で浮遊・静止させる
  │    └─ aimGuide.ShowAimGuide(true) で照準と予測線を表示
  └─ [既にエイム中の場合（キャンセル操作）]
       └─ 状態を「待機中」に更新
       └─ aimGuide.ShowAimGuide(false) でUIを非表示
  ```

---

### ③ 落下方向の確定（等速直線移動の開始）
* **トリガー:** `inputReceiver.OnConfirmPressed`（決定キー押下）
* **処理フロー:**
  ```text
  └─ 「エイム中」でなければ処理を弾く
  └─ 状態を「落下中」に更新
  └─ aimGuide.ShowAimGuide(false) で照準UIを消す
  └─ カメラの正面方向ベクトル (playerCamera.transform.forward) を取得
  └─ cameraEffect.PlayFallEffect() で画面揺れ・FOV拡大演出を発火
  └─ gravityMover.StartFalling(fallDirection) で落下移動を開始
     （※この移動開始はNetworkTransform等を通じて全員のPCへ同期される）
  ```

---

### ④ ブレーキ / 緊急停止
* **トリガー:** `inputReceiver.OnBrakePressed`（ブレーキキー押下）
* **処理フロー:**
  ```text
  └─ 「落下中」でなければ処理を弾く
  └─ 状態を「待機（ホバリング）中」に更新
  └─ gravityMover.StopFalling() で慣性をゼロにしてピタッと止める
  ```

---

### ⑤ 外部モジュールからの通知窓口（コールバック）

他の担当モジュールから呼ばれる、安全なデータ連携の窓口です。

#### 1. ボール所持状態の変化
* **呼び出し元:** `PlayerBallCarrier`（ボールを拾った / 奪われた / パスした時）
* **処理フロー:**
  ```text
  └─ public void NotifyBallStateChanged(bool hasBall)
       └─ [hasBall == true の場合]
       │    └─ gravityMover.SetSpeedMultiplier(0.7f) （移動速度デバフを適用）
       └─ [hasBall == false の場合]
            └─ gravityMover.SetSpeedMultiplier(1.0f) （通常速度に復帰）
  ```

#### 2. 被タックル・衝突によるスタン処理
* **呼び出し元:** `PlayerHitDetector` または サーバーRPC
* **処理フロー:**
  ```text
  └─ public void ApplyStun(float duration)
       └─ isStunned フラグを true にする
       └─ aimGuide.ShowAimGuide(false)（エイム中なら強制解除）
       └─ gravityMover.StopFalling()（強制停止またはノックバック）
       └─ コルーチン等で指定秒数（duration）待機後、isStunned を false に戻す
  ```

---

### `PlayerController.cs` の実装コード骨組み（完成イメージ）

設計担当者として、まずこのファイルをプロジェクトに配置し、中身をメンバーに共有してください。

```csharp
using Unity.Netcode;
using UnityEngine;
using System.Collections;

public class PlayerController : NetworkBehaviour
{
    [Header("自端末専用オブジェクト")]
    [SerializeField] private GameObject playerCameraObject;
    [SerializeField] private PlayerInputReceiver inputReceiver;

    // 各担当者が実装するインターフェースの参照
    private IGravityMover gravityMover;
    private IAimGuide aimGuide;
    private IBallCarrier ballCarrier;
    private PlayerCameraEffect cameraEffect;

    // プレイヤーの内部状態
    public enum PlayerState { Idle, Aiming, Falling, Stunned }
    public PlayerState CurrentState { get; private set; } = PlayerState.Idle;

    private void Awake()
    {
        gravityMover = GetComponent<IGravityMover>();
        aimGuide = GetComponent<IAimGuide>();
        ballCarrier = GetComponent<IBallCarrier>();
        cameraEffect = GetComponent<PlayerCameraEffect>();
    }

    public override void OnNetworkSpawn()
    {
        // 他人の画面なら、カメラ・入力を切って処理を終える
        if (!IsOwner)
        {
            if (playerCameraObject != null) playerCameraObject.SetActive(false);
            if (inputReceiver != null) inputReceiver.enabled = false;
            if (aimGuide != null) aimGuide.ShowAimGuide(false);
            return;
        }

        // 自分のキャラならカメラを有効化し、入力をバインドする
        if (playerCameraObject != null) playerCameraObject.SetActive(true);
        BindInputEvents();
    }

    private void BindInputEvents()
    {
        inputReceiver.OnAimTogglePressed += HandleAimToggle;
        inputReceiver.OnConfirmPressed += HandleConfirm;
        inputReceiver.OnBrakePressed += HandleBrake;
    }

    private void Update()
    {
        if (!IsOwner) return;

        // エイム中のみ、毎フレームカメラの正面ベクトルを照準・予測線へ渡す
        if (CurrentState == PlayerState.Aiming)
        {
            aimGuide.UpdateAimDirection(playerCameraObject.transform.forward);
        }
    }

    // --- 状態遷移ロジック ---

    private void HandleAimToggle()
    {
        if (CurrentState == PlayerState.Stunned) return;

        if (CurrentState != PlayerState.Aiming)
        {
            CurrentState = PlayerState.Aiming;
            gravityMover.StopFalling();
            aimGuide.ShowAimGuide(true);
        }
        else
        {
            CurrentState = PlayerState.Idle;
            aimGuide.ShowAimGuide(false);
        }
    }

    private void HandleConfirm()
    {
        if (CurrentState != PlayerState.Aiming) return;

        CurrentState = PlayerState.Falling;
        aimGuide.ShowAimGuide(false);

        Vector3 direction = playerCameraObject.transform.forward;
        if (cameraEffect != null) cameraEffect.PlayFallEffect();
        gravityMover.StartFalling(direction);
    }

    private void HandleBrake()
    {
        if (CurrentState != PlayerState.Falling) return;

        CurrentState = PlayerState.Idle;
        gravityMover.StopFalling();
    }

    // --- 外部モジュール連携用メソッド ---

    public void NotifyBallStateChanged(bool hasBall)
    {
        float speedMultiplier = hasBall ? 0.7f : 1.0f;
        gravityMover.SetSpeedMultiplier(speedMultiplier);
    }

    public void ApplyStun(float duration)
    {
        StartCoroutine(StunRoutine(duration));
    }

    private IEnumerator StunRoutine(float duration)
    {
        CurrentState = PlayerState.Stunned;
        aimGuide.ShowAimGuide(false);
        gravityMover.StopFalling();

        yield return new WaitForSeconds(duration);

        CurrentState = PlayerState.Idle;
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner && inputReceiver != null)
        {
            inputReceiver.OnAimTogglePressed -= HandleAimToggle;
            inputReceiver.OnConfirmPressed -= HandleConfirm;
            inputReceiver.OnBrakePressed -= HandleBrake;
        }
    }
}
```

---

# 担当A：物理・移動担当

## 1. `GravityMover.cs` 実装仕様
このクラスは `PlayerController` からインターフェース `IGravityMover` 経由で呼び出され、キャラクターの物理移動・姿勢・着地制御を担当します。

* **ネットワーク上の動作:** **全員の画面で動作**（位置同期は `NetworkTransform` コンポーネントに任せるか、Owner側で計算して同期）
* **必須コンポーネント:** `Rigidbody` (useGravity = false, isKinematic = false)

### ① 等速直線運動（重力落下）
* **トリガー:** `PlayerController` から `StartFalling(Vector3 direction)` が呼ばれた時
* **処理フロー:**
  ```text
  └─ 落下フラグ (isFalling) を true に設定
  └─ 引数の direction を正規化 (normalized) して currentDirection に保持
  └─ FixedUpdate にて:
      rb.linearVelocity = currentDirection * (baseFallSpeed * speedMultiplier)
      transform.forward = currentDirection (進行方向を向く)
  ```

### ② ブレーキ / 軌道キャンセル（ホバリング）
* **トリガー:** `PlayerController` から `StopFalling()` が呼ばれた時（エイム移行時やブレーキボタン押下時）
* **処理フロー:**
  ```text
  └─ 落下フラグ (isFalling) を false に設定
  └─ rb.linearVelocity = Vector3.zero (慣性を完全に消して空中で静止)
  ```

### ③ 姿勢制御と接地（着地）判定
* **トリガー:** 壁・床・浮遊物との衝突（`OnCollisionEnter`）
* **処理フロー:**
  ```text
  └─ 衝突相手の接地面の法線ベクトル (hitNormal) を取得
  └─ StopFalling() を呼び出して落下を停止
  └─ キャラクターの「足元」が衝突面を向くように回転を補正
      Quaternion targetRot = Quaternion.FromToRotation(transform.up, hitNormal) * transform.rotation
      transform.rotation = targetRot
  └─ 接地状態 (isGrounded = true) へ遷移
  ```

### ④ 移動速度補正（デバフ反映）
* **トリガー:** `PlayerController` から `SetSpeedMultiplier(float multiplier)` が呼ばれた時
* **処理フロー:**
  ```text
  └─ 内部変数 currentSpeedMultiplier を更新（例: ボール所持時は 0.7f、通常時は 1.0f）
  └─ 次の FixedUpdate の速度計算に即座に反映される
  ```

---

# 担当B：エイム・演出・カメラ担当

## 1. `PlayerAimGuide.cs` ＆ `PlayerCameraEffect.cs` 実装仕様
このモジュールは、照準表示・着地予測線の描画およびカメラ操作・演出を担当します。

* **ネットワーク上の動作:** **自分のみ（IsOwner == true）** ※他人の画面では非表示・停止させること

### ① エイム開始とホバリング移行
* **トリガー:** `PlayerController` から `ShowAimGuide(true)` が呼ばれた時
* **処理フロー:**
  ```text
  └─ 照準UI（レティクル）と軌道予測線 (LineRenderer) をアクティブ化
  └─ （※GravityMover.StopFalling() はPlayerController側で並行して呼ばれる）
  ```

### ② 予測線と着地プレビューのリアルタイム描画
* **トリガー:** エイムモード中の毎フレーム（`Update`）
* **処理フロー:**
  ```text
  └─ カメラの正面方向へ Raycast を飛ばす（Physics.Raycast）
  ├─ [何かに当たった場合]
  │    └─ 当たった座標 (hit.point) まで LineRenderer で予測線を引く
  │    └─ 着地マーカーUIを hit.point に配置し、法線に合わせてサークルを表示
  └─ [何にも当たらない場合]
       └─ 最大射程（例: 100m先）まで直線を描画し、マーカーは非表示
  ```

### ③ 確定演出（カメラシェイク・画角演出）
* **トリガー:** `PlayerController` から確定キー入力の通知を受けた時
* **処理フロー:**
  ```text
  └─ ShowAimGuide(false) でUIを非表示
  └─ Cinemachine Impulse Source を発火させて画面を短時間振動させる
  └─ CinemachineのFOV（画角）を一時的に広げ、高速移動のスピード感を演出
  ```

---

# 担当C：球技・ボール担当

## 1. `PlayerBallCarrier.cs` ＆ `BallController.cs` 実装仕様
ボールの所持状態・パス・シュートおよびボール自体の同期を担当します。判定のズレを防ぐため、**状態の確定はHost（サーバー）**が行います。

* **ネットワーク上の動作:**
  * 入力・射出リクエスト: **自分 (IsOwner)**
  * 所持判定・物理判定: **Host (サーバー)**
  * 見た目の同期: **全員**

### ① ボールキャッチ（接触時）
* **トリガー:** プレイヤーがボールのコライダーに触れた時（`OnTriggerEnter`）
* **処理フロー:**
  ```text
  └─ 触れたクライアント ──▶ [ServerRpc] でHostへ「ボールに触れました」と通知
  └─ [Host側での処理]:
      └─ ボールがフリー状態か確認（二重取得防止）
      └─ ボール所持者変数 (NetworkVariable<ulong> currentHolderId) を更新
  └─ [全員の画面で反映 (NetworkVariable経由)]:
      └─ ボールオブジェクトを該当プレイヤーの「手元ボーン」に親子付け (Transform.SetParent)
      └─ ボール自体のRigidbodyを kinematic = true にして物理を止める
  └─ [所持者本人のPCのみ]:
      └─ PlayerController.NotifyBallStateChanged(true) を呼び出す
         └─ GravityMover の速度倍率が 0.7f に減速
  ```

### ② パス / シュート処理
* **トリガー:** ボール所持中にシュートボタンを押した時
* **処理フロー:**
  ```text
  └─ [IsOwner] ──▶ [ServerRpc] でHostへ「方向ベクトル (shootDir)」を添えて要求送信
  └─ [Host側での処理]:
      └─ ボールの親子付けを解除 (SetParent = null)
      └─ ボール自体のRigidbodyを kinematic = false に戻す
      └─ shootDir 方向へ力を加える (rb.linearVelocity = shootDir * shootPower)
      └─ currentHolderId を「なし (0)」に更新
  └─ [元所持者のPCのみ]:
      └─ PlayerController.NotifyBallStateChanged(false) を呼び出す
         └─ GravityMover の速度倍率を 1.0f（等倍）に復旧
  ```

---

# 担当D：当たり判定・ステージ環境担当

## 1. `PlayerHitDetector.cs` ＆ `StageManager.cs` 実装仕様
プレイヤー同士の衝突（タックルによるボール剥奪）、浮遊物との判定、およびコート外への落下判定を担当します。

### ① タックルによるボール剥奪
* **トリガー:** プレイヤー同士の接触（`OnCollisionEnter` / `OnTriggerEnter`）
* **処理フロー:**
  ```text
  └─ 相手が「ボール所持者」かつ自身が「落下移動中（タックル攻撃）」か判定
  └─ 条件を満たしたら ──▶ [ServerRpc] でHostへ「タックル成功」を通知
  └─ [Host側での処理]:
      └─ ボール所持者の PlayerBallCarrier.ForceReleaseBall() を呼び出してボールをドロップさせる
      └─ 被タックル者にスタン（一定時間移動停止）フラグを付与
  ```

### ② 浮遊物との衝突判定
* **トリガー:** フィールドに浮いている障害物への接触
* **処理フロー:**
  ```text
  └─ 浮遊物側の重さ（質量）に応じて、プレイヤーを跳ね返すか破壊するかを判定
  └─ 単なる障害物の場合は GravityMover.StopFalling() を呼んで姿勢制御（着地）へ移行
  ```

### ③ 場外判定（アウトオブバウンズ）とリスポーン
* **トリガー:** フィールドの外枠に配置した見えない巨大トリガー（`StageBoundaryTrigger`）に侵入した時
* **処理フロー:**
  ```text
  └─ [Host側でのみ検知]:
  ├─ [侵入したのがプレイヤーの場合]:
  │    └─ 落下を即座に停止 (StopFalling)
  │    └─ 自陣側の安全なリスポーン地点の座標へ transform.position を強制移動
  └─ [侵入したのがボールの場合]:
       └─ ボールの速度をゼロにし、コート中央（センタースポット）へリセット
  ```