using System.Text;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// タイトル画面でのネットワーク接続・初期化を管理するクラス。
/// Host(観戦用/デバッグプレイヤー兼任)およびClientとしての接続、IPアドレス設定、
/// およびJoinPanelの開閉(Start/Cancel)を担当します。
/// </summary>
public class NetworkConnectManager : MonoBehaviour
{
	[Header("パネル・メニューUI参照")]
	[FormerlySerializedAs("titleMenuRoot")]
	[SerializeField] private GameObject m_TitleMenuRoot;
	[FormerlySerializedAs("joinPanel")]
	[SerializeField] private GameObject m_JoinPanel;
	[FormerlySerializedAs("startButton")]
	[SerializeField] private Button m_StartButton;
	[FormerlySerializedAs("cancelButton")]
	[SerializeField] private Button m_CancelButton;

	[Header("接続UI参照(ボタン・入力)")]
	[FormerlySerializedAs("hostSpectatorButton")]
	[SerializeField] private Button m_HostSpectatorButton;
	[FormerlySerializedAs("hostPlayerButton")]
	[SerializeField] private Button m_HostPlayerButton;
	[FormerlySerializedAs("clientButton")]
	[SerializeField] private Button m_ClientButton;
	[FormerlySerializedAs("ipInputField")]
	[SerializeField] private TMP_InputField m_IpInputField;
	[FormerlySerializedAs("statusText")]
	[SerializeField] private TextMeshProUGUI m_StatusText;

	[Header("接続設定")]
	[FormerlySerializedAs("defaultPort")]
	[SerializeField] private ushort m_DefaultPort;

	[Header("シーン遷移先")]
	[FormerlySerializedAs("lobbySceneName")]
	[SerializeField] private string m_LobbySceneName;

	// デバッグ用兼任ホストかどうかの共有フラグ
	public static bool IsPlayerHost { get; private set; }

	private UnityTransport m_UnityTransport;
	private readonly StringBuilder m_StatusStringBuilder = new StringBuilder();

	private void Awake()
	{
		InitializeTransport();
		SetupUIListeners();
	}

	private void Start()
	{
		if (NetworkManager.Singleton != null)
		{
			NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnect;
		}

		// 初期表示状態の設定(タイトルメニューを表示、JoinPanelは非表示)
		SetJoinPanelActive(false);
		SetStatusMessage("待機中: Host起動またはClient接続を選択してください");
	}

	private void OnDestroy()
	{
		if (NetworkManager.Singleton != null)
		{
			NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnect;
		}
	}

	private void InitializeTransport()
	{
		if (NetworkManager.Singleton != null)
		{
			m_UnityTransport = NetworkManager.Singleton.GetComponent<UnityTransport>();
		}
	}

	private void SetupUIListeners()
	{
		// タイトル画面と接続パネルの切り替え
		if (m_StartButton != null)
		{
			m_StartButton.onClick.AddListener(() => SetJoinPanelActive(true));
		}

		if (m_CancelButton != null)
		{
			m_CancelButton.onClick.AddListener(() => SetJoinPanelActive(false));
		}

		// 接続ボタン
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
	}

	/// <summary>
	/// JoinPanelの表示/非表示を切り替えます。
	/// </summary>
	/// <param name="isActive">trueの場合はJoinPanelを表示しメインメニューを非表示</param>
	public void SetJoinPanelActive(bool isActive)
	{
		if (m_JoinPanel != null)
		{
			m_JoinPanel.SetActive(isActive);
		}

		if (m_TitleMenuRoot != null)
		{
			m_TitleMenuRoot.SetActive(!isActive);
		}

		if (!isActive)
		{
			SetStatusMessage("待機中: Host起動またはClient接続を選択してください");
		}
	}

	/// <summary>
	/// ホストとしてセッションを開始します。
	/// </summary>
	/// <param name="asPlayer">trueの場合はプレイヤー兼任(デバッグ用)、falseの場合は観戦専用(本番用)</param>
	public void StartAsHost(bool asPlayer)
	{
		if (NetworkManager.Singleton == null)
		{
			SetStatusMessage("エラー: NetworkManagerが見つかりません");
			return;
		}

		IsPlayerHost = asPlayer;
		SetStatusMessage(asPlayer ? "Host(Player兼任)起動中..." : "Host(観戦専用)起動中...");

		ushort port = m_DefaultPort > 0 ? m_DefaultPort : (ushort)7777;
		if (m_UnityTransport != null)
		{
			// "0.0.0.0" をリッスンアドレスに指定し、LAN(192.168.x.x)や別PCからの接続を待ち受け可能にする
			m_UnityTransport.SetConnectionData("127.0.0.1", port, "0.0.0.0");
		}

		bool started = NetworkManager.Singleton.StartHost();
		if (started)
		{
			SetStatusMessage("Host起動成功。LobbySceneへ遷移します");
			if (!string.IsNullOrEmpty(m_LobbySceneName))
			{
				NetworkManager.Singleton.SceneManager.LoadScene(m_LobbySceneName, LoadSceneMode.Single);
			}
		}
		else
		{
			SetStatusMessage("エラー: Host起動に失敗しました");
		}
	}

	/// <summary>
	/// クライアントとしてホストに接続します。
	/// </summary>
	public void StartAsClient()
	{
		if (NetworkManager.Singleton == null)
		{
			SetStatusMessage("エラー: NetworkManagerが見つかりません");
			return;
		}

		IsPlayerHost = false;

		string targetIp = GetTargetIpAddress();
		ushort port = m_DefaultPort > 0 ? m_DefaultPort : (ushort)7777;

		if (m_UnityTransport != null)
		{
			m_UnityTransport.SetConnectionData(targetIp, port);
		}

		SetStatusMessage($"接続試行中: {targetIp}:{port} ...");
		bool started = NetworkManager.Singleton.StartClient();
		if (!started)
		{
			SetStatusMessage("エラー: Client接続開始に失敗しました");
		}
	}

	private string GetTargetIpAddress()
	{
		if (m_IpInputField != null && !string.IsNullOrWhiteSpace(m_IpInputField.text))
		{
			return m_IpInputField.text.Trim();
		}

		// 入力が空の場合はローカルホスト(デバッグ用)をデフォルトとする
		return "127.0.0.1";
	}


	private void HandleClientDisconnect(ulong clientId)
	{
		// 自機が切断された場合のハンドリング
		if (NetworkManager.Singleton != null && clientId == NetworkManager.Singleton.LocalClientId)
		{
			SetStatusMessage("サーバーから切断されました。");
		}
	}

	private void SetStatusMessage(string message)
	{
		if (m_StatusText == null) return;

		m_StatusStringBuilder.Clear();
		m_StatusStringBuilder.Append(message);
		m_StatusText.text = m_StatusStringBuilder.ToString();
	}
}
