using UnityEngine;

public abstract class ALightningBolt : MonoBehaviour
{
    [Header("プレハブエフェクトの設定")]
    [Tooltip("オンかつプレハブ登録済みの場合、プログラム生成の電撃の代わりにプレハブを再生します。")]
    [SerializeField] private bool m_UsePrefabEffect;
    [Tooltip("電撃全体を置き換えるプレハブ。ParticleSystemは明示的に再生し、全粒子の終了後も再生条件が成立していれば再生し直します。")]
    [SerializeField] private GameObject m_EffectPrefab;

    [Header("電撃本数の設定")]
    [SerializeField] private int m_BoltCount;
    [SerializeField] private int m_MaxActiveBoltCount;

    [Header("電撃形状の設定")]
    [SerializeField] private int m_SegmentCount;
    [SerializeField] private float m_Displacement;

    [Header("電撃の見た目設定")]
    [SerializeField] private Material m_CoreMaterial;
    [SerializeField] private Material m_GlowMaterial;
    [SerializeField] private float m_CoreWidth;
    [SerializeField] private float m_GlowWidth;

    [Range(0f, 1f)]
    [SerializeField] private float m_EndWidthRatio;

    [Header("電撃アニメーションの設定")]
    [SerializeField] private float m_RefreshInterval;
    [SerializeField] private float m_Lifetime;
    [SerializeField] private float m_SpawnInterval;

    private BoltData[] m_Bolts;
    private Transform m_BoltContainer;
    private float m_SpawnTimer;
    private int m_ActiveBoltCount;
    private int m_SpawnCycle;
    private bool m_HasFinishedSpawning;
    private GameObject m_PrefabContainer;
    private GameObject m_InstantiatedPrefab;
    private ParticleSystem[] m_PrefabParticles;
    private bool m_IsUsingPrefab;

    protected sealed class BoltData
    {
        public LineRenderer m_CoreLineRenderer;
        public LineRenderer m_GlowLineRenderer;
        public Vector3[] m_Positions;
        public Vector3 m_StartPosition;
        public Vector3 m_EndPosition;
        public Vector3 m_Direction;
        public float m_LifetimeTimer;
        public float m_RefreshTimer;
        public bool m_IsActive;
        public int m_LastActivatedSpawnCycle;
    }

    private void Update()
    {
        UpdateSource();

        bool usePrefab = m_UsePrefabEffect && m_EffectPrefab != null;
        if (m_IsUsingPrefab != usePrefab)
        {
            ClearActiveBolts();
            StopPrefabEffect();
            m_IsUsingPrefab = usePrefab;
        }

        if (usePrefab)
        {
            UpdatePrefabEffect();
            return;
        }

        if (m_Bolts == null)
        {
            CreateBoltContainer();
            CreateBoltPool();
        }

        UpdateActiveBolts();
        UpdateSpawn();
    }

    protected virtual void OnDisable()
    {
        ClearActiveBolts();
        StopPrefabEffect();
    }

    protected virtual bool CanPlayPrefabEffect()
    {
        return CanSpawnBolt();
    }

    protected virtual void UpdatePrefabEffectTransform(Transform effectTransform)
    {
    }

    private void UpdatePrefabEffect()
    {
        if (!CanPlayPrefabEffect())
        {
            StopPrefabEffect();
            return;
        }

        if (m_PrefabContainer == null || m_InstantiatedPrefab != m_EffectPrefab)
        {
            StopPrefabEffect();
            if (m_PrefabContainer != null)
            {
                Destroy(m_PrefabContainer);
            }

            // 非アクティブな親の下で生成し、位置設定前の自動再生を防ぎます。
            m_PrefabContainer = new GameObject("LightningPrefabEffect");
            m_PrefabContainer.SetActive(false);
            m_PrefabContainer.transform.SetParent(transform, false);
            GameObject effect = Instantiate(m_EffectPrefab, m_PrefabContainer.transform, false);
            effect.SetActive(true);
            m_PrefabParticles = effect.GetComponentsInChildren<ParticleSystem>(true);
            m_InstantiatedPrefab = m_EffectPrefab;
        }

        UpdatePrefabEffectTransform(m_PrefabContainer.transform);
        if (!m_PrefabContainer.activeSelf)
        {
            m_PrefabContainer.SetActive(true);
            PlayPrefabParticles();
        }
        else if (m_PrefabParticles.Length > 0 && !ArePrefabParticlesAlive())
        {
            PlayPrefabParticles();
        }
    }

    private bool ArePrefabParticlesAlive()
    {
        // 子も個別に確認し、再生待機中や残存粒子がある間は再スタートしません。
        for (int i = 0; i < m_PrefabParticles.Length; i++)
        {
            ParticleSystem particles = m_PrefabParticles[i];
            if (particles != null && particles.gameObject.activeInHierarchy && particles.IsAlive(false))
            {
                return true;
            }
        }

        return false;
    }

    private void PlayPrefabParticles()
    {
        for (int i = 0; i < m_PrefabParticles.Length; i++)
        {
            ParticleSystem particles = m_PrefabParticles[i];
            if (particles != null && particles.gameObject.activeInHierarchy)
            {
                // 階層を重複して再生しないよう、各ParticleSystemを個別に開始します。
                particles.Play(false);
            }
        }
    }

    private void StopPrefabEffect()
    {
        if (m_PrefabContainer == null || !m_PrefabContainer.activeSelf)
        {
            return;
        }

        for (int i = 0; i < m_PrefabParticles.Length; i++)
        {
            if (m_PrefabParticles[i] != null)
            {
                m_PrefabParticles[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        m_PrefabContainer.SetActive(false);
    }

    protected virtual void UpdateSource()
    {
    }

    protected virtual void InitializeBolt(BoltData bolt)
    {
    }

    protected virtual bool CanSpawnBolt()
    {
        return true;
    }

    protected virtual bool ReusesDeactivatedBolts => true;

    protected float Displacement => m_Displacement;

    protected abstract void UpdateBoltPosition(BoltData bolt);

    protected virtual void UpdateBoltTransform(BoltData bolt)
    {
    }

    protected virtual Vector3 AdjustRandomOffset(Vector3 randomOffset)
    {
        return randomOffset;
    }

    protected void ResetSpawnCycle()
    {
        StopPrefabEffect();
        m_SpawnCycle++;
        m_HasFinishedSpawning = false;
        m_SpawnTimer = 0f;
    }

    protected void ClearActiveBolts()
    {
        if (m_Bolts == null)
        {
            return;
        }

        for (int i = 0; i < m_Bolts.Length; i++)
        {
            if (m_Bolts[i].m_IsActive)
            {
                DeactivateBolt(m_Bolts[i]);
            }
        }
    }

    private void CreateBoltContainer()
    {
        GameObject containerObject = new GameObject("LightningBolt");

        containerObject.transform.SetParent(transform);
        containerObject.transform.localPosition = Vector3.zero;
        containerObject.transform.localRotation = Quaternion.identity;
        containerObject.transform.localScale = Vector3.one;

        m_BoltContainer = containerObject.transform;
    }

    private void CreateBoltPool()
    {
        m_Bolts = new BoltData[m_BoltCount];

        for (int i = 0; i < m_BoltCount; i++)
        {
            GameObject boltObject = new GameObject($"LightningBolt_{i}");

            boltObject.transform.SetParent(m_BoltContainer);
            boltObject.transform.localPosition = Vector3.zero;
            boltObject.transform.localRotation = Quaternion.identity;
            boltObject.transform.localScale = Vector3.one;

            LineRenderer glowLineRenderer =
                CreateLineRenderer(
                    boltObject.transform,
                    "Glow",
                    m_GlowMaterial,
                    m_GlowWidth);

            LineRenderer coreLineRenderer =
                CreateLineRenderer(
                    boltObject.transform,
                    "Core",
                    m_CoreMaterial,
                    m_CoreWidth);

            m_Bolts[i] = new BoltData
            {
                m_CoreLineRenderer = coreLineRenderer,
                m_GlowLineRenderer = glowLineRenderer,
                m_Positions = new Vector3[m_SegmentCount + 1],
                m_IsActive = false,
                m_LastActivatedSpawnCycle = -1
            };
        }
    }

    private LineRenderer CreateLineRenderer(
        Transform parent,
        string objectName,
        Material material,
        float lineWidth)
    {
        GameObject lineObject = new GameObject(objectName);

        lineObject.transform.SetParent(parent);
        lineObject.transform.localPosition = Vector3.zero;
        lineObject.transform.localRotation = Quaternion.identity;
        lineObject.transform.localScale = Vector3.one;

        LineRenderer lineRenderer = lineObject.AddComponent<LineRenderer>();

        lineRenderer.positionCount = m_SegmentCount + 1;
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth * m_EndWidthRatio;
        lineRenderer.useWorldSpace = true;
        lineRenderer.enabled = false;

        if (material != null)
        {
            lineRenderer.sharedMaterial = material;
        }

        return lineRenderer;
    }

    private void UpdateActiveBolts()
    {
        for (int i = 0; i < m_Bolts.Length; i++)
        {
            BoltData bolt = m_Bolts[i];

            if (!bolt.m_IsActive)
            {
                continue;
            }

            bolt.m_LifetimeTimer += Time.deltaTime;
            bolt.m_RefreshTimer += Time.deltaTime;

            if (bolt.m_LifetimeTimer >= m_Lifetime)
            {
                DeactivateBolt(bolt);
                continue;
            }

            if (bolt.m_RefreshTimer >= m_RefreshInterval)
            {
                bolt.m_RefreshTimer = 0f;

                UpdateBoltPosition(bolt);
                GenerateLightning(bolt);
            }

            UpdateBoltTransform(bolt);
        }
    }

    private void UpdateSpawn()
    {
        if (m_HasFinishedSpawning ||
            m_ActiveBoltCount >= m_MaxActiveBoltCount ||
            !CanSpawnBolt())
        {
            return;
        }

        m_SpawnTimer += Time.deltaTime;

        if (m_SpawnTimer < m_SpawnInterval)
        {
            return;
        }

        BoltData availableBolt = GetAvailableBolt();

        if (availableBolt == null)
        {
            if (!ReusesDeactivatedBolts)
            {
                m_HasFinishedSpawning = true;
            }

            return;
        }

        m_SpawnTimer = 0f;

        ActivateBolt(availableBolt);
    }

    private BoltData GetAvailableBolt()
    {
        for (int i = 0; i < m_Bolts.Length; i++)
        {
            BoltData bolt = m_Bolts[i];

            if (!bolt.m_IsActive &&
                (bolt.m_LastActivatedSpawnCycle != m_SpawnCycle ||
                 ReusesDeactivatedBolts))
            {
                return bolt;
            }
        }

        return null;
    }

    private void ActivateBolt(BoltData bolt)
    {
        bolt.m_IsActive = true;
        bolt.m_LastActivatedSpawnCycle = m_SpawnCycle;
        bolt.m_LifetimeTimer = 0f;
        bolt.m_RefreshTimer = 0f;

        ApplyBoltSettings(bolt);
        InitializeBolt(bolt);
        UpdateBoltPosition(bolt);
        GenerateLightning(bolt);
        UpdateBoltTransform(bolt);
        SetBoltVisible(bolt, true);

        m_ActiveBoltCount++;
    }

    private void DeactivateBolt(BoltData bolt)
    {
        bolt.m_IsActive = false;
        SetBoltVisible(bolt, false);

        m_ActiveBoltCount--;
    }

    private static void SetBoltVisible(
        BoltData bolt,
        bool isVisible)
    {
        bolt.m_CoreLineRenderer.enabled = isVisible;
        bolt.m_GlowLineRenderer.enabled = isVisible;
    }

    private void ApplyBoltSettings(BoltData bolt)
    {
        int requiredPositionCount = m_SegmentCount + 1;

        if (bolt.m_Positions.Length != requiredPositionCount)
        {
            bolt.m_Positions = new Vector3[requiredPositionCount];
            bolt.m_CoreLineRenderer.positionCount = requiredPositionCount;
            bolt.m_GlowLineRenderer.positionCount = requiredPositionCount;
        }

        bolt.m_CoreLineRenderer.startWidth = m_CoreWidth;
        bolt.m_CoreLineRenderer.endWidth = m_CoreWidth * m_EndWidthRatio;
        bolt.m_GlowLineRenderer.startWidth = m_GlowWidth;
        bolt.m_GlowLineRenderer.endWidth = m_GlowWidth * m_EndWidthRatio;
    }

    protected virtual void GenerateLightning(BoltData bolt)
    {
        bolt.m_Positions[0] = bolt.m_StartPosition;
        bolt.m_Positions[^1] = bolt.m_EndPosition;

        for (int i = 1; i < m_SegmentCount; i++)
        {
            float progress = (float)i / m_SegmentCount;
            Vector3 basePosition =
                Vector3.Lerp(
                    bolt.m_StartPosition,
                    bolt.m_EndPosition,
                    progress);

            Vector3 randomOffset =
                AdjustRandomOffset(Random.insideUnitSphere * m_Displacement);

            bolt.m_Positions[i] = basePosition + randomOffset;
        }

        bolt.m_GlowLineRenderer.SetPositions(bolt.m_Positions);
        bolt.m_CoreLineRenderer.SetPositions(bolt.m_Positions);
    }
}
