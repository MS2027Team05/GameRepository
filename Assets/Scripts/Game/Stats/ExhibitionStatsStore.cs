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
	public sealed class CharacterSelectionCounts
	{
		public int m_Man;
		public int m_Woman;
	}

	[Serializable]
	public sealed class ColorSelectionCounts
	{
		public int m_Black;
		public int m_Blue;
		public int m_DeepBlue;
		public int m_DeepRed;
		public int m_Green;
		public int m_LightBlue;
		public int m_LightGreen;
		public int m_Orange;
		public int m_Purple;
		public int m_Red;
		public int m_White;
		public int m_Yellow;
	}

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
		public int m_TotalStunCount;
		public int m_TotalLockOnPassCount;
		public int m_TotalNormalThrowCount;
		public string m_BallThrowSummary;
		public CharacterSelectionCounts m_CharacterSelections = new CharacterSelectionCounts();
		public ColorSelectionCounts m_ColorSelections = new ColorSelectionCounts();
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
				m_CachedData.m_TotalStunCount += Mathf.Max(0, stat.m_StunCount);
				m_CachedData.m_TotalLockOnPassCount += Mathf.Max(0, stat.m_LockOnPassCount);
				m_CachedData.m_TotalNormalThrowCount += Mathf.Max(0, stat.m_NormalThrowCount);
			}
		}

		m_CachedData.m_BallThrowSummary = BallThrowStatsFormatter.FormatBreakdown(
			m_CachedData.m_TotalLockOnPassCount,
			m_CachedData.m_TotalNormalThrowCount);

		Save();
		return ToSnapshot();
	}

	/// <summary>
	/// ロビーから試合へ出発する時点で、各参加プレイヤーが選択していたキャラクター・カラーを累計加算して保存します。
	/// </summary>
	public static void RecordLobbySelections(IReadOnlyList<LobbyPlayerData> players)
	{
		if (players == null || players.Count == 0)
		{
			return;
		}

		EnsureLoaded();
		EnsureSelectionContainers(m_CachedData);

		bool changed = false;
		for (int i = 0; i < players.Count; i++)
		{
			LobbyPlayerData player = players[i];
			if (player.Team != ETeamType.Red && player.Team != ETeamType.Blue)
			{
				continue;
			}

			IncrementCharacterSelection(m_CachedData.m_CharacterSelections, player.CharacterType);
			IncrementColorSelection(m_CachedData.m_ColorSelections, player.HighlightColor);
			changed = true;
		}

		if (changed)
		{
			Save();
		}
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
			m_TotalFuelConsumed = m_CachedData.m_TotalFuelConsumed,
			m_TotalStunCount = m_CachedData.m_TotalStunCount,
			m_TotalLockOnPassCount = m_CachedData.m_TotalLockOnPassCount,
			m_TotalNormalThrowCount = m_CachedData.m_TotalNormalThrowCount
		};
	}

	/// <summary>
	/// 展示用累計統計をすべて初期化します。
	/// </summary>
	public static void ResetStats()
	{
		m_CachedData = new ExhibitionStatsData();
		EnsureSelectionContainers(m_CachedData);
		m_CachedData.m_BallThrowSummary = BallThrowStatsFormatter.FormatBreakdown(0, 0);
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

		EnsureLoaded();
		EnsureSelectionContainers(m_CachedData);

		string holder = snapshot.m_MaxPlayerGoalsHolderName.Length > 0
			? snapshot.m_MaxPlayerGoalsHolderName.ToString()
			: "-";
		string throwBreakdown = BallThrowStatsFormatter.FormatBreakdown(
			snapshot.m_TotalLockOnPassCount,
			snapshot.m_TotalNormalThrowCount);

		builder.AppendLine("【展示累計統計レコード】");
		builder.AppendLine($"総試合数: {snapshot.m_TotalMatches}試合 | 総ゴール: {snapshot.m_TotalGoals}点 (Red {snapshot.m_RedTotalGoals} / Blue {snapshot.m_BlueTotalGoals})");
		builder.AppendLine($"1試合最多得点(チーム): {snapshot.m_MaxTeamScoreInSingleMatch}点 | 個人最多ゴール: {snapshot.m_MaxPlayerGoalsInSingleMatch}点 ({holder})");
		builder.AppendLine($"最長ゴール距離: {snapshot.m_LongestGoalDistance:F1}m | 最長パス距離: {snapshot.m_LongestPassDistance:F1}m");
		builder.AppendLine($"累計飛行距離: {snapshot.m_TotalFlightDistance:F1}m | 累計移動回数: {snapshot.m_TotalAirMoveCount}回");
		builder.AppendLine($"累計パス: {snapshot.m_TotalPassCount}回 | パスカット: {snapshot.m_TotalPassCutCount}回 | セーブ: {snapshot.m_TotalSaveCount}回 | スタン: {snapshot.m_TotalStunCount}回");
		builder.AppendLine($"累計投球数: {throwBreakdown}");

		CharacterSelectionCounts chars = m_CachedData.m_CharacterSelections;
		builder.AppendLine($"累計キャラ選択: Man {chars.m_Man}回 / Woman {chars.m_Woman}回");
	}

	private static void EnsureLoaded()
	{
		if (m_IsLoaded && m_CachedData != null)
		{
			EnsureSelectionContainers(m_CachedData);
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
					EnsureSelectionContainers(m_CachedData);
					return;
				}
			}
		}
		catch (Exception ex)
		{
			Debug.LogWarning($"[ExhibitionStatsStore] 統計データの読み込みに失敗しました: {ex.Message}");
		}

		m_CachedData = new ExhibitionStatsData();
		EnsureSelectionContainers(m_CachedData);
		m_CachedData.m_BallThrowSummary = BallThrowStatsFormatter.FormatBreakdown(0, 0);
	}

	private static void EnsureSelectionContainers(ExhibitionStatsData data)
	{
		if (data == null)
		{
			return;
		}

		data.m_CharacterSelections ??= new CharacterSelectionCounts();
		data.m_ColorSelections ??= new ColorSelectionCounts();
		if (string.IsNullOrEmpty(data.m_BallThrowSummary))
		{
			data.m_BallThrowSummary = BallThrowStatsFormatter.FormatBreakdown(
				data.m_TotalLockOnPassCount,
				data.m_TotalNormalThrowCount);
		}
	}

	private static void IncrementCharacterSelection(CharacterSelectionCounts counts, ECharacterType characterType)
	{
		switch (characterType)
		{
			case ECharacterType.Man:
				counts.m_Man++;
				break;
			case ECharacterType.Woman:
				counts.m_Woman++;
				break;
		}
	}

	private static void IncrementColorSelection(ColorSelectionCounts counts, EHighlightColor highlightColor)
	{
		switch (highlightColor)
		{
			case EHighlightColor.Black:
				counts.m_Black++;
				break;
			case EHighlightColor.Blue:
				counts.m_Blue++;
				break;
			case EHighlightColor.DeepBlue:
				counts.m_DeepBlue++;
				break;
			case EHighlightColor.DeepRed:
				counts.m_DeepRed++;
				break;
			case EHighlightColor.Green:
				counts.m_Green++;
				break;
			case EHighlightColor.LightBlue:
				counts.m_LightBlue++;
				break;
			case EHighlightColor.LightGreen:
				counts.m_LightGreen++;
				break;
			case EHighlightColor.Orange:
				counts.m_Orange++;
				break;
			case EHighlightColor.Purple:
				counts.m_Purple++;
				break;
			case EHighlightColor.Red:
				counts.m_Red++;
				break;
			case EHighlightColor.White:
				counts.m_White++;
				break;
			case EHighlightColor.Yellow:
				counts.m_Yellow++;
				break;
		}
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
