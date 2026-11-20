using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// 電流Prefabの再生、発生停止、残存粒子の表示終了を管理します。
/// </summary>
public sealed class LightningPrefabPlayback
{
    private readonly GameObject m_Container;
    private readonly ParticleSystem[] m_Particles;
    private readonly VisualEffect[] m_Effects;
    private bool m_IsDraining;
    private int m_StopFrame;
    private int m_PlayFrame;
    private bool m_IsStopPending;

    public LightningPrefabPlayback(GameObject container, GameObject instance)
    {
        m_Container = container;
        m_Particles = instance.GetComponentsInChildren<ParticleSystem>(true);
        m_Effects = instance.GetComponentsInChildren<VisualEffect>(true);
    }

    public void Play()
    {
        bool wasActive = m_Container.activeSelf;
        if (wasActive && !m_IsDraining)
        {
            // 再生条件が継続している場合の既存ParticleSystemの繰り返しを維持します。
            if (m_Particles.Length > 0 && !AreParticlesAlive())
            {
                PlayParticles();
            }
            return;
        }

        if (wasActive && m_IsStopPending)
        {
            m_IsStopPending = false;
            m_IsDraining = false;
            return;
        }

        m_Container.SetActive(true);
        PlayParticles();
        for (int i = 0; i < m_Effects.Length; i++)
        {
            VisualEffect effect = m_Effects[i];
            if (effect == null || !effect.isActiveAndEnabled)
            {
                continue;
            }

            if (wasActive)
            {
                // 残存粒子を消さず、Graphの開始イベントで発生を再開します。
                effect.SendEvent(effect.initialEventID);
            }
            else
            {
                // 非表示中の状態をリセットし、初期イベントから再生します。
                effect.Reinit();
            }
        }
        m_IsDraining = false;
        m_IsStopPending = false;
        m_PlayFrame = Time.frameCount;
    }

    /// <summary>
    /// clearがfalseの場合は発生だけを止め、既存の粒子を残します。
    /// </summary>
    public void Stop(bool clear)
    {
        if (!m_Container.activeSelf || (!clear && m_IsDraining))
        {
            return;
        }

        m_IsDraining = !clear;
        m_StopFrame = Time.frameCount;
        // 接触が1フレーム未満でも、最初のVFX更新より前に発生を止めないようにします。
        m_IsStopPending = !clear && Time.frameCount <= m_PlayFrame;
        if (!m_IsStopPending)
        {
            StopEmission(clear);
        }
        if (clear)
        {
            m_Container.SetActive(false);
        }
    }

    private void StopEmission(bool clear)
    {
        for (int i = 0; i < m_Particles.Length; i++)
        {
            if (m_Particles[i] != null)
            {
                m_Particles[i].Stop(false, clear
                    ? ParticleSystemStopBehavior.StopEmittingAndClear
                    : ParticleSystemStopBehavior.StopEmitting);
            }
        }
        for (int i = 0; i < m_Effects.Length; i++)
        {
            if (m_Effects[i] != null)
            {
                m_Effects[i].Stop();
            }
        }

    }

    public void UpdateDrain()
    {
        if (m_IsStopPending)
        {
            if (Time.frameCount <= m_PlayFrame)
            {
                return;
            }
            m_IsStopPending = false;
            StopEmission(false);
            m_StopFrame = Time.frameCount;
        }

        // GPU側の更新前に粒子数0を読んで非表示にしないよう、停止直後は待ちます。
        if (!m_IsDraining || Time.frameCount <= m_StopFrame + 1 || AreParticlesAlive())
        {
            return;
        }

        for (int i = 0; i < m_Effects.Length; i++)
        {
            VisualEffect effect = m_Effects[i];
            if (effect != null && effect.isActiveAndEnabled &&
                (effect.aliveParticleCount > 0 || effect.HasAnySystemAwake()))
            {
                return;
            }
        }

        m_Container.SetActive(false);
        m_IsDraining = false;
    }

    private bool AreParticlesAlive()
    {
        for (int i = 0; i < m_Particles.Length; i++)
        {
            ParticleSystem particles = m_Particles[i];
            if (particles != null && particles.gameObject.activeInHierarchy && particles.IsAlive(false))
            {
                return true;
            }
        }
        return false;
    }

    private void PlayParticles()
    {
        for (int i = 0; i < m_Particles.Length; i++)
        {
            ParticleSystem particles = m_Particles[i];
            if (particles != null && particles.gameObject.activeInHierarchy)
            {
                particles.Play(false);
            }
        }
    }
}
