/// <summary>
/// ゴール発生時のハイライト評価および将来の試合貢献度連動で使用するコンテキスト情報。
/// </summary>
public struct GoalHighlightContext
{
	public ETeamType m_ScoringTeam;
	public int m_RedScoreAfterGoal;
	public int m_BlueScoreAfterGoal;
	public float m_RemainingTimeSeconds;
	public float m_MatchDurationSeconds;
	public bool m_WasFriendlyPassAssist;
	public float m_ShotDistance;
	public ulong m_LastTouchClientId;
	public uint m_GoalSequence;
	// 将来の試合貢献度システム連携用(パス連鎖数や関与プレイヤー行動スコアを流し込む拡張枠)
	public int m_PassChainCount;
	public float m_ContributorActionScore;
}

/// <summary>
/// ゴールが試合のベストゴール(ハイライト)にふさわしいかをスコア化する評価インターフェース。
/// 将来的に試合貢献度トラッカーの行動ログと接続して評価式を差し替える拡張点として機能します。
/// </summary>
public interface IGoalHighlightEvaluator
{
	float Evaluate(in GoalHighlightContext context);
}

/// <summary>
/// 既定のゴールハイライト評価ロジック。
/// 試合終盤度・同点/逆転弾・味方パス連携・シュート距離からスコアを算出します。
/// </summary>
public sealed class DefaultGoalHighlightEvaluator : IGoalHighlightEvaluator
{
	private readonly float m_LateGameWeight;
	private readonly float m_ClutchGoalBonus;
	private readonly float m_FriendlyPassBonus;
	private readonly float m_ShotDistanceWeight;
	private readonly float m_ContributorActionWeight;

	public DefaultGoalHighlightEvaluator(
		float lateGameWeight = 40.0f,
		float clutchGoalBonus = 30.0f,
		float friendlyPassBonus = 20.0f,
		float shotDistanceWeight = 0.5f,
		float contributorActionWeight = 1.0f)
	{
		m_LateGameWeight = lateGameWeight;
		m_ClutchGoalBonus = clutchGoalBonus;
		m_FriendlyPassBonus = friendlyPassBonus;
		m_ShotDistanceWeight = shotDistanceWeight;
		m_ContributorActionWeight = contributorActionWeight;
	}

	public float Evaluate(in GoalHighlightContext context)
	{
		float score = 10.0f;

		// 1. 試合終盤度(残り時間が少ないほど高得点)
		if (context.m_MatchDurationSeconds > 0.01f)
		{
			float elapsedRatio = 1.0f - UnityEngine.Mathf.Clamp01(context.m_RemainingTimeSeconds / context.m_MatchDurationSeconds);
			score += elapsedRatio * m_LateGameWeight;
		}

		// 2. 同点弾またはリードを奪う勝ち越しゴールへの加点
		int diffAfter = context.m_ScoringTeam == ETeamType.Red
			? context.m_RedScoreAfterGoal - context.m_BlueScoreAfterGoal
			: context.m_BlueScoreAfterGoal - context.m_RedScoreAfterGoal;
		if (diffAfter == 0 || diffAfter == 1)
		{
			score += m_ClutchGoalBonus;
		}

		// 3. 味方パス経由での得点加点
		if (context.m_WasFriendlyPassAssist)
		{
			score += m_FriendlyPassBonus;
		}

		// 4. シュート距離による加点(上限キャップ付き)
		score += UnityEngine.Mathf.Min(context.m_ShotDistance, 60.0f) * m_ShotDistanceWeight;

		// 5. 将来の貢献度スコア・パス連鎖数連携
		score += context.m_PassChainCount * 5.0f;
		score += context.m_ContributorActionScore * m_ContributorActionWeight;

		return score;
	}
}
