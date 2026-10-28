# ディレクトリ構造 (STRUCTURE.md)

本プロジェクトのディレクトリ構成およびスクリプト配置を記録するドキュメントです。各階層構造の責務と重要なエントリーポイントを記載します。

```text
GameRepository/
├── .agents/                     # AIエージェント設定およびカスタムスキル格納
│   └── skills/                  # Unity公式AIエージェントSkills
├── Assets/
│   ├── Audios/                  # 音声リソース格納 (AI学習対象外)
│   ├── Fonts/                   # フォントリソース格納 (AI学習対象外)
│   ├── haikei/                  # 背景モデル・マテリアル・テクスチャ・Prefab格納
│   ├── Materials/               # マテリアル・シェーダー関連格納
│   │   ├── Ball/                # ボール描画用マテリアル (通常・透過X-Ray等)
│   │   └── Player/              # プレイヤー用マテリアル格納 (性別・チームカラー別)
│   ├── Meshes/                  # 3Dモデル・メッシュデータ格納 (AI学習対象外)
│   ├── Models/                  # 3Dモデル・メッシュデータ格納 (AI学習対象外)
│   ├── Plugins/                 # 外部プラグインライブラリ格納 (DOTween等)
│   ├── Prefabs/                 # ゲーム内プレハブ格納
│   ├── Resources/               # 動的読み込み用アセット格納
│   ├── Scenes/                  # 各画面・遷移用シーン格納
│   ├── Scripts/                 # プロジェクト固有のC#スクリプト
│   │   ├── Ball/                # ボール物理・同期制御
│   │   │   ├── BallController.cs        # [EntryPoint] ボール状態管理・所持・シュート制御
│   │   │   ├── BallPhysicsController.cs # ボール物理補正・バウンド制御
│   │   │   ├── BallBounce.cs            # ボールの跳ね返り挙動制御
│   │   │   ├── BallSurfaceGravity.cs    # ボールの接地面重力・吸着制御
│   │   │   ├── BallSurfaceSearch.cs     # ボール周辺の面探索制御
│   │   │   ├── BallScreenIndicator.cs   # ボール位置表示用画面内・画面外インジケーターUI制御
│   │   │   └── BallDebugRespawner.cs    # ボール位置リセット用デバッグ機能
│   │   ├── Editor/              # エディタ拡張・開発支援ツール
│   │   │   ├── StagePrefabReplacerWindow.cs # [EntryPoint] Model PrefabからPrefab Variantへの一括置換・Transform保持ツール
│   │   │   └── StageSymmetricSynchronizerEditor.cs # [EntryPoint] ステージ地形の点対称(180度回転)同期・チーム別Prefab自動置換Inspector拡張
│   │   ├── Network/             # 通信接続・同期制御
│   │   │   ├── NetworkConnectManager.cs # [EntryPoint] 接続初期化・起動制御
│   │   │   ├── LobbyManager.cs          # [EntryPoint] ロビー通信同期・ゲーム開始制御
│   │   │   ├── LobbyUIController.cs     # ロビー画面UI表示・端末ロール別パネル制御
│   │   │   ├── LobbyCharacterDisplayManager.cs # ロビー内3Dモデル表示・台座同期制御
│   │   │   ├── LobbyPlayerData.cs       # ロビー参加プレイヤー同期データ構造体
│   │   │   ├── SessionPlayerDataStore.cs# ロビー設定永続化・シーン間データ引継ぎ管理
│   │   │   └── PlayerSpawner.cs         # 陣営別プレイヤースポーン制御
│   │   ├── Shader/              # シェーダー連携・描画制御スクリプト
│   │   │   ├── PlayerOutline.cs         # アウトライン描画制御
│   │   │   ├── PlayerSilhouette.cs      # チーム別アウトライン・味方/観戦者向け遮蔽シルエット描画制御
│   │   │   └── ToolSilhouette.cs        # ツール類遮蔽時シルエット描画制御
│   │   ├── Stage/               # ステージ・地形管理スクリプト
│   │   │   └── StageSymmetricSynchronizer.cs # [EntryPoint] ステージ地形の点対称同期設定・データ保持コンポーネント
│   │   └── Player/              # プレイヤー制御・挙動ロジック
│   │       ├── PlayerNetworkProfile.cs  # プレイヤープロファイル(名前・チーム・カラー等)同期制御
│   │       ├── PlayerNamePlate.cs       # 頭上3Dネームプレート・Billboard追従・距離スケール補正・敵味方表示制御
│   │       ├── Interfaces/      # 各サブモジュール用インターフェース
│   │       │   ├── IAimGuide.cs         # エイム・ガイド表示制御インターフェース
│   │       │   ├── IBallCarrier.cs      # ボール所持・操作インターフェース
│   │       │   ├── ICameraEffect.cs     # カメラ演出制御用インターフェース
│   │       │   ├── IGravityMover.cs     # 等速直線移動(落下)・ホバリング制御インターフェース
│   │       │   ├── IGroundDetector.cs   # 接地検知・法線計測インターフェース
│   │       │   ├── ISurfaceMover.cs     # 地上・曲面歩行移動インターフェース
│   │       │   └── IPlayerRespawnHandler.cs # 場外復帰要求の受け口インターフェース
│   │       ├── GravityMover.cs          # 等速直線移動(落下)・空中ホバリング・着地衝突制御
│   │       ├── LandingMarkerEffect.cs   # 着地予測マーカーの描画・アニメーション演出制御
│   │       ├── PlayerAimGuide.cs        # レティクル表示・軌道予測線の描画およびエイム照準制御
│   │       ├── PlayerAnimationController.cs # 地上移動速度に基づくAnimator状態切り替え・移動状態の通信同期
│   │       ├── PlayerAppearance.cs      # プレイヤー外見(性別モデル・マテリアル・カラー)管理制御
│   │       ├── PlayerBallCarrier.cs     # プレイヤーのボール所持・キャッチ・投擲(シュート)制御
│   │       ├── PlayerCameraEffect.cs    # 三人称視点カメラ旋回(接地面法線追従)・高速移動時FOV変更・カメラシェイク制御・視点リセット
│   │       ├── PlayerEdgeDiagnostics.cs # 角部・接地境界脱落時の挙動解析・診断HUD描画制御
│   │       ├── PlayerGroundDetector.cs  # 足元/角/壁面の接地・法線検知およびデバッグGizmo描画
│   │       ├── PlayerInputReceiver.cs   # 入力検知・視点操作・カーソルロック管理
│   │       ├── PlayerOutOfBoundsRespawner.cs # [EntryPoint] XYZ範囲外検知・初期スポーン地点への復帰要求
│   │       ├── PlayerSurfaceMover.cs    # 地上/曲面歩行移動・運動量転送・ダウンフォース・姿勢回転制御
│   │       └── PlayerController.cs      # [EntryPoint] プレイヤー状態統括・調停
│   │   └── UI/                  # 汎用・共通UI制御
│   │       ├── SettingsPanelController.cs # 設定画面(音量・描画設定)開閉・UI制御
│   │       └── SceneTransitionEffect.cs # 画面トランジション(水平ワイプ・暗転解除演出)制御
│   ├── Settings/                # プロジェクト設定・URP描画設定
│   ├── Shader/                  # カスタムシェーダー格納 (アウトライン・遮蔽シルエット等)
│   ├── SkySeries Freebie/       # Skybox背景アセット格納
│   ├── TextMesh Pro/            # TextMesh Pro関連リソース
│   ├── Textures/                # テクスチャ・スプライト格納 (AI学習対象外)
│   └── VFX/                     # VFX関連リソース
├── docs/                        # 企画書・仕様書等の設計ドキュメント格納
├── AGENTS.md                    # AI用ルール・規約定義
├── player_structure.md          # プレイヤーモジュール仕様・連携手順書
└── STRUCTURE.md                 # 本ドキュメント
```
