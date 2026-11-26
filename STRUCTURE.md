# ディレクトリ構造 (STRUCTURE.md)

本プロジェクトのディレクトリ構成および各階層・主要エントリーポイントの責務を定義します。

## 全体アーキテクチャ・主要フロー

### 1. 試合進行・リスタート・得点演出
- **エントリーポイント**: `Game/MatchManager` がサーバー権限で試合状態 (`Waiting` / `PreparingRestart` / `Countdown` / `Playing` / `GoalCelebration` / `GoalReplay` / `Finished`)・制限時間・チーム得点・全端末のシーン読込完了待機およびデバッグ用時間/燃料無制限設定を統括します。
- **スポーンとリスタート**: `Network/PlayerSpawner` が陣営別・配置番号別の自陣ゴール付近/中央付近スポーン地点 (`RedGoalSpawn` / `RedCenterSpawn` / `BlueGoalSpawn` / `BlueCenterSpawn`) を保持し、初期配置は両チーム自陣ゴール付近、得点後は得点側を自陣ゴール付近・失点側を中央付近へ `PlayerController.PrepareForRestart` で再配置します。Owner 側は `PlayerController.Respawn` による姿勢・カメラ・アニメーション初期化に加えて `IPlayerResource.ResetToFull` で燃料を100%へ戻し、完了応答後に `Countdown` へ遷移します。
- **得点演出とHUD**: `MatchManager.TryAddGoalScore` で得点確定後に全端末へ通知し、各端末の `GoalController` が配置済み ParticleSystem を再生します。全端末での粒子消滅完了を受けて次フェーズへ移行し、`UI/MatchHudController` が同期状態に応じた得点メッセージやカウントダウンを表示します。

### 2. ゴールリプレイ・戦績・統計
- **リプレイ記録・再生**: `Game/Replay/ReplayRecorder` がクライアント側リングバッファへプレイヤーおよびボール (`BallController.VisualTransform`) の座標・状態を記録し、`MatchManager` の設定 (`AfterEveryGoal` / `AfterMatchBestGoal` / `Both`) に応じて `ReplayPlayer` が表示専用アクターとゴールカメラ視点で再生します。ベストゴール評価は `IGoalHighlightEvaluator` に分離しています。
- **行動統計・表彰・ログ**: `Game/Stats/MatchStatsTracker` がクライアント・サーバー双方の行動記録を集約し、`MatchAwardEvaluator` が個人表彰を選出して `SessionPlayerDataStore` 経由で `ResultUIController` へ引き継ぎます。展示用累計統計は `ExhibitionStatsStore`、1試合ごとの詳細統計は `MatchDebugStatsStore` がそれぞれ永続化します。
- **ロビー復帰**: `ResultUIController` から `LobbyScene` へ戻る際は通信を維持したまま `SessionPlayerDataStore.ResetAllReadyStates()` とシーン同期遷移を実行し、`TitleScene` へ戻る場合のみ切断します。

### 3. ボール制御・所持追従・場外復帰
- **状態管理・物理**: `Ball/BallController` が所持・奪取・投球・パス判定をサーバー権限で管理し、物理挙動を `BallPhysicsController` 系へ委譲します。
- **描画階層の追従**: `BallHoldAttachment` が描画専用階層 (`VisualRoot`) のみを `PlayerBallCarrier.BallHoldPoint` (表示中の男女モデルに応じたジョイント) へ付け替え・復元し、物理・通信用本体は `BallController` がサーバー側 `LateUpdate` で追従させます。
- **場外復帰**: `BallOutOfBoundsRespawner` がサーバー上の未所持かつ試合進行中ボールの範囲外を検知し、最後に場内だった位置の近傍 Ground Surface へ `BallController.RespawnAtPosition()` で復帰させます。近傍 Ground を取得できない場合は `BallController.RespawnAtInitialPosition()` で初期位置へ戻します。

### 4. 電撃VFX・カラー連携
- **共通基底とリソース**: `Assets/VFX/Lightning/VFXScripts/ALightningBolt` が共通マテリアル (`Resources/Lightning/`) の読込、線描画プールと差し替え Prefab 再生の切り替え、および所有者からの色同期を担当します。派生クラスは形状・接触検知・移動軌跡などの発生ロジックを担当します。
- **色連動**: `PlayerAppearance.TryGetEffectColor` / `EffectColorChanged` が `EEffectColorSource` (選択色またはチーム色) に応じた色とパレット種別を提供し、ボール所持時は `BallHoldAttachment` が描画配下の電撃へ所有者を明示します。
- **箱表面の電撃**: `CubeSurfaceLightningBolt` がローカル座標で定義した箱の表面を這う線を描画し、`CubeSurfaceLightningPath` が面上の移動と辺での方向変換を担当します。面の境界を描画頂点に追加し、各線分を同一面上に収めます。
- **自プレイヤーの回復演出**: `PlayerResourceController` が回復状態と表示設定を通知し、`PlayerRecoveryEffectController` が直接登録された急速回復用・パス回復用の `ALightningBolt` コンポーネントを有効・無効に切り替えます。未指定の側はデバッグ稲妻を表示し、パス回復の表示時間は回復演出側で管理します。同じ電流を両方に登録した場合は、どちらかの表示条件が成立している間だけ有効にします。

---

## ディレクトリ構成

```text
GameRepository/
├── .agents/                         # AIエージェント設定およびカスタムスキル格納
│   └── skills/                      # Unity公式AIエージェントSkills
├── Assets/
│   ├── Animation/                   # キャラクター・ボール・デバイスのAnimatorController格納
│   ├── Audios/                      # 音声リソース格納 (AI学習対象外)
│   ├── Effect/                      # エフェクト用プレハブ格納
│   ├── Fonts/                       # フォントリソース格納 (AI学習対象外)
│   ├── haikei/                      # 背景モデル・マテリアル・テクスチャ・Prefab格納
│   ├── Materials/                   # マテリアル・シェーダー関連格納
│   │   ├── Ball/                    # ボール描画用マテリアル (通常・透過X-Ray等)
│   │   └── Player/                  # プレイヤー・デバイス用マテリアル格納
│   ├── Meshes/                      # 3Dモデル・メッシュデータ格納 (AI学習対象外)
│   │   └── Material/Models/         # キャラクター・ボール・デバイス3Dモデル(FBX) (AI学習対象外)
│   ├── Plugins/                     # 外部プラグインライブラリ格納 (DOTween等)
│   ├── Prefabs/                     # ゲーム内プレハブ格納
│   ├── Resources/                   # 動的読み込み用アセット格納
│   ├── Scenes/                      # 各画面・遷移用シーン格納
│   ├── Scripts/                     # プロジェクト固有のC#スクリプト
│   │   ├── Ball/                    # ボール制御・物理・Surface・UI・場外復帰
│   │   │   ├── BallController.cs            # [EntryPoint] ボール所持・奪取・シュート・復帰統括
│   │   │   ├── BallHoldAttachment.cs        # 描画専用階層(VisualRoot)の所持ジョイント付け替え・復元
│   │   │   ├── BallOutOfBoundsRespawner.cs  # 未所持ボールのワールド範囲外検知・近傍Ground/初期位置復帰
│   │   │   ├── Physics/                     # Free Flight・Homing・Surface物理制御
│   │   │   │   ├── BallPhysicsController.cs # [EntryPoint] ボール物理状態統括 (Air/Collision/Homing/Surface・障害物判定・診断)
│   │   │   │   ├── BallBounce.cs            # ボールの跳ね返り挙動制御
│   │   │   │   └── Attraction/              # 疑似重力・Surface探索 (BallSurfaceGravity/Search)
│   │   │   ├── Surface/                     # BallSurfaceのMesh解析・曲面上移動シミュレーション
│   │   │   │   ├── BallSurfaceSource.cs     # [EntryPoint] MeshColliderからSurfaceデータを構築
│   │   │   │   └── BallSurfaceSimulation.cs # Surface Physics進行処理 (Simulation/配下に初期化・形状別遷移・World状態を分割)
│   │   │   ├── UI/
│   │   │   │   ├── BallScreenIndicator.cs   # 画面内・画面外インジケーターUI制御
│   │   │   │   └── BallSpectatorHighlight.cs# 観戦時の最小視認スケール維持・強調制御
│   │   │   └── Debug/
│   │   │       └── BallDebugRespawner.cs    # ボール位置リセット用デバッグ機能
│   │   ├── Game/                    # 試合進行・ゴール判定・リプレイ・統計・観戦
│   │   │   ├── MatchManager.cs              # [EntryPoint] 試合状態・制限時間・得点・遷移演出のサーバー管理
│   │   │   ├── GoalController.cs            # ゴール構造管理・得点判定委譲・ゴール演出再生制御
│   │   │   ├── GoalArea.cs                  # ゴール判定コライダー検知・GoalControllerへの通知
│   │   │   ├── Replay/                      # ゴールリプレイ記録・評価・表示専用再生
│   │   │   │   ├── GoalHighlightEvaluation.cs # ハイライト評価コンテキスト・IGoalHighlightEvaluator定義
│   │   │   │   ├── ReplayFrameData.cs       # リプレイフレーム・クリップデータ構造体
│   │   │   │   ├── ReplayRecorder.cs        # [EntryPoint] クライアント側リングバッファ記録・クリップ切り出し
│   │   │   │   └── ReplayPlayer.cs          # [EntryPoint] 表示専用アクター制御・ゴールカメラ再生・復帰
│   │   │   ├── Stats/                       # 行動統計・個人表彰・ログ永続化
│   │   │   │   ├── MatchAwardData.cs        # 統計・称号・同期ペイロード定義
│   │   │   │   ├── MatchAwardEvaluator.cs   # 個人表彰(最大3個)の選出・ラベル整形
│   │   │   │   ├── ExhibitionStatsStore.cs  # 展示用累計統計・選択累計のJSON永続化
│   │   │   │   ├── MatchDebugStatsStore.cs  # 試合別デバッグ統計のJSON出力
│   │   │   │   └── MatchStatsTracker.cs     # [EntryPoint] クライアント/サーバー行動集計・確定処理
│   │   │   └── Spectator/                   # 観戦カメラ・中継テロップ制御
│   │   │       ├── SpectatorCameraDirector.cs # [EntryPoint] 定点・追従・ゴール前カメラの自動切替
│   │   │       └── SpectatorOverlayUIController.cs # 観戦中継テロップ表示制御
│   │   ├── Editor/                  # エディタ拡張・ステージ配置/メニュー構築ツール
│   │   │   ├── SetupResultAndStaffMenu.cs   # [EntryPoint] スタッフメニュー・ResultScene生成ツール
│   │   │   ├── StageObjectBilateralPlacerEditor.cs
│   │   │   ├── StagePrefabReplacerWindow.cs # [EntryPoint] Model PrefabからPrefab Variantへの一括置換
│   │   │   ├── StageRailPatternSwitcherEditor.cs
│   │   │   ├── StageRailRingBuilderEditor.cs
│   │   │   └── StageSymmetricSynchronizerEditor.cs
│   │   ├── Network/                 # 通信接続・ロビー・スポーン・セッション管理
│   │   │   ├── NetworkConnectManager.cs     # [EntryPoint] 接続初期化・起動制御
│   │   │   ├── NetworkConnectionApprovalHandler.cs # 許可シーン(Title/Lobby)限定接続承認・試合中途中参加拒否
│   │   │   ├── ClientNetworkTransform.cs    # Owner権限でのTransform同期
│   │   │   ├── LobbyManager.cs              # [EntryPoint] ロビー通信同期・ゲーム開始制御
│   │   │   ├── LobbyTeamSelectPanelController.cs # 途中参加・未所属時・席替え用チーム/観戦選択UI制御
│   │   │   ├── LobbyUIController.cs         # ロビーUI表示・ロール別パネル制御
│   │   │   ├── LobbyAppearancePickerUI.cs   # ロビー内キャラ・カラー選択UI制御
│   │   │   ├── LobbyCharacterDisplayManager.cs # ロビー内3Dモデル表示・台座同期
│   │   │   ├── LobbyPlayerDisplay.cs        # 表示特化キャラクター外見・頭上ネーム・リプレイアニメ制御
│   │   │   ├── LobbyPlayerData.cs           # ロビー参加プレイヤー同期データ構造体
│   │   │   ├── LocalPlayerProfilePreferences.cs # 端末別設定(キャラ・色・チーム・IP・感度)永続化
│   │   │   ├── SessionPlayerDataStore.cs    # ロビー設定・試合結果/個人表彰のシーン間引継ぎ管理
│   │   │   ├── PlayerSpawner.cs             # [EntryPoint] 陣営別プレイヤースポーン・リスタート統括
│   │   │   ├── SpectatorDisplayManager.cs   # Display 1/2 観戦カメラ・UI出力統括
│   │   │   ├── TitleOverlayController.cs    # [EntryPoint] タイトル画面演出・個別開始制御
│   │   │   ├── StaffInspectionMenuController.cs # [EntryPoint] F1管理・診断・統計ページ・インゲーム操作
│   │   │   ├── GameSessionResetCoordinator.cs # [EntryPoint] 終了時オブジェクト破棄・セッション初期化
│   │   │   ├── TutorialManager.cs           # [EntryPoint] チュートリアル進捗監視・同期遷移
│   │   │   ├── NetworkSceneTimeoutController.cs # ネットワークシーン遷移時のLoadSceneTimeOut動的切替・復元
│   │   │   └── NetworkConnectSample.cs      # 接続テスト用サンプルUI
│   │   ├── Shader/                  # シェーダー連携・描画制御 (PlayerOutline/PlayerSilhouette/ToolSilhouette/ShaderPrewarmController)
│   │   ├── Stage/                   # ステージ・地形・ギミック管理
│   │   │   ├── StageSymmetricSynchronizer.cs # [EntryPoint] ステージ点対称同期設定
│   │   │   ├── StageRailRingBuilder.cs      # [EntryPoint] 外周レール・往復キューブ・足場一括生成
│   │   │   ├── StageRailPatternSwitcher.cs  # 外周レールx足場パターン切替・マルチ同期
│   │   │   ├── StageShuttleBlock.cs         # レール区間往復ブロック制御
│   │   │   └── StageObjectBilateralPlacer.cs# 等間隔両端配置コンポーネント
│   │   ├── Player/                  # プレイヤー制御・入力・物理・外見・リソース
│   │   │   ├── PlayerController.cs          # [EntryPoint] プレイヤー状態統括・アクション調停・RPC窓口
│   │   │   ├── Interfaces/                  # モジュール間インターフェース群 (IAimAssist, IAimGuide, IBallCarrier, ICameraEffect, IElectricAction, IGravityMover, IGroundDetector, ILockOnProvider, IMovingSurface, IPlayerResource, IPlayerRespawnHandler, ISurfaceMover)
│   │   │   ├── PlayerNetworkProfile.cs      # プレイヤープロファイル同期
│   │   │   ├── PlayerAppearance.cs          # 外見(性別モデル・12色・チーム色・電流色ソース)管理
│   │   │   ├── PlayerAppearanceCatalog.cs   # 外見マテリアル・プレビュー色・共通発光管理ScriptableObject
│   │   │   ├── PlayerEmissionController.cs  # MaterialPropertyBlockによるプレイヤー共通発光制御
│   │   │   ├── EEffectColorSource.cs        # 電流色取得元(選択色/チーム色)Enum
│   │   │   ├── PlayerNamePlate.cs           # 頭上3Dネームプレート・敵味方表示制御
│   │   │   ├── PlayerInputReceiver.cs       # InputSystem入力・視点操作(マウス/PAD感度)・カーソル管理
│   │   │   ├── PlayerCameraEffect.cs        # 三人称カメラ旋回(法線追従)・FOV・シェイク・視点リセット
│   │   │   ├── GravityMover.cs              # 等速直線移動・空中ホバリング・着地衝突
│   │   │   ├── PlayerSurfaceMover.cs        # 地上/曲面歩行・運動量転送・姿勢回転
│   │   │   ├── PlayerGroundDetector.cs      # 接地検知・法線計測
│   │   │   ├── PlayerAimAssist.cs           # ゲームパッド用エイムアシスト(入力減衰・緩やかな追従補正)
│   │   │   ├── PlayerAimGuide.cs            # 常時レティクル・地上予備浮上・着地点追跡
│   │   │   ├── LandingMarkerEffect.cs       # 着地予測マーカー演出
│   │   │   ├── LockOnManager.cs             # 優先度付きロックオン検索・捕捉
│   │   │   ├── PlayerBallCarrier.cs         # ボール所持・男女別ジョイント提供・パス/シュート
│   │   │   ├── ElectricAction.cs            # 電撃チャージ・発射・奪取/スタン命中処理
│   │   │   ├── PlayerChargeProgress.cs      # 電撃・追尾パス共通の円形チャージ進捗UI表示
│   │   │   ├── PlayerResourceController.cs  # [EntryPoint] Owner側リソース消費・回復・パスボーナス・100%リセット
│   │   │   ├── PlayerRecoveryEffectController.cs # 回復・パスボーナスVFX再生および同期
│   │   │   ├── PlayerAnimationController.cs # Animator状態切替・移動状態同期
│   │   │   ├── PlayerOutOfBoundsRespawner.cs# [EntryPoint] 範囲外検知・復帰要求
│   │   │   └── PlayerEdgeDiagnostics.cs     # 接地境界脱落時の診断HUD描画
│   │   └── UI/                      # 汎用・画面別UI制御
│   │       ├── UIInputReceiver.cs           # UI系統入力検知・InputAction分離
│   │       ├── UIFocusGlowOutline.cs        # 汎用UIフォーカス発光枠線制御
│   │       ├── PlayerResourceGaugeUI.cs     # リソースゲージ表示更新
│   │       ├── PlayerActionHelpUI.cs        # 入力デバイス(マウス/PAD)連動の右下操作補助UI制御
│   │       ├── PlayerWorldTargetUI.cs       # ボール・ゴール・味方プレイヤーの画面上補助マーカーUI制御
│   │       ├── PlayerUIResolutionManager.cs # 画面解像度比率に応じたプレイヤーUIスケール調整
│   │       ├── InGamePauseMenuController.cs # [EntryPoint] Escメニュー・設定連携・ロビー復帰
│   │       ├── MatchHudController.cs        # 試合時間・得点・状態メッセージ表示
│   │       ├── ResultAwardItemUI.cs         # リザルト個人表彰1項目表示
│   │       ├── ResultUIController.cs        # [EntryPoint] リザルト画面制御・外見/表彰表示・復帰制御
│   │       ├── SettingsPanelController.cs   # 設定画面(感度・音量・描画設定)制御
│   │       └── SceneTransitionEffect.cs     # 画面トランジション演出制御
│   ├── Settings/                    # プロジェクト設定・URP描画設定
│   ├── Shader/                      # カスタムシェーダー格納 (アウトライン・遮蔽シルエット等)
│   ├── SkySeries Freebie/           # Skybox背景アセット格納
│   ├── TextMesh Pro/                # TextMesh Pro関連リソース
│   ├── Textures/                    # テクスチャ・スプライト格納 (AI学習対象外)
│   └── VFX/                         # VFX関連リソース
│       └── Lightning/
│           ├── Resources/Lightning/ # 全電撃共通マテリアル格納
│           └── VFXScripts/          # ALightningBolt基底・派生電撃・Prefab色/再生連携スクリプト群
├── Tools/                           # 開発・展示運用・ネットワーク支援ツール
│   └── Firewall/                    # 展示用ファイアウォールポート開放・解除バッチ
├── docs/                            # 企画書・仕様書等の設計ドキュメント格納
├── AGENTS.md                        # AI用ルール・規約定義
└── STRUCTURE.md                     # 本ドキュメント
```

## プレイヤー共通の発光設定

- `Assets/Scripts/Player/`の外見制御は、共有カタログ`PlayerAppearanceCatalog`で共通発光強度とチーム別・チーム未確定時の発光色を管理する。値と有効化はInspectorで設定する。
- `PlayerAppearance`が外見・所属チームの変更時に発光を再適用し、Play中はカタログの設定変更通知も購読する。
- 描画への上書きと解除は`PlayerEmissionController`へ分離する。既存の本体・チームカラーRendererの先頭スロットだけを`MaterialPropertyBlock`で調整し、Emissionが有効かつEmission Mapが設定されたマテリアルを対象にする。元のマテリアルアセットと追加のアウトライン用スロットは変更しない。
- 共通設定の無効化やコンポーネントの無効化・破棄時には、保存したスロット別プロパティを復元する。Bloomの設定はこの制御に含まない。
