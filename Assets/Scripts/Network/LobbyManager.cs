using System;
using System.Collections;
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
	[SerializeField] private string m_TutorialSceneName;

	[Header("人数設定")]
	[FormerlySerializedAs("defaultRequiredPlayers")]
	[SerializeField] private int m_DefaultRequiredPlayers;

	[Header("切断通知設定")]
	// 想定設定値: 3.0f (ホスト切断通知を表示してから自動でタイトル画面へ遷移するまでの秒数)
	[SerializeField] private float m_DisconnectReturnDelay;

	// 各チームの最大人数(2vs2)
	private const int m_MaxTeamMembers = 2;
	private const int m_MaxPlayerNameLength = 12;
	private const int m_MaxPlayerNameBytes = 60;

	public static int MaxTeamMembers { get => m_MaxTeamMembers; }
	public static int MaxPlayerNameLength { get => m_MaxPlayerNameLength; }
	public static int MaxPlayerNameBytes { get => m_MaxPlayerNameBytes; }

	// ロビーデータや開始条件が変化した際に発火するイベント
	public event Action OnLobbyDataChanged;

	// ゲームシーンの非同期ロードが開始された際に発火するイベント
	public event Action OnGameSceneLoadStarted;

	// ホストとの接続が切断された際に発火するイベント
	public event Action OnDisconnectedFromHost;

	private int m_CurrentRequiredPlayers;
	// 現在選択されている遷移先がレベルデザインシーンかどうか
	private bool m_UseLevelDesignScene;
	private bool m_IsGameStarting;
	private bool m_SkipTutorial;

	public bool SkipTutorial
	{
		get => m_SkipTutorial;
		set => m_SkipTutorial = value;
	}

	public string TutorialSceneName { get => m_TutorialSceneName; }
	private bool m_IsHandlingDisconnect;
	private string m_CachedHostIp = "127.0.0.1";
	private Coroutine m_DisconnectCoroutine;

	// 参加者情報のネットワーク同期リスト
	private readonly NetworkList<LobbyPlayerData> m_LobbyPlayers = new NetworkList<LobbyPlayerData>();

	public NetworkList<LobbyPlayerData> LobbyPlayers { get => m_LobbyPlayers; }
	public int ConnectedPlayerCount { get => m_LobbyPlayers.Count; }
	public int RequiredPlayerCount { get => m_CurrentRequiredPlayers; }
	public bool UseLevelDesignScene { get => m_UseLevelDesignScene; }
	public string HostIpAddress { get => m_CachedHostIp; }

	public string TargetSceneName
	{
		get => m_UseLevelDesignScene
			? (!string.IsNullOrEmpty(m_LevelDesignSceneName) ? m_LevelDesignSceneName : "未設定(LevelDesign)")
			: (!string.IsNullOrEmpty(m_GameSceneName) ? m_GameSceneName : "未設定(GameScene)");
	}

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

			// タイトルシーン等で既に接続済みの全クライアントをロビーリストへ初期登録
			IReadOnlyList<ulong> connectedIds = NetworkManager.Singleton.ConnectedClientsIds;
			for (int i = 0; i < connectedIds.Count; i++)
			{
				ulong clientId = connectedIds[i];

				// 観戦専用ホスト(非プレイヤーホスト)の場合はホスト端末自身を除外
				if (clientId == NetworkManager.ServerClientId && !NetworkConnectManager.IsPlayerHost)
				{
					continue;
				}

				AddPlayer(clientId);
			}
		}

		// クライアント側でサーバーが切断された場合の検知
		NetworkManager.Singleton.OnClientDisconnectCallback += HandleServerDisconnect;

		if (NetworkManager.Singleton.SceneManager != null)
		{
			NetworkManager.Singleton.SceneManager.OnSceneEvent += HandleSceneEvent;
		}

		NotifyDataChanged();

		// ローカル端末に保存されている設定キャッシュをサーバーへ自動送信・適用
		ApplyLocalCachedProfile();
	}

	public override void OnNetworkDespawn()
	{
		m_LobbyPlayers.OnListChanged -= HandleLobbyPlayersChanged;

		if (NetworkManager.Singleton != null)
		{
			if (NetworkManager.Singleton.SceneManager != null)
			{
				NetworkManager.Singleton.SceneManager.OnSceneEvent -= HandleSceneEvent;
			}

			if (IsServer)
			{
				NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
				NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
			}
		}
	}

	public override void OnDestroy()
	{
		if (NetworkManager.Singleton != null)
		{
			NetworkManager.Singleton.OnClientDisconnectCallback -= HandleServerDisconnect;
		}

		if (m_DisconnectCoroutine != null)
		{
			StopCoroutine(m_DisconnectCoroutine);
			m_DisconnectCoroutine = null;
		}

		base.OnDestroy();
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
		if (clientId == NetworkManager.ServerClientId && !NetworkConnectManager.IsPlayerHost)
		{
			return;
		}

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
		if (!IsServer)
		{
			return;
		}

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
			FixedString64Bytes defaultName = $"Player {clientId}";
			initialData = new LobbyPlayerData(clientId, defaultName);
		}

		m_LobbyPlayers.Add(initialData);
	}

	private void RemovePlayer(ulong clientId)
	{
		if (!IsServer)
		{
			return;
		}

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
		if (m_IsHandlingDisconnect || m_IsGameStarting)
		{
			return;
		}

		if (!IsServer &&
			NetworkManager.Singleton != null &&
			(clientId == NetworkManager.Singleton.LocalClientId || clientId == NetworkManager.ServerClientId))
		{
			m_IsHandlingDisconnect = true;

			if (OnDisconnectedFromHost != null)
			{
				OnDisconnectedFromHost.Invoke();
				m_DisconnectCoroutine = StartCoroutine(DisconnectReturnRoutine());
			}
			else
			{
				ReturnToTitleScene();
			}
		}
	}

	private IEnumerator DisconnectReturnRoutine()
	{
		float delay = m_DisconnectReturnDelay > 0f ? m_DisconnectReturnDelay : 3.0f;
		yield return new WaitForSecondsRealtime(delay);

		if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
		{
			NetworkManager.Singleton.Shutdown();
		}

		ReturnToTitleScene();
	}

	private void NotifyDataChanged()
	{
		OnLobbyDataChanged?.Invoke();
	}

	/// <summary>
	/// ローカル端末の保存設定(PlayerPrefs)を読み込み、サーバーへ一括送信して反映させます。
	/// </summary>
	public void ApplyLocalCachedProfile()
	{
		if (NetworkManager.Singleton == null)
		{
			return;
		}

		// 観戦専用ホスト端末(非プレイヤーホスト)の場合はプレイヤープロファイルを適用しない
		if (IsServer && !NetworkConnectManager.IsPlayerHost)
		{
			return;
		}

		ulong localClientId = NetworkManager.Singleton.LocalClientId;
		ECharacterType charType = LocalPlayerProfilePreferences.GetCharacterType();
		EHighlightColor highlightColor = LocalPlayerProfilePreferences.GetHighlightColor();
		ETeamType assignedTeam = LocalPlayerProfilePreferences.GetAssignedTeam();

		RequestUpdateCachedAppearanceServerRpc(localClientId, charType, highlightColor, assignedTeam);
	}

	private void HandleSceneEvent(SceneEvent sceneEvent)
	{
		if (sceneEvent == null)
		{
			return;
		}

		// シーンロード開始イベントを検知(ホスト・クライアント双方で受信)
		if (sceneEvent.SceneEventType == SceneEventType.Load)
		{
			string sceneName = sceneEvent.SceneName;
			if (sceneName == m_GameSceneName || sceneName == m_LevelDesignSceneName)
			{
				NotifyGameSceneLoadStarted();
			}
		}
	}

	private void NotifyGameSceneLoadStarted()
	{
		m_IsGameStarting = true;
		OnGameSceneLoadStarted?.Invoke();
	}

	[Rpc(SendTo.Everyone)]
	private void NotifyGameSceneLoadStartedRpc()
	{
		NotifyGameSceneLoadStarted();
	}

	/// <summary>
	/// プレイヤーネームが有効な形式(1文字以上、最大文字数以内、UTF-8バイト数上限以内、空白のみでない)かを判定します。
	/// </summary>
	public static bool IsValidPlayerName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}

		string trimmed = name.Trim();
		if (trimmed.Length < 1 || trimmed.Length > m_MaxPlayerNameLength)
		{
			return false;
		}

		return System.Text.Encoding.UTF8.GetByteCount(trimmed) <= m_MaxPlayerNameBytes;
	}

	// --- 外部公開API (UIControllerから呼び出し) ---

	public void SetPlayerName(string newName)
	{
		if (NetworkManager.Singleton == null)
		{
			return;
		}

		string trimmed = newName != null ? newName.Trim() : string.Empty;
		if (!IsValidPlayerName(trimmed))
		{
			// 不正な入力の場合はUI側を元の正常値に戻すため再通知
			NotifyDataChanged();
			return;
		}

		RequestUpdatePlayerNameServerRpc(NetworkManager.Singleton.LocalClientId, trimmed);
	}

	public void JoinTeam(ETeamType team)
	{
		if (NetworkManager.Singleton == null)
		{
			return;
		}

		LocalPlayerProfilePreferences.SetAssignedTeam(team);
		RequestJoinTeamServerRpc(NetworkManager.Singleton.LocalClientId, team);
	}

	public void LeaveTeam()
	{
		if (NetworkManager.Singleton == null)
		{
			return;
		}

		RequestLeaveTeamServerRpc(NetworkManager.Singleton.LocalClientId);
	}

	/// <summary>
	/// 指定したキャラクターモデル種別(男/女)をローカルプレイヤーに設定します。
	/// </summary>
	public void SetCharacterType(ECharacterType characterType)
	{
		if (!TryGetLocalPlayerData(out var localPlayer) || localPlayer.IsReady || NetworkManager.Singleton == null)
		{
			return;
		}

		LocalPlayerProfilePreferences.SetCharacterType(characterType);
		RequestUpdateCharacterTypeServerRpc(NetworkManager.Singleton.LocalClientId, characterType);
	}

	/// <summary>
	/// 指定したカラーをローカルプレイヤーに設定します。
	/// </summary>
	public void SetHighlightColor(EHighlightColor highlightColor)
	{
		if (!TryGetLocalPlayerData(out var localPlayer) || localPlayer.IsReady || NetworkManager.Singleton == null)
		{
			return;
		}

		LocalPlayerProfilePreferences.SetHighlightColor(highlightColor);
		RequestUpdateHighlightColorServerRpc(NetworkManager.Singleton.LocalClientId, highlightColor);
	}

	public void ToggleReady()
	{
		if (!TryGetLocalPlayerData(out var localPlayer) || NetworkManager.Singleton == null)
		{
			return;
		}

		if (localPlayer.Team == ETeamType.None)
		{
			return;
		}

		// プレイヤーネームが不正な場合はReady状態への移行をブロック
		if (!IsValidPlayerName(localPlayer.PlayerName.ToString()))
		{
			return;
		}

		RequestToggleReadyServerRpc(NetworkManager.Singleton.LocalClientId);
	}

	public void CycleMinPlayersDebug()
	{
		if (!IsServer)
		{
			return;
		}

		if (m_CurrentRequiredPlayers == 4)
		{
			m_CurrentRequiredPlayers = 2;
		}
		else if (m_CurrentRequiredPlayers == 2)
		{
			m_CurrentRequiredPlayers = 1;
		}
		else
		{
			m_CurrentRequiredPlayers = 4;
		}

		NotifyDataChanged();
	}

	public void ToggleTargetSceneDebug()
	{
		if (!IsServer)
		{
			return;
		}

		m_UseLevelDesignScene = !m_UseLevelDesignScene;
		NotifyDataChanged();
	}

	public void ForceStartGameDebug()
	{
		if (!IsServer)
		{
			return;
		}

		AutoAssignUnseatedPlayers();
		StartGameTransition();
	}

	public void StartGame()
	{
		if (!IsServer)
		{
			return;
		}

		StartGameTransition();
	}

	public void LeaveLobby()
	{
		m_IsHandlingDisconnect = true;

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
		bool allNamesValid = true;

		for (int i = 0; i < m_LobbyPlayers.Count; i++)
		{
			var p = m_LobbyPlayers[i];
			if (p.Team == ETeamType.None)
			{
				allSeated = false;
			}
			if (!p.IsReady)
			{
				allReady = false;
			}
			if (!IsValidPlayerName(p.PlayerName.ToString()))
			{
				allNamesValid = false;
			}
		}

		bool canStart = (connectedCount >= m_CurrentRequiredPlayers) && allSeated && allReady && allNamesValid;

		if (canStart)
		{
			statusMessage = "全員の準備が完了しました: ゲーム開始ボタンを押してください";
		}
		else if (connectedCount < m_CurrentRequiredPlayers)
		{
			statusMessage = $"待機中: あと {m_CurrentRequiredPlayers - connectedCount} 台の接続が必要です";
		}
		else if (!allNamesValid)
		{
			statusMessage = "プレイヤー名が未入力または不正なプレイヤーがいます";
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

	private bool IsTeamFull(ETeamType team)
	{
		if (team == ETeamType.None)
		{
			return false;
		}

		int count = 0;
		for (int i = 0; i < m_LobbyPlayers.Count; i++)
		{
			if (m_LobbyPlayers[i].Team == team)
			{
				count++;
			}
		}

		return count >= m_MaxTeamMembers;
	}

	// --- RPC通信処理 ---

	[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
	private void RequestJoinTeamServerRpc(ulong clientId, ETeamType targetTeam)
	{
		if (targetTeam == ETeamType.None)
		{
			return;
		}

		int count = 0;
		for (int i = 0; i < m_LobbyPlayers.Count; i++)
		{
			if (m_LobbyPlayers[i].Team == targetTeam)
			{
				count++;
			}
		}

		if (count >= m_MaxTeamMembers)
		{
			Debug.LogWarning($"[LobbyManager] {targetTeam} は満員です (最大 {m_MaxTeamMembers} 名)。");
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
	private void RequestUpdateCachedAppearanceServerRpc(
		ulong clientId,
		ECharacterType charType,
		EHighlightColor highlightColor,
		ETeamType assignedTeam)
	{
		for (int i = 0; i < m_LobbyPlayers.Count; i++)
		{
			if (m_LobbyPlayers[i].ClientId == clientId)
			{
				var data = m_LobbyPlayers[i];
				if (data.IsReady)
				{
					return;
				}

				data.CharacterType = charType;
				data.HighlightColor = highlightColor;

				if (data.Team == ETeamType.None && assignedTeam != ETeamType.None && !IsTeamFull(assignedTeam))
				{
					data.Team = assignedTeam;
				}

				m_LobbyPlayers[i] = data;
				SessionPlayerDataStore.Instance?.SavePlayerData(data);
				break;
			}
		}
	}

	[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
	private void RequestUpdatePlayerNameServerRpc(ulong clientId, FixedString64Bytes newName)
	{
		string nameStr = newName.ToString().Trim();
		if (!IsValidPlayerName(nameStr))
		{
			return;
		}

		for (int i = 0; i < m_LobbyPlayers.Count; i++)
		{
			if (m_LobbyPlayers[i].ClientId == clientId)
			{
				var data = m_LobbyPlayers[i];
				if (data.IsReady)
				{
					return;
				}

				data.PlayerName = nameStr;
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
				if (data.IsReady)
				{
					return;
				}

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
				if (data.IsReady)
				{
					return;
				}

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
				if (data.Team == ETeamType.None)
				{
					return;
				}

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
			if (m_LobbyPlayers[i].ClientId == clientA)
			{
				indexA = i;
			}
			if (m_LobbyPlayers[i].ClientId == clientB)
			{
				indexB = i;
			}
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
			if (m_LobbyPlayers[i].Team == ETeamType.Red)
			{
				redCount++;
			}
			if (m_LobbyPlayers[i].Team == ETeamType.Blue)
			{
				blueCount++;
			}
		}

		for (int i = 0; i < m_LobbyPlayers.Count; i++)
		{
			if (m_LobbyPlayers[i].Team == ETeamType.None)
			{
				var data = m_LobbyPlayers[i];
				if (redCount <= blueCount && redCount < m_MaxTeamMembers)
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
		if (m_IsGameStarting)
		{
			return;
		}
		m_IsGameStarting = true;

		if (SessionPlayerDataStore.Instance != null)
		{
			for (int i = 0; i < m_LobbyPlayers.Count; i++)
			{
				SessionPlayerDataStore.Instance.SavePlayerData(m_LobbyPlayers[i]);
			}
		}

		string targetScene;
		if (!m_SkipTutorial && !string.IsNullOrEmpty(m_TutorialSceneName))
		{
			targetScene = m_TutorialSceneName;
		}
		else
		{
			targetScene = m_UseLevelDesignScene ? m_LevelDesignSceneName : m_GameSceneName;
		}

		if (!string.IsNullOrEmpty(targetScene))
		{
			// 全端末(ホストおよび全クライアント)へ即座にロード中UIの表示を通知
			NotifyGameSceneLoadStartedRpc();
			NetworkManager.Singleton.SceneManager.LoadScene(targetScene, LoadSceneMode.Single);
		}
		else
		{
			m_IsGameStarting = false;
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

	public static string GetLocalIPv4Address()
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

				// 仮想アダプタ名(WSL, VirtualBox, vEthernet, Hyper-V, VMwareなど)を除外
				string name = ni.Name.ToLower();
				string desc = ni.Description.ToLower();
				if (name.Contains("vethernet") || name.Contains("wsl") || name.Contains("virtual") ||
					desc.Contains("virtual") || name.Contains("hyper-v") || desc.Contains("hyper-v") || desc.Contains("vmware"))
				{
					continue;
				}

				IPInterfaceProperties props = ni.GetIPProperties();
				foreach (UnicastIPAddressInformation addr in props.UnicastAddresses)
				{
					if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
					{
						// 有線LAN(Ethernet)を優先で返却
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
