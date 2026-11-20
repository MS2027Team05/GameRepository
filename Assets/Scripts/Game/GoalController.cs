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
	[Header("得点エフェクト")]
	[Tooltip("このゴールに配置したGoalEffectのParticleSystemを指定します。")]
	[SerializeField] private ParticleSystem m_GoalEffect;
	private ParticleSystem[] m_EffectSystems;
	private bool m_IsPlayingGoalEffect;
	private bool m_IsPlayingReplayEffect;
	private uint m_GoalEffectSequence;

	/// <summary>
	/// このゴールに入った際に得点するチームを取得します。
	/// </summary>
	public ETeamType ScoringTeam => m_ScoringTeam;

	private void Awake()
	{
		if (m_GoalEffect != null)
		{
			m_EffectSystems = m_GoalEffect.GetComponentsInChildren<ParticleSystem>(true);
		}
	}

	private void OnEnable()
	{
		if (m_GoalArea == null || m_MatchManager == null ||
			(m_ScoringTeam != ETeamType.Red && m_ScoringTeam != ETeamType.Blue))
		{
			Debug.LogError("[GoalController] ゴール範囲、試合管理、得点先チーム(RedまたはBlue)を設定してください。", this);
			return;
		}

		m_GoalArea.BallEntered += HandleBallEntered;
		m_MatchManager.GoalScored += HandleGoalScored;
		m_MatchManager.MatchStateChanged += HandleMatchStateChanged;
		if (m_GoalEffect == null)
		{
			Debug.LogWarning("[GoalController] GoalEffectが未設定のため、得点エフェクトを再生できません。", this);
		}
	}

	private void OnDisable()
	{
		StopGoalEffect();
		StopReplayGoalEffect();
		if (m_GoalArea != null)
		{
			m_GoalArea.BallEntered -= HandleBallEntered;
		}
		if (m_MatchManager != null)
		{
			m_MatchManager.GoalScored -= HandleGoalScored;
			m_MatchManager.MatchStateChanged -= HandleMatchStateChanged;
		}
	}

	private void HandleBallEntered(BallController ball)
	{
		if (!isActiveAndEnabled || ball == null || !ball.IsSpawned || !ball.IsServer ||
			m_MatchManager == null || !m_MatchManager.IsSpawned || !m_MatchManager.IsServer)
		{
			return;
		}

		m_MatchManager.TryAddGoalScore(m_ScoringTeam, transform.position);
	}

	private void HandleGoalScored(ETeamType scoringTeam)
	{
		if (!isActiveAndEnabled || scoringTeam != m_ScoringTeam || m_GoalEffect == null) return;
		StopGoalEffect();
		StopReplayGoalEffect();
		if (!TryPrepareGoalEffectSystems()) return;
		// 配置済みの同じエフェクトを使い回し、前回の粒子を残さず再生します。
		m_GoalEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
		m_GoalEffect.Play(true);
		m_GoalEffectSequence = m_MatchManager.RegisterLocalGoalEffect();
		m_IsPlayingGoalEffect = true;
	}

	/// <summary>
	/// リプレイ内のゴール到達タイミングに合わせてゴールエフェクトを再生します。
	/// </summary>
	public void PlayReplayGoalEffect()
	{
		if (!isActiveAndEnabled || m_GoalEffect == null) return;
		StopGoalEffect();
		StopReplayGoalEffect();
		if (!TryPrepareGoalEffectSystems()) return;
		m_GoalEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
		m_GoalEffect.Play(true);
		m_IsPlayingReplayEffect = true;
	}

	/// <summary>
	/// リプレイ再生中に開始したゴールエフェクトを停止・消去します。
	/// </summary>
	public void StopReplayGoalEffect()
	{
		if (!m_IsPlayingReplayEffect) return;
		m_IsPlayingReplayEffect = false;
		if (m_GoalEffect != null)
		{
			m_GoalEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
		}
	}

	private bool TryPrepareGoalEffectSystems()
	{
		if (m_GoalEffect == null || !m_GoalEffect.gameObject.activeInHierarchy || m_EffectSystems == null)
		{
			return false;
		}

		foreach (ParticleSystem effect in m_EffectSystems)
		{
			if (effect == null) continue;
			var main = effect.main;
			if (main.loop)
			{
				Debug.LogError("[GoalController] 終了を待つGoalEffectと子のParticleSystemはLoopingをオフにしてください。演出を省略します。", this);
				return false;
			}
			// 画面外でも終了判定が進むよう、粒子のシミュレーションを継続します。
			main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
		}

		return true;
	}

	private void Update()
	{
		if (m_IsPlayingGoalEffect && (m_GoalEffect == null || !m_GoalEffect.gameObject.activeInHierarchy || !m_GoalEffect.IsAlive(true)))
		{
			StopGoalEffect();
		}

		if (m_IsPlayingReplayEffect && (m_GoalEffect == null || !m_GoalEffect.gameObject.activeInHierarchy || !m_GoalEffect.IsAlive(true)))
		{
			StopReplayGoalEffect();
		}
	}

	private void HandleMatchStateChanged(MatchManager.EMatchState state)
	{
		if (state != MatchManager.EMatchState.GoalCelebration) StopGoalEffect();
	}

	private void StopGoalEffect()
	{
		if (!m_IsPlayingGoalEffect) return;
		m_IsPlayingGoalEffect = false;
		if (m_GoalEffect != null)
		{
			m_GoalEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
		}
		if (m_MatchManager != null)
		{
			m_MatchManager.CompleteLocalGoalEffect(m_GoalEffectSequence);
		}
	}
}
