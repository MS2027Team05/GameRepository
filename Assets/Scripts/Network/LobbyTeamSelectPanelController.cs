using System;
using System.Collections;
using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// LobbyScene専用のチーム・観戦選択パネル制御クラス。
/// 再接続や複数端末切断時にチーム未所属(None)状態となったプレイヤーへ自動で選択画面を表示し、
/// またチーム確定後の手動席替え操作にも対応します。
/// </summary>
public class LobbyTeamSelectPanelController : MonoBehaviour
{
	private const float m_DefaultAutoOpenGraceSeconds = 0.4f;

	[Header("マネージャー参照")]
	[SerializeField] private LobbyManager m_LobbyManager;

	[Header("パネルルート・表示テキスト")]
	[Tooltip("表示/非表示を切り替えるパネルの子ルート(本コンポーネント自身とは別オブジェクトを指定)")]
	[SerializeField] private GameObject m_PanelRoot;
	[SerializeField] private TextMeshProUGUI m_StatusText;
	[SerializeField] private TextMeshProUGUI m_TeamSummaryText;
	[SerializeField] private TextMeshProUGUI m_RedCountText;
	[SerializeField] private TextMeshProUGUI m_BlueCountText;
	[SerializeField] private TextMeshProUGUI m_SpectatorCountText;

	[Header("操作ボタン")]
	[SerializeField] private Button m_JoinRedButton;
	[SerializeField] private Button m_JoinBlueButton;
	[SerializeField] private Button m_JoinSpectatorButton;
	[SerializeField] private Button m_CloseButton;

	[Header("自動表示設定")]
	[Tooltip("ロビー入室後、チーム未所属(None)が継続した際に自動表示するまでの猶予時間(秒、0以下時は既定値0.4)")]
	[SerializeField] private float m_AutoOpenGraceSeconds;

	/// <summary>
	/// チーム選択パネルが閉じられた際に発火します。
	/// </summary>
	public event Action OnPanelClosed;

	/// <summary>
	/// チーム選択パネルが現在表示中であるかを取得します。
	/// </summary>
	public bool IsPanelOpen
	{
		get => m_PanelRoot != null && m_PanelRoot.activeSelf;
	}

	private readonly StringBuilder m_SummaryBuilder = new StringBuilder();
	private Coroutine m_AutoOpenCoroutine;
	private bool m_IsForcedSelection;
	private bool m_HasPendingSelection;
	private ETeamType m_PendingTargetTeam = ETeamType.None;
	private bool m_IsBlockedByTransition;

	private void Awake()
	{
		if (m_LobbyManager == null)
		{
			m_LobbyManager = FindFirstObjectByType<LobbyManager>();
		}

		SetupButtonListeners();
	}

	private void Start()
	{
		if (m_PanelRoot == gameObject)
		{
			Debug.LogWarning("[LobbyTeamSelectPanel] m_PanelRootには本コンポーネントと別の子パネルを指定してください。", this);
		}
		else if (m_PanelRoot != null)
		{
			m_PanelRoot.SetActive(false);
		}

		if (m_LobbyManager != null)
		{
			m_LobbyManager.OnLobbyDataChanged += HandleLobbyDataChanged;
			m_LobbyManager.OnGameSceneLoadStarted += HandleBlockedTransition;
			m_LobbyManager.OnDisconnectedFromHost += HandleBlockedTransition;
		}

		RefreshPanelUI();

		if (!IsSpectatorHost())
		{
			m_AutoOpenCoroutine = StartCoroutine(AutoOpenCheckRoutine());
		}
	}

	private void OnDestroy()
	{
		if (m_AutoOpenCoroutine != null)
		{
			StopCoroutine(m_AutoOpenCoroutine);
			m_AutoOpenCoroutine = null;
		}

		if (m_LobbyManager != null)
		{
			m_LobbyManager.OnLobbyDataChanged -= HandleLobbyDataChanged;
			m_LobbyManager.OnGameSceneLoadStarted -= HandleBlockedTransition;
			m_LobbyManager.OnDisconnectedFromHost -= HandleBlockedTransition;
		}
	}

	/// <summary>
	/// ロール確定後などにユーザー操作でチーム選択パネルを手動で開きます。
	/// </summary>
	public void OpenManual()
	{
		if (m_IsBlockedByTransition || IsSpectatorHost())
		{
			return;
		}

		bool isUnseated = !TryGetLocalTeam(out ETeamType currentTeam) || currentTeam == ETeamType.None;
		OpenPanel(isForcedSelection: isUnseated);
	}

	/// <summary>
	/// チーム選択パネルを強制的に閉じます(試合開始時や切断時など)。
	/// </summary>
	public void ClosePanel()
	{
		if (m_PanelRoot == null || m_PanelRoot == gameObject)
		{
			return;
		}

		if (!m_PanelRoot.activeSelf)
		{
			return;
		}

		ClearSelectionIfInsidePanel();
		m_PanelRoot.SetActive(false);
		m_IsForcedSelection = false;
		m_HasPendingSelection = false;
		m_PendingTargetTeam = ETeamType.None;

		OnPanelClosed?.Invoke();
	}

	private void SetupButtonListeners()
	{
		if (m_JoinRedButton != null)
		{
			m_JoinRedButton.onClick.AddListener(() => SelectTeam(ETeamType.Red));
		}

		if (m_JoinBlueButton != null)
		{
			m_JoinBlueButton.onClick.AddListener(() => SelectTeam(ETeamType.Blue));
		}

		if (m_JoinSpectatorButton != null)
		{
			m_JoinSpectatorButton.onClick.AddListener(() => SelectTeam(ETeamType.Spectator));
		}

		if (m_CloseButton != null)
		{
			m_CloseButton.onClick.AddListener(HandleCloseButtonClicked);
		}
	}

	private IEnumerator AutoOpenCheckRoutine()
	{
		float waitSeconds = m_AutoOpenGraceSeconds > 0.0f
			? m_AutoOpenGraceSeconds
			: m_DefaultAutoOpenGraceSeconds;

		yield return new WaitForSecondsRealtime(waitSeconds);
		m_AutoOpenCoroutine = null;

		if (m_IsBlockedByTransition || IsSpectatorHost())
		{
			yield break;
		}

		if (TryGetLocalTeam(out ETeamType currentTeam) && currentTeam == ETeamType.None)
		{
			OpenPanel(isForcedSelection: true);
		}
	}

	private void HandleBlockedTransition()
	{
		m_IsBlockedByTransition = true;
		ClosePanel();
	}

	private void HandleLobbyDataChanged()
	{
		if (m_IsBlockedByTransition || IsSpectatorHost())
		{
			return;
		}

		RefreshPanelUI();

		if (!TryGetLocalTeam(out ETeamType currentTeam))
		{
			return;
		}

		// 未所属に戻された場合は自動猶予待機中でも即座にパネルを表示
		if (currentTeam == ETeamType.None)
		{
			if (!IsPanelOpen && m_AutoOpenCoroutine == null)
			{
				OpenPanel(isForcedSelection: true);
			}
			else if (IsPanelOpen && m_HasPendingSelection && m_PendingTargetTeam != ETeamType.None)
			{
				// 選択先が満員などで確定しなかった場合は案内を表示
				m_HasPendingSelection = false;
				m_PendingTargetTeam = ETeamType.None;
				SetStatusMessage("選択した枠に空きがありません。別のチームまたは観戦を選択してください。");
				FocusBestAvailableButton();
			}

			return;
		}

		// チーム確定待ち中または強制選択中に所属が確定した場合は自動で閉じる
		if (IsPanelOpen && (m_IsForcedSelection || m_HasPendingSelection))
		{
			ClosePanel();
		}
	}

	private void OpenPanel(bool isForcedSelection)
	{
		if (m_PanelRoot == null || m_PanelRoot == gameObject)
		{
			return;
		}

		m_IsForcedSelection = isForcedSelection;
		m_HasPendingSelection = false;
		m_PendingTargetTeam = ETeamType.None;

		m_PanelRoot.SetActive(true);
		// TitleOverlayより前面に出して未所属時の操作迷いを防止
		m_PanelRoot.transform.SetAsLastSibling();

		if (m_CloseButton != null)
		{
			m_CloseButton.gameObject.SetActive(!isForcedSelection);
		}

		SetStatusMessage(isForcedSelection
			? "参加するチーム、または観戦を選択してください。"
			: "変更先のチーム、または観戦を選択してください。");

		RefreshPanelUI();
		FocusBestAvailableButton();
	}

	private void SelectTeam(ETeamType targetTeam)
	{
		if (m_LobbyManager == null || m_IsBlockedByTransition)
		{
			return;
		}

		if (TryGetLocalTeam(out ETeamType currentTeam) && currentTeam == targetTeam)
		{
			ClosePanel();
			return;
		}

		m_HasPendingSelection = true;
		m_PendingTargetTeam = targetTeam;
		SetStatusMessage("選択を反映しています...");
		m_LobbyManager.JoinTeam(targetTeam);
	}

	private void HandleCloseButtonClicked()
	{
		if (m_IsForcedSelection)
		{
			return;
		}

		ClosePanel();
	}

	private void RefreshPanelUI()
	{
		if (m_LobbyManager == null)
		{
			return;
		}

		int redCount = 0;
		int blueCount = 0;
		int spectatorCount = 0;

		m_SummaryBuilder.Clear();
		m_SummaryBuilder.Append("【現在の参加状況】\n");

		var players = m_LobbyManager.LobbyPlayers;
		for (int i = 0; i < players.Count; i++)
		{
			var p = players[i];
			string teamLabel;
			if (p.Team == ETeamType.Red)
			{
				redCount++;
				teamLabel = "<color=#FF5555>[RED]</color>";
			}
			else if (p.Team == ETeamType.Blue)
			{
				blueCount++;
				teamLabel = "<color=#5588FF>[BLUE]</color>";
			}
			else if (p.Team == ETeamType.Spectator)
			{
				spectatorCount++;
				teamLabel = "<color=#88FF88>[観戦]</color>";
			}
			else
			{
				teamLabel = "[未所属]";
			}

			m_SummaryBuilder.Append("・");
			m_SummaryBuilder.Append(p.PlayerName);
			m_SummaryBuilder.Append(" : ");
			m_SummaryBuilder.Append(teamLabel);
			m_SummaryBuilder.Append('\n');
		}

		int maxMembers = LobbyManager.MaxTeamMembers;
		if (m_RedCountText != null)
		{
			m_RedCountText.text = $"RED ({redCount}/{maxMembers})";
		}

		if (m_BlueCountText != null)
		{
			m_BlueCountText.text = $"BLUE ({blueCount}/{maxMembers})";
		}

		if (m_SpectatorCountText != null)
		{
			m_SpectatorCountText.text = $"観戦 ({spectatorCount})";
		}

		if (m_TeamSummaryText != null)
		{
			m_TeamSummaryText.text = m_SummaryBuilder.ToString();
		}

		if (m_JoinRedButton != null)
		{
			m_JoinRedButton.interactable = redCount < maxMembers;
		}

		if (m_JoinBlueButton != null)
		{
			m_JoinBlueButton.interactable = blueCount < maxMembers;
		}

		if (m_JoinSpectatorButton != null)
		{
			m_JoinSpectatorButton.interactable = true;
		}
	}

	private void FocusBestAvailableButton()
	{
		if (EventSystem.current == null || !IsPanelOpen)
		{
			return;
		}

		Button target = null;
		if (m_JoinRedButton != null && m_JoinRedButton.gameObject.activeInHierarchy && m_JoinRedButton.interactable)
		{
			target = m_JoinRedButton;
		}
		else if (m_JoinBlueButton != null && m_JoinBlueButton.gameObject.activeInHierarchy && m_JoinBlueButton.interactable)
		{
			target = m_JoinBlueButton;
		}
		else if (m_JoinSpectatorButton != null && m_JoinSpectatorButton.gameObject.activeInHierarchy && m_JoinSpectatorButton.interactable)
		{
			target = m_JoinSpectatorButton;
		}

		if (target != null)
		{
			EventSystem.current.SetSelectedGameObject(null);
			EventSystem.current.SetSelectedGameObject(target.gameObject);
		}
	}

	private void ClearSelectionIfInsidePanel()
	{
		if (EventSystem.current == null || m_PanelRoot == null)
		{
			return;
		}

		GameObject selected = EventSystem.current.currentSelectedGameObject;
		if (selected != null && (selected == m_PanelRoot || selected.transform.IsChildOf(m_PanelRoot.transform)))
		{
			EventSystem.current.SetSelectedGameObject(null);
		}
	}

	private void SetStatusMessage(string message)
	{
		if (m_StatusText != null)
		{
			m_StatusText.text = message;
		}
	}

	private bool TryGetLocalTeam(out ETeamType team)
	{
		if (m_LobbyManager != null && m_LobbyManager.TryGetLocalPlayerData(out LobbyPlayerData localData))
		{
			team = localData.Team;
			return true;
		}

		team = ETeamType.None;
		return false;
	}

	private static bool IsSpectatorHost()
	{
		return NetworkManager.Singleton != null &&
			NetworkManager.Singleton.IsServer &&
			!NetworkConnectManager.IsPlayerHost;
	}
}
