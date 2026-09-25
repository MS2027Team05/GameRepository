# ディレクトリ構造 (STRUCTURE.md)

本プロジェクトのディレクトリ構成およびスクリプト配置を記録するドキュメントです。新規機能やスクリプト追加時に随時更新します。

```text
GameRepository/
├── .agents/
│   └── skills/                  # Unity公式AIエージェントSkills (Unity-Technologies/skills)
│       ├── 2d-pixel-perfect/
│       ├── physics-3d-collision/
│       ├── setup-multiplayer-services/
│       ├── unity-cli/
│       ├── unity-package-management/
│       └── ... (全32種類のUnityスキル)
├── Assets/
│   ├── Prefabs/
│   │   ├── Player.prefab        # プレイヤー用Prefab (NetworkObject, Rigidbody等)
│   │   └── NetworkManager.prefab# ネットワーク管理用Prefab (DontDestroyOnLoad)
│   ├── Scenes/
│   │   ├── TitleScene.unity     # タイトル・接続画面 (Host/Client選択、IP入力)
│   │   ├── LobbyScene.unity     # ロビー画面 (参加者一覧、チーム分け、ゲーム開始)
│   │   └── GameScene.unity      # 試合画面 (対戦フィールド)
│   ├── Scripts/
│   │   ├── Network/             # 通信・ネットワーク関連スクリプト
│   │   │   ├── ClientNetworkTransform.cs    # クライアント主権の移動同期
│   │   │   ├── NetworkConnectManager.cs     # タイトル接続制御 (IP動的設定、Host/Client起動)
│   │   │   ├── LobbyManager.cs              # ロビー管理 (参加人数同期、デバッグ人数切替、ゲーム開始)
│   │   │   ├── SpectatorDisplayManager.cs   # 観戦カメラ・Display 2マルチスクリーン制御
│   │   │   ├── PlayerSpawner.cs             # GameSceneでのプレイヤー自動配置・生成管理
│   │   │   └── NetworkConnectSample.cs      # (旧サンプル用コード)
│   │   └── Player/              # プレイヤー制御関連スクリプト
│   │       ├── Interfaces/      # 各担当モジュール用インターフェース
│   │       │   ├── IGravityMover.cs         # 物理・重力落下インターフェース (担当A)
│   │       │   ├── IAimGuide.cs             # エイム・予測線インターフェース (担当B)
│   │       │   └── IBallCarrier.cs          # ボール所持・操作インターフェース (担当C)
│   │       ├── PlayerController.cs          # プレイヤー全体の状態管理・調停役 (コア設計)
│   │       ├── PlayerInputReceiver.cs       # InputSystem入力検知・イベント発火
│   │       └── PlayerCameraEffect.cs        # カメラ演出用骨組みスタブ (担当B)
│   ├── Audios/                  # (AI学習対象外)
│   ├── Models/                  # (AI学習対象外)
│   ├── Textures/                # (AI学習対象外)
│   └── ...
├── AGENTS.md                    # AI用ルール・規約定義
├── player_structure.md          # プレイヤーモジュール仕様・連携手順書
└── STRUCTURE.md                 # 本ドキュメント
```
