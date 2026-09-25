using Unity.Netcode;
using UnityEngine;

/// <summary>
/// ホストPC専用の観戦カメラおよびマルチディスプレイ(Display 2への全画面投影)を管理するクラス。
/// 複数モニター接続時はプロジェクター等の外部スクリーン(Display 2)へ出力し、単一モニター時は自動フォールバックします。
/// </summary>
public class SpectatorDisplayManager : MonoBehaviour
{
    [Header("観戦カメラ参照")]
    [SerializeField] private Camera spectatorCamera;
    [SerializeField] private GameObject spectatorUIRoot;

    [Header("動作設定")]
    [SerializeField] private bool forceEnableInEditor;

    private void Start()
    {
        InitializeSpectatorDisplay();
    }

    private void InitializeSpectatorDisplay()
    {
        bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
        bool isSpectatorMode = !NetworkConnectManager.IsPlayerHost;

        // エディタデバッグ強制有効化フラグ、または「ホストかつ観戦専用モード」の場合に観戦機能を有効化
        bool shouldActivateSpectator = (isServer && isSpectatorMode) || (Application.isEditor && forceEnableInEditor);

        if (!shouldActivateSpectator)
        {
            // クライアントまたはプレイヤー兼任ホストの場合は観戦カメラを無効化
            if (spectatorCamera != null) spectatorCamera.gameObject.SetActive(false);
            if (spectatorUIRoot != null) spectatorUIRoot.SetActive(false);
            return;
        }

        // マルチディスプレイの検出と有効化
        if (Display.displays.Length > 1)
        {
            // 2枚目のディスプレイ(Display 2)をアクティブ化
            Display.displays[1].Activate();

            if (spectatorCamera != null)
            {
                spectatorCamera.targetDisplay = 1; // Display 2へ出力
                spectatorCamera.gameObject.SetActive(true);
            }
        }
        else
        {
            // 外部モニターがない環境(ノートPC単体やデバッグ環境)へのフォールバック
            if (spectatorCamera != null)
            {
                spectatorCamera.targetDisplay = 0; // Display 1(メイン画面)へ出力
                spectatorCamera.gameObject.SetActive(true);
            }
        }

        if (spectatorUIRoot != null)
        {
            spectatorUIRoot.SetActive(true);
        }
    }
}
