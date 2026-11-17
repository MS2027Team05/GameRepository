# ディレクトリ構造 (STRUCTURE.md)

本プロジェクトのディレクトリ構成およびスクリプト配置を記録するドキュメントです。各階層構造の責務と重要なエントリーポイントを記載します。

試合開始・得点後の再開は`Game/MatchManager`を入口とし、サーバーが共通のカウントダウン終了時刻を同期します。
`Network/PlayerSpawner`は生成時の陣営別配置を保持してOwnerへ再配置・行動制限を通知し、`Ball/BallController`は所持解除・初期位置への復帰・物理停止を担当します。
`UI/MatchHudController`は同期された終了時刻からカウントダウンを表示します。カウントダウン中は試合時計を停止し、得点と秒未満を含む残り時間を維持します。
再開時は`PreparingRestart`で待機し、各`PlayerController`がOwner側で位置を即時反映して返す完了応答を`PlayerSpawner`が確認した後に、`Countdown`へ遷移します。
復帰時の表示初期化は`PlayerController.Respawn`から各専門モジュールへ委譲します。カメラは視点・追従・FOVを即時に復帰姿勢へ戻し、地上移動は速度履歴を消去します。アニメーションはOwnerが復帰世代を同期し、全端末でAnimatorの初期状態を再評価します。
ゴール演出は`MatchManager.TryAddGoalScore`で得点確定後にRPCで通知し、各端末の`GoalController`が得点先チームに対応する配置済みParticleSystemを再生します。ゴールごとのNetworkObjectやエフェクトの都度生成は使用しません。
得点後は`GoalCelebration`で全プレイヤーとボールをその場に停止します。各端末の`GoalController`が子を含めた粒子の消滅を確認し、`MatchManager`が接続中の対象端末すべての完了を受信してから再配置へ進めます。`MatchHudController`は同じ状態に連動してInspector指定の得点メッセージを表示します。
得点メッセージの文言はHUDのInspectorでチームごとに設定し、`MatchManager.GoalScoringTeam`が同期する得点先から選択します。
ゴールリプレイは`Game/Replay/ReplayRecorder`がクライアント側リングバッファへプレイヤーとボールの座標・状態を記録し、`MatchManager`の再生設定(`AfterEveryGoal` / `AfterMatchBestGoal` / `Both`)に応じて`GoalCelebration`後または試合終了後に`GoalReplay`へ遷移して再生する。ベストゴール判定は`IGoalHighlightEvaluator`に分離し、試合内の貢献度スコアとも連携する。
試合終了時の個人表彰(称号)と展示用累計統計は`Game/Stats/MatchStatsTracker`が各クライアント(移動・燃料・視認時間)とサーバー(パス・パスカット・セーブ・ゴール・アシスト・陣地・保持時間・スタン・投球内訳)の記録を集約し、`MatchAwardEvaluator`で各プレイヤー1〜3個の称号を選出後、`MatchManager`が`SessionPlayerDataStore`経由でリザルト画面へ引き継ぐ。リザルト画面ではローカルプレイヤー自身の個人表彰のみを`UI/ResultAwardItemUI`で表示する。累計統計は`ExhibitionStatsStore`がローカルJSON(`Path.Combine(Application.persistentDataPath, "ExhibitionStats.json")` / Windows標準例: `%USERPROFILE%\AppData\LocalLow\<CompanyName>\<ProductName>\ExhibitionStats.json`)へ保存し(ロビー出発時のキャラ・カラー選択累計も記録)、F1スタッフメニューから参照・初期化できる。また、1試合ごとの全体・チーム別・個人別デバッグ統計は`MatchDebugStatsStore`が`MatchStatsLogs/Match_XXXX_yyyyMMdd_HHmmss.json`へ整形出力する。
リザルト画面(`ResultUIController`)から`LobbyScene`へ戻る際は、ホスト(Server)が通信を切断せずに`SessionPlayerDataStore.ResetAllReadyStates()`および`NetworkManager.Singleton.SceneManager.LoadScene`を実行し、全端末を同期して`LobbyScene`へ復帰させる(ただし遷移先が`TitleScene`の場合は従来通り切断してタイトルへ戻る)。

```text
GameRepository/
├── .agents/                     # AIエージェント設定およびカスタムスキル格納
│   └── skills/                  # Unity公式AIエージェントSkills
├── Assets/
│   ├── Audios/                  # 音声リソース格納 (AI学習対象外)
│   ├── Effect/                  # エフェクト用プレハブ格納
│   ├── Fonts/                   # フォントリソース格納 (AI学習対象外)
│   ├── haikei/                  # 背景モデル・マテリアル・テクスチャ・Prefab格納
│   ├── Materials/               # マテリアル・シェーダー関連格納
│   │   ├── Ball/                # ボール描画用マテリアル (通常・透過X-Ray等)
│   │   └── Player/              # プレイヤー用マテリアル格納 (性別・チームカラー別)
│   ├── Meshes/                  # 3Dモデル・メッシュデータ格納 (AI学習対象外)
│   │   └── Material/Models/     # キャラクター3Dモデル(FBX)およびアニメーション格納 (AI学習対象外)
│   ├── Plugins/                 # 外部プラグインライブラリ格納 (DOTween等)
│   ├── Prefabs/                 # ゲーム内プレハブ格納
│   ├── Resources/               # 動的読み込み用アセット格納
│   ├── Scenes/                  # 各画面・遷移用シーン格納
│   ├── Scripts/                 # プロジェクト固有のC#スクリプト
│   │   ├── Ball/                # ボール物理・同期制御
│   │   │   ├── BallController.cs        # [EntryPoint] ボール状態管理・所持・シュート制御
│   │   │   ├── Physics/                 # ボール物理状態・衝突・Surface連携
│   │   │   │   ├── BallPhysicsController.cs           # [EntryPoint] Free Flight・Surface Physics状態統括
│   │   │   │   ├── BallPhysicsController.Air.cs       # 飛行・Surface探索・吸着処理
│   │   │   │   ├── BallPhysicsController.Collision.cs # Collision処理
│   │   │   │   ├── BallPhysicsController.Homing.cs    # 追尾パス物理処理
│   │   │   │   ├── BallPhysicsController.Surface.cs   # BallSurface Physics連携
│   │   │   │   ├── BallBounce.cs                      # ボールの跳ね返り挙動制御
│   │   │   │   └── Attraction/                        # 疑似重力・Surface探索
│   │   │   │       ├── BallSurfaceGravity.cs          # ボールの接地面重力・吸着制御
│   │   │   │       └── BallSurfaceSearch.cs           # ボール周辺の面探索制御
│   │   │   ├── Surface/                 # BallSurfaceのMesh解析・曲面上移動シミュレーション
│   │   │   │   ├── BallSurfaceSource.cs       # [EntryPoint] MeshColliderからSurfaceデータを構築
│   │   │   │   ├── BallSurfaceMeshData.cs     # Triangle・Edge・隣接情報の解析データ
│   │   │   │   ├── BallSurfaceSimulation.cs   # Surface Physicsの共通状態・進行処理
│   │   │   │   └── Simulation/                # Edge種別・形状別のSurface遷移処理
│   │   │   │       ├── BallSurfaceSimulation.Edge.cs
│   │   │   │       ├── BallSurfaceSimulation.FlatSmooth.cs
│   │   │   │       ├── BallSurfaceSimulation.Convex.cs
│   │   │   │       ├── BallSurfaceSimulation.Concave.cs
│   │   │   │       ├── BallSurfaceSimulation.ConcaveAirborne.cs
│   │   │   │       └── BallSurfaceSimulation.Vertex.cs
│   │   │   ├── UI/
│   │   │   │   ├── BallScreenIndicator.cs     # ボール位置表示用画面内・画面外インジケーターUI制御
│   │   │   │   └── BallSpectatorHighlight.cs  # 観戦時のボール最小視認スケール維持・強調制御
│   │   │   └── Debug/
│   │   │       └── BallDebugRespawner.cs      # ボール位置リセット用デバッグ機能
│   │   ├── Game/                # 試合進行・ルール管理。ゴール範囲からの通知を得点先へ接続し、両ゴール共通の得点制限をサーバー側で管理
│   │   │   ├── MatchManager.cs          # [EntryPoint] 試合状態・制限時間・チーム得点のサーバー権限管理・リザルト遷移演出制御
│   │   │   ├── GoalController.cs        # ゴール構造管理・得点判定委譲・ゴール演出Particle再生制御
│   │   │   ├── GoalArea.cs              # ゴール判定コライダー検知・GoalControllerへの通知制御
│   │   │   ├── Replay/                  # ゴールリプレイ記録・ベストゴール評価・表示専用再生制御
│   │   │   │   ├── GoalHighlightEvaluation.cs # ゴールハイライト評価コンテキスト・IGoalHighlightEvaluator定義
│   │   │   │   ├── ReplayFrameData.cs   # リプレイフレーム記録構造体・クリップデータ定義
│   │   │   │   ├── ReplayRecorder.cs    # [EntryPoint] クライアント側リングバッファ記録・クリップ切り出し
│   │   │   │   └── ReplayPlayer.cs      # [EntryPoint] 表示専用アクター制御・ゴールカメラ視点再生・復帰
│   │   │   ├── Stats/                   # 試合行動統計・個人表彰(称号)・展示用累計統計
│   │   │   │   ├── MatchAwardData.cs    # 統計・称号・同期ペイロードの共通データ定義
│   │   │   │   ├── MatchAwardEvaluator.cs # No.1/No.2個人表彰(最大3個)の選出とラベル整形
│   │   │   │   ├── ExhibitionStatsStore.cs # 展示用累計統計・キャラ/カラー選択累計のJSON永続化・サマリー生成
│   │   │   │   ├── MatchDebugStatsStore.cs # 1試合ごとの全体・チーム別・個人別デバッグ統計の個別JSON保存
│   │   │   │   └── MatchStatsTracker.cs # [EntryPoint] クライアント/サーバー行動集計・確定処理
│   │   │   └── Spectator/               # 観戦カメラ演出・自動スイッチング制御
│   │   │       ├── SpectatorCameraDirector.cs # [EntryPoint] 定点・天地固定追従・ゴール前カメラの自動スイッチング制御
│   │   │       └── SpectatorOverlayUIController.cs # 観戦中継テロップ(カメラ名・ボール保持チーム)表示制御
│   │   ├── Editor/              # エディタ拡張・開発支援ツール
│   │   │   ├── SetupResultAndStaffMenu.cs # [EntryPoint] スタッフメニュー拡張・GameScene配置・ResultScene自動生成ツール
│   │   │   ├── StageObjectBilateralPlacerEditor.cs # StageObjectBilateralPlacer用カスタムInspector(手動/編集時再構築・Prefab誤編集防止)
│   │   │   ├── StagePrefabReplacerWindow.cs # [EntryPoint] Model PrefabからPrefab Variantへの一括置換・Transform保持ツール
│   │   │   └── StageSymmetricSynchronizerEditor.cs # [EntryPoint] ステージ地形の点対称(180度回転)同期・チーム別Prefab自動置換Inspector拡張
│   │   ├── Network/             # 通信接続・同期制御
│   │   │   ├── NetworkConnectManager.cs # [EntryPoint] 接続初期化・起動制御
│   │   │   ├── LobbyManager.cs          # [EntryPoint] ロビー通信同期・ゲーム開始制御
│   │   │   ├── LobbyUIController.cs     # ロビー画面UI表示・端末ロール別パネル制御
│   │   │   ├── LobbyAppearancePickerUI.cs # ロビー内モデル(男女)・カラー(12色)共通選択ピッカーUI制御
│   │   │   ├── LobbyCharacterDisplayManager.cs # ロビー内3Dモデル表示・台座同期制御
│   │   │   ├── LobbyPlayerDisplay.cs    # ロビー台座用の表示特化キャラクター外見・頭上3Dネーム追従制御
│   │   │   ├── LobbyPlayerData.cs       # ロビー参加プレイヤー同期データ構造体
│   │   │   ├── LocalPlayerProfilePreferences.cs # 端末別設定(キャラ・色・固定チーム・IP・視点感度)永続化・自動反映
│   │   │   ├── SessionPlayerDataStore.cs# ロビー設定永続化・試合結果(スコア/勝敗)保持・シーン間データ引継ぎ管理
│   │   │   ├── PlayerSpawner.cs         # 陣営別プレイヤースポーン制御
│   │   │   ├── TitleOverlayController.cs # [EntryPoint] ロビー内タイトル画面演出・個別開始制御
│   │   │   ├── StaffInspectionMenuController.cs # [EntryPoint] F1キー緊急管理・診断・インゲーム操作・統計ページ切替制御
│   │   │   ├── GameSessionResetCoordinator.cs # [EntryPoint] ゲーム終了時の動的オブジェクト破棄・セッション初期化・ロビー復帰制御
│   │   │   └── TutorialManager.cs       # [EntryPoint] チーム別チュートリアル進捗監視・同期遷移制御
│   │   ├── Shader/              # シェーダー連携・描画制御スクリプト
│   │   │   ├── PlayerOutline.cs         # アウトライン描画制御
│   │   │   ├── PlayerSilhouette.cs      # チーム別アウトライン・味方/観戦者向け遮蔽シルエット描画制御
│   │   │   └── ToolSilhouette.cs        # ツール類遮蔽時シルエット描画制御
│   │   ├── Stage/               # ステージ・地形管理スクリプト
│   │   │   ├── StageSymmetricSynchronizer.cs # [EntryPoint] ステージ地形の点対称同期設定・データ保持コンポーネント
│   │   │   ├── StageRailRingBuilder.cs       # [EntryPoint] 外周レール(3種)・往復キューブ・外側足場一括生成コンポーネント
│   │   │   ├── StageRailPatternSwitcher.cs   # 外周レール(3種+None)x足場(On/Off)2x3パターン切り替え・マルチ同期コンポーネント
│   │   │   ├── StageShuttleBlock.cs          # 4分割レール区間を往復移動するブロック(キューブ)コンポーネント
│   │   │   └── StageObjectBilateralPlacer.cs # 基準BoxColliderサイズに基づく等間隔両端配置コンポーネント(CustomEditorから再構築)
│   │   └── Player/              # プレイヤー制御・挙動ロジック
│   │       ├── PlayerNetworkProfile.cs  # プレイヤープロファイル(名前・チーム・カラー等)同期制御
│   │       ├── PlayerNamePlate.cs       # 頭上3Dネームプレート・Billboard追従・距離スケール補正・敵味方表示制御
│   │       ├── PlayerAppearanceCatalog.cs # 男女別12色マテリアル・チームマテリアル・UIプレビュー色管理用ScriptableObject
│   │       ├── Interfaces/      # 各サブモジュール用インターフェース
│   │       │   ├── IAimGuide.cs         # 常時レティクル・地上予備浮上・着地点追跡/シフト制御インターフェース
│   │       │   ├── IBallCarrier.cs      # ボール所持・単押しパス・追尾パスチャージ制御インターフェース
│   │       │   ├── ICameraEffect.cs     # カメラ演出・移動基準基底提供用インターフェース
│   │       │   ├── IElectricAction.cs   # 電撃チャージ・発射(ボール奪取/敵スタン)制御インターフェース
│   │       │   ├── IGravityMover.cs     # 等速直線移動(落下)・ホバリング制御インターフェース
│   │       │   ├── IGroundDetector.cs   # 接地検知・法線計測インターフェース
│   │       │   ├── ILockOnProvider.cs   # 優先度付きロックオン対象検索・捕捉インターフェース
│   │       │   ├── IPlayerResource.cs   # プレイヤーリソース(電力)消費・回復管理インターフェース
│   │       │   ├── IPlayerRespawnHandler.cs # 場外復帰要求の受け口インターフェース
│   │       │   └── ISurfaceMover.cs     # 地上・曲面歩行移動インターフェース
│   │       ├── ElectricAction.cs        # 電撃チャージ・発射・ボール奪取・敵スタン命中処理制御
│   │       ├── GravityMover.cs          # 等速直線移動(落下)・空中ホバリング・着地衝突制御
│   │       ├── LandingMarkerEffect.cs   # 着地予測マーカーの描画・アニメーション演出制御
│   │       ├── LockOnManager.cs         # ロックオン制御(ILockOnProvider実装・優先度付き対象検索)
│   │       ├── PlayerAimGuide.cs        # 常時レティクル・地上予備浮上・着地点追跡およびエイム照準制御
│   │       ├── PlayerAnimationController.cs # 地上移動速度に基づくAnimator状態切り替え・移動状態の通信同期
│   │       ├── PlayerAppearance.cs      # プレイヤー外見(性別モデル・12色マテリアル・チームカラー)管理制御
│   │       ├── PlayerBallCarrier.cs     # プレイヤーのボール所持・キャッチ・単押しパス/追尾パス制御
│   │       ├── PlayerCameraEffect.cs    # 三人称視点カメラ旋回(接地面法線追従)・高速移動時FOV変更・カメラシェイク制御・視点リセット
│   │       ├── PlayerEdgeDiagnostics.cs # 角部・接地境界脱落時の挙動解析・診断HUD描画制御
│   │       ├── PlayerGroundDetector.cs  # 足元/角/壁面の接地・法線検知およびデバッグGizmo描画
│   │       ├── PlayerInputReceiver.cs   # 入力検知(R1移動・L1干渉アクション押下/離し対応)・視点操作(マウス/PAD感度適用)・カーソルロック管理
│   │       ├── PlayerRecoveryEffectController.cs # リソース急速回復・パスボーナス時のVFX再生およびネットワーク同期制御
│   │       ├── PlayerResourceController.cs # [EntryPoint] Owner側リソース消費・通常回復・遅延急速回復・パス受取ボーナス管理
│   │       ├── PlayerOutOfBoundsRespawner.cs # [EntryPoint] XYZ範囲外検知・初期スポーン地点への復帰要求
│   │       ├── PlayerSurfaceMover.cs    # 地上/曲面歩行移動・運動量転送・ダウンフォース・姿勢回転制御
│   │       └── PlayerController.cs      # [EntryPoint] プレイヤー状態統括・アクションフロー調停・OwnerRpc窓口
│   │   └── UI/                  # 汎用・共通UI制御
│   │       ├── UIInputReceiver.cs       # UI系統入力検知・InputActionインスタンス分離制御
│   │       ├── UIFocusGlowOutline.cs    # 汎用UIフォーカス(Pad選択/マウスホバー)発光黄色枠線制御
│   │       ├── PlayerResourceGaugeUI.cs # リソース現在値・最大値からFilled Imageを更新するゲージUI
│   │       ├── InGamePauseMenuController.cs # [EntryPoint] インゲームEscメニュー・設定パネル連携・IP表示・ロビー復帰制御
│   │       ├── MatchHudController.cs    # 試合残り時間・チーム得点のHUD表示制御
│   │       ├── ResultAwardItemUI.cs     # リザルト画面の個人表彰1項目分(金/銀メダル・文言・個別背景)表示制御
│   │       ├── ResultUIController.cs    # [EntryPoint] リザルト画面制御・スコア/プレイヤー外見/自身の実績表示・復帰制御
│   │       ├── SettingsPanelController.cs # 設定画面(視点感度・音量・描画設定)開閉・UI制御
│   │       └── SceneTransitionEffect.cs # 画面トランジション(水平ワイプ・暗転解除演出)制御
│   ├── Settings/                # プロジェクト設定・URP描画設定
│   ├── Shader/                  # カスタムシェーダー格納 (アウトライン・遮蔽シルエット等)
│   ├── SkySeries Freebie/       # Skybox背景アセット格納
│   ├── TextMesh Pro/            # TextMesh Pro関連リソース
│   ├── Textures/                # テクスチャ・スプライト格納 (AI学習対象外)
│   └── VFX/                     # VFX関連リソース
├── Tools/                       # 開発・展示運用・ネットワーク支援ツール
│   └── Firewall/                # 展示用ファイアウォールポート開放・解除バッチ
├── docs/                        # 企画書・仕様書等の設計ドキュメント格納
├── AGENTS.md                    # AI用ルール・規約定義
├── player_structure.md          # プレイヤーモジュール仕様・連携手順書
└── STRUCTURE.md                 # 本ドキュメント
```

## ボール所持ジョイントへの追従
- BallControllerは所持者の有無と所持者NetworkObjectIdの変更を各端末へ反映し、サーバー上の物理・通信用本体をLateUpdateで所持ジョイントへ合わせる。
- BallHoldAttachmentは描画専用階層の付け替えと復元を担当する。所持時はジョイント直下のローカル位置・回転をゼロにし、解放・Despawn時は元の階層とローカルTransformへ戻す。
- 設定の入口はPlayerBallCarrierのBall Hold Point(所持ジョイント)とBallControllerのVisual Root(ボール直下の描画専用階層)。物理・通信コンポーネントはVisual Rootに含めない。

## 男女モデル別のボール所持ジョイント
- PlayerBallCarrierは男女別の所持ジョイントを保持し、PlayerAppearanceが実際に表示しているキャラクター種別に応じてBallHoldPointを返す。外見の初期反映前はPlayerNetworkProfileの種別を参照する。
- モデル切り替え後は既存のBallController.LateUpdateとBallHoldAttachment.Attachが新しいジョイントへ描画を付け替え、サーバー上の本体も追従する。追加の通信状態は持たない。
- InspectorではMan Ball Hold PointとWoman Ball Hold Pointに各モデルのジョイントを登録する。従来のBall Hold Point参照は男性用へ引き継ぐ。

## 電流の外見管理
- `Assets/VFX/Lightning/VFXScripts/`: 継承元の `ALightningBolt` が共通マテリアルの読込、所有者の色取得、変更通知の購読と表示色の反映を担当する。継承先は形状と発生方法を担当する。
- `Assets/VFX/Lightning/Resources/Lightning/`: 全種類の電流で使う芯用・発光用マテリアルを保持する。色は電流ごとの `MaterialPropertyBlock` で上書きする。
- `PlayerAppearance.TryGetEffectColor` / `EffectColorChanged`: プレイヤーの選択色を演出へ提供する取得口と変更通知。プレイヤーの子以外へ生成する電流は `ALightningBolt.SetOwner` で所有者を渡す。

## 球面を這う電撃VFX
- `Assets/VFX/Lightning/VFXScripts` は、電撃のプール・発生周期・寿命・芯と発光の描画を基底クラスで管理し、派生クラスで形状と追従方法を定義する。
- `ALightningBolt.GenerateLightning` は形状生成、`UpdateBoltTransform` は生成直後と有効な各フレームの追従更新の拡張ポイント。既存の派生クラスは従来の直線ベースの形状生成を利用する。
- `SphereSurfaceLightningBolt` は球面用の入口。ローカル座標の円弧に接線方向の揺れを加えて球面へ戻し、中心Transformへの追従と球面上の移動を行う。形状の更新間隔と追従更新を分離し、電撃ごとの頂点配列を再利用する。
- 半径と表面オフセットは中心Transformのローカル単位。球形を保つ均等スケールを想定する。LineRendererの線分による埋まりは分割数と表面オフセットで調整する。

## 移動経路に残る電撃VFX

- `ALightningBolt.UpdateSource` は電撃の更新・発生より前に追跡元の状態を更新する拡張ポイント。`ClearActiveBolts` は有効な電撃をプールへ戻し、表示を終了する。
- `TrailLightningBolt` は移動経路用の入口。追跡対象の移動を距離でサンプリングし、発生時の経路をワールド座標で固定する。停止中は新規発生を止め、瞬間移動・追跡対象の変更・再有効化で記録を区切る。既存の電撃は寿命で終了するが、コンポーネント無効化時は表示を終了する。
- 経路の保存・距離制限・等間隔の経路取得は固定長の循環バッファに分離する。電撃ごとの経路配列は再利用する。記録点数の上限でも古い記録を破棄するため、最大長は保存を保証する距離ではなく上限として扱う。

## alpha更新とモデル・電撃色の統合

- 所持状態と追尾パスは `BallController` が管理し、描画の付け替えと復元は `BallHoldAttachment` に委譲する。所持位置は `PlayerBallCarrier.BallHoldPoint` が表示中の男女モデルに応じて提供する。
- `BallHoldAttachment` は描画配下の電撃へ所有者を明示し、解放時に色の上書きを解除する。ボール本体の投球状態は軌跡電撃の表示条件として同期する。
- `ALightningBolt` はプログラム生成の電撃と差し替えPrefabの再生を切り替える。生成プールは遅延初期化し、初期化前に受け取った選択色も反映する。無効化時は色通知の購読解除と両方式の再生停止を行う。
- 色の個別上書きはLineRendererを対象とし、差し替えPrefab内のParticleSystemやVFX Graphの色制御は含めない。
- `VFX_testScene` はモデルの保持ジョイント確認用配置を残し、alphaの独立したボールとモデル接触電撃の確認用配置を併設する。

## Prefab電流のVFXカラー連携
- `ALightningBolt.SetColor`: 通常の線描画とPrefab方式に共通する色指定の入口。所有者の色変更とPrefab生成時に反映する。
- `LightningPrefabColor`: 生成インスタンスの公開入力 `ColorIndex` へ色番号を渡し、VFX Graph内のSwitchで色を選択させる。直接色指定と従来Graphでは `EffectColor` を使い、所有者解除時には生成時の設定へ戻す。
- デザイナー向けの公開入力仕様は `docs/LightningVfxColor.md` を参照する。
- `LightningPrefabPlayback`: Prefab内のParticleSystemとVFX Graphの再生・停止・残存粒子の終了待ちを担当する。足元の接触エフェクトは離地後も粒子の寿命まで表示を維持する。
- `PlayerAppearance` の電流カラー設定で取得元を選択色/チーム色から切り替える。`TryGetEffectColor` が描画色とVFXパレット種別を返し、設定・選択色・所属チームの変更を電流へ通知する。

## ボールの場外復帰

- `Assets/Scripts/Ball/`の場外判定は、サーバー上で未所持かつ試合進行中のボールを対象とする。判定範囲はワールド座標で設定する。
- 復帰の共通エントリーポイントは`BallController.RespawnAtInitialPosition()`。所持状態、投球・追尾・物理状態、再取得制限を初期化し、試合の停止状態を維持して生成時の位置へ戻す。
- 試合再開用の`ResetForRestart()`とデバッグ用の手動復帰も同じエントリーポイントを使用する。試合再開時のみ、復帰後にボールを停止状態へ切り替える。
