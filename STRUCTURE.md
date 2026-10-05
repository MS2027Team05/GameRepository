# ディレクトリ構造 (STRUCTURE.md)

本プロジェクトのディレクトリ構成およびスクリプト配置を記録するドキュメントです。各階層構造の責務と重要なエントリーポイントを記載します。

```text
GameRepository/
├── .agents/                     # AIエージェント設定およびカスタムスキル格納
│   └── skills/                  # Unity公式AIエージェントSkills
├── Assets/
│   ├── Audios/                  # 音声リソース格納 (AI学習対象外)
│   ├── Fonts/                   # フォントリソース格納 (AI学習対象外)
│   ├── Materials/               # マテリアル・シェーダー関連格納
│   ├── Models/                  # 3Dモデル・メッシュデータ格納 (AI学習対象外)
│   ├── Plugins/                 # 外部プラグインライブラリ格納 (DOTween等)
│   ├── Prefabs/                 # ゲーム内プレハブ格納
│   ├── Resources/               # 動的読み込み用アセット格納
│   ├── Scenes/                  # 各画面・遷移用シーン格納
│   ├── Scripts/                 # プロジェクト固有のC#スクリプト
│   │   ├── Network/             # 通信接続・同期制御
│   │   │   ├── NetworkConnectManager.cs # [EntryPoint] 接続初期化・起動制御
│   │   │   └── LobbyManager.cs          # [EntryPoint] ロビー管理・試合開始
│   │   └── Player/              # プレイヤー制御・挙動ロジック
│   │       ├── Interfaces/      # 各サブモジュール用インターフェース
│   │       │   └── ICameraEffect.cs     # カメラ演出制御用インターフェース
│   │       ├── LandingMarkerEffect.cs   # 着地予測マーカーの描画・アニメーション演出制御
│   │       ├── PlayerAimGuide.cs        # レティクル表示・軌道予測線の描画およびエイム照準制御
│   │       ├── PlayerCameraEffect.cs    # 高速移動時FOV変更・カメラシェイク等の演出制御
│   │       └── PlayerController.cs      # [EntryPoint] プレイヤー状態統括・調停
│   ├── Settings/                # プロジェクト設定・URP描画設定
│   ├── TextMesh Pro/            # TextMesh Pro関連リソース
│   └── Textures/                # テクスチャ・スプライト格納 (AI学習対象外)
├── docs/                        # 企画書・仕様書等の設計ドキュメント格納
├── AGENTS.md                    # AI用ルール・規約定義
├── player_structure.md          # プレイヤーモジュール仕様・連携手順書
└── STRUCTURE.md                 # 本ドキュメント
```
