using System;
using Unity.Netcode;

/// <summary>
/// ロビーに参加しているプレイヤーの同期用データ構造体。
/// クライアント間での端末情報・チーム情報・準備状態の同期を担当します。
/// </summary>
public struct LobbyPlayerData : INetworkSerializable, IEquatable<LobbyPlayerData>
{
	public ulong ClientId;
	public byte TeamId;
	public bool IsReady;

	public LobbyPlayerData(ulong clientId, byte teamId = 0, bool isReady = false)
	{
		ClientId = clientId;
		TeamId = teamId;
		IsReady = isReady;
	}

	public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
	{
		serializer.SerializeValue(ref ClientId);
		serializer.SerializeValue(ref TeamId);
		serializer.SerializeValue(ref IsReady);
	}

	public bool Equals(LobbyPlayerData other)
	{
		return ClientId == other.ClientId && TeamId == other.TeamId && IsReady == other.IsReady;
	}

	public override bool Equals(object obj)
	{
		return obj is LobbyPlayerData other && Equals(other);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(ClientId, TeamId, IsReady);
	}
}
