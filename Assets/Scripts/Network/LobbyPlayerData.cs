using System;
using Unity.Netcode;

/// <summary>
/// ロビーに参加しているプレイヤーの同期用データ構造体。
/// クライアント間での端末情報・チーム情報・準備状態の同期を担当します。
/// </summary>
public struct LobbyPlayerData : INetworkSerializable, IEquatable<LobbyPlayerData>
{
	private ulong m_ClientId;
	private byte m_TeamId;
	private bool m_IsReady;

	public ulong ClientId
	{
		get => m_ClientId;
		set => m_ClientId = value;
	}

	public byte TeamId
	{
		get => m_TeamId;
		set => m_TeamId = value;
	}

	public bool IsReady
	{
		get => m_IsReady;
		set => m_IsReady = value;
	}

	public LobbyPlayerData(ulong clientId, byte teamId = 0, bool isReady = false)
	{
		m_ClientId = clientId;
		m_TeamId = teamId;
		m_IsReady = isReady;
	}

	public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
	{
		serializer.SerializeValue(ref m_ClientId);
		serializer.SerializeValue(ref m_TeamId);
		serializer.SerializeValue(ref m_IsReady);
	}

	public bool Equals(LobbyPlayerData other)
	{
		return m_ClientId == other.m_ClientId && m_TeamId == other.m_TeamId && m_IsReady == other.m_IsReady;
	}

	public override bool Equals(object obj)
	{
		return obj is LobbyPlayerData other && Equals(other);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(m_ClientId, m_TeamId, m_IsReady);
	}
}
