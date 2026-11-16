using Unity.Collections;
using UnityEngine;

/// <summary>
/// リプレイ1フレーム分のボール状態。
/// </summary>
public struct ReplayBallPose
{
	public bool m_IsValid;
	public Vector3 m_Position;
	public Quaternion m_Rotation;
	public bool m_HasHolder;
	public ulong m_HolderClientId;
	public bool m_IsShotInFlight;
}

/// <summary>
/// リプレイ1フレーム分のプレイヤー姿勢・状態。
/// </summary>
public struct ReplayPlayerPose
{
	public bool m_IsActive;
	public ulong m_ClientId;
	public Vector3 m_Position;
	public Quaternion m_Rotation;
	public bool m_IsMoving;
}

/// <summary>
/// リプレイクリップ内でプレイヤー外見を再現するためのメタ情報。
/// </summary>
public struct ReplayPlayerMeta
{
	public bool m_IsValid;
	public ulong m_ClientId;
	public FixedString64Bytes m_PlayerName;
	public ETeamType m_Team;
	public ECharacterType m_CharacterType;
	public EHighlightColor m_HighlightColor;

	public LobbyPlayerData ToLobbyPlayerData()
	{
		return new LobbyPlayerData(
			m_ClientId,
			m_PlayerName,
			m_Team,
			m_CharacterType,
			m_HighlightColor,
			true);
	}
}

/// <summary>
/// リプレイの1サンプルフレーム。事前確保して再利用します。
/// </summary>
public sealed class ReplayFrameSlot
{
	public const int m_MaxPlayers = 4;

	public float m_Time;
	public ReplayBallPose m_Ball;
	public int m_PlayerCount;
	public readonly ReplayPlayerPose[] m_Players;

	public ReplayFrameSlot()
	{
		m_Players = new ReplayPlayerPose[m_MaxPlayers];
	}

	public void CopyFrom(ReplayFrameSlot source)
	{
		m_Time = source.m_Time;
		m_Ball = source.m_Ball;
		m_PlayerCount = source.m_PlayerCount;
		int count = Mathf.Min(source.m_PlayerCount, m_MaxPlayers);
		for (int i = 0; i < count; i++)
		{
			m_Players[i] = source.m_Players[i];
		}
	}
}

/// <summary>
/// 1ゴール分の切り出し済みリプレイクリップデータ。
/// </summary>
public sealed class ReplayClipData
{
	private readonly ReplayFrameSlot[] m_Frames;
	private readonly ReplayPlayerMeta[] m_PlayerMetas;

	public int FrameCount { get; private set; }
	public float Duration { get; private set; }
	public ETeamType ScoringTeam { get; private set; }
	public uint GoalSequence { get; private set; }
	public int PlayerMetaCount { get; private set; }
	public bool IsValid => FrameCount >= 2 && Duration > 0.05f;

	public ReplayClipData(int maxFrames)
	{
		int capacity = Mathf.Max(2, maxFrames);
		m_Frames = new ReplayFrameSlot[capacity];
		for (int i = 0; i < capacity; i++)
		{
			m_Frames[i] = new ReplayFrameSlot();
		}
		m_PlayerMetas = new ReplayPlayerMeta[ReplayFrameSlot.m_MaxPlayers];
	}

	public int Capacity => m_Frames.Length;

	public void Clear()
	{
		FrameCount = 0;
		Duration = 0f;
		ScoringTeam = ETeamType.None;
		GoalSequence = 0;
		PlayerMetaCount = 0;
	}

	public void BeginBuild(ETeamType scoringTeam, uint goalSequence, ReplayPlayerMeta[] metas, int metaCount)
	{
		FrameCount = 0;
		Duration = 0f;
		ScoringTeam = scoringTeam;
		GoalSequence = goalSequence;
		PlayerMetaCount = Mathf.Min(metaCount, m_PlayerMetas.Length);
		for (int i = 0; i < PlayerMetaCount; i++)
		{
			m_PlayerMetas[i] = metas[i];
		}
	}

	public void AppendFrame(ReplayFrameSlot source, float relativeTime)
	{
		if (FrameCount >= m_Frames.Length)
		{
			return;
		}

		ReplayFrameSlot target = m_Frames[FrameCount];
		target.CopyFrom(source);
		target.m_Time = relativeTime;
		Duration = relativeTime;
		FrameCount++;
	}

	public ReplayFrameSlot GetFrame(int index)
	{
		return m_Frames[index];
	}

	public ReplayPlayerMeta GetPlayerMeta(int index)
	{
		return m_PlayerMetas[index];
	}

	public void CopyFrom(ReplayClipData source)
	{
		if (source == null)
		{
			Clear();
			return;
		}

		ScoringTeam = source.ScoringTeam;
		GoalSequence = source.GoalSequence;
		Duration = source.Duration;
		PlayerMetaCount = Mathf.Min(source.PlayerMetaCount, m_PlayerMetas.Length);
		for (int i = 0; i < PlayerMetaCount; i++)
		{
			m_PlayerMetas[i] = source.m_PlayerMetas[i];
		}

		int copyFrames = Mathf.Min(source.FrameCount, m_Frames.Length);
		for (int i = 0; i < copyFrames; i++)
		{
			m_Frames[i].CopyFrom(source.m_Frames[i]);
		}
		FrameCount = copyFrames;
	}
}
