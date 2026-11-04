using UnityEngine;

/// <summary>
/// Inspectorで指定されたゴール範囲と得点先を試合管理へ接続します。
/// </summary>
[DisallowMultipleComponent]
public class GoalController : MonoBehaviour
{
	[Header("ゴール設定")]
	[Tooltip("ボールの進入を検知する判定用オブジェクトを指定します。")]
	[SerializeField] private GoalArea m_GoalArea;
	[Tooltip("このゴールに入った際に得点を加算するチームを指定します。")]
	[SerializeField] private ETeamType m_ScoringTeam;
	[SerializeField] private MatchManager m_MatchManager;

	private void OnEnable()
	{
		if (m_GoalArea == null || m_MatchManager == null ||
			(m_ScoringTeam != ETeamType.Red && m_ScoringTeam != ETeamType.Blue))
		{
			Debug.LogError("[GoalController] ゴール範囲、試合管理、得点先チーム(RedまたはBlue)を設定してください。", this);
			return;
		}

		m_GoalArea.BallEntered += HandleBallEntered;
	}

	private void OnDisable()
	{
		if (m_GoalArea != null)
		{
			m_GoalArea.BallEntered -= HandleBallEntered;
		}
	}

	private void HandleBallEntered(BallController ball)
	{
		if (!isActiveAndEnabled || ball == null || !ball.IsSpawned || !ball.IsServer ||
			m_MatchManager == null || !m_MatchManager.IsSpawned || !m_MatchManager.IsServer)
		{
			return;
		}

		m_MatchManager.TryAddGoalScore(m_ScoringTeam);
	}
}
