using System;
using Unity.Collections;
using Unity.Netcode;

/// <summary>
/// 試合後の個人表彰(称号)対象となる評価項目。
/// </summary>
public enum EMatchAwardCategory : byte
{
	Goals = 0,
	Assists = 1,
	Saves = 2,
	LongestGoal = 3,
	PassCuts = 4,
	LongestPass = 5,
	Passes = 6,
	BallHoldTime = 7,
	ObservedTime = 8,
	FlightDistance = 9,
	EnemyTerritoryTime = 10,
	OwnTerritoryTime = 11,
	AirMoveCount = 12,
	FuelConsumed = 13,
	GroundWalkTime = 14,
	EnemyStuns = 15,
	AllyPassLockOns = 16,
}

/// <summary>
/// 1試合における1プレイヤーの行動統計データ。
/// </summary>
public struct PlayerMatchStats : INetworkSerializable
{
	public ulong m_ClientId;
	public ETeamType m_Team;
	public float m_BallHoldSeconds;
	public float m_EnemyTerritorySeconds;
	public float m_OwnTerritorySeconds;
	public int m_PassCutCount;
	public int m_SaveCount;
	public float m_FlightDistance;
	public int m_AirMoveCount;
	public float m_FuelConsumed;
	public int m_PassCount;
	public int m_GoalCount;
	public int m_AssistCount;
	public float m_GroundWalkSeconds;
	public float m_ObservedSeconds;
	public float m_LongestPassDistance;
	public float m_LongestGoalDistance;
	public int m_StunCount;
	public int m_LockOnPassCount;
	public int m_NormalThrowCount;

	public float GetMetricValue(EMatchAwardCategory category)
	{
		return category switch
		{
			EMatchAwardCategory.Goals => m_GoalCount,
			EMatchAwardCategory.Assists => m_AssistCount,
			EMatchAwardCategory.Saves => m_SaveCount,
			EMatchAwardCategory.LongestGoal => m_LongestGoalDistance,
			EMatchAwardCategory.PassCuts => m_PassCutCount,
			EMatchAwardCategory.LongestPass => m_LongestPassDistance,
			EMatchAwardCategory.Passes => m_PassCount,
			EMatchAwardCategory.BallHoldTime => m_BallHoldSeconds,
			EMatchAwardCategory.ObservedTime => m_ObservedSeconds,
			EMatchAwardCategory.FlightDistance => m_FlightDistance,
			EMatchAwardCategory.EnemyTerritoryTime => m_EnemyTerritorySeconds,
			EMatchAwardCategory.OwnTerritoryTime => m_OwnTerritorySeconds,
			EMatchAwardCategory.AirMoveCount => m_AirMoveCount,
			EMatchAwardCategory.FuelConsumed => m_FuelConsumed,
			EMatchAwardCategory.GroundWalkTime => m_GroundWalkSeconds,
			EMatchAwardCategory.EnemyStuns => m_StunCount,
			EMatchAwardCategory.AllyPassLockOns => m_LockOnPassCount,
			_ => 0f,
		};
	}

	public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
	{
		serializer.SerializeValue(ref m_ClientId);

		byte teamByte = (byte)m_Team;
		serializer.SerializeValue(ref teamByte);
		if (serializer.IsReader)
		{
			m_Team = (ETeamType)teamByte;
		}

		serializer.SerializeValue(ref m_BallHoldSeconds);
		serializer.SerializeValue(ref m_EnemyTerritorySeconds);
		serializer.SerializeValue(ref m_OwnTerritorySeconds);
		serializer.SerializeValue(ref m_PassCutCount);
		serializer.SerializeValue(ref m_SaveCount);
		serializer.SerializeValue(ref m_FlightDistance);
		serializer.SerializeValue(ref m_AirMoveCount);
		serializer.SerializeValue(ref m_FuelConsumed);
		serializer.SerializeValue(ref m_PassCount);
		serializer.SerializeValue(ref m_GoalCount);
		serializer.SerializeValue(ref m_AssistCount);
		serializer.SerializeValue(ref m_GroundWalkSeconds);
		serializer.SerializeValue(ref m_ObservedSeconds);
		serializer.SerializeValue(ref m_LongestPassDistance);
		serializer.SerializeValue(ref m_LongestGoalDistance);
		serializer.SerializeValue(ref m_StunCount);
		serializer.SerializeValue(ref m_LockOnPassCount);
		serializer.SerializeValue(ref m_NormalThrowCount);
	}
}

/// <summary>
/// プレイヤーへ授与された1つの称号データ。
/// </summary>
public struct PlayerAwardEntry : INetworkSerializable
{
	public EMatchAwardCategory m_Category;
	public byte m_Rank;
	public float m_Value;

	public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
	{
		byte catByte = (byte)m_Category;
		serializer.SerializeValue(ref catByte);
		if (serializer.IsReader)
		{
			m_Category = (EMatchAwardCategory)catByte;
		}

		serializer.SerializeValue(ref m_Rank);
		serializer.SerializeValue(ref m_Value);
	}
}

/// <summary>
/// クライアント(Owner)からサーバーへ定期・最終送信する行動統計スナップショット。
/// </summary>
public struct ClientStatsSyncPayload : INetworkSerializable
{
	public ulong m_SenderClientId;
	public float m_FlightDistance;
	public int m_AirMoveCount;
	public float m_FuelConsumed;
	public float m_GroundWalkSeconds;
	public int m_ObservedEntryCount;
	public ulong m_ObservedTarget0;
	public float m_ObservedSeconds0;
	public ulong m_ObservedTarget1;
	public float m_ObservedSeconds1;
	public ulong m_ObservedTarget2;
	public float m_ObservedSeconds2;
	public ulong m_ObservedTarget3;
	public float m_ObservedSeconds3;

	public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
	{
		serializer.SerializeValue(ref m_SenderClientId);
		serializer.SerializeValue(ref m_FlightDistance);
		serializer.SerializeValue(ref m_AirMoveCount);
		serializer.SerializeValue(ref m_FuelConsumed);
		serializer.SerializeValue(ref m_GroundWalkSeconds);
		serializer.SerializeValue(ref m_ObservedEntryCount);
		serializer.SerializeValue(ref m_ObservedTarget0);
		serializer.SerializeValue(ref m_ObservedSeconds0);
		serializer.SerializeValue(ref m_ObservedTarget1);
		serializer.SerializeValue(ref m_ObservedSeconds1);
		serializer.SerializeValue(ref m_ObservedTarget2);
		serializer.SerializeValue(ref m_ObservedSeconds2);
		serializer.SerializeValue(ref m_ObservedTarget3);
		serializer.SerializeValue(ref m_ObservedSeconds3);
	}
}

/// <summary>
/// リザルト画面へ引き継ぐプレイヤー1人分の確定サマリー情報。
/// </summary>
public sealed class PlayerMatchSummary
{
	public LobbyPlayerData m_PlayerData;
	public PlayerMatchStats m_Stats;
	public PlayerAwardEntry[] m_Awards = Array.Empty<PlayerAwardEntry>();
}

/// <summary>
/// 展示用の累計統計をクライアント側にも共有するための軽量スナップショット。
/// </summary>
public struct ExhibitionStatsSnapshot : INetworkSerializable
{
	public int m_TotalMatches;
	public int m_TotalGoals;
	public int m_RedTotalGoals;
	public int m_BlueTotalGoals;
	public int m_MaxTeamScoreInSingleMatch;
	public int m_MaxTotalGoalsInSingleMatch;
	public int m_MaxPlayerGoalsInSingleMatch;
	public FixedString64Bytes m_MaxPlayerGoalsHolderName;
	public float m_LongestGoalDistance;
	public float m_LongestPassDistance;
	public float m_TotalFlightDistance;
	public int m_TotalAirMoveCount;
	public int m_TotalPassCount;
	public int m_TotalPassCutCount;
	public int m_TotalSaveCount;
	public float m_TotalFuelConsumed;
	public int m_TotalStunCount;
	public int m_TotalLockOnPassCount;
	public int m_TotalNormalThrowCount;

	public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
	{
		serializer.SerializeValue(ref m_TotalMatches);
		serializer.SerializeValue(ref m_TotalGoals);
		serializer.SerializeValue(ref m_RedTotalGoals);
		serializer.SerializeValue(ref m_BlueTotalGoals);
		serializer.SerializeValue(ref m_MaxTeamScoreInSingleMatch);
		serializer.SerializeValue(ref m_MaxTotalGoalsInSingleMatch);
		serializer.SerializeValue(ref m_MaxPlayerGoalsInSingleMatch);
		serializer.SerializeValue(ref m_MaxPlayerGoalsHolderName);
		serializer.SerializeValue(ref m_LongestGoalDistance);
		serializer.SerializeValue(ref m_LongestPassDistance);
		serializer.SerializeValue(ref m_TotalFlightDistance);
		serializer.SerializeValue(ref m_TotalAirMoveCount);
		serializer.SerializeValue(ref m_TotalPassCount);
		serializer.SerializeValue(ref m_TotalPassCutCount);
		serializer.SerializeValue(ref m_TotalSaveCount);
		serializer.SerializeValue(ref m_TotalFuelConsumed);
		serializer.SerializeValue(ref m_TotalStunCount);
		serializer.SerializeValue(ref m_TotalLockOnPassCount);
		serializer.SerializeValue(ref m_TotalNormalThrowCount);
	}
}

/// <summary>
/// ボール投球回数(ロックオンパス / 通常投げ)と確率の書式整形を提供します。
/// </summary>
public static class BallThrowStatsFormatter
{
	/// <summary>
	/// ロックオンパス回数と通常投げ回数から総投球数とそれぞれの確率(%)を算出します。
	/// </summary>
	public static void CalculateRates(
		int lockOnPassCount,
		int normalThrowCount,
		out int totalThrows,
		out float lockOnRatePercent,
		out float normalRatePercent)
	{
		int safeLockOn = lockOnPassCount > 0 ? lockOnPassCount : 0;
		int safeNormal = normalThrowCount > 0 ? normalThrowCount : 0;
		totalThrows = safeLockOn + safeNormal;
		if (totalThrows <= 0)
		{
			lockOnRatePercent = 0f;
			normalRatePercent = 0f;
			return;
		}

		lockOnRatePercent = (float)safeLockOn / totalThrows * 100f;
		normalRatePercent = (float)safeNormal / totalThrows * 100f;
	}

	/// <summary>
	/// 「10回 (ロックオン: 7回(70.0%) / 通常投げ: 3回(30.0%))」形式の文字列を生成します。
	/// </summary>
	public static string FormatBreakdown(int lockOnPassCount, int normalThrowCount)
	{
		CalculateRates(
			lockOnPassCount,
			normalThrowCount,
			out int totalThrows,
			out float lockOnRate,
			out float normalRate);

		int safeLockOn = lockOnPassCount > 0 ? lockOnPassCount : 0;
		int safeNormal = normalThrowCount > 0 ? normalThrowCount : 0;
		return $"{totalThrows}回 (ロックオン: {safeLockOn}回({lockOnRate:F1}%) / 通常投げ: {safeNormal}回({normalRate:F1}%))";
	}
}
