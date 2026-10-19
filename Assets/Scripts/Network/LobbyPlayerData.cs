using System;
using Unity.Collections;
using Unity.Netcode;

/// <summary>
/// 所属チーム種別。
/// </summary>
public enum ETeamType : byte
{
	None = 0,
	Red = 1,
	Blue = 2,
}

/// <summary>
/// キャラクターモデル種別。
/// </summary>
public enum ECharacterType : byte
{
	ModelA = 0,
	ModelB = 1,
}

/// <summary>
/// ハイライトカラー種別 (マテリアル適用用のプリセットカラー)。
/// </summary>
public enum EHighlightColor : byte
{
	Default = 0,
	Cyan = 1,
	Lime = 2,
	Yellow = 3,
	Purple = 4,
	Orange = 5,
}

/// <summary>
/// ロビーに参加しているプレイヤーの同期用データ構造体。
/// クライアント間での端末情報・名前・チーム情報・キャラメイク情報・準備状態の同期を担当します。
/// </summary>
public struct LobbyPlayerData : INetworkSerializable, IEquatable<LobbyPlayerData>
{
	private ulong m_ClientId;
	private FixedString32Bytes m_PlayerName;
	private ETeamType m_Team;
	private ECharacterType m_CharacterType;
	private EHighlightColor m_HighlightColor;
	private bool m_IsReady;

	public ulong ClientId
	{
		get => m_ClientId;
		set => m_ClientId = value;
	}

	public FixedString32Bytes PlayerName
	{
		get => m_PlayerName;
		set => m_PlayerName = value;
	}

	public ETeamType Team
	{
		get => m_Team;
		set => m_Team = value;
	}

	public ECharacterType CharacterType
	{
		get => m_CharacterType;
		set => m_CharacterType = value;
	}

	public EHighlightColor HighlightColor
	{
		get => m_HighlightColor;
		set => m_HighlightColor = value;
	}

	public bool IsReady
	{
		get => m_IsReady;
		set => m_IsReady = value;
	}

	public LobbyPlayerData(
		ulong clientId,
		FixedString32Bytes playerName,
		ETeamType team = ETeamType.None,
		ECharacterType characterType = ECharacterType.ModelA,
		EHighlightColor highlightColor = EHighlightColor.Default,
		bool isReady = false)
	{
		m_ClientId = clientId;
		m_PlayerName = playerName;
		m_Team = team;
		m_CharacterType = characterType;
		m_HighlightColor = highlightColor;
		m_IsReady = isReady;
	}

	public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
	{
		serializer.SerializeValue(ref m_ClientId);
		serializer.SerializeValue(ref m_PlayerName);

		byte teamByte = (byte)m_Team;
		serializer.SerializeValue(ref teamByte);
		if (serializer.IsReader)
		{
			m_Team = (ETeamType)teamByte;
		}

		byte charByte = (byte)m_CharacterType;
		serializer.SerializeValue(ref charByte);
		if (serializer.IsReader)
		{
			m_CharacterType = (ECharacterType)charByte;
		}

		byte colorByte = (byte)m_HighlightColor;
		serializer.SerializeValue(ref colorByte);
		if (serializer.IsReader)
		{
			m_HighlightColor = (EHighlightColor)colorByte;
		}

		serializer.SerializeValue(ref m_IsReady);
	}

	public bool Equals(LobbyPlayerData other)
	{
		return m_ClientId == other.m_ClientId &&
			m_PlayerName.Equals(other.m_PlayerName) &&
			m_Team == other.m_Team &&
			m_CharacterType == other.m_CharacterType &&
			m_HighlightColor == other.m_HighlightColor &&
			m_IsReady == other.m_IsReady;
	}

	public override bool Equals(object obj)
	{
		return obj is LobbyPlayerData other && Equals(other);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(m_ClientId, m_PlayerName, m_Team, m_CharacterType, m_HighlightColor, m_IsReady);
	}
}

