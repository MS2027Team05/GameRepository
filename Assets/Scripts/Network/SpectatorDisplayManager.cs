using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// ホストPC(Display 2への全画面投影)および観戦者クライアント(Display 1出力)の
/// 観戦カメラとUI表示を統括管理するクラス。
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

	private static SpectatorDisplayManager m_Instance;

	/// <summary>
	/// シーン内で稼働しているSpectatorDisplayManagerのインスタンス。
	/// </summary>
	public static SpectatorDisplayManager Instance => m_Instance;

	/// <summary>
	/// 現在アクティブな観戦カメラを取得します。
	/// </summary>
	public static Camera ActiveSpectatorCamera
	{
		get
		{
			if (m_Instance != null && m_Instance.m_SpectatorCamera != null && m_Instance.m_SpectatorCamera.gameObject.activeInHierarchy)
			{
				return m_Instance.m_SpectatorCamera;
			}
			return null;
		}
	}

	/// <summary>
	/// 自端末が観戦モード(観戦専用ホスト、または観戦者クライアント)であるかを取得します。
	/// </summary>
	public static bool IsLocalSpectator
	{
		get
		{
			// 1. エディタ強制有効化時
			if (m_Instance != null && Application.isEditor && m_Instance.m_ForceEnableInEditor)
			{
				return true;
			}

			// 2. 観戦専用ホスト(非プレイヤーホスト)
			if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && !NetworkConnectManager.IsPlayerHost)
			{
				return true;
			}

			// 3. 観戦クライアント判定
			if (NetworkConnectManager.IsSpectatorClient)
			{
				return true;
			}

			// 4. ローカルプロファイル/セッションデータでの観戦チーム判定
			if (LocalPlayerProfilePreferences.GetAssignedTeam() == ETeamType.Spectator)
			{
				return true;
			}

			if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
			{
				ulong localClientId = NetworkManager.Singleton.LocalClientId;
				if (SessionPlayerDataStore.Instance != null)
				{
					var data = SessionPlayerDataStore.Instance.GetOrCreatePlayerData(localClientId);
					if (data.Team == ETeamType.Spectator)
					{
						return true;
					}
				}
			}

			return false;
		}
	}

	private void Awake()
	{
		m_Instance = this;
	}

	private void OnDestroy()
	{
		if (m_Instance == this)
		{
			m_Instance = null;
		}
	}

	private void Start()
	{
		InitializeSpectatorDisplay();
	}

	/// <summary>
	/// 観戦カメラおよび観戦用UIの表示先ディスプレイを初期化・更新します。
	/// </summary>
	public void InitializeSpectatorDisplay()
	{
		bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
		bool isSpectatorMode = IsLocalSpectator;

		// ホスト(本番観戦ホスト・デバッグホスト問わず)または観戦クライアント、またはエディタ強制有効化時に稼働
		bool shouldActivate = isServer || isSpectatorMode || (Application.isEditor && m_ForceEnableInEditor);

		if (!shouldActivate)
		{
			if (m_SpectatorCamera != null) m_SpectatorCamera.gameObject.SetActive(false);
			if (m_SpectatorUIRoot != null) m_SpectatorUIRoot.SetActive(false);
			return;
		}

		int targetDisplayIndex = 0; // デフォルトはDisplay 1(メイン画面)

		if (isServer)
		{
			// ホスト端末(本番環境・デバッグホスト共通)は、Display 2へ出力
			targetDisplayIndex = 1;
			if (Display.displays.Length > 1)
			{
				Display.displays[1].Activate();
			}
			else
			{
				// セカンドモニター未接続時のフォールバック(単一モニターホスト用)
				if (!NetworkConnectManager.IsPlayerHost)
				{
					// 観戦専用ホストで1画面しかない場合はDisplay 1へフォールバック
					targetDisplayIndex = 0;
				}
			}
		}
		else
		{
			// 観戦者ボタンで入ったクライアント端末は、自身のDisplay 1(メイン画面)へ出力
			targetDisplayIndex = 0;
		}

		if (m_SpectatorCamera != null)
		{
			m_SpectatorCamera.targetDisplay = targetDisplayIndex;
			m_SpectatorCamera.gameObject.SetActive(true);
		}

		if (m_SpectatorUIRoot != null)
		{
			m_SpectatorUIRoot.SetActive(true);

			// 観戦UIルート配下の全CanvasのTargetDisplayもカメラに合わせて同期
			Canvas[] canvases = m_SpectatorUIRoot.GetComponentsInChildren<Canvas>(true);
			for (int i = 0; i < canvases.Length; i++)
			{
				if (canvases[i] != null)
				{
					canvases[i].targetDisplay = targetDisplayIndex;
				}
			}
		}
	}
}
