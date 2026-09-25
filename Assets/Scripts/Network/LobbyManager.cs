using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ロビー画面での参加者管理・チーム分け表示・ゲーム開始制御を担当するクラス。
/// 接続人数に応じた開始判定およびデバッグ用の人数切り替え機能を提供します。
/// </summary>
public class LobbyManager : NetworkBehaviour
{
    [Header("UI参照(ボタン・情報表示)")]
    [SerializeField] private Button startGameButton;
    [SerializeField] private Button leaveLobbyButton;
    [SerializeField] private TextMeshProUGUI playerCountText;
    [SerializeField] private TextMeshProUGUI memberListText;
    [SerializeField] private TextMeshProUGUI lobbyStatusText;

    [Header("デバッグ用UI(画面操作用)")]
    [SerializeField] private GameObject debugPanel;
    [SerializeField] private Button debugCycleMinPlayersButton;
    [SerializeField] private TextMeshProUGUI debugMinPlayersText;

    [Header("シーン設定")]
    [SerializeField] private string gameSceneName;
    [SerializeField] private string titleSceneName;

    [Header("人数設定")]
    [SerializeField] private int defaultRequiredPlayers;

    // 現在の必要開始人数(デバッグトグルで変更可能)
    private int currentRequiredPlayers;
    private readonly StringBuilder infoStringBuilder = new StringBuilder();

    private void Awake()
    {
        SetupUIListeners();
    }

    private void Start()
    {
        currentRequiredPlayers = defaultRequiredPlayers > 0 ? defaultRequiredPlayers : 4;
        SetupDebugUI();
        UpdateUI();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
        }

        // クライアント側でサーバーが切断された場合の検知
        NetworkManager.Singleton.OnClientDisconnectCallback += HandleServerDisconnect;

        UpdateUI();
    }

    public override void OnNetworkDespawn()
    {
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
        if (startGameButton != null)
        {
            startGameButton.onClick.AddListener(OnStartGameButtonClicked);
        }

        if (leaveLobbyButton != null)
        {
            leaveLobbyButton.onClick.AddListener(OnLeaveLobbyButtonClicked);
        }

        if (debugCycleMinPlayersButton != null)
        {
            debugCycleMinPlayersButton.onClick.AddListener(CycleMinPlayersDebug);
        }
    }

    private void SetupDebugUI()
    {
        // エディタ実行時またはデバッグビルド時のみデバッグUIを表示
        bool isDebug = Application.isEditor || Debug.isDebugBuild;
        if (debugPanel != null)
        {
            debugPanel.SetActive(isDebug && IsServer);
        }

        UpdateDebugMinPlayersText();
    }

    private void HandleClientConnected(ulong clientId)
    {
        UpdateUI();
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        UpdateUI();
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
        if (NetworkManager.Singleton == null) return;

        int connectedCount = NetworkManager.Singleton.ConnectedClients.Count;

        // 参加人数テキストの更新
        if (playerCountText != null)
        {
            infoStringBuilder.Clear();
            infoStringBuilder.Append("参加人数: ").Append(connectedCount).Append(" / ").Append(currentRequiredPlayers);
            playerCountText.text = infoStringBuilder.ToString();
        }

        // メンバーリストの更新
        if (memberListText != null)
        {
            infoStringBuilder.Clear();
            infoStringBuilder.AppendLine("【接続端末一覧】");
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                string hostTag = client.ClientId == NetworkManager.ServerClientId ? " (Host)" : "";
                infoStringBuilder.Append("- ClientId: ").Append(client.ClientId).Append(hostTag).AppendLine();
            }
            memberListText.text = infoStringBuilder.ToString();
        }

        // 開始ボタンの有効化判定(Hostのみ、かつ必要人数を満たしているか)
        if (startGameButton != null)
        {
            bool canStart = IsServer && (connectedCount >= currentRequiredPlayers);
            startGameButton.interactable = canStart;
            startGameButton.gameObject.SetActive(IsServer);
        }

        if (lobbyStatusText != null)
        {
            if (!IsServer)
            {
                lobbyStatusText.text = "ホストのゲーム開始を待機しています...";
            }
            else
            {
                lobbyStatusText.text = connectedCount >= currentRequiredPlayers
                    ? "開始準備完了: ゲーム開始ボタンを押してください"
                    : $"待機中: あと {currentRequiredPlayers - connectedCount} 台の接続が必要です";
            }
        }
    }

    /// <summary>
    /// デバッグ用: 必要人数を 4人 -> 2人 -> 1人 -> 4人 とトグル切り替えします。
    /// </summary>
    private void CycleMinPlayersDebug()
    {
        if (!IsServer) return;

        if (currentRequiredPlayers == 4) currentRequiredPlayers = 2;
        else if (currentRequiredPlayers == 2) currentRequiredPlayers = 1;
        else currentRequiredPlayers = 4;

        UpdateDebugMinPlayersText();
        UpdateUI();
    }

    private void UpdateDebugMinPlayersText()
    {
        if (debugMinPlayersText != null)
        {
            debugMinPlayersText.text = $"[Debug] 開始人数: {currentRequiredPlayers}人";
        }
    }

    private void OnStartGameButtonClicked()
    {
        if (!IsServer) return;

        if (!string.IsNullOrEmpty(gameSceneName))
        {
            NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
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
        if (!string.IsNullOrEmpty(titleSceneName))
        {
            SceneManager.LoadScene(titleSceneName);
        }
    }
}
