using System.Net;
using System.Net.Sockets;
using System.Text;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// タイトル画面でのネットワーク接続・初期化を管理するクラス。
/// Host(観戦用/デバッグプレイヤー兼任)およびClientとしての接続、IPアドレス設定、
/// およびJoinPanelの開閉(Start/Cancel)を担当します。
/// </summary>
public class NetworkConnectManager : MonoBehaviour
{
    [Header("パネル・メニューUI参照")]
    [SerializeField] private GameObject titleMenuRoot;
    [SerializeField] private GameObject joinPanel;
    [SerializeField] private Button startButton;
    [SerializeField] private Button cancelButton;

    [Header("接続UI参照(ボタン・入力)")]
    [SerializeField] private Button hostSpectatorButton;
    [SerializeField] private Button hostPlayerButton;
    [SerializeField] private Button clientButton;
    [SerializeField] private TMP_InputField ipInputField;
    [SerializeField] private TextMeshProUGUI localIpText;
    [SerializeField] private TextMeshProUGUI statusText;

    [Header("接続設定")]
    [SerializeField] private ushort defaultPort;

    [Header("シーン遷移先")]
    [SerializeField] private string lobbySceneName;

    // デバッグ用兼任ホストかどうかの共有フラグ
    public static bool IsPlayerHost { get; private set; }

    private UnityTransport unityTransport;
    private readonly StringBuilder statusStringBuilder = new StringBuilder();

    private void Awake()
    {
        InitializeTransport();
        SetupUIListeners();
        DisplayLocalIPAddress();
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
            unityTransport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        }
    }

    private void SetupUIListeners()
    {
        // タイトル画面と接続パネルの切り替え
        if (startButton != null)
        {
            startButton.onClick.AddListener(() => SetJoinPanelActive(true));
        }

        if (cancelButton != null)
        {
            cancelButton.onClick.AddListener(() => SetJoinPanelActive(false));
        }

        // 接続ボタン
        if (hostSpectatorButton != null)
        {
            hostSpectatorButton.onClick.AddListener(() => StartAsHost(false));
        }

        if (hostPlayerButton != null)
        {
            hostPlayerButton.onClick.AddListener(() => StartAsHost(true));
        }

        if (clientButton != null)
        {
            clientButton.onClick.AddListener(StartAsClient);
        }
    }

    /// <summary>
    /// JoinPanelの表示/非表示を切り替えます。
    /// </summary>
    /// <param name="isActive">trueの場合はJoinPanelを表示しメインメニューを非表示</param>
    public void SetJoinPanelActive(bool isActive)
    {
        if (joinPanel != null)
        {
            joinPanel.SetActive(isActive);
        }

        if (titleMenuRoot != null)
        {
            titleMenuRoot.SetActive(!isActive);
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

        bool started = NetworkManager.Singleton.StartHost();
        if (started)
        {
            SetStatusMessage("Host起動成功。LobbySceneへ遷移します");
            if (!string.IsNullOrEmpty(lobbySceneName))
            {
                NetworkManager.Singleton.SceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single);
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
        ushort port = defaultPort > 0 ? defaultPort : (ushort)7777;

        if (unityTransport != null)
        {
            unityTransport.SetConnectionData(targetIp, port);
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
        if (ipInputField != null && !string.IsNullOrWhiteSpace(ipInputField.text))
        {
            return ipInputField.text.Trim();
        }

        // 入力が空の場合はローカルホスト(デバッグ用)をデフォルトとする
        return "127.0.0.1";
    }

    private void DisplayLocalIPAddress()
    {
        if (localIpText == null) return;

        string localIp = GetLocalIPv4Address();
        localIpText.text = $"自機IP: {localIp}";
    }

    private string GetLocalIPv4Address()
    {
        try
        {
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
            Debug.LogWarning($"[NetworkConnectManager] ローカルIP取得エラー: {ex.Message}");
        }

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
        if (statusText == null) return;

        statusStringBuilder.Clear();
        statusStringBuilder.Append(message);
        statusText.text = statusStringBuilder.ToString();
    }
}
