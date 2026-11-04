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
