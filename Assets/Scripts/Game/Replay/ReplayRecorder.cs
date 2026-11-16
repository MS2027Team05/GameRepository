using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 試合中の全プレイヤーとボールの座標・回転・歩行状態をクライアント側リングバッファへ記録し、
/// 得点時に直近ゴールおよびベストゴールのリプレイクリップを切り出して保持します。
/// </summary>
public sealed class ReplayRecorder : MonoBehaviour
{
	private struct TrackedPlayerEntry
	{
		public PlayerNetworkProfile m_Profile;
		public PlayerAnimationController m_Animation;
	}

	private const float m_DefaultPreGoalDuration = 5.0f;
	private const float m_DefaultPostGoalDuration = 0.8f;
	private const float m_DefaultRecordInterval = 0.02f;
	private const float m_PlayerScanInterval = 0.5f;

	[Header("記録設定")]
	[Tooltip("ゴール確定前の記録保持秒数です(0以下で既定値5秒)。")]
	[SerializeField] private float m_PreGoalDuration;
	[Tooltip("ゴール確定後の余韻記録秒数です(0以下で既定値0.8秒)。")]
	[SerializeField] private float m_PostGoalDuration;
	[Tooltip("フレームを記録する間隔(秒)です(0以下で既定値0.02秒)。")]
	[SerializeField] private float m_RecordInterval;

	[Header("追跡対象参照(未設定時は自動検索)")]
	[SerializeField] private BallController m_Ball;

	private ReplayFrameSlot[] m_RingBuffer;
	private ReplayClipData m_LatestGoalClip;
	private ReplayClipData m_BestGoalClip;
	private int m_WriteIndex;
	private int m_BufferedCount;
	private float m_LastSampleTime;
	private float m_NextPlayerScanTime;

	private bool m_IsCapturingPostGoal;
	private ETeamType m_PendingScoringTeam;
	private uint m_PendingGoalSequence;
	private bool m_PendingIsNewBestGoal;
	private float m_PendingGoalTime;
	private float m_PendingFinalizeTime;
	private MatchManager.EMatchState m_PreviousMatchState = MatchManager.EMatchState.Waiting;

	private readonly List<TrackedPlayerEntry> m_TrackedPlayers = new List<TrackedPlayerEntry>(ReplayFrameSlot.m_MaxPlayers);
	private readonly ReplayPlayerMeta[] m_SessionPlayerMetas = new ReplayPlayerMeta[ReplayFrameSlot.m_MaxPlayers];
	private int m_SessionPlayerMetaCount;

	/// <summary>
	/// 直近で確定したゴールのリプレイクリップを取得します。
	/// </summary>
	public ReplayClipData LatestGoalClip => m_LatestGoalClip;

	/// <summary>
	/// 試合内で最も評価の高いベストゴールのリプレイクリップを取得します。
	/// </summary>
	public ReplayClipData BestGoalClip => m_BestGoalClip;

	/// <summary>
	/// リプレイ1クリップの目標再生時間(秒)を取得します。
	/// </summary>
	public float MaxClipDuration => GetPreGoalDuration() + GetPostGoalDuration();

	private void Awake()
	{
		EnsureInitialized();
	}

	private void LateUpdate()
	{
		EnsureInitialized();

		MatchManager matchManager = MatchManager.Instance;
		if (matchManager == null || !matchManager.IsSpawned)
		{
			return;
		}

		MatchManager.EMatchState currentState = matchManager.MatchState;
		HandleStateTransition(currentState);

		if (m_IsCapturingPostGoal && Time.time >= m_PendingFinalizeTime)
		{
			FinalizePendingGoalClip();
		}

		bool shouldRecord = currentState == MatchManager.EMatchState.Playing ||
			(m_IsCapturingPostGoal && currentState == MatchManager.EMatchState.GoalCelebration);

		if (!shouldRecord)
		{
			return;
		}

		float interval = GetRecordInterval();
		if (m_LastSampleTime >= 0f && Time.time - m_LastSampleTime < interval)
		{
			return;
		}

		RecordFrameSample();
	}

	/// <summary>
	/// 新しい試合の開始時に記録バッファと保存済みクリップをすべて初期化します。
	/// </summary>
	public void ResetForNewMatch()
	{
		EnsureInitialized();
		ClearBuffer();
		m_LatestGoalClip.Clear();
		m_BestGoalClip.Clear();
		m_SessionPlayerMetaCount = 0;
		for (int i = 0; i < m_SessionPlayerMetas.Length; i++)
		{
			m_SessionPlayerMetas[i] = default;
		}
	}

	/// <summary>
	/// ゴール確定通知を受け取り、ゴール後の余韻記録フェーズを開始します。
	/// </summary>
	public void NotifyGoalScored(ETeamType scoringTeam, uint goalSequence, bool isNewBestGoal)
	{
		EnsureInitialized();

		if (m_IsCapturingPostGoal)
		{
			FinalizePendingGoalClip();
		}

		// ゴール瞬間の最新フレームを確実に含めるため即時1サンプル記録します。
		RecordFrameSample();

		m_IsCapturingPostGoal = true;
		m_PendingScoringTeam = scoringTeam;
		m_PendingGoalSequence = goalSequence;
		m_PendingIsNewBestGoal = isNewBestGoal;
		m_PendingGoalTime = Time.time;
		m_PendingFinalizeTime = Time.time + GetPostGoalDuration();
	}

	/// <summary>
	/// 余韻記録の途中であっても直ちにクリップ確定を完了させます。
	/// </summary>
	public void ForceFinalizePendingClip()
	{
		if (!m_IsCapturingPostGoal)
		{
			return;
		}

		RecordFrameSample();
		FinalizePendingGoalClip();
	}

	private void HandleStateTransition(MatchManager.EMatchState currentState)
	{
		if (m_PreviousMatchState == currentState)
		{
			return;
		}

		if (m_PreviousMatchState == MatchManager.EMatchState.GoalCelebration &&
			currentState != MatchManager.EMatchState.GoalCelebration &&
			m_IsCapturingPostGoal)
		{
			ForceFinalizePendingClip();
		}

		// 再配置・カウントダウン開始時に前ラウンドの残存座標を破棄します。
		if (currentState == MatchManager.EMatchState.PreparingRestart ||
			currentState == MatchManager.EMatchState.Waiting)
		{
			ClearBuffer();
		}

		if (currentState == MatchManager.EMatchState.Waiting &&
			m_PreviousMatchState != MatchManager.EMatchState.Waiting)
		{
			m_LatestGoalClip.Clear();
			m_BestGoalClip.Clear();
		}

		m_PreviousMatchState = currentState;
	}

	private void EnsureInitialized()
	{
		if (m_RingBuffer != null)
		{
			return;
		}

		float interval = GetRecordInterval();
		float totalWindow = GetPreGoalDuration() + GetPostGoalDuration() + 2.0f;
		int capacity = Mathf.Max(64, Mathf.CeilToInt(totalWindow / interval));

		m_RingBuffer = new ReplayFrameSlot[capacity];
		for (int i = 0; i < capacity; i++)
		{
			m_RingBuffer[i] = new ReplayFrameSlot();
		}

		m_LatestGoalClip = new ReplayClipData(capacity);
		m_BestGoalClip = new ReplayClipData(capacity);
		m_LastSampleTime = -1f;
	}

	private void ClearBuffer()
	{
		m_IsCapturingPostGoal = false;
		m_WriteIndex = 0;
		m_BufferedCount = 0;
		m_LastSampleTime = -1f;
	}

	private void RecordFrameSample()
	{
		m_LastSampleTime = Time.time;

		if (m_Ball == null)
		{
			m_Ball = FindFirstObjectByType<BallController>();
		}

		RefreshTrackedPlayersIfNeeded();

		ReplayFrameSlot slot = m_RingBuffer[m_WriteIndex];
		slot.m_Time = Time.time;

		if (m_Ball != null)
		{
			slot.m_Ball = new ReplayBallPose
			{
				m_IsValid = true,
				m_Position = m_Ball.transform.position,
				m_Rotation = m_Ball.transform.rotation,
				m_HasHolder = m_Ball.HasHolder,
				m_HolderClientId = m_Ball.CurrentHolderId,
				m_IsShotInFlight = m_Ball.IsShotInFlight
			};
		}
		else
		{
			slot.m_Ball = default;
		}

		int playerCount = 0;
		for (int i = 0; i < m_TrackedPlayers.Count && playerCount < ReplayFrameSlot.m_MaxPlayers; i++)
		{
			TrackedPlayerEntry entry = m_TrackedPlayers[i];
			if (entry.m_Profile == null || !entry.m_Profile.gameObject.activeInHierarchy)
			{
				continue;
			}

			if (entry.m_Profile.Team == ETeamType.Spectator)
			{
				continue;
			}

			ulong clientId = entry.m_Profile.OwnerClientId;
			UpsertPlayerMeta(entry.m_Profile, clientId);

			slot.m_Players[playerCount] = new ReplayPlayerPose
			{
				m_IsActive = true,
				m_ClientId = clientId,
				m_Position = entry.m_Profile.transform.position,
				m_Rotation = entry.m_Profile.transform.rotation,
				m_IsMoving = entry.m_Animation != null && entry.m_Animation.IsMoving
			};
			playerCount++;
		}

		slot.m_PlayerCount = playerCount;

		m_WriteIndex = (m_WriteIndex + 1) % m_RingBuffer.Length;
		if (m_BufferedCount < m_RingBuffer.Length)
		{
			m_BufferedCount++;
		}
	}

	private void RefreshTrackedPlayersIfNeeded()
	{
		if (Time.time < m_NextPlayerScanTime && m_TrackedPlayers.Count > 0)
		{
			bool hasNull = false;
			for (int i = 0; i < m_TrackedPlayers.Count; i++)
			{
				if (m_TrackedPlayers[i].m_Profile == null)
				{
					hasNull = true;
					break;
				}
			}

			if (!hasNull)
			{
				return;
			}
		}

		m_NextPlayerScanTime = Time.time + m_PlayerScanInterval;
		m_TrackedPlayers.Clear();

		PlayerNetworkProfile[] profiles = FindObjectsByType<PlayerNetworkProfile>(FindObjectsSortMode.None);
		for (int i = 0; i < profiles.Length; i++)
		{
			PlayerNetworkProfile profile = profiles[i];
			if (profile == null || profile.Team == ETeamType.Spectator)
			{
				continue;
			}

			m_TrackedPlayers.Add(new TrackedPlayerEntry
			{
				m_Profile = profile,
				m_Animation = profile.GetComponent<PlayerAnimationController>()
			});
		}
	}

	private void UpsertPlayerMeta(PlayerNetworkProfile profile, ulong clientId)
	{
		for (int i = 0; i < m_SessionPlayerMetaCount; i++)
		{
			if (m_SessionPlayerMetas[i].m_IsValid && m_SessionPlayerMetas[i].m_ClientId == clientId)
			{
				m_SessionPlayerMetas[i].m_PlayerName = profile.PlayerName;
				m_SessionPlayerMetas[i].m_Team = profile.Team;
				m_SessionPlayerMetas[i].m_CharacterType = profile.CharacterType;
				m_SessionPlayerMetas[i].m_HighlightColor = profile.HighlightColor;
				return;
			}
		}

		if (m_SessionPlayerMetaCount >= m_SessionPlayerMetas.Length)
		{
			return;
		}

		m_SessionPlayerMetas[m_SessionPlayerMetaCount] = new ReplayPlayerMeta
		{
			m_IsValid = true,
			m_ClientId = clientId,
			m_PlayerName = profile.PlayerName,
			m_Team = profile.Team,
			m_CharacterType = profile.CharacterType,
			m_HighlightColor = profile.HighlightColor
		};
		m_SessionPlayerMetaCount++;
	}

	private void FinalizePendingGoalClip()
	{
		m_IsCapturingPostGoal = false;
		if (m_BufferedCount < 2)
		{
			return;
		}

		float minTime = m_PendingGoalTime - GetPreGoalDuration();
		int capacity = m_RingBuffer.Length;
		int startOffset = m_BufferedCount < capacity ? 0 : m_WriteIndex;

		m_LatestGoalClip.BeginBuild(
			m_PendingScoringTeam,
			m_PendingGoalSequence,
			m_SessionPlayerMetas,
			m_SessionPlayerMetaCount);

		bool hasStartTime = false;
		float clipStartTime = 0f;

		for (int i = 0; i < m_BufferedCount; i++)
		{
			int ringIndex = (startOffset + i) % capacity;
			ReplayFrameSlot slot = m_RingBuffer[ringIndex];

			if (slot.m_Time < minTime && i < m_BufferedCount - 2)
			{
				continue;
			}

			if (!hasStartTime)
			{
				clipStartTime = slot.m_Time;
				hasStartTime = true;
			}

			m_LatestGoalClip.AppendFrame(slot, slot.m_Time - clipStartTime);
		}

		float goalTime = hasStartTime
			? Mathf.Clamp(m_PendingGoalTime - clipStartTime, 0f, m_LatestGoalClip.Duration)
			: 0f;
		m_LatestGoalClip.SetGoalTime(goalTime);

		if (m_PendingIsNewBestGoal && m_LatestGoalClip.IsValid)
		{
			m_BestGoalClip.CopyFrom(m_LatestGoalClip);
		}
	}

	private float GetPreGoalDuration()
	{
		return m_PreGoalDuration > 0.01f ? m_PreGoalDuration : m_DefaultPreGoalDuration;
	}

	private float GetPostGoalDuration()
	{
		return m_PostGoalDuration > 0.01f ? m_PostGoalDuration : m_DefaultPostGoalDuration;
	}

	private float GetRecordInterval()
	{
		return m_RecordInterval > 0.004f ? m_RecordInterval : m_DefaultRecordInterval;
	}
}
