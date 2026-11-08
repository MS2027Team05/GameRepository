using Unity.Netcode;
using UnityEngine;

/// <summary>
/// ゲームシーン終了時や中断時に、接続を切断することなく
/// スポーンされた全オブジェクトの破棄およびセッション同期データのリセットを行い、
/// 全クライアントをLobbyScene(タイトルオーバーレイ待機状態)へ安全に同期遷移させる調停クラス。
/// </summary>
public class GameSessionResetCoordinator : NetworkBehaviour
{
	public static GameSessionResetCoordinator Instance { get; private set; }

	[Header("遷移先ロビーシーン名")]
	[SerializeField] private string m_LobbySceneName;

	private bool m_IsResetting;

	private void Awake()
	{
		if (Instance != null && Instance != this)
		{
			Destroy(gameObject);
			return;
		}

		Instance = this;
	}

	public override void OnDestroy()
	{
		if (Instance == this)
		{
			Instance = null;
		}

		base.OnDestroy();
	}

	/// <summary>
	/// ゲームセッションを終了し、全端末をLobbySceneへ同期遷移させます。
	/// クライアント端末から呼び出された場合はServerRpcを経由してサーバーに要求します。
	/// </summary>
	public void EndGameAndReturnToLobby()
	{
		if (m_IsResetting) return;

		if (IsServer)
		{
			ExecuteResetAndTransition();
		}
		else
		{
			RequestReturnToLobbyServerRpc();
		}
	}

	[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
	private void RequestReturnToLobbyServerRpc()
	{
		if (m_IsResetting) return;
		ExecuteResetAndTransition();
	}

	private void ExecuteResetAndTransition()
	{
		if (!IsServer) return;
		m_IsResetting = true;

		// 1. スポーンされた全プレイヤーキャラクターのDespawn
		PlayerSpawner spawner = FindFirstObjectByType<PlayerSpawner>();
		if (spawner != null)
		{
			spawner.DespawnAllPlayers();
		}

		// 2. セッションデータストア内の全プレイヤーのReadyフラグをリセット
		if (SessionPlayerDataStore.Instance != null)
		{
			SessionPlayerDataStore.Instance.ResetAllReadyStates();
		}

		// 3. 入力モード・カーソル状態をUIモードにリセット
		ResetLocalClientInputStateRpc();

		// 4. 全クライアントをLobbySceneへ同期遷移
		string targetScene = !string.IsNullOrEmpty(m_LobbySceneName) ? m_LobbySceneName : "LobbyScene";
		if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
		{
			NetworkManager.Singleton.SceneManager.LoadScene(targetScene, UnityEngine.SceneManagement.LoadSceneMode.Single);
		}
	}

	[Rpc(SendTo.Everyone)]
	private void ResetLocalClientInputStateRpc()
	{
		PlayerInputReceiver.SetAllPaused(false);
		PlayerInputReceiver.SetCursorLocked(false);
	}
}

