using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 試合の進行状態、残り時間、チーム得点をサーバー権限で管理します。
/// </summary>
public class MatchManager : NetworkBehaviour
{
	/// <summary>
	/// 現在の試合状態。
	/// </summary>
	public enum EMatchState : byte
	{
		Waiting = 0,
		Playing = 1,
		Finished = 2,
		Countdown = 3,
		PreparingRestart = 4,
		GoalCelebration = 5,
	}

	[SerializeField] private int m_MatchDurationSeconds;
	[SerializeField] private bool m_StartAutomatically;
	[Header("開始・リスタート")]
	[SerializeField, Min(1)] private int m_CountdownDurationSeconds;
	[SerializeField] private PlayerSpawner m_PlayerSpawner;
	[SerializeField] private BallController m_Ball;

	[Header("試合終了・リザルト遷移")]
	[Tooltip("試合終了後、リザルトシーンへ遷移するまでの演出待機時間(秒)です。")]
	[SerializeField] private float m_FinishMatchTransitionDelay;
	[Tooltip("遷移先のリザルトシーン名です。空欄の場合はResultSceneが使用されます。")]
	[SerializeField] private string m_ResultSceneName;

	private readonly NetworkVariable<double> m_CountdownEndTime = new NetworkVariable<double>();
	private readonly NetworkVariable<ETeamType> m_GoalScoringTeam = new NetworkVariable<ETeamType>(
		ETeamType.None,
		NetworkVariableReadPermission.Everyone,
		NetworkVariableWritePermission.Server);
	private double m_PlayTimeRemaining;
	private bool m_IsStartRequested;
	private bool m_IsGoalProcessing;
	private readonly HashSet<ulong> m_PendingGoalEffectClients = new HashSet<ulong>();
	private uint m_GoalEffectSequence;
	private uint m_LocalGoalEffectSequence;
	private int m_LocalGoalEffectCount;
	private bool m_IsLocalGoalEffectPending;

	[Header("ゴール判定のデバッグ設定")]
	[InspectorName("ゴール得点を1回に制限")]
	[Tooltip("ONにすると両ゴール共通で1回のプレーにつき1回の得点に制限します。リスタート完了時に制限を解除します。カウントダウン中は設定にかかわらず得点できません。")]
	[FormerlySerializedAs("m_DisableGoalScoreLimit")]
	[SerializeField] private bool m_LimitGoalScore;

	private readonly NetworkVariable<EMatchState> m_MatchState = new NetworkVariable<EMatchState>(
		EMatchState.Waiting,
		NetworkVariableReadPermission.Everyone,
		NetworkVariableWritePermission.Server);
	private readonly NetworkVariable<int> m_RemainingTimeSeconds = new NetworkVariable<int>(
		0,
		NetworkVariableReadPermission.Everyone,
		NetworkVariableWritePermission.Server);
	private readonly NetworkVariable<int> m_RedScore = new NetworkVariable<int>(
		0,
		NetworkVariableReadPermission.Everyone,
		NetworkVariableWritePermission.Server);
	private readonly NetworkVariable<int> m_BlueScore = new NetworkVariable<int>(
		0,
		NetworkVariableReadPermission.Everyone,
		NetworkVariableWritePermission.Server);

	private double m_MatchEndTime;
	private bool m_HasScoredGoal;

	/// <summary>
	/// シーン内でネットワーク生成されている試合管理インスタンス。
	/// </summary>
	public static MatchManager Instance { get; private set; }

	/// <summary>
	/// 試合状態が変化した際に通知します。
	/// </summary>
	public event Action<EMatchState> MatchStateChanged;

	/// <summary>
	/// 残り時間が変化した際に秒単位で通知します。
	/// </summary>
	public event Action<int> RemainingTimeChanged;

	/// <summary>
	/// Redチームの得点が変化した際に通知します。
	/// </summary>
	public event Action<int> RedScoreChanged;

	/// <summary>
	/// Blueチームの得点が変化した際に通知します。
	/// </summary>
	public event Action<int> BlueScoreChanged;

	/// <summary>
	/// サーバーで確定したゴールの得点先チームを各表示端末へ通知します。
	/// </summary>
	public event Action<ETeamType> GoalScored;

	public EMatchState MatchState => m_MatchState.Value;
	public int RemainingTimeSeconds => m_RemainingTimeSeconds.Value;
	public int RedScore => m_RedScore.Value;
	public int BlueScore => m_BlueScore.Value;
	public ETeamType GoalScoringTeam => m_GoalScoringTeam.Value;
	public int CountdownRemainingSeconds => m_MatchState.Value == EMatchState.Countdown && NetworkManager != null
		? Mathf.Max(0, Mathf.CeilToInt((float)(m_CountdownEndTime.Value - NetworkManager.ServerTime.Time)))
		: 0;

	/// <summary>
	/// 試合終了時点で得点が高いチームを返します。同点の場合はNoneを返します。
	/// </summary>
	public ETeamType WinnerTeam
	{
		get
		{
			if (m_RedScore.Value == m_BlueScore.Value)
			{
				return ETeamType.None;
			}

			return m_RedScore.Value > m_BlueScore.Value ? ETeamType.Red : ETeamType.Blue;
		}
	}

	public override void OnNetworkSpawn()
	{
		if (Instance != null && Instance != this)
		{
			Debug.LogError("[MatchManager] シーン内に複数のMatchManagerが存在します。");
			enabled = false;
			return;
		}

		Instance = this;
		SubscribeToNetworkVariables();

		if (IsServer)
		{
			NetworkManager.OnClientDisconnectCallback += HandleGoalEffectClientDisconnected;
			ResetMatch();

			if (m_StartAutomatically)
			{
				StartMatch();
			}
		}

		NotifyCurrentValues();
	}

	public override void OnNetworkDespawn()
	{
		if (IsServer)
		{
			NetworkManager.OnClientDisconnectCallback -= HandleGoalEffectClientDisconnected;
		}
		m_IsLocalGoalEffectPending = false;
		m_LocalGoalEffectCount = 0;
		m_PendingGoalEffectClients.Clear();
		UnsubscribeFromNetworkVariables();

		if (Instance == this)
		{
			Instance = null;
		}
	}

	private void Update()
	{
		if (IsSpawned && IsClient && m_IsLocalGoalEffectPending && m_LocalGoalEffectCount == 0)
		{
			m_IsLocalGoalEffectPending = false;
			CompleteGoalEffectServerRpc(m_LocalGoalEffectSequence);
		}

		if (!IsSpawned || !IsServer)
		{
			return;
		}

		if (m_IsStartRequested && m_PlayerSpawner.IsSpawned && m_PlayerSpawner.IsReady && m_Ball.IsSpawned)
		{
			m_IsStartRequested = false;
			BeginRestartPreparation();
		}

		if (m_MatchState.Value == EMatchState.GoalCelebration && m_PendingGoalEffectClients.Count == 0)
		{
			BeginRestartPreparation();
		}

		if (m_MatchState.Value == EMatchState.PreparingRestart && m_PlayerSpawner.ArePlayersReadyForRestart)
		{
			m_CountdownEndTime.Value = NetworkManager.ServerTime.Time + m_CountdownDurationSeconds;
			m_MatchState.Value = EMatchState.Countdown;
		}

		if (m_MatchState.Value == EMatchState.Countdown && NetworkManager.ServerTime.Time >= m_CountdownEndTime.Value)
		{
			m_MatchEndTime = NetworkManager.ServerTime.Time + m_PlayTimeRemaining;
			m_HasScoredGoal = false;
			m_MatchState.Value = EMatchState.Playing;
			m_Ball.SetMatchPaused(false);
			m_PlayerSpawner.SetMatchActionsEnabled(true);
		}

		if (m_MatchState.Value != EMatchState.Playing)
		{
			return;
		}

		int remainingSeconds = Mathf.Max(
			0,
			Mathf.CeilToInt((float)(m_MatchEndTime - NetworkManager.ServerTime.Time)));

		if (remainingSeconds != m_RemainingTimeSeconds.Value)
		{
			m_RemainingTimeSeconds.Value = remainingSeconds;
		}

		if (remainingSeconds <= 0)
		{
			FinishMatch();
		}
	}

	/// <summary>
	/// 得点と残り時間を初期化し、試合開始待ち状態へ戻します。
	/// </summary>
	public void ResetMatch()
	{
		if (!IsServer)
		{
			Debug.LogWarning("[MatchManager] 試合の初期化はサーバーのみ実行できます。");
			return;
		}

		m_HasScoredGoal = false;
		m_IsStartRequested = false;
		m_GoalScoringTeam.Value = ETeamType.None;
		m_IsGoalProcessing = false;
		m_PendingGoalEffectClients.Clear();
		m_IsLocalGoalEffectPending = false;
		m_LocalGoalEffectCount = 0;
		m_CountdownEndTime.Value = 0d;
		m_PlayTimeRemaining = 0d;
		m_RedScore.Value = 0;
		m_BlueScore.Value = 0;
		m_RemainingTimeSeconds.Value = Mathf.Max(0, m_MatchDurationSeconds);
		m_MatchState.Value = EMatchState.Waiting;
		m_MatchEndTime = 0d;
		if (m_PlayerSpawner != null && m_PlayerSpawner.IsSpawned)
		{
			m_PlayerSpawner.SetMatchActionsEnabled(false);
		}
		if (m_Ball != null && m_Ball.IsSpawned)
		{
			m_Ball.SetMatchPaused(true);
		}
	}

	/// <summary>
	/// Inspectorで設定された制限時間を使用して試合を開始します。
	/// </summary>
	public void StartMatch()
	{
		if (!IsServer)
		{
			Debug.LogWarning("[MatchManager] 試合開始はサーバーのみ実行できます。");
			return;
		}

		if (m_MatchState.Value != EMatchState.Waiting || m_IsStartRequested)
		{
			return;
		}

		if (m_MatchDurationSeconds <= 0 || m_CountdownDurationSeconds <= 0 || m_PlayerSpawner == null || m_Ball == null)
		{
			Debug.LogError("[MatchManager] 制限時間とカウントダウン秒数を1以上にし、PlayerSpawnerとBallを設定してください。", this);
			return;
		}

		m_RemainingTimeSeconds.Value = m_MatchDurationSeconds;
		m_PlayTimeRemaining = m_MatchDurationSeconds;
		// シーン内NetworkObjectの生成順に依存しないよう、必要な生成完了まで待ちます。
		m_IsStartRequested = true;
	}

	private void BeginRestartPreparation()
	{
		// 再配置中は得点と試合時計を停止し、完了応答が揃うまでカウントを表示しません。
		m_MatchState.Value = EMatchState.PreparingRestart;
		m_CountdownEndTime.Value = 0d;
		m_PlayerSpawner.SetMatchActionsEnabled(false);
		if (!m_Ball.ResetForRestart())
		{
			Debug.LogError("[MatchManager] ボールの再配置に失敗したため、開始待ちへ戻します。", this);
			m_MatchState.Value = EMatchState.Waiting;
			return;
		}
		m_PlayerSpawner.ResetPlayersForRestart();
	}

	/// <summary>
	/// 指定チームへ得点を加算します。
	/// </summary>
	/// <param name="team">得点を加算するチーム。</param>
	/// <param name="amount">加算する得点。</param>
	/// <returns>得点を加算できた場合はtrue。</returns>
	public bool AddScore(ETeamType team, int amount = 1)
	{
		if (!IsServer)
		{
			Debug.LogWarning("[MatchManager] 得点の加算はサーバーのみ実行できます。");
			return false;
		}

		if (m_MatchState.Value != EMatchState.Playing || amount <= 0)
		{
			return false;
		}

		switch (team)
		{
			case ETeamType.Red:
				m_RedScore.Value += amount;
				return true;
			case ETeamType.Blue:
				m_BlueScore.Value += amount;
				return true;
			default:
				Debug.LogWarning("[MatchManager] 得点対象にはRedまたはBlueを指定してください。");
				return false;
		}
	}

	/// <summary>
	/// 両ゴール共通の得点制限を確認し、指定チームへ1点加算します。
	/// </summary>
	/// <param name="team">得点を加算するチーム。</param>
	/// <returns>得点を加算できた場合はtrue。</returns>
	public bool TryAddGoalScore(ETeamType team)
	{
		if (!isActiveAndEnabled || !IsSpawned || !IsServer ||
			m_MatchState.Value != EMatchState.Playing ||
			NetworkManager.ServerTime.Time >= m_MatchEndTime ||
			m_IsGoalProcessing ||
			(m_LimitGoalScore && m_HasScoredGoal))
		{
			return false;
		}

		// 得点変更イベントから再度呼ばれても重複加算しないよう、先に制限を設定します。
		bool hadScoredGoal = m_HasScoredGoal;
		m_IsGoalProcessing = true;
		m_HasScoredGoal = true;
		if (!AddScore(team))
		{
			m_HasScoredGoal = hadScoredGoal;
			m_IsGoalProcessing = false;
			return false;
		}

		m_PlayTimeRemaining = Math.Max(0d, m_MatchEndTime - NetworkManager.ServerTime.Time);
		m_RemainingTimeSeconds.Value = Mathf.CeilToInt((float)m_PlayTimeRemaining);
		BeginGoalCelebration(team);
		m_IsGoalProcessing = false;
		return true;
	}

	private void BeginGoalCelebration(ETeamType team)
	{
		m_GoalScoringTeam.Value = team;
		m_MatchState.Value = EMatchState.GoalCelebration;
		m_PlayerSpawner.SetMatchActionsEnabled(false);
		m_Ball.SetMatchPaused(true);
		m_GoalEffectSequence++;
		m_PendingGoalEffectClients.Clear();
		foreach (var client in NetworkManager.ConnectedClientsList)
		{
			if (client.ClientId != Unity.Netcode.NetworkManager.ServerClientId || IsHost)
			{
				m_PendingGoalEffectClients.Add(client.ClientId);
			}
		}
		NotifyGoalScoredRpc(team, m_GoalEffectSequence);
	}

	[Rpc(SendTo.ClientsAndHost)]
	private void NotifyGoalScoredRpc(ETeamType team, uint sequence, RpcParams rpcParams = default)
	{
		if (rpcParams.Receive.SenderClientId != Unity.Netcode.NetworkManager.ServerClientId) return;
		m_LocalGoalEffectSequence = sequence;
		m_LocalGoalEffectCount = 0;
		m_IsLocalGoalEffectPending = true;
		GoalScored?.Invoke(team);
	}

	/// <summary>
	/// この端末で再生を開始したエフェクトを完了待ちへ登録します。
	/// </summary>
	public uint RegisterLocalGoalEffect()
	{
		if (m_IsLocalGoalEffectPending) m_LocalGoalEffectCount++;
		return m_LocalGoalEffectSequence;
	}

	/// <summary>
	/// 現在の得点演出に属するエフェクトの完了を受け付けます。
	/// </summary>
	public void CompleteLocalGoalEffect(uint sequence)
	{
		if (m_IsLocalGoalEffectPending && sequence == m_LocalGoalEffectSequence && m_LocalGoalEffectCount > 0)
		{
			m_LocalGoalEffectCount--;
		}
	}

	[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
	private void CompleteGoalEffectServerRpc(uint sequence, RpcParams rpcParams = default)
	{
		if (m_MatchState.Value == EMatchState.GoalCelebration && sequence == m_GoalEffectSequence)
		{
			m_PendingGoalEffectClients.Remove(rpcParams.Receive.SenderClientId);
		}
	}

	private void HandleGoalEffectClientDisconnected(ulong clientId)
	{
		m_PendingGoalEffectClients.Remove(clientId);
	}

	/// <summary>
	/// 現在の得点を保持したまま試合を終了し、演出待機後にリザルトシーンへ遷移します。
	/// </summary>
	public void FinishMatch()
	{
		if (!IsServer)
		{
			Debug.LogWarning("[MatchManager] 試合終了はサーバーのみ実行できます。");
			return;
		}

		m_RemainingTimeSeconds.Value = 0;
		m_PendingGoalEffectClients.Clear();
		m_IsLocalGoalEffectPending = false;
		m_IsStartRequested = false;
		m_MatchState.Value = EMatchState.Finished;
		m_PlayerSpawner?.SetMatchActionsEnabled(false);
		m_Ball?.SetMatchPaused(true);

		// 試合結果を永続ストアへ保存
		if (SessionPlayerDataStore.Instance != null)
		{
			SessionPlayerDataStore.Instance.SetMatchResult(m_RedScore.Value, m_BlueScore.Value, WinnerTeam);
		}

		StartCoroutine(TransitionToResultSceneCoroutine());
	}

	/// <summary>
	/// 演出待機後、全クライアントを同期してリザルトシーンへ遷移させます。
	/// </summary>
	private IEnumerator TransitionToResultSceneCoroutine()
	{
		float delay = Mathf.Max(0.1f, m_FinishMatchTransitionDelay);
		yield return new WaitForSeconds(delay);

		string targetScene = !string.IsNullOrEmpty(m_ResultSceneName) ? m_ResultSceneName : "ResultScene";

		if (NetworkManager != null && NetworkManager.SceneManager != null)
		{
			NetworkManager.SceneManager.LoadScene(targetScene, UnityEngine.SceneManagement.LoadSceneMode.Single);
		}
		else
		{
			UnityEngine.SceneManagement.SceneManager.LoadScene(targetScene);
		}
	}

	private void SubscribeToNetworkVariables()
	{
		m_MatchState.OnValueChanged += HandleMatchStateChanged;
		m_RemainingTimeSeconds.OnValueChanged += HandleRemainingTimeChanged;
		m_RedScore.OnValueChanged += HandleRedScoreChanged;
		m_BlueScore.OnValueChanged += HandleBlueScoreChanged;
	}

	private void UnsubscribeFromNetworkVariables()
	{
		m_MatchState.OnValueChanged -= HandleMatchStateChanged;
		m_RemainingTimeSeconds.OnValueChanged -= HandleRemainingTimeChanged;
		m_RedScore.OnValueChanged -= HandleRedScoreChanged;
		m_BlueScore.OnValueChanged -= HandleBlueScoreChanged;
	}

	private void NotifyCurrentValues()
	{
		MatchStateChanged?.Invoke(m_MatchState.Value);
		RemainingTimeChanged?.Invoke(m_RemainingTimeSeconds.Value);
		RedScoreChanged?.Invoke(m_RedScore.Value);
		BlueScoreChanged?.Invoke(m_BlueScore.Value);
	}

	private void HandleMatchStateChanged(EMatchState previousValue, EMatchState newValue)
	{
		if (previousValue == EMatchState.GoalCelebration && newValue != EMatchState.GoalCelebration)
		{
			m_IsLocalGoalEffectPending = false;
			m_LocalGoalEffectCount = 0;
		}

		if (newValue == EMatchState.Finished && SessionPlayerDataStore.Instance != null)
		{
			SessionPlayerDataStore.Instance.SetMatchResult(m_RedScore.Value, m_BlueScore.Value, WinnerTeam);
		}

		MatchStateChanged?.Invoke(newValue);
	}

	private void HandleRemainingTimeChanged(int previousValue, int newValue)
	{
		RemainingTimeChanged?.Invoke(newValue);
	}

	private void HandleRedScoreChanged(int previousValue, int newValue)
	{
		RedScoreChanged?.Invoke(newValue);
	}

	private void HandleBlueScoreChanged(int previousValue, int newValue)
	{
		BlueScoreChanged?.Invoke(newValue);
	}
}
