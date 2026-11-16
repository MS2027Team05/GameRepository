using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 展示会全体を通じた累計統計・歴代レコードをJSONでローカル永続化・管理するストア。
/// 観戦モニターや展示ボードへの掲出データ提供、F1スタッフメニューからの確認・リセットに対応します。
/// </summary>
public static class ExhibitionStatsStore
{
	[Serializable]
	public sealed class ExhibitionStatsData
	{
		public int m_TotalMatches;
		public int m_TotalGoals;
		public int m_RedTotalGoals;
		public int m_BlueTotalGoals;
		public int m_MaxTeamScoreInSingleMatch;
		public int m_MaxTotalGoalsInSingleMatch;
		public int m_MaxPlayerGoalsInSingleMatch;
		public string m_MaxPlayerGoalsHolderName;
		public float m_LongestGoalDistance;
		public float m_LongestPassDistance;
		public float m_TotalFlightDistance;
		public int m_TotalAirMoveCount;
		public int m_TotalPassCount;
		public int m_TotalPassCutCount;
		public int m_TotalSaveCount;
		public float m_TotalFuelConsumed;
	}

	private const string m_FileName = "ExhibitionStats.json";
	private static ExhibitionStatsData m_CachedData;
	private static bool m_IsLoaded;

	private static string FilePath => Path.Combine(Application.persistentDataPath, m_FileName);

	/// <summary>
	/// 現在の累計統計データを取得します(未読込時はディスクから読み込みます)。
	/// </summary>
	public static ExhibitionStatsData GetData()
	{
		EnsureLoaded();
		return m_CachedData;
	}

	/// <summary>
	/// 1試合分の結果を累計統計へ加算し、JSONファイルへ永続化します。
	/// </summary>
	public static ExhibitionStatsSnapshot RecordMatchResult(
		int redScore,
		int blueScore,
		IReadOnlyList<PlayerMatchSummary> playerSummaries)
	{
		EnsureLoaded();

		m_CachedData.m_TotalMatches++;
		int matchGoals = Mathf.Max(0, redScore) + Mathf.Max(0, blueScore);
		m_CachedData.m_RedTotalGoals += Mathf.Max(0, redScore);
		m_CachedData.m_BlueTotalGoals += Mathf.Max(0, blueScore);
		m_CachedData.m_TotalGoals += matchGoals;

		int highTeamScore = Mathf.Max(redScore, blueScore);
		if (highTeamScore > m_CachedData.m_MaxTeamScoreInSingleMatch)
		{
			m_CachedData.m_MaxTeamScoreInSingleMatch = highTeamScore;
		}

		if (matchGoals > m_CachedData.m_MaxTotalGoalsInSingleMatch)
		{
			m_CachedData.m_MaxTotalGoalsInSingleMatch = matchGoals;
		}

		if (playerSummaries != null)
		{
			for (int i = 0; i < playerSummaries.Count; i++)
			{
				PlayerMatchSummary summary = playerSummaries[i];
				if (summary == null)
				{
					continue;
				}

				PlayerMatchStats stat = summary.m_Stats;
				if (stat.m_GoalCount > m_CachedData.m_MaxPlayerGoalsInSingleMatch)
				{
					m_CachedData.m_MaxPlayerGoalsInSingleMatch = stat.m_GoalCount;
					m_CachedData.m_MaxPlayerGoalsHolderName = summary.m_PlayerData.PlayerName.ToString();
				}

				if (stat.m_LongestGoalDistance > m_CachedData.m_LongestGoalDistance)
				{
					m_CachedData.m_LongestGoalDistance = stat.m_LongestGoalDistance;
				}

				if (stat.m_LongestPassDistance > m_CachedData.m_LongestPassDistance)
				{
					m_CachedData.m_LongestPassDistance = stat.m_LongestPassDistance;
				}

				m_CachedData.m_TotalFlightDistance += Mathf.Max(0f, stat.m_FlightDistance);
				m_CachedData.m_TotalAirMoveCount += Mathf.Max(0, stat.m_AirMoveCount);
				m_CachedData.m_TotalPassCount += Mathf.Max(0, stat.m_PassCount);
				m_CachedData.m_TotalPassCutCount += Mathf.Max(0, stat.m_PassCutCount);
				m_CachedData.m_TotalSaveCount += Mathf.Max(0, stat.m_SaveCount);
				m_CachedData.m_TotalFuelConsumed += Mathf.Max(0f, stat.m_FuelConsumed);
			}
		}

		Save();
		return ToSnapshot();
	}

	/// <summary>
	/// ネットワーク送信用のスナップショットを生成します。
	/// </summary>
	public static ExhibitionStatsSnapshot ToSnapshot()
	{
		EnsureLoaded();
		return new ExhibitionStatsSnapshot
		{
			m_TotalMatches = m_CachedData.m_TotalMatches,
			m_TotalGoals = m_CachedData.m_TotalGoals,
			m_RedTotalGoals = m_CachedData.m_RedTotalGoals,
			m_BlueTotalGoals = m_CachedData.m_BlueTotalGoals,
			m_MaxTeamScoreInSingleMatch = m_CachedData.m_MaxTeamScoreInSingleMatch,
			m_MaxTotalGoalsInSingleMatch = m_CachedData.m_MaxTotalGoalsInSingleMatch,
			m_MaxPlayerGoalsInSingleMatch = m_CachedData.m_MaxPlayerGoalsInSingleMatch,
			m_MaxPlayerGoalsHolderName = string.IsNullOrEmpty(m_CachedData.m_MaxPlayerGoalsHolderName)
				? "-"
				: m_CachedData.m_MaxPlayerGoalsHolderName,
			m_LongestGoalDistance = m_CachedData.m_LongestGoalDistance,
			m_LongestPassDistance = m_CachedData.m_LongestPassDistance,
			m_TotalFlightDistance = m_CachedData.m_TotalFlightDistance,
			m_TotalAirMoveCount = m_CachedData.m_TotalAirMoveCount,
			m_TotalPassCount = m_CachedData.m_TotalPassCount,
			m_TotalPassCutCount = m_CachedData.m_TotalPassCutCount,
			m_TotalSaveCount = m_CachedData.m_TotalSaveCount,
			m_TotalFuelConsumed = m_CachedData.m_TotalFuelConsumed
		};
	}

	/// <summary>
	/// 展示用累計統計をすべて初期化します。
	/// </summary>
	public static void ResetStats()
	{
		m_CachedData = new ExhibitionStatsData();
		m_IsLoaded = true;
		Save();
	}

	/// <summary>
	/// 展示ボード・スタッフメニュー・リザルト画面向けの統計一覧テキストを追記します。
	/// </summary>
	public static void AppendSummaryText(StringBuilder builder, in ExhibitionStatsSnapshot snapshot)
	{
		if (builder == null)
		{
			return;
		}

		string holder = snapshot.m_MaxPlayerGoalsHolderName.Length > 0
			? snapshot.m_MaxPlayerGoalsHolderName.ToString()
			: "-";

		builder.AppendLine("【展示累計統計レコード】");
		builder.AppendLine($"総試合数: {snapshot.m_TotalMatches}試合 | 総ゴール: {snapshot.m_TotalGoals}点 (Red {snapshot.m_RedTotalGoals} / Blue {snapshot.m_BlueTotalGoals})");
		builder.AppendLine($"1試合最多得点(チーム): {snapshot.m_MaxTeamScoreInSingleMatch}点 | 個人最多ゴール: {snapshot.m_MaxPlayerGoalsInSingleMatch}点 ({holder})");
		builder.AppendLine($"最長ゴール距離: {snapshot.m_LongestGoalDistance:F1}m | 最長パス距離: {snapshot.m_LongestPassDistance:F1}m");
		builder.AppendLine($"累計飛行距離: {snapshot.m_TotalFlightDistance:F1}m | 累計移動回数: {snapshot.m_TotalAirMoveCount}回");
		builder.AppendLine($"累計パス: {snapshot.m_TotalPassCount}回 | パスカット: {snapshot.m_TotalPassCutCount}回 | セーブ: {snapshot.m_TotalSaveCount}回");
	}

	private static void EnsureLoaded()
	{
		if (m_IsLoaded && m_CachedData != null)
		{
			return;
		}

		m_IsLoaded = true;
		try
		{
			string path = FilePath;
			if (File.Exists(path))
			{
				string json = File.ReadAllText(path, Encoding.UTF8);
				ExhibitionStatsData loaded = JsonUtility.FromJson<ExhibitionStatsData>(json);
				if (loaded != null)
				{
					m_CachedData = loaded;
					return;
				}
			}
		}
		catch (Exception ex)
		{
			Debug.LogWarning($"[ExhibitionStatsStore] 統計データの読み込みに失敗しました: {ex.Message}");
		}

		m_CachedData = new ExhibitionStatsData();
	}

	private static void Save()
	{
		if (m_CachedData == null)
		{
			return;
		}

		try
		{
			string path = FilePath;
			string dir = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}

			string json = JsonUtility.ToJson(m_CachedData, true);
			File.WriteAllText(path, json, Encoding.UTF8);
		}
		catch (Exception ex)
		{
			Debug.LogWarning($"[ExhibitionStatsStore] 統計データの保存に失敗しました: {ex.Message}");
		}
	}
}
