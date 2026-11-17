using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 1試合ごとのデバッグ用行動統計を、試合全体・チーム別(Red/Blue)・個人別の
/// 見やすい整形済みJSONとして個別ファイルへ保存するストア。
/// </summary>
public static class MatchDebugStatsStore
{
	private const string m_LogDirectoryName = "MatchStatsLogs";

	[Serializable]
	public sealed class MatchDebugLogData
	{
		public int m_MatchNumber;
		public string m_RecordedAt;
		public string m_WinnerTeam;
		public int m_RedScore;
		public int m_BlueScore;
		public GroupStatsSummary m_MatchTotal;
		public GroupStatsSummary m_RedTeamTotal;
		public GroupStatsSummary m_BlueTeamTotal;
		public PlayerDebugStatsEntry[] m_Players;
	}

	[Serializable]
	public sealed class GroupStatsSummary
	{
		public string m_GroupName;
		public int m_PlayerCount;
		public string m_BallThrowSummary;
		public int m_TotalThrowCount;
		public int m_LockOnPassCount;
		public float m_LockOnPassRatePercent;
		public int m_NormalThrowCount;
		public float m_NormalThrowRatePercent;
		public int m_GoalCount;
		public int m_AssistCount;
		public int m_PassCount;
		public int m_PassCutCount;
		public int m_SaveCount;
		public int m_StunCount;
		public float m_LongestGoalDistance;
		public float m_LongestPassDistance;
		public float m_BallHoldSeconds;
		public float m_EnemyTerritorySeconds;
		public float m_OwnTerritorySeconds;
		public float m_ObservedSeconds;
		public float m_FlightDistance;
		public int m_AirMoveCount;
		public float m_FuelConsumed;
		public float m_GroundWalkSeconds;
	}

	[Serializable]
	public sealed class PlayerDebugStatsEntry
	{
		public ulong m_ClientId;
		public string m_PlayerName;
		public string m_Team;
		public string m_CharacterType;
		public string m_HighlightColor;
		public string[] m_Awards;
		public string m_BallThrowSummary;
		public int m_TotalThrowCount;
		public int m_LockOnPassCount;
		public float m_LockOnPassRatePercent;
		public int m_NormalThrowCount;
		public float m_NormalThrowRatePercent;
		public PlayerMatchStats m_Stats;
	}

	/// <summary>
	/// 1試合分の統計を試合全体・Redチーム・Blueチーム・個人別に集計し、個別JSONファイルへ保存します。
	/// </summary>
	public static string SaveMatchLog(
		int matchNumber,
		int redScore,
		int blueScore,
		ETeamType winnerTeam,
		IReadOnlyList<PlayerMatchSummary> playerSummaries)
	{
		IReadOnlyList<PlayerMatchSummary> safeSummaries = playerSummaries ?? Array.Empty<PlayerMatchSummary>();

		var logData = new MatchDebugLogData
		{
			m_MatchNumber = Mathf.Max(1, matchNumber),
			m_RecordedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
			m_WinnerTeam = FormatWinnerLabel(winnerTeam),
			m_RedScore = redScore,
			m_BlueScore = blueScore,
			m_MatchTotal = BuildGroupSummary("MatchTotal", safeSummaries, ETeamType.None),
			m_RedTeamTotal = BuildGroupSummary("RedTeam", safeSummaries, ETeamType.Red),
			m_BlueTeamTotal = BuildGroupSummary("BlueTeam", safeSummaries, ETeamType.Blue),
			m_Players = BuildPlayerEntries(safeSummaries)
		};

		try
		{
			string dir = Path.Combine(Application.persistentDataPath, m_LogDirectoryName);
			if (!Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}

			string fileName = $"Match_{logData.m_MatchNumber:D4}_{DateTime.Now:yyyyMMdd_HHmmss}.json";
			string path = Path.Combine(dir, fileName);
			string json = JsonUtility.ToJson(logData, true);
			File.WriteAllText(path, json, Encoding.UTF8);

			Debug.Log(
				$"[MatchDebugStatsStore] 試合統計ログを保存しました: {path}\n" +
				$"全体投球: {logData.m_MatchTotal.m_BallThrowSummary} | " +
				$"Red投球: {logData.m_RedTeamTotal.m_BallThrowSummary} | " +
				$"Blue投球: {logData.m_BlueTeamTotal.m_BallThrowSummary}");

			return path;
		}
		catch (Exception ex)
		{
			Debug.LogWarning($"[MatchDebugStatsStore] 試合統計ログの保存に失敗しました: {ex.Message}");
			return string.Empty;
		}
	}

	private static GroupStatsSummary BuildGroupSummary(
		string groupName,
		IReadOnlyList<PlayerMatchSummary> summaries,
		ETeamType filterTeam)
	{
		var summary = new GroupStatsSummary
		{
			m_GroupName = groupName
		};

		for (int i = 0; i < summaries.Count; i++)
		{
			PlayerMatchSummary item = summaries[i];
			if (item == null)
			{
				continue;
			}

			ETeamType team = item.m_Stats.m_Team;
			if (filterTeam == ETeamType.None)
			{
				if (team != ETeamType.Red && team != ETeamType.Blue)
				{
					continue;
				}
			}
			else if (team != filterTeam)
			{
				continue;
			}

			PlayerMatchStats stat = item.m_Stats;
			summary.m_PlayerCount++;
			summary.m_LockOnPassCount += Mathf.Max(0, stat.m_LockOnPassCount);
			summary.m_NormalThrowCount += Mathf.Max(0, stat.m_NormalThrowCount);
			summary.m_GoalCount += Mathf.Max(0, stat.m_GoalCount);
			summary.m_AssistCount += Mathf.Max(0, stat.m_AssistCount);
			summary.m_PassCount += Mathf.Max(0, stat.m_PassCount);
			summary.m_PassCutCount += Mathf.Max(0, stat.m_PassCutCount);
			summary.m_SaveCount += Mathf.Max(0, stat.m_SaveCount);
			summary.m_StunCount += Mathf.Max(0, stat.m_StunCount);
			summary.m_LongestGoalDistance = Mathf.Max(summary.m_LongestGoalDistance, stat.m_LongestGoalDistance);
			summary.m_LongestPassDistance = Mathf.Max(summary.m_LongestPassDistance, stat.m_LongestPassDistance);
			summary.m_BallHoldSeconds += Mathf.Max(0f, stat.m_BallHoldSeconds);
			summary.m_EnemyTerritorySeconds += Mathf.Max(0f, stat.m_EnemyTerritorySeconds);
			summary.m_OwnTerritorySeconds += Mathf.Max(0f, stat.m_OwnTerritorySeconds);
			summary.m_ObservedSeconds += Mathf.Max(0f, stat.m_ObservedSeconds);
			summary.m_FlightDistance += Mathf.Max(0f, stat.m_FlightDistance);
			summary.m_AirMoveCount += Mathf.Max(0, stat.m_AirMoveCount);
			summary.m_FuelConsumed += Mathf.Max(0f, stat.m_FuelConsumed);
			summary.m_GroundWalkSeconds += Mathf.Max(0f, stat.m_GroundWalkSeconds);
		}

		BallThrowStatsFormatter.CalculateRates(
			summary.m_LockOnPassCount,
			summary.m_NormalThrowCount,
			out int totalThrows,
			out float lockOnRate,
			out float normalRate);

		summary.m_TotalThrowCount = totalThrows;
		summary.m_LockOnPassRatePercent = Round1(lockOnRate);
		summary.m_NormalThrowRatePercent = Round1(normalRate);
		summary.m_BallThrowSummary = BallThrowStatsFormatter.FormatBreakdown(
			summary.m_LockOnPassCount,
			summary.m_NormalThrowCount);

		summary.m_LongestGoalDistance = Round1(summary.m_LongestGoalDistance);
		summary.m_LongestPassDistance = Round1(summary.m_LongestPassDistance);
		summary.m_BallHoldSeconds = Round1(summary.m_BallHoldSeconds);
		summary.m_EnemyTerritorySeconds = Round1(summary.m_EnemyTerritorySeconds);
		summary.m_OwnTerritorySeconds = Round1(summary.m_OwnTerritorySeconds);
		summary.m_ObservedSeconds = Round1(summary.m_ObservedSeconds);
		summary.m_FlightDistance = Round1(summary.m_FlightDistance);
		summary.m_FuelConsumed = Round1(summary.m_FuelConsumed);
		summary.m_GroundWalkSeconds = Round1(summary.m_GroundWalkSeconds);

		return summary;
	}

	private static PlayerDebugStatsEntry[] BuildPlayerEntries(IReadOnlyList<PlayerMatchSummary> summaries)
	{
		var entries = new PlayerDebugStatsEntry[summaries.Count];
		for (int i = 0; i < summaries.Count; i++)
		{
			PlayerMatchSummary summary = summaries[i];
			if (summary == null)
			{
				entries[i] = new PlayerDebugStatsEntry();
				continue;
			}

			PlayerMatchStats stat = summary.m_Stats;
			BallThrowStatsFormatter.CalculateRates(
				stat.m_LockOnPassCount,
				stat.m_NormalThrowCount,
				out int totalThrows,
				out float lockOnRate,
				out float normalRate);

			int awardCount = summary.m_Awards != null ? summary.m_Awards.Length : 0;
			var awardLabels = new string[awardCount];
			for (int a = 0; a < awardCount; a++)
			{
				awardLabels[a] = MatchAwardEvaluator.FormatAwardLabel(summary.m_Awards[a]);
			}

			entries[i] = new PlayerDebugStatsEntry
			{
				m_ClientId = stat.m_ClientId,
				m_PlayerName = summary.m_PlayerData.PlayerName.ToString(),
				m_Team = stat.m_Team.ToString(),
				m_CharacterType = summary.m_PlayerData.CharacterType.ToString(),
				m_HighlightColor = summary.m_PlayerData.HighlightColor.ToString(),
				m_Awards = awardLabels,
				m_BallThrowSummary = BallThrowStatsFormatter.FormatBreakdown(
					stat.m_LockOnPassCount,
					stat.m_NormalThrowCount),
				m_TotalThrowCount = totalThrows,
				m_LockOnPassCount = stat.m_LockOnPassCount,
				m_LockOnPassRatePercent = Round1(lockOnRate),
				m_NormalThrowCount = stat.m_NormalThrowCount,
				m_NormalThrowRatePercent = Round1(normalRate),
				m_Stats = stat
			};
		}

		return entries;
	}

	private static string FormatWinnerLabel(ETeamType winnerTeam)
	{
		return winnerTeam switch
		{
			ETeamType.Red => "Red",
			ETeamType.Blue => "Blue",
			_ => "Draw",
		};
	}

	private static float Round1(float value)
	{
		return Mathf.Round(value * 10f) / 10f;
	}
}
