using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// サーバー(Host)側でクライアントの接続承認を行い、
/// TitleSceneおよびLobbySceneでの通常参加・再接続のみを許可して試合中の途中接続を拒否するハンドラー。
/// NetworkManagerと同一GameObject上で動作し、シーン遷移を跨いで維持されます。
/// </summary>
public class NetworkConnectionApprovalHandler : MonoBehaviour
{
	private const string m_DefaultLobbySceneName = "LobbyScene";
	private const string m_InMatchRejectReason = "試合進行中のため接続できません。ロビー復帰後に再接続してください。";
	private const string m_StartingMatchRejectReason = "試合開始処理中のため接続できません。ロビー復帰後に再接続してください。";

	private string m_TitleSceneName;
	private string m_LobbySceneName;
	private bool m_IsCallbackRegistered;

	/// <summary>
	/// 指定されたNetworkManagerオブジェクト上にハンドラーを確保して返却します。
	/// </summary>
	public static NetworkConnectionApprovalHandler EnsureAttached(NetworkManager networkManager)
	{
		if (networkManager == null)
		{
			return null;
		}

		NetworkConnectionApprovalHandler handler = networkManager.GetComponent<NetworkConnectionApprovalHandler>();
		if (handler == null)
		{
			handler = networkManager.gameObject.AddComponent<NetworkConnectionApprovalHandler>();
		}

		return handler;
	}

	/// <summary>
	/// ホスト起動前に接続承認設定および許可シーン名を構成します。
	/// </summary>
	/// <param name="titleSceneName">接続受付を許可するタイトルシーン名</param>
	/// <param name="lobbySceneName">再接続受付を許可するロビーシーン名</param>
	public void ConfigureForHost(string titleSceneName, string lobbySceneName)
	{
		if (NetworkManager.Singleton == null)
		{
			return;
		}

		m_TitleSceneName = !string.IsNullOrWhiteSpace(titleSceneName)
			? titleSceneName.Trim()
			: SceneManager.GetActiveScene().name;

		m_LobbySceneName = !string.IsNullOrWhiteSpace(lobbySceneName)
			? lobbySceneName.Trim()
			: m_DefaultLobbySceneName;

		if (NetworkManager.Singleton.NetworkConfig != null)
		{
			NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
		}

		NetworkManager.Singleton.ConnectionApprovalCallback = HandleConnectionApproval;
		m_IsCallbackRegistered = true;
	}

	/// <summary>
	/// クライアント接続前にサーバーとNetworkConfigを一致させるためConnectionApprovalを有効化します。
	/// </summary>
	public void ConfigureForClient()
	{
		if (NetworkManager.Singleton == null)
		{
			return;
		}

		ClearApprovalCallback();

		if (NetworkManager.Singleton.NetworkConfig != null)
		{
			NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
		}
	}

	/// <summary>
	/// 登録した接続承認コールバックを解除します。
	/// </summary>
	public void ClearApprovalCallback()
	{
		if (m_IsCallbackRegistered && NetworkManager.Singleton != null)
		{
			if (NetworkManager.Singleton.ConnectionApprovalCallback == HandleConnectionApproval)
			{
				NetworkManager.Singleton.ConnectionApprovalCallback = null;
			}
		}

		m_IsCallbackRegistered = false;
	}

	private void OnDestroy()
	{
		ClearApprovalCallback();
	}

	private void HandleConnectionApproval(
		NetworkManager.ConnectionApprovalRequest request,
		NetworkManager.ConnectionApprovalResponse response)
	{
		bool shouldCreatePlayer =
			NetworkManager.Singleton != null &&
			NetworkManager.Singleton.NetworkConfig != null &&
			NetworkManager.Singleton.NetworkConfig.PlayerPrefab != null;

		// ホスト端末自身のローカル接続は常に許可
		if (request.ClientNetworkId == NetworkManager.ServerClientId)
		{
			response.Approved = true;
			response.CreatePlayerObject = shouldCreatePlayer;
			response.Pending = false;
			return;
		}

		string activeSceneName = SceneManager.GetActiveScene().name;
		bool isAllowedScene =
			string.Equals(activeSceneName, m_TitleSceneName, StringComparison.Ordinal) ||
			string.Equals(activeSceneName, m_LobbySceneName, StringComparison.Ordinal);

		if (!isAllowedScene)
		{
			response.Approved = false;
			response.CreatePlayerObject = false;
			response.Reason = m_InMatchRejectReason;
			response.Pending = false;
			Debug.Log($"[ConnectionApproval] 試合進行中のため接続を拒否しました: ClientId={request.ClientNetworkId}, Scene={activeSceneName}");
			return;
		}

		LobbyManager lobbyManager = FindFirstObjectByType<LobbyManager>();
		if (lobbyManager != null && lobbyManager.IsGameStarting)
		{
			response.Approved = false;
			response.CreatePlayerObject = false;
			response.Reason = m_StartingMatchRejectReason;
			response.Pending = false;
			Debug.Log($"[ConnectionApproval] 試合開始処理中のため接続を拒否しました: ClientId={request.ClientNetworkId}");
			return;
		}

		response.Approved = true;
		response.CreatePlayerObject = shouldCreatePlayer;
		response.Pending = false;
	}
}
