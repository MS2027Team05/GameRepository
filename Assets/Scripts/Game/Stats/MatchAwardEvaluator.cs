using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 試合統計から各プレイヤーの個人表彰(称号: No.1 / No.2、最大3個)を選出し、表示用テキストを生成します。
/// </summary>
public static class MatchAwardEvaluator
{
	public const int m_MaxAwardsPerPlayer = 3;

	private readonly struct AwardDefinition
	{
		public readonly EMatchAwardCategory m_Category;
		public readonly int m_Priority;
		public readonly string m_Rank1Title;
		public readonly string m_Rank2Title;
		public readonly string m_Unit;
		public readonly bool m_IsInteger;
		public readonly float m_MinThreshold;

		public AwardDefinition(
			EMatchAwardCategory category,
			int priority,
			string rank1Title,
			string rank2Title,
			string unit,
			bool isInteger,
			float minThreshold)
		{
			m_Category = category;
			m_Priority = priority;
			m_Rank1Title = rank1Title;
			m_Rank2Title = rank2Title;
			m_Unit = unit;
			m_IsInteger = isInteger;
			m_MinThreshold = minThreshold;
		}
	}

	private static readonly AwardDefinition[] m_Definitions =
	{
		new AwardDefinition(EMatchAwardCategory.Goals, 0, "最多ゴール", "ゴール貢献", "回", true, 1f),
		new AwardDefinition(EMatchAwardCategory.Assists, 1, "最多アシスト", "アシスト貢献", "回", true, 1f),
		new AwardDefinition(EMatchAwardCategory.Saves, 2, "ゴール守護神", "セーブ貢献", "回", true, 1f),
		new AwardDefinition(EMatchAwardCategory.LongestGoal, 3, "ロングシューター", "ミドルシューター", "m", false, 0.5f),
		new AwardDefinition(EMatchAwardCategory.PassCuts, 4, "インターセプター", "パスカット", "回", true, 1f),
		new AwardDefinition(EMatchAwardCategory.LongestPass, 5, "ロングパサー", "ワイドパサー", "m", false, 0.5f),
		new AwardDefinition(EMatchAwardCategory.Passes, 6, "パスマスター", "パスワーク", "回", true, 1f),
		new AwardDefinition(EMatchAwardCategory.EnemyStuns, 7, "スタンマスター", "電撃ヒット", "回", true, 1f),
		new AwardDefinition(EMatchAwardCategory.AllyPassLockOns, 8, "百発百中", "ターゲットパス", "回", true, 1f),
		new AwardDefinition(EMatchAwardCategory.BallHoldTime, 9, "ボールキーパー", "ボール保持", "秒", false, 0.5f),
		new AwardDefinition(EMatchAwardCategory.ObservedTime, 10, "注目プレイヤー", "カメラアピール", "秒", false, 0.5f),
		new AwardDefinition(EMatchAwardCategory.FlightDistance, 11, "空中機動王", "スカイトラベラー", "m", false, 0.5f),
		new AwardDefinition(EMatchAwardCategory.EnemyTerritoryTime, 12, "前線アタッカー", "敵陣滞在", "秒", false, 0.5f),
		new AwardDefinition(EMatchAwardCategory.OwnTerritoryTime, 13, "後衛ディフェンダー", "自陣防衛", "秒", false, 0.5f),
		new AwardDefinition(EMatchAwardCategory.AirMoveCount, 14, "重力ジャンパー", "空中起動", "回", true, 1f),
		new AwardDefinition(EMatchAwardCategory.FuelConsumed, 15, "フルスロットル", "エネルギー消費", "消費", false, 1f),
		new AwardDefinition(EMatchAwardCategory.GroundWalkTime, 16, "フィールドランナー", "地上移動", "秒", false, 0.5f),
	};

	/// <summary>
	/// 全プレイヤーの統計から、各プレイヤーごとに1〜3個の表彰を選出します。
	/// </summary>
	public static Dictionary<ulong, PlayerAwardEntry[]> EvaluateAwards(IReadOnlyList<PlayerMatchStats> allStats)
	{
		var results = new Dictionary<ulong, PlayerAwardEntry[]>();
		if (allStats == null || allStats.Count == 0)
		{
			return results;
		}

		var candidatesByClient = new Dictionary<ulong, List<(PlayerAwardEntry Award, int Priority)>>(allStats.Count);
		for (int i = 0; i < allStats.Count; i++)
		{
			candidatesByClient[allStats[i].m_ClientId] = new List<(PlayerAwardEntry, int)>();
		}

		for (int d = 0; d < m_Definitions.Length; d++)
		{
			AwardDefinition def = m_Definitions[d];
			float firstValue = -1f;
			float secondValue = -1f;

			for (int i = 0; i < allStats.Count; i++)
			{
				float val = allStats[i].GetMetricValue(def.m_Category);
				if (val < def.m_MinThreshold)
				{
					continue;
				}

				if (val > firstValue + 0.0001f)
				{
					secondValue = firstValue;
					firstValue = val;
				}
				else if (val < firstValue - 0.0001f && val > secondValue + 0.0001f)
				{
					secondValue = val;
				}
			}

			if (firstValue < def.m_MinThreshold)
			{
				continue;
			}

			for (int i = 0; i < allStats.Count; i++)
			{
				float val = allStats[i].GetMetricValue(def.m_Category);
				if (val < def.m_MinThreshold)
				{
					continue;
				}

				byte rank = 0;
				if (Mathf.Abs(val - firstValue) <= 0.0001f)
				{
					rank = 1;
				}
				else if (secondValue >= def.m_MinThreshold && Mathf.Abs(val - secondValue) <= 0.0001f)
				{
					rank = 2;
				}

				if (rank != 0)
				{
					candidatesByClient[allStats[i].m_ClientId].Add((
						new PlayerAwardEntry
						{
							m_Category = def.m_Category,
							m_Rank = rank,
							m_Value = val
						},
						def.m_Priority));
				}
			}
		}

		for (int i = 0; i < allStats.Count; i++)
		{
			PlayerMatchStats stat = allStats[i];
			List<(PlayerAwardEntry Award, int Priority)> candidates = candidatesByClient[stat.m_ClientId];

			// 順位(#1優先) -> カテゴリの重要度優先度の順で上位3つを選出します。
			candidates.Sort((a, b) =>
			{
				int rankCmp = a.Award.m_Rank.CompareTo(b.Award.m_Rank);
				if (rankCmp != 0)
				{
					return rankCmp;
				}
				return a.Priority.CompareTo(b.Priority);
			});

			if (candidates.Count == 0)
			{
				results[stat.m_ClientId] = new[]
				{
					BuildFallbackAward(stat)
				};
				continue;
			}

			int awardCount = Mathf.Min(candidates.Count, m_MaxAwardsPerPlayer);
			var selected = new PlayerAwardEntry[awardCount];
			for (int a = 0; a < awardCount; a++)
			{
				selected[a] = candidates[a].Award;
			}
			results[stat.m_ClientId] = selected;
		}

		return results;
	}

	private static PlayerAwardEntry BuildFallbackAward(in PlayerMatchStats stat)
	{
		// 全項目が3位以下・記録僅少だった場合でも、最も高い行動記録を1つ称号として表示します。
		if (stat.m_FlightDistance > 0.01f)
		{
			return new PlayerAwardEntry
			{
				m_Category = EMatchAwardCategory.FlightDistance,
				m_Rank = 2,
				m_Value = stat.m_FlightDistance
			};
		}

		if (stat.m_GroundWalkSeconds > 0.01f)
		{
			return new PlayerAwardEntry
			{
				m_Category = EMatchAwardCategory.GroundWalkTime,
				m_Rank = 2,
				m_Value = stat.m_GroundWalkSeconds
			};
		}

		return new PlayerAwardEntry
		{
			m_Category = EMatchAwardCategory.AirMoveCount,
			m_Rank = 2,
			m_Value = stat.m_AirMoveCount
		};
	}

	/// <summary>
	/// 称号データを「#1 最多ゴール (3回)」形式の文字列へ整形します。
	/// </summary>
	public static string FormatAwardLabel(in PlayerAwardEntry award)
	{
		string rankTag = award.m_Rank <= 1 ? "#1" : "#2";
		return $"{rankTag} {FormatAwardUiText(award)}";
	}

	/// <summary>
	/// 称号データをUI表示用の「最多ゴール (3回)」形式の文字列へ整形します。
	/// </summary>
	public static string FormatAwardUiText(in PlayerAwardEntry award)
	{
		AwardDefinition def = GetDefinition(award.m_Category);
		string title = award.m_Rank <= 1 ? def.m_Rank1Title : def.m_Rank2Title;
		string valueStr = def.m_IsInteger
			? $"{Mathf.RoundToInt(award.m_Value)}{def.m_Unit}"
			: $"{award.m_Value:F1}{def.m_Unit}";

		return $"{title} ({valueStr})";
	}

	/// <summary>
	/// リッチテキストカラー付きで称号文字列を整形します(#1は金色、#2は銀色)。
	/// </summary>
	public static string FormatAwardRichText(in PlayerAwardEntry award)
	{
		string label = FormatAwardLabel(award);
		string colorHex = award.m_Rank <= 1 ? "#FFD54F" : "#CFD8DC";
		return $"<color={colorHex}>{label}</color>";
	}

	private static AwardDefinition GetDefinition(EMatchAwardCategory category)
	{
		for (int i = 0; i < m_Definitions.Length; i++)
		{
			if (m_Definitions[i].m_Category == category)
			{
				return m_Definitions[i];
			}
		}

		return m_Definitions[0];
	}
}
