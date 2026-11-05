using UnityEngine;

/// <summary>
/// MatchManagerが同期する残り時間とチーム得点をHUDへ表示します。
/// </summary>
public class MatchHudController : MonoBehaviour
{
	[SerializeField] private MatchManager m_MatchManager;
	[SerializeField] private TMPro.TMP_Text m_RemainingTimeText;
	[SerializeField] private TMPro.TMP_Text m_RedScoreText;
	[SerializeField] private TMPro.TMP_Text m_BlueScoreText;
	[SerializeField] private TMPro.TMP_Text m_CountdownText;
	private int m_LastCountdownSeconds = -1;

	private void OnEnable()
	{
		if (m_MatchManager == null)
		{
			Debug.LogError("[MatchHudController] MatchManagerが設定されていません。");
			return;
		}

		m_MatchManager.RemainingTimeChanged += UpdateRemainingTime;
		m_MatchManager.RedScoreChanged += UpdateRedScore;
		m_MatchManager.BlueScoreChanged += UpdateBlueScore;

		UpdateRemainingTime(m_MatchManager.RemainingTimeSeconds);
		UpdateRedScore(m_MatchManager.RedScore);
		UpdateBlueScore(m_MatchManager.BlueScore);
		UpdateCountdown();
	}

	private void OnDisable()
	{
		if (m_MatchManager == null)
		{
			return;
		}

		m_MatchManager.RemainingTimeChanged -= UpdateRemainingTime;
		m_MatchManager.RedScoreChanged -= UpdateRedScore;
		m_MatchManager.BlueScoreChanged -= UpdateBlueScore;
	}

	private void UpdateRemainingTime(int remainingTimeSeconds)
	{
		if (m_RemainingTimeText == null)
		{
			return;
		}

		int minutes = remainingTimeSeconds / 60;
		int seconds = remainingTimeSeconds % 60;
		m_RemainingTimeText.SetText("{0:00}:{1:00}", minutes, seconds);
	}

	private void Update()
	{
		UpdateCountdown();
	}

	private void UpdateCountdown()
	{
		if (m_MatchManager == null || m_CountdownText == null) return;
		bool visible = m_MatchManager.IsSpawned && m_MatchManager.MatchState == MatchManager.EMatchState.Countdown;
		m_CountdownText.enabled = visible;
		if (!visible)
		{
			m_LastCountdownSeconds = -1;
			return;
		}

		// サーバー時刻から表示を算出し、数値が変化したときだけテキストを更新します。
		int seconds = Mathf.Max(1, m_MatchManager.CountdownRemainingSeconds);
		if (seconds != m_LastCountdownSeconds)
		{
			m_LastCountdownSeconds = seconds;
			m_CountdownText.SetText("{0}", seconds);
		}
	}

	private void UpdateRedScore(int score)
	{
		if (m_RedScoreText != null)
		{
			m_RedScoreText.SetText("{0}", score);
		}
	}

	private void UpdateBlueScore(int score)
	{
		if (m_BlueScoreText != null)
		{
			m_BlueScoreText.SetText("{0}", score);
		}
	}
}
