using System;
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
	}

	[SerializeField] private int m_MatchDurationSeconds;
	[SerializeField] private bool m_StartAutomatically;
	[Header("開始・リスタート")]
	[SerializeField, Min(1)] private int m_CountdownDurationSeconds;
	[SerializeField] private PlayerSpawner m_PlayerSpawner;
	[SerializeField] private BallController m_Ball;

	private readonly NetworkVariable<double> m_CountdownEndTime = new NetworkVariable<double>();
	private double m_PlayTimeRemaining;
	private bool m_IsStartRequested;
	private bool m_IsGoalProcessing;

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

	public EMatchState MatchState => m_MatchState.Value;
	public int RemainingTimeSeconds => m_RemainingTimeSeconds.Value;
	public int RedScore => m_RedScore.Value;
	public int BlueScore => m_BlueScore.Value;
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
		UnsubscribeFromNetworkVariables();

		if (Instance == this)
		{
			Instance = null;
		}
	}

	private void Update()
	{
		if (!IsSpawned || !IsServer)
		{
			return;
		}

		if (m_IsStartRequested && m_PlayerSpawner.IsSpawned && m_PlayerSpawner.IsReady && m_Ball.IsSpawned)
		{
			m_IsStartRequested = false;
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
		m_IsGoalProcessing = false;
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
		BeginRestartPreparation();
		m_IsGoalProcessing = false;
		return true;
	}

	/// <summary>
	/// 現在の得点を保持したまま試合を終了します。
	/// </summary>
	public void FinishMatch()
	{
		if (!IsServer)
		{
			Debug.LogWarning("[MatchManager] 試合終了はサーバーのみ実行できます。");
			return;
		}

		m_RemainingTimeSeconds.Value = 0;
		m_IsStartRequested = false;
		m_MatchState.Value = EMatchState.Finished;
		m_PlayerSpawner?.SetMatchActionsEnabled(false);
		m_Ball?.SetMatchPaused(true);
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
