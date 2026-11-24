using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 起動時接続シーン(TitleScene / Bootstrap)において、
/// 接続前のJoinPanel管理、接続後のGeneralPanel・ClientPlayerPanel・HostPlayerPanelの排他表示、
/// チーム割り当てのリアルタイム同期、およびLobbySceneへの同期遷移を統括するクラス。
/// </summary>
public class NetworkConnectManager : NetworkBehaviour
{
	[Header("接続前: JoinPanel UI参照")]
	[SerializeField] private GameObject m_JoinPanel;
	[SerializeField] private Button m_HostSpectatorButton;
	[SerializeField] private Button m_HostPlayerButton;
	[SerializeField] private Button m_ClientButton;
	[SerializeField] private Button m_ClientSpectatorButton;
	[SerializeField] private Button m_QuitButton;
	[SerializeField] private TMP_InputField m_IpInputField;
	[SerializeField] private TextMeshProUGUI m_StatusText;

	[Header("接続後: 共通 GeneralPanel UI参照")]
	[SerializeField] private GameObject m_GeneralPanel;
	[SerializeField] private TextMeshProUGUI m_PlayerCountText;
	[SerializeField] private TextMeshProUGUI m_MemberListText;

	[Header("接続後: プレイヤー用 ClientPlayerPanel UI参照")]
	[SerializeField] private GameObject m_ClientPlayerPanel;
	[SerializeField] private Button m_JoinRedTeamButton;
	[SerializeField] private Button m_JoinBlueTeamButton;
	[SerializeField] private Button m_JoinSpectatorTeamButton;
	[SerializeField] private Button m_LeaveTeamButton;
	[SerializeField] private Button m_ClientDisconnectButton;

	[Header("接続後: ホスト用 HostPlayerPanel UI参照")]
	[SerializeField] private GameObject m_HostPlayerPanel;
	[SerializeField] private Button m_StartLobbyButton;
	[SerializeField] private TextMeshProUGUI m_HostIpText;
	[SerializeField] private Button m_HostDisconnectButton;

	[Header("接続・遷移設定")]
	[SerializeField] private ushort m_DefaultPort;
	[SerializeField] private string m_LobbySceneName;

	// デバッグ用兼任ホストかどうかの共有フラグ
	public static bool IsPlayerHost { get; private set; }

	// 観戦者として接続したかどうかのローカルフラグ
	public static bool IsSpectatorClient { get; private set; }

	/// <summary>
	/// 他モジュール(ロビー同期等)からローカル観戦クライアント状態をサーバー確定値へ同期します。
	/// </summary>
	public static void SetSpectatorClientState(bool isSpectator)
	{
		IsSpectatorClient = isSpectator;
	}

	private UnityTransport m_UnityTransport;
	private readonly StringBuilder m_StatusBuilder = new StringBuilder();
	private readonly StringBuilder m_MemberInfoBuilder = new StringBuilder();
	private string m_CachedHostIp = "127.0.0.1";
	private bool m_PendingInitialSpectatorSync;
	private bool m_IsClientConnecting;

	// 接続参加者のリアルタイム同期リスト
	private readonly NetworkList<LobbyPlayerData> m_ConnectedPlayers = new NetworkList<LobbyPlayerData>();

	private void Awake()
	{
		InitializeTransport();
		SetupButtonListeners();
		SetupInputConstraints();
	}

	private void Start()
	{
		// 起動時はJoinPanelのみを有効化し、他パネルは完全に非表示
		ShowJoinPanelOnly();

		// 前回接続したIPアドレスを復元
		if (m_IpInputField != null)
		{
			string savedIp = LocalPlayerProfilePreferences.GetTargetIp("127.0.0.1");
			m_IpInputField.text = savedIp;
		}

		SetStatusMessage("待機中: Host起動またはClient接続を選択してください");
	}

	public override void OnNetworkSpawn()
	{
		m_IsClientConnecting = false;
		UnsubscribePendingClientDisconnect();
		EnsureSessionDataStore();

		m_ConnectedPlayers.OnListChanged += HandlePlayersListChanged;

		if (IsServer)
		{
			NetworkManager.Singleton.OnClientConnectedCallback += HandleServerClientConnected;
			NetworkManager.Singleton.OnClientDisconnectCallback += HandleServerClientDisconnected;

			m_CachedHostIp = GetLocalIPv4Address();
			if (m_HostIpText != null)
			{
				m_HostIpText.text = $"Host IP: {m_CachedHostIp}";
			}

			// プレイヤー兼任ホストの場合のみホスト端末自身をプレイヤーリストに追加
			if (IsPlayerHost)
			{
				AddPlayer(NetworkManager.Singleton.LocalClientId);
			}

			ShowConnectedPanels(isHost: true);
		}
		else
		{
			// クライアント側でサーバー切断を検知
			NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnectedFromServer;

			ShowConnectedPanels(isHost: false);

			// 観戦者クライアントとして接続した場合は、即座にサーバーへ観戦チーム所属を通知して同期
			if (IsSpectatorClient)
			{
				m_PendingInitialSpectatorSync = true;
				RequestJoinTeamServerRpc(NetworkManager.Singleton.LocalClientId, ETeamType.Spectator);
			}
		}

		SyncLocalTeamFromConnectedPlayers();
		UpdateGeneralPanelUI();
	}

	public override void OnNetworkDespawn()
	{
		UnsubscribePendingClientDisconnect();
		m_ConnectedPlayers.OnListChanged -= HandlePlayersListChanged;

		if (NetworkManager.Singleton != null)
		{
			if (IsServer)
			{
				NetworkManager.Singleton.OnClientConnectedCallback -= HandleServerClientConnected;
				NetworkManager.Singleton.OnClientDisconnectCallback -= HandleServerClientDisconnected;
			}
			else
			{
				NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnectedFromServer;
			}
		}

		base.OnNetworkDespawn();
	}

	public override void OnDestroy()
	{
		UnsubscribePendingClientDisconnect();
		base.OnDestroy();
	}

	private void InitializeTransport()
	{
		if (NetworkManager.Singleton != null)
		{
			m_UnityTransport = NetworkManager.Singleton.GetComponent<UnityTransport>();
		}
	}

	private void SetupInputConstraints()
	{
		if (m_IpInputField != null)
		{
			m_IpInputField.characterLimit = 15;
		}
	}

	private void SetupButtonListeners()
	{
		// JoinPanel ボタン
		if (m_HostSpectatorButton != null)
		{
			m_HostSpectatorButton.onClick.AddListener(() => StartAsHost(false));
		}

		if (m_HostPlayerButton != null)
		{
			m_HostPlayerButton.onClick.AddListener(() => StartAsHost(true));
		}

		if (m_ClientButton != null)
		{
			m_ClientButton.onClick.AddListener(StartAsClient);
		}

		if (m_ClientSpectatorButton != null)
		{
			m_ClientSpectatorButton.onClick.AddListener(StartAsSpectatorClient);
		}

		if (m_QuitButton != null)
		{
			m_QuitButton.onClick.AddListener(QuitGame);
		}

		// ClientPlayerPanel ボタン
		if (m_JoinRedTeamButton != null)
		{
			m_JoinRedTeamButton.onClick.AddListener(() => RequestSelectTeam(ETeamType.Red));
		}

		if (m_JoinBlueTeamButton != null)
		{
			m_JoinBlueTeamButton.onClick.AddListener(() => RequestSelectTeam(ETeamType.Blue));
		}

		if (m_JoinSpectatorTeamButton != null)
		{
			m_JoinSpectatorTeamButton.onClick.AddListener(() => RequestSelectTeam(ETeamType.Spectator));
		}

		if (m_LeaveTeamButton != null)
		{
			m_LeaveTeamButton.onClick.AddListener(() => RequestSelectTeam(ETeamType.None));
		}

		if (m_ClientDisconnectButton != null)
		{
			m_ClientDisconnectButton.onClick.AddListener(DisconnectClientAndReturnToJoinPanel);
		}

		// HostPlayerPanel ボタン
		if (m_StartLobbyButton != null)
		{
			m_StartLobbyButton.onClick.AddListener(ProceedToLobbyScene);
		}

		if (m_HostDisconnectButton != null)
		{
			m_HostDisconnectButton.onClick.AddListener(ShutdownHostAndReturnToJoinPanel);
		}
	}

	/// <summary>
	/// JoinPanelのみを表示し、接続後パネルをすべて非表示にします(初期状態・切断時)。
	/// </summary>
	public void ShowJoinPanelOnly()
	{
		if (m_JoinPanel != null) m_JoinPanel.SetActive(true);
		if (m_GeneralPanel != null) m_GeneralPanel.SetActive(false);
		if (m_ClientPlayerPanel != null) m_ClientPlayerPanel.SetActive(false);
		if (m_HostPlayerPanel != null) m_HostPlayerPanel.SetActive(false);

		// JoinPanelの最初の選択ボタンを設定
		SetInitialSelectedButton(m_HostSpectatorButton);
	}

	/// <summary>
	/// 接続成功時にJoinPanelを閉じ、GeneralPanelおよび端末ロールに応じたパネルを表示します。
	/// </summary>
	private void ShowConnectedPanels(bool isHost)
	{
		if (m_JoinPanel != null) m_JoinPanel.SetActive(false);
		if (m_GeneralPanel != null) m_GeneralPanel.SetActive(true);

		if (isHost)
		{
			if (m_HostPlayerPanel != null) m_HostPlayerPanel.SetActive(true);
			// 兼任ホストの場合はクライアント用チーム操作パネルも合わせて表示
			if (m_ClientPlayerPanel != null) m_ClientPlayerPanel.SetActive(IsPlayerHost);

			// ホスト用パネルの最初の選択ボタンを設定
			SetInitialSelectedButton(m_StartLobbyButton);
		}
		else
		{
			if (m_ClientPlayerPanel != null) m_ClientPlayerPanel.SetActive(true);
			if (m_HostPlayerPanel != null) m_HostPlayerPanel.SetActive(false);

			// クライアント用パネルの最初の選択ボタンを設定
			SetInitialSelectedButton(m_JoinRedTeamButton);
		}
	}

	/// <summary>
	/// ホストとして接続を開始します。
	/// </summary>
	public void StartAsHost(bool asPlayer)
	{
		if (NetworkManager.Singleton == null)
		{
			SetStatusMessage("エラー: NetworkManagerが見つかりません");
			return;
		}

		IsPlayerHost = asPlayer;
		IsSpectatorClient = false;
		m_PendingInitialSpectatorSync = false;
		if (asPlayer && LocalPlayerProfilePreferences.GetAssignedTeam() == ETeamType.Spectator)
		{
			LocalPlayerProfilePreferences.SetAssignedTeam(ETeamType.None);
		}
		SetStatusMessage(asPlayer ? "Host(Player兼任)起動中..." : "Host(観戦専用)起動中...");

		ushort port = m_DefaultPort > 0 ? m_DefaultPort : (ushort)7777;
		if (m_UnityTransport != null)
		{
			m_UnityTransport.SetConnectionData("127.0.0.1", port, "0.0.0.0");
		}

		NetworkConnectionApprovalHandler approvalHandler =
			NetworkConnectionApprovalHandler.EnsureAttached(NetworkManager.Singleton);
		approvalHandler?.ConfigureForHost(SceneManager.GetActiveScene().name, m_LobbySceneName);

		bool started = NetworkManager.Singleton.StartHost();
		if (!started)
		{
			approvalHandler?.ClearApprovalCallback();
			SetStatusMessage("エラー: Host起動に失敗しました");
			ShowJoinPanelOnly();
		}
	}

	/// <summary>
	/// クライアントとしてホストに接続します。
	/// </summary>
	public void StartAsClient()
	{
		ConnectClientInternal(asSpectator: false);
	}

	/// <summary>
	/// 観戦者クライアントとしてホストに接続します(試合には参加せずDisplay 1に観戦画面を出力)。
	/// </summary>
	public void StartAsSpectatorClient()
	{
		ConnectClientInternal(asSpectator: true);
	}

	private void ConnectClientInternal(bool asSpectator)
	{
		if (NetworkManager.Singleton == null)
		{
			SetStatusMessage("エラー: NetworkManagerが見つかりません");
			return;
		}

		IsPlayerHost = false;
		IsSpectatorClient = asSpectator;
		m_PendingInitialSpectatorSync = asSpectator;

		if (asSpectator)
		{
			LocalPlayerProfilePreferences.SetAssignedTeam(ETeamType.Spectator);
		}
		else if (LocalPlayerProfilePreferences.GetAssignedTeam() == ETeamType.Spectator)
		{
			LocalPlayerProfilePreferences.SetAssignedTeam(ETeamType.None);
		}

		string targetIp = GetTargetIpAddress();
		if (!IsValidIpAddress(targetIp))
		{
			SetStatusMessage("エラー: IPアドレスの形式が不正です(例: 192.168.1.10)");
			return;
		}

		LocalPlayerProfilePreferences.SetTargetIp(targetIp);

		ushort port = m_DefaultPort > 0 ? m_DefaultPort : (ushort)7777;
		if (m_UnityTransport != null)
		{
			m_UnityTransport.SetConnectionData(targetIp, port);
		}

		SetStatusMessage(asSpectator ? $"観戦者として接続試行中: {targetIp}:{port} ..." : $"接続試行中: {targetIp}:{port} ...");
		NetworkConnectionApprovalHandler approvalHandler =
			NetworkConnectionApprovalHandler.EnsureAttached(NetworkManager.Singleton);
		approvalHandler?.ConfigureForClient();

		SubscribePendingClientDisconnect();
		bool started = NetworkManager.Singleton.StartClient();
		if (!started)
		{
			m_IsClientConnecting = false;
			UnsubscribePendingClientDisconnect();
			SetStatusMessage("エラー: Client接続開始に失敗しました");
			ShowJoinPanelOnly();
		}
	}

	private bool IsValidIpAddress(string ip)
	{
		if (string.IsNullOrWhiteSpace(ip)) return false;
		return IPAddress.TryParse(ip.Trim(), out _);
	}

	private string GetTargetIpAddress()
	{
		if (m_IpInputField != null && !string.IsNullOrWhiteSpace(m_IpInputField.text))
		{
			return m_IpInputField.text.Trim();
		}
		return "127.0.0.1";
	}

	private void HandleServerClientConnected(ulong clientId)
	{
		AddPlayer(clientId);
	}

	private void HandleServerClientDisconnected(ulong clientId)
	{
		RemovePlayer(clientId);
	}

	private void SubscribePendingClientDisconnect()
	{
		if (NetworkManager.Singleton == null)
		{
			return;
		}

		m_IsClientConnecting = true;
		NetworkManager.Singleton.OnClientDisconnectCallback -= HandlePendingClientDisconnected;
		NetworkManager.Singleton.OnClientDisconnectCallback += HandlePendingClientDisconnected;
	}

	private void UnsubscribePendingClientDisconnect()
	{
		if (NetworkManager.Singleton != null)
		{
			NetworkManager.Singleton.OnClientDisconnectCallback -= HandlePendingClientDisconnected;
		}
	}

	private void HandlePendingClientDisconnected(ulong clientId)
	{
		if (!m_IsClientConnecting || NetworkManager.Singleton == null || NetworkManager.Singleton.IsServer)
		{
			return;
		}

		if (clientId != NetworkManager.Singleton.LocalClientId && clientId != NetworkManager.ServerClientId)
		{
			return;
		}

		string reason = NetworkManager.Singleton.DisconnectReason;
		DisconnectClientAndReturnToJoinPanel();
		SetStatusMessage(!string.IsNullOrEmpty(reason)
			? reason
			: "サーバーへ接続できませんでした。IPアドレスとホスト状態を確認してください。");
	}

	private void HandleClientDisconnectedFromServer(ulong clientId)
	{
		if (!IsServer && NetworkManager.Singleton != null &&
			(clientId == NetworkManager.Singleton.LocalClientId || clientId == NetworkManager.ServerClientId))
		{
			string reason = NetworkManager.Singleton.DisconnectReason;
			DisconnectClientAndReturnToJoinPanel();
			SetStatusMessage(!string.IsNullOrEmpty(reason)
				? reason
				: "サーバーとの接続が切断されました。");
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

	private void AddPlayer(ulong clientId)
	{
		if (!IsServer) return;

		for (int i = 0; i < m_ConnectedPlayers.Count; i++)
		{
			if (m_ConnectedPlayers[i].ClientId == clientId) return;
		}

		LobbyPlayerData initialData;
		if (SessionPlayerDataStore.Instance != null)
		{
			initialData = SessionPlayerDataStore.Instance.GetOrCreatePlayerData(clientId);
		}
		else
		{
			FixedString64Bytes defaultName = $"Player {clientId}";
			initialData = new LobbyPlayerData(clientId, defaultName);
		}

		m_ConnectedPlayers.Add(initialData);
	}

	private void RemovePlayer(ulong clientId)
	{
		if (!IsServer) return;

		for (int i = 0; i < m_ConnectedPlayers.Count; i++)
		{
			if (m_ConnectedPlayers[i].ClientId == clientId)
			{
				m_ConnectedPlayers.RemoveAt(i);
				break;
			}
		}
	}

	private void HandlePlayersListChanged(NetworkListEvent<LobbyPlayerData> changeEvent)
	{
		SyncLocalTeamFromConnectedPlayers();
		UpdateGeneralPanelUI();
	}

	/// <summary>
	/// サーバーから同期された確定済みチーム情報をもとに、ローカルの観戦フラグおよび保存設定を更新します。
	/// 満員等でサーバー側が変更を拒否した場合でもローカル状態がサーバーと食い違わないようにします。
	/// </summary>
	private void SyncLocalTeamFromConnectedPlayers()
	{
		if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
		{
			return;
		}

		ulong localClientId = NetworkManager.Singleton.LocalClientId;
		for (int i = 0; i < m_ConnectedPlayers.Count; i++)
		{
			if (m_ConnectedPlayers[i].ClientId != localClientId)
			{
				continue;
			}

			ETeamType syncedTeam = m_ConnectedPlayers[i].Team;

			// 観戦接続直後のサーバー確定待ちの間は、初期状態のNone通知でローカル観戦フラグを上書きしない
			if (m_PendingInitialSpectatorSync)
			{
				if (syncedTeam != ETeamType.Spectator)
				{
					return;
				}

				m_PendingInitialSpectatorSync = false;
			}

			IsSpectatorClient = (syncedTeam == ETeamType.Spectator);
			LocalPlayerProfilePreferences.SetAssignedTeam(syncedTeam);
			return;
		}
	}

	private void UpdateGeneralPanelUI()
	{
		int count = 0;
		for (int i = 0; i < m_ConnectedPlayers.Count; i++)
		{
			ETeamType team = m_ConnectedPlayers[i].Team;
			if (team == ETeamType.Red || team == ETeamType.Blue)
			{
				count++;
			}
		}

		if (m_PlayerCountText != null)
		{
			m_PlayerCountText.text = $"参加人数: {count} / 4";
		}

		if (m_MemberListText != null)
		{
			m_MemberInfoBuilder.Clear();
			m_MemberInfoBuilder.Append("【参加プレイヤー一覧】\n");

			int redCount = 0;
			int blueCount = 0;
			int spectatorCount = 0;

			for (int i = 0; i < m_ConnectedPlayers.Count; i++)
			{
				var p = m_ConnectedPlayers[i];
				string teamStr;
				if (p.Team == ETeamType.Red)
				{
					teamStr = "<color=#FF5555>[RED]</color>";
					redCount++;
				}
				else if (p.Team == ETeamType.Blue)
				{
					teamStr = "<color=#5588FF>[BLUE]</color>";
					blueCount++;
				}
				else if (p.Team == ETeamType.Spectator)
				{
					teamStr = "<color=#88FF88>[観戦]</color>";
					spectatorCount++;
				}
				else
				{
					teamStr = "[未所属]";
				}

				m_MemberInfoBuilder.Append($"・#{p.ClientId} {p.PlayerName} : {teamStr}\n");
			}

			m_MemberInfoBuilder.Append($"\n(Red: {redCount}/2, Blue: {blueCount}/2, 観戦: {spectatorCount})");
			m_MemberListText.text = m_MemberInfoBuilder.ToString();
		}
	}

	private void RequestSelectTeam(ETeamType team)
	{
		if (NetworkManager.Singleton == null) return;
		ulong localId = NetworkManager.Singleton.LocalClientId;

		// ユーザー自身がチーム変更を明示操作した場合は初期観戦同期の保護を解除し、サーバー確定値に従う
		m_PendingInitialSpectatorSync = false;

		if (team == ETeamType.None)
		{
			RequestLeaveTeamServerRpc(localId);
		}
		else
		{
			RequestJoinTeamServerRpc(localId, team);
		}
	}

	[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
	private void RequestJoinTeamServerRpc(ulong clientId, ETeamType targetTeam)
	{
		if (targetTeam == ETeamType.None) return;

		if (targetTeam != ETeamType.Spectator)
		{
			int count = 0;
			for (int i = 0; i < m_ConnectedPlayers.Count; i++)
			{
				if (m_ConnectedPlayers[i].Team == targetTeam) count++;
			}

			// 各チーム最大2名(観戦者は人数制限なし)
			if (count >= LobbyManager.MaxTeamMembers)
			{
				return;
			}
		}

		for (int i = 0; i < m_ConnectedPlayers.Count; i++)
		{
			if (m_ConnectedPlayers[i].ClientId == clientId)
			{
				var data = m_ConnectedPlayers[i];
				data.Team = targetTeam;
				if (SessionPlayerDataStore.Instance != null)
				{
					data = SessionPlayerDataStore.Instance.NormalizeDefaultPlayerName(data);
				}
				m_ConnectedPlayers[i] = data;
				break;
			}
		}
	}

	[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
	private void RequestLeaveTeamServerRpc(ulong clientId)
	{
		for (int i = 0; i < m_ConnectedPlayers.Count; i++)
		{
			if (m_ConnectedPlayers[i].ClientId == clientId)
			{
				var data = m_ConnectedPlayers[i];
				data.Team = ETeamType.None;
				if (SessionPlayerDataStore.Instance != null)
				{
					data = SessionPlayerDataStore.Instance.NormalizeDefaultPlayerName(data);
				}
				m_ConnectedPlayers[i] = data;
				break;
			}
		}
	}

	/// <summary>
	/// チーム未所属(None)のプレイヤーをRed/Blueの空いている枠へ自動割り当てします。
	/// </summary>
	private void AutoAssignUnseatedPlayers()
	{
		if (!IsServer) return;

		int redCount = 0;
		int blueCount = 0;

		for (int i = 0; i < m_ConnectedPlayers.Count; i++)
		{
			if (m_ConnectedPlayers[i].Team == ETeamType.Red) redCount++;
			else if (m_ConnectedPlayers[i].Team == ETeamType.Blue) blueCount++;
		}

		for (int i = 0; i < m_ConnectedPlayers.Count; i++)
		{
			// 観戦者(Spectator)は自動割り当て対象外
			if (m_ConnectedPlayers[i].Team == ETeamType.None)
			{
				var data = m_ConnectedPlayers[i];
				if (redCount <= blueCount && redCount < LobbyManager.MaxTeamMembers)
				{
					data.Team = ETeamType.Red;
					redCount++;
				}
				else if (blueCount < LobbyManager.MaxTeamMembers)
				{
					data.Team = ETeamType.Blue;
					blueCount++;
				}
				else if (redCount < LobbyManager.MaxTeamMembers)
				{
					data.Team = ETeamType.Red;
					redCount++;
				}

				m_ConnectedPlayers[i] = data;
			}
		}
	}

	/// <summary>
	/// ホスト操作により、全員を一括してLobbySceneへ同期遷移させます。
	/// </summary>
	public void ProceedToLobbyScene()
	{
		if (!IsServer) return;

		// 未所属プレイヤーをRed/Blueの空いている枠へ自動割り当てして解消
		AutoAssignUnseatedPlayers();

		if (SessionPlayerDataStore.Instance != null)
		{
			for (int i = 0; i < m_ConnectedPlayers.Count; i++)
			{
				SessionPlayerDataStore.Instance.SavePlayerData(m_ConnectedPlayers[i]);
			}
		}

		string targetScene = !string.IsNullOrEmpty(m_LobbySceneName) ? m_LobbySceneName : "LobbyScene";
		if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
		{
			NetworkSceneTimeoutController.LoadNetworkScene(targetScene, LoadSceneMode.Single);
		}
	}

	/// <summary>
	/// クライアントが切断してJoinPanel初期画面へ復帰します。
	/// </summary>
	public void DisconnectClientAndReturnToJoinPanel()
	{
		m_IsClientConnecting = false;
		UnsubscribePendingClientDisconnect();
		m_PendingInitialSpectatorSync = false;
		IsSpectatorClient = false;
		IsPlayerHost = false;

		if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
		{
			NetworkManager.Singleton.Shutdown();
		}

		ShowJoinPanelOnly();
		SetStatusMessage("切断しました。待機画面に戻ります");
	}

	/// <summary>
	/// ホストが解散・シャットダウンしてJoinPanel初期画面へ復帰します。
	/// </summary>
	public void ShutdownHostAndReturnToJoinPanel()
	{
		m_IsClientConnecting = false;
		UnsubscribePendingClientDisconnect();
		m_PendingInitialSpectatorSync = false;
		IsSpectatorClient = false;
		IsPlayerHost = false;

		if (NetworkManager.Singleton != null)
		{
			NetworkConnectionApprovalHandler approvalHandler =
				NetworkManager.Singleton.GetComponent<NetworkConnectionApprovalHandler>();
			approvalHandler?.ClearApprovalCallback();

			if (NetworkManager.Singleton.IsListening)
			{
				NetworkManager.Singleton.Shutdown();
			}
		}

		ShowJoinPanelOnly();
		SetStatusMessage("ホストを終了しました。待機画面に戻ります");
	}

	private void SetStatusMessage(string message)
	{
		if (m_StatusText == null) return;
		m_StatusBuilder.Clear();
		m_StatusBuilder.Append(message);
		m_StatusText.text = m_StatusBuilder.ToString();
	}

	private string GetLocalIPv4Address()
	{
		try
		{
			foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
			{
				if (ni.OperationalStatus != OperationalStatus.Up ||
					ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
				{
					continue;
				}

				foreach (var ua in ni.GetIPProperties().UnicastAddresses)
				{
					if (ua.Address.AddressFamily == AddressFamily.InterNetwork &&
						!IPAddress.IsLoopback(ua.Address))
					{
						return ua.Address.ToString();
					}
				}
			}
		}
		catch
		{
			// 例外発生時はローカルホストへフォールバック
		}

		return "127.0.0.1";
	}

	/// <summary>
	/// 指定したボタンをEventSystemの初期選択対象に設定します。
	/// </summary>
	private void SetInitialSelectedButton(Button button)
	{
		if (button == null || !button.gameObject.activeInHierarchy)
		{
			return;
		}

		if (EventSystem.current != null)
		{
			EventSystem.current.SetSelectedGameObject(null);
			EventSystem.current.SetSelectedGameObject(button.gameObject);
		}
	}

	/// <summary>
	/// アプリケーションを終了します。
	/// </summary>
	public void QuitGame()
	{
#if UNITY_EDITOR
		UnityEditor.EditorApplication.isPlaying = false;
#else
		Application.Quit();
#endif
	}
}
