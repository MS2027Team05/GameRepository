using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// ホストPC専用の観戦カメラおよびマルチディスプレイ(Display 2への全画面投影)を管理するクラス。
/// 複数モニター接続時はプロジェクター等の外部スクリーン(Display 2)へ出力し、単一モニター時は自動フォールバックします。
/// </summary>
public class SpectatorDisplayManager : MonoBehaviour
{
	[Header("観戦カメラ参照")]
	[FormerlySerializedAs("spectatorCamera")]
	[SerializeField] private Camera m_SpectatorCamera;
	[FormerlySerializedAs("spectatorUIRoot")]
	[SerializeField] private GameObject m_SpectatorUIRoot;

	[Header("動作設定")]
	[FormerlySerializedAs("forceEnableInEditor")]
	[SerializeField] private bool m_ForceEnableInEditor;

	private void Start()
	{
		InitializeSpectatorDisplay();
	}

	private void InitializeSpectatorDisplay()
	{
		bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
		bool isSpectatorMode = !NetworkConnectManager.IsPlayerHost;

		// エディタデバッグ強制有効化フラグ、または「ホストかつ観戦専用モード」の場合に観戦機能を有効化
		bool shouldActivateSpectator = (isServer && isSpectatorMode) || (Application.isEditor && m_ForceEnableInEditor);

		if (!shouldActivateSpectator)
		{
			// クライアントまたはプレイヤー兼任ホストの場合は観戦カメラを無効化
			if (m_SpectatorCamera != null) m_SpectatorCamera.gameObject.SetActive(false);
			if (m_SpectatorUIRoot != null) m_SpectatorUIRoot.SetActive(false);
			return;
		}

		// マルチディスプレイの検出と有効化
		if (Display.displays.Length > 1)
		{
			// 2枚目のディスプレイ(Display 2)をアクティブ化
			Display.displays[1].Activate();

			if (m_SpectatorCamera != null)
			{
				m_SpectatorCamera.targetDisplay = 1; // Display 2へ出力
				m_SpectatorCamera.gameObject.SetActive(true);
			}
		}
		else
		{
			// 外部モニターがない環境(ノートPC単体やデバッグ環境)へのフォールバック
			if (m_SpectatorCamera != null)
			{
				m_SpectatorCamera.targetDisplay = 0; // Display 1(メイン画面)へ出力
				m_SpectatorCamera.gameObject.SetActive(true);
			}
		}

		if (m_SpectatorUIRoot != null)
		{
			m_SpectatorUIRoot.SetActive(true);
		}
	}
}
