using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

/// <summary>
/// ロビー画面での参加者管理・チーム分けデータ同期・ゲーム開始制御を担当する通信層クラス。
/// UIコンポーネントを直接操作せず、イベントおよびAPIを通じてUI層へ状態を提供します。
/// </summary>
public class LobbyManager : NetworkBehaviour
{
	[Header("シーン設定")]
	[FormerlySerializedAs("gameSceneName")]
	[SerializeField] private string m_GameSceneName;
	[SerializeField] private string m_LevelDesignSceneName;
	[FormerlySerializedAs("titleSceneName")]
	[SerializeField] private string m_TitleSceneName;

	[Header("人数設定")]
	[FormerlySerializedAs("defaultRequiredPlayers")]
	[SerializeField] private int m_DefaultRequiredPlayers;

	// 各チームの最大人数(2vs2)
	public const int MaxTeamMembers = 2;

	// ロビーデータや開始条件が変化した際に発火するイベント
	public event Action OnLobbyDataChanged;

	private int m_CurrentRequiredPlayers;
	// 現在選択されている遷移先がレベルデザインシーンかどうか
	private bool m_UseLevelDesignScene;
	private string m_CachedHostIp = "127.0.0.1";

	// 参加者情報のネットワーク同期リスト
	private readonly NetworkList<LobbyPlayerData> m_LobbyPlayers = new NetworkList<LobbyPlayerData>();

	public NetworkList<LobbyPlayerData> LobbyPlayers => m_LobbyPlayers;
	public int ConnectedPlayerCount => m_LobbyPlayers.Count;
	public int RequiredPlayerCount => m_CurrentRequiredPlayers;
	public bool UseLevelDesignScene => m_UseLevelDesignScene;
	public string HostIpAddress => m_CachedHostIp;

	public string TargetSceneName => m_UseLevelDesignScene
		? (!string.IsNullOrEmpty(m_LevelDesignSceneName) ? m_LevelDesignSceneName : "未設定(LevelDesign)")
		: (!string.IsNullOrEmpty(m_GameSceneName) ? m_GameSceneName : "未設定(GameScene)");

	private void Awake()
	{
		m_CurrentRequiredPlayers = m_DefaultRequiredPlayers > 0 ? m_DefaultRequiredPlayers : 4;
	}

	public override void OnNetworkSpawn()
	{
		EnsureSessionDataStore();

		m_LobbyPlayers.OnListChanged += HandleLobbyPlayersChanged;

		if (IsServer)
		{
			NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
			NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;

			m_CachedHostIp = GetLocalIPv4Address();

			// ホスト端末自身の情報を追加
			AddPlayer(NetworkManager.Singleton.LocalClientId);
		}

		// クライアント側でサーバーが切断された場合の検知
		NetworkManager.Singleton.OnClientDisconnectCallback += HandleServerDisconnect;

		NotifyDataChanged();
	}

	public override void OnNetworkDespawn()
	{
		m_LobbyPlayers.OnListChanged -= HandleLobbyPlayersChanged;

		if (NetworkManager.Singleton != null)
		{
			if (IsServer)
			{
				NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
				NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
			}
			NetworkManager.Singleton.OnClientDisconnectCallback -= HandleServerDisconnect;
		}
	}

	private void EnsureSessionDataStore()
	{
		if (IsServer && SessionPlayerDataStore.Instance == null)
		{
			var storeObj = new GameObject("SessionPlayerDataStore");
			storeObj.AddComponent<SessionPlayerDataStore>();
		}
	}

	private void HandleLobbyPlayersChanged(NetworkListEvent<LobbyPlayerData> changeEvent)
	{
		NotifyDataChanged();
	}

	private void HandleClientConnected(ulong clientId)
	{
		AddPlayer(clientId);
		NotifyDataChanged();
	}

	private void HandleClientDisconnected(ulong clientId)
	{
		RemovePlayer(clientId);
		NotifyDataChanged();
	}

	private void AddPlayer(ulong clientId)
	{
		if (!IsServer) return;

		for (int i = 0; i < m_LobbyPlayers.Count; i++)
		{
			if (m_LobbyPlayers[i].ClientId == clientId)
			{
				return;
			}
		}

		LobbyPlayerData initialData;
		if (SessionPlayerDataStore.Instance != null)
		{
			initialData = SessionPlayerDataStore.Instance.GetOrCreatePlayerData(clientId);
		}
		else
		{
			FixedString32Bytes defaultName = $"Player {clientId}";
			initialData = new LobbyPlayerData(clientId, defaultName);
		}

		m_LobbyPlayers.Add(initialData);
	}

	private void RemovePlayer(ulong clientId)
	{
		if (!IsServer) return;

		for (int i = 0; i < m_LobbyPlayers.Count; i++)
		{
			if (m_LobbyPlayers[i].ClientId == clientId)
			{
				m_LobbyPlayers.RemoveAt(i);
				break;
			}
		}
	}

	private void HandleServerDisconnect(ulong clientId)
	{
		if (!IsServer && clientId == NetworkManager.Singleton.LocalClientId)
		{
			// 自身が切断された場合、タイトルへ戻る
			ReturnToTitleScene();
		}
	}

	private void NotifyDataChanged()
	{
		OnLobbyDataChanged?.Invoke();
	}

	// --- 外部公開API (UIControllerから呼び出し) ---

	public void SetPlayerName(string newName)
	{
		if (string.IsNullOrWhiteSpace(newName) || NetworkManager.Singleton == null) return;
		RequestUpdatePlayerNameServerRpc(NetworkManager.Singleton.LocalClientId, newName.Trim());
	}

	public void JoinTeam(ETeamType team)
	{
		if (NetworkManager.Singleton == null) return;
		RequestJoinTeamServerRpc(NetworkManager.Singleton.LocalClientId, team);
	}

	public void LeaveTeam()
	{
		if (NetworkManager.Singleton == null) return;
		RequestLeaveTeamServerRpc(NetworkManager.Singleton.LocalClientId);
	}

	public void ToggleCharacter()
	{
		if (!TryGetLocalPlayerData(out var localPlayer) || localPlayer.IsReady || NetworkManager.Singleton == null) return;

		var nextChar = localPlayer.CharacterType == ECharacterType.ModelA
			? ECharacterType.ModelB
			: ECharacterType.ModelA;

		RequestUpdateCharacterTypeServerRpc(NetworkManager.Singleton.LocalClientId, nextChar);
	}

	public void CycleHighlightColor()
	{
		if (!TryGetLocalPlayerData(out var localPlayer) || localPlayer.IsReady || NetworkManager.Singleton == null) return;

		int nextColorIndex = ((int)localPlayer.HighlightColor + 1) % 6;
		RequestUpdateHighlightColorServerRpc(NetworkManager.Singleton.LocalClientId, (EHighlightColor)nextColorIndex);
	}

	public void ToggleReady()
	{
		if (!TryGetLocalPlayerData(out var localPlayer) || NetworkManager.Singleton == null) return;
		if (localPlayer.Team == ETeamType.None) return;

		RequestToggleReadyServerRpc(NetworkManager.Singleton.LocalClientId);
	}

	public void CycleMinPlayersDebug()
	{
		if (!IsServer) return;

		if (m_CurrentRequiredPlayers == 4) m_CurrentRequiredPlayers = 2;
		else if (m_CurrentRequiredPlayers == 2) m_CurrentRequiredPlayers = 1;
		else m_CurrentRequiredPlayers = 4;

		NotifyDataChanged();
	}

	public void ToggleTargetSceneDebug()
	{
		if (!IsServer) return;

		m_UseLevelDesignScene = !m_UseLevelDesignScene;
		NotifyDataChanged();
	}

	public void ForceStartGameDebug()
	{
		if (!IsServer) return;

		AutoAssignUnseatedPlayers();
		StartGameTransition();
	}

	public void StartGame()
	{
		if (!IsServer) return;

		StartGameTransition();
	}

	public void LeaveLobby()
	{
		if (NetworkManager.Singleton != null)
		{
			NetworkManager.Singleton.Shutdown();
		}

		ReturnToTitleScene();
	}

	public bool TryGetLocalPlayerData(out LobbyPlayerData playerData)
	{
		if (NetworkManager.Singleton != null)
		{
			ulong localId = NetworkManager.Singleton.LocalClientId;
			for (int i = 0; i < m_LobbyPlayers.Count; i++)
			{
				if (m_LobbyPlayers[i].ClientId == localId)
				{
					playerData = m_LobbyPlayers[i];
					return true;
				}
			}
		}

		playerData = default;
		return false;
	}

	public bool CheckCanStartGame(out string statusMessage)
	{
		int connectedCount = m_LobbyPlayers.Count;

		if (!IsServer)
		{
			statusMessage = "ホストのゲーム開始を待機しています...";
			return false;
		}

		bool allSeated = true;
		bool allReady = true;

		for (int i = 0; i < m_LobbyPlayers.Count; i++)
		{
			var p = m_LobbyPlayers[i];
			if (p.Team == ETeamType.None) allSeated = false;
			if (!p.IsReady) allReady = false;
		}

		bool canStart = (connectedCount >= m_CurrentRequiredPlayers) && allSeated && allReady;

		if (canStart)
		{
			statusMessage = "全員の準備が完了しました: ゲーム開始ボタンを押してください";
		}
		else if (connectedCount < m_CurrentRequiredPlayers)
		{
			statusMessage = $"待機中: あと {m_CurrentRequiredPlayers - connectedCount} 台の接続が必要です";
		}
		else if (!allSeated)
		{
			statusMessage = "チーム未所属のプレイヤーがいます";
		}
		else
		{
			statusMessage = "全員のReady完了を待機しています...";
		}

		return canStart;
	}

	// --- RPC通信処理 ---

	[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
	private void RequestJoinTeamServerRpc(ulong clientId, ETeamType targetTeam)
	{
		if (targetTeam == ETeamType.None) return;

		int count = 0;
		for (int i = 0; i < m_LobbyPlayers.Count; i++)
		{
			if (m_LobbyPlayers[i].Team == targetTeam) count++;
		}

		if (count >= MaxTeamMembers)
		{
			Debug.LogWarning($"[LobbyManager] {targetTeam} は満員です (最大 {MaxTeamMembers} 名)。");
			return;
		}

		for (int i = 0; i < m_LobbyPlayers.Count; i++)
		{
			if (m_LobbyPlayers[i].ClientId == clientId)
			{
				var data = m_LobbyPlayers[i];
				data.Team = targetTeam;
				data.IsReady = false;
				m_LobbyPlayers[i] = data;

				SessionPlayerDataStore.Instance?.SavePlayerData(data);
				break;
			}
		}
	}

	[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
	private void RequestLeaveTeamServerRpc(ulong clientId)
	{
		for (int i = 0; i < m_LobbyPlayers.Count; i++)
		{
			if (m_LobbyPlayers[i].ClientId == clientId)
			{
				var data = m_LobbyPlayers[i];
				data.Team = ETeamType.None;
				data.IsReady = false;
				m_LobbyPlayers[i] = data;

				SessionPlayerDataStore.Instance?.UnseatPlayer(clientId);
				break;
			}
		}
	}

	[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
	private void RequestUpdatePlayerNameServerRpc(ulong clientId, FixedString32Bytes newName)
	{
		for (int i = 0; i < m_LobbyPlayers.Count; i++)
		{
			if (m_LobbyPlayers[i].ClientId == clientId)
			{
				var data = m_LobbyPlayers[i];
				if (data.IsReady) return;

				data.PlayerName = newName;
				m_LobbyPlayers[i] = data;
				SessionPlayerDataStore.Instance?.SavePlayerData(data);
				break;
			}
		}
	}

	[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
	private void RequestUpdateCharacterTypeServerRpc(ulong clientId, ECharacterType characterType)
	{
		for (int i = 0; i < m_LobbyPlayers.Count; i++)
		{
			if (m_LobbyPlayers[i].ClientId == clientId)
			{
				var data = m_LobbyPlayers[i];
				if (data.IsReady) return;

				data.CharacterType = characterType;
				m_LobbyPlayers[i] = data;
				SessionPlayerDataStore.Instance?.SavePlayerData(data);
				break;
			}
		}
	}

	[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
	private void RequestUpdateHighlightColorServerRpc(ulong clientId, EHighlightColor highlightColor)
	{
		for (int i = 0; i < m_LobbyPlayers.Count; i++)
		{
			if (m_LobbyPlayers[i].ClientId == clientId)
			{
				var data = m_LobbyPlayers[i];
				if (data.IsReady) return;

				data.HighlightColor = highlightColor;
				m_LobbyPlayers[i] = data;
				SessionPlayerDataStore.Instance?.SavePlayerData(data);
				break;
			}
		}
	}

	[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
	private void RequestToggleReadyServerRpc(ulong clientId)
	{
		for (int i = 0; i < m_LobbyPlayers.Count; i++)
		{
			if (m_LobbyPlayers[i].ClientId == clientId)
			{
				var data = m_LobbyPlayers[i];
				if (data.Team == ETeamType.None) return;

				data.IsReady = !data.IsReady;
				m_LobbyPlayers[i] = data;

				SessionPlayerDataStore.Instance?.SavePlayerData(data);
				break;
			}
		}
	}

	[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
	public void SwapPlayersServerRpc(ulong clientA, ulong clientB, RpcParams rpcParams = default)
	{
		if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId)
		{
			Debug.LogWarning("[LobbyManager] SwapPlayersはHostのみ実行可能です。");
			return;
		}

		int indexA = -1;
		int indexB = -1;

		for (int i = 0; i < m_LobbyPlayers.Count; i++)
		{
			if (m_LobbyPlayers[i].ClientId == clientA) indexA = i;
			if (m_LobbyPlayers[i].ClientId == clientB) indexB = i;
		}

		if (indexA >= 0 && indexB >= 0)
		{
			var dataA = m_LobbyPlayers[indexA];
			var dataB = m_LobbyPlayers[indexB];

			var tempTeam = dataA.Team;
			dataA.Team = dataB.Team;
			dataB.Team = tempTeam;

			dataA.IsReady = false;
			dataB.IsReady = false;

			m_LobbyPlayers[indexA] = dataA;
			m_LobbyPlayers[indexB] = dataB;

			SessionPlayerDataStore.Instance?.SavePlayerData(dataA);
			SessionPlayerDataStore.Instance?.SavePlayerData(dataB);
		}
	}

	private void AutoAssignUnseatedPlayers()
	{
		int redCount = 0;
		int blueCount = 0;
		for (int i = 0; i < m_LobbyPlayers.Count; i++)
		{
			if (m_LobbyPlayers[i].Team == ETeamType.Red) redCount++;
			if (m_LobbyPlayers[i].Team == ETeamType.Blue) blueCount++;
		}

		for (int i = 0; i < m_LobbyPlayers.Count; i++)
		{
			if (m_LobbyPlayers[i].Team == ETeamType.None)
			{
				var data = m_LobbyPlayers[i];
				if (redCount <= blueCount && redCount < MaxTeamMembers)
				{
					data.Team = ETeamType.Red;
					redCount++;
				}
				else
				{
					data.Team = ETeamType.Blue;
					blueCount++;
				}
				data.IsReady = true;
				m_LobbyPlayers[i] = data;

				SessionPlayerDataStore.Instance?.SavePlayerData(data);
			}
		}
	}

	private void StartGameTransition()
	{
		if (SessionPlayerDataStore.Instance != null)
		{
			for (int i = 0; i < m_LobbyPlayers.Count; i++)
			{
				SessionPlayerDataStore.Instance.SavePlayerData(m_LobbyPlayers[i]);
			}
		}

		string targetScene = m_UseLevelDesignScene ? m_LevelDesignSceneName : m_GameSceneName;
		if (!string.IsNullOrEmpty(targetScene))
		{
			NetworkManager.Singleton.SceneManager.LoadScene(targetScene, LoadSceneMode.Single);
		}
		else
		{
			Debug.LogWarning("[LobbyManager] 遷移先シーン名が設定されていません。");
		}
	}

	private void ReturnToTitleScene()
	{
		if (!string.IsNullOrEmpty(m_TitleSceneName))
		{
			SceneManager.LoadScene(m_TitleSceneName);
		}
	}

	private string GetLocalIPv4Address()
	{
		try
		{
			string fallbackIp = null;

			// 稼働中のネットワークインターフェースを検索
			foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
			{
				// 有効(Up)かつループバックやトンネルでないものを対象
				if (ni.OperationalStatus != OperationalStatus.Up ||
					ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
					ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
				{
					continue;
				}

				// 仮想アダプタ名（WSL, VirtualBox, vEthernet, Hyper-V, VMwareなど）を除外
				string name = ni.Name.ToLower();
				string desc = ni.Description.ToLower();
				if (name.Contains("vethernet") || name.Contains("wsl") || name.Contains("virtual") ||
					desc.Contains("virtual") || desc.Contains("hyper-v") || desc.Contains("vmware"))
				{
					continue;
				}

				IPInterfaceProperties props = ni.GetIPProperties();
				foreach (UnicastIPAddressInformation addr in props.UnicastAddresses)
				{
					if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
					{
						// 有線LAN(Ethernet)を最優先で返却
						if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
						{
							return addr.Address.ToString();
						}

						// Wi-Fiなどの他の有効なアダプタは候補として保持
						if (fallbackIp == null)
						{
							fallbackIp = addr.Address.ToString();
						}
					}
				}
			}

			if (!string.IsNullOrEmpty(fallbackIp))
			{
				return fallbackIp;
			}

			// フォールバック: Dns.GetHostEntry で取得
			IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
			foreach (IPAddress ip in host.AddressList)
			{
				if (ip.AddressFamily == AddressFamily.InterNetwork)
				{
					return ip.ToString();
				}
			}
		}
		catch (Exception ex)
		{
			Debug.LogWarning($"[LobbyManager] ローカルIP取得エラー: {ex.Message}");
		}

		return "127.0.0.1";
	}
}



