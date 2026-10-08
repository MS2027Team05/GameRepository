using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// ロビー画面での参加者管理・チーム分け表示・ゲーム開始制御を担当するクラス。
/// 接続人数に応じた開始判定およびデバッグ用の人数切り替え機能を提供します。
/// </summary>
public class LobbyManager : NetworkBehaviour
{
	[Header("UI参照(ボタン・情報表示)")]
	[FormerlySerializedAs("startGameButton")]
	[SerializeField] private Button m_StartGameButton;
	[FormerlySerializedAs("leaveLobbyButton")]
	[SerializeField] private Button m_LeaveLobbyButton;
	[FormerlySerializedAs("playerCountText")]
	[SerializeField] private TextMeshProUGUI m_PlayerCountText;
	[FormerlySerializedAs("memberListText")]
	[SerializeField] private TextMeshProUGUI m_MemberListText;
	[FormerlySerializedAs("lobbyStatusText")]
	[SerializeField] private TextMeshProUGUI m_LobbyStatusText;
	[FormerlySerializedAs("hostIpText")]
	[SerializeField] private TextMeshProUGUI m_HostIpText;

	[Header("デバッグ用UI(画面操作用)")]
	[FormerlySerializedAs("debugPanel")]
	[SerializeField] private GameObject m_DebugPanel;
	[FormerlySerializedAs("debugCycleMinPlayersButton")]
	[SerializeField] private Button m_DebugCycleMinPlayersButton;
	[FormerlySerializedAs("debugMinPlayersText")]
	[SerializeField] private TextMeshProUGUI m_DebugMinPlayersText;

	[Header("シーン設定")]
	[FormerlySerializedAs("gameSceneName")]
	[SerializeField] private string m_GameSceneName;
	[FormerlySerializedAs("titleSceneName")]
	[SerializeField] private string m_TitleSceneName;

	[Header("人数設定")]
	[FormerlySerializedAs("defaultRequiredPlayers")]
	[SerializeField] private int m_DefaultRequiredPlayers;

	// 現在の必要開始人数(デバッグトグルで変更可能)
	private int m_CurrentRequiredPlayers;
	private readonly StringBuilder m_InfoStringBuilder = new StringBuilder();

	// 参加者情報のネットワーク同期リスト
	private readonly NetworkList<LobbyPlayerData> m_LobbyPlayers = new NetworkList<LobbyPlayerData>();

	private void Awake()
	{
		SetupUIListeners();
	}

	private void Start()
	{
		m_CurrentRequiredPlayers = m_DefaultRequiredPlayers > 0 ? m_DefaultRequiredPlayers : 4;
		SetupDebugUI();
		UpdateUI();
	}

	public override void OnNetworkSpawn()
	{
		m_LobbyPlayers.OnListChanged += HandleLobbyPlayersChanged;

		if (IsServer)
		{
			NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
			NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;

			// ホスト端末自身の情報を追加
			AddPlayer(NetworkManager.Singleton.LocalClientId);
		}

		// クライアント側でサーバーが切断された場合の検知
		NetworkManager.Singleton.OnClientDisconnectCallback += HandleServerDisconnect;

		SetupDebugUI();
		UpdateUI();
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

	private void SetupUIListeners()
	{
		if (m_StartGameButton != null)
		{
			m_StartGameButton.onClick.AddListener(OnStartGameButtonClicked);
		}

		if (m_LeaveLobbyButton != null)
		{
			m_LeaveLobbyButton.onClick.AddListener(OnLeaveLobbyButtonClicked);
		}

		if (m_DebugCycleMinPlayersButton != null)
		{
			m_DebugCycleMinPlayersButton.onClick.AddListener(CycleMinPlayersDebug);
		}
	}

	private void SetupDebugUI()
	{
		// プレイヤー兼任ホスト(デバッグ用)かつエディタ/デバッグビルド時のみデバッグUIを表示
		bool isDebugHost = NetworkConnectManager.IsPlayerHost;
		bool isDebugBuild = Application.isEditor || Debug.isDebugBuild;
		bool shouldShowDebugUI = IsServer && isDebugHost && isDebugBuild;

		if (m_DebugPanel != null)
		{
			m_DebugPanel.SetActive(shouldShowDebugUI);
		}

		UpdateDebugMinPlayersText();
	}

	private void HandleLobbyPlayersChanged(NetworkListEvent<LobbyPlayerData> changeEvent)
	{
		UpdateUI();
	}

	private void HandleClientConnected(ulong clientId)
	{
		AddPlayer(clientId);
		UpdateUI();
	}

	private void HandleClientDisconnected(ulong clientId)
	{
		RemovePlayer(clientId);
		UpdateUI();
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

		m_LobbyPlayers.Add(new LobbyPlayerData(clientId));
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

	private void UpdateUI()
	{
		if (NetworkManager.Singleton == null || !IsSpawned) return;

		int connectedCount = m_LobbyPlayers.Count;

		// 参加人数テキストの更新
		if (m_PlayerCountText != null)
		{
			m_InfoStringBuilder.Clear();
			m_InfoStringBuilder.Append("参加人数: ").Append(connectedCount).Append(" / ").Append(m_CurrentRequiredPlayers);
			m_PlayerCountText.text = m_InfoStringBuilder.ToString();
		}

		// メンバーリストの更新
		if (m_MemberListText != null)
		{
			m_InfoStringBuilder.Clear();
			m_InfoStringBuilder.AppendLine("【接続端末一覧】");
			for (int i = 0; i < m_LobbyPlayers.Count; i++)
			{
				var player = m_LobbyPlayers[i];
				string hostTag = player.ClientId == NetworkManager.ServerClientId ? " (Host)" : "";
				string localTag = player.ClientId == NetworkManager.Singleton.LocalClientId ? " (You)" : "";
				m_InfoStringBuilder.Append("- ClientId: ").Append(player.ClientId).Append(hostTag).Append(localTag).AppendLine();
			}
			m_MemberListText.text = m_InfoStringBuilder.ToString();
		}

		// 開始ボタンの有効化判定(Hostのみ、かつ必要人数を満たしているか)
		if (m_StartGameButton != null)
		{
			bool canStart = IsServer && (connectedCount >= m_CurrentRequiredPlayers);
			m_StartGameButton.interactable = canStart;
			m_StartGameButton.gameObject.SetActive(IsServer);
		}

		if (m_LobbyStatusText != null)
		{
			if (!IsServer)
			{
				m_LobbyStatusText.text = "ホストのゲーム開始を待機しています...";
			}
			else
			{
				m_LobbyStatusText.text = connectedCount >= m_CurrentRequiredPlayers
					? "開始準備完了: ゲーム開始ボタンを押してください"
					: $"待機中: あと {m_CurrentRequiredPlayers - connectedCount} 台の接続が必要です";
			}
		}

		UpdateHostIpDisplay();
	}

	private void UpdateHostIpDisplay()
	{
		if (m_HostIpText == null) return;

		if (IsServer)
		{
			string ip = GetLocalIPv4Address();
			m_HostIpText.text = $"接続先IP: {ip}";
			m_HostIpText.gameObject.SetActive(true);
		}
		else
		{
			m_HostIpText.gameObject.SetActive(false);
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
		catch (System.Exception ex)
		{
			Debug.LogWarning($"[LobbyManager] ローカルIP取得エラー: {ex.Message}");
		}

		return "127.0.0.1";
	}

	/// <summary>
	/// デバッグ用: 必要人数を 4人 -> 2人 -> 1人 -> 4人 とトグル切り替えします。
	/// </summary>
	private void CycleMinPlayersDebug()
	{
		if (!IsServer) return;

		if (m_CurrentRequiredPlayers == 4) m_CurrentRequiredPlayers = 2;
		else if (m_CurrentRequiredPlayers == 2) m_CurrentRequiredPlayers = 1;
		else m_CurrentRequiredPlayers = 4;

		UpdateDebugMinPlayersText();
		UpdateUI();
	}

	private void UpdateDebugMinPlayersText()
	{
		if (m_DebugMinPlayersText != null)
		{
			m_DebugMinPlayersText.text = $"[Debug] 開始人数: {m_CurrentRequiredPlayers}人";
		}
	}

	private void OnStartGameButtonClicked()
	{
		if (!IsServer) return;

		if (!string.IsNullOrEmpty(m_GameSceneName))
		{
			NetworkManager.Singleton.SceneManager.LoadScene(m_GameSceneName, LoadSceneMode.Single);
		}
	}

	private void OnLeaveLobbyButtonClicked()
	{
		if (NetworkManager.Singleton != null)
		{
			NetworkManager.Singleton.Shutdown();
		}

		ReturnToTitleScene();
	}

	private void ReturnToTitleScene()
	{
		if (!string.IsNullOrEmpty(m_TitleSceneName))
		{
			SceneManager.LoadScene(m_TitleSceneName);
		}
	}
}

