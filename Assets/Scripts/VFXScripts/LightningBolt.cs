using UnityEngine;

public class LightningBolt : MonoBehaviour
{
    [Header("電撃位置の設定")]
    [SerializeField] private Transform m_CenterPoint;
    [SerializeField] private Transform m_StartPoint;
    [SerializeField] private Transform m_EndPoint;

    [Header("電撃生成モード")]
    [SerializeField] private bool m_SphericalMode;
    [SerializeField] private bool m_UseStartPoint;

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

    private class BoltData
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
    }

    private void Awake()
    {
        CreateBoltContainer();
        CreateBoltPool();
    }

    private void Update()
    {
        UpdateActiveBolts();
        UpdateSpawn();
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
            GameObject boltObject =
                new GameObject($"LightningBolt_{i}");

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
                m_IsActive = false
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

        LineRenderer lineRenderer =
            lineObject.AddComponent<LineRenderer>();

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

            if (bolt.m_RefreshTimer < m_RefreshInterval)
            {
                continue;
            }

            bolt.m_RefreshTimer = 0f;

            UpdateBoltPosition(bolt);
            GenerateLightning(bolt);
        }
    }

    private void UpdateSpawn()
    {
        if (m_ActiveBoltCount >= m_MaxActiveBoltCount)
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
            return;
        }

        m_SpawnTimer = 0f;

        ActivateBolt(availableBolt);
    }

    private BoltData GetAvailableBolt()
    {
        for (int i = 0; i < m_Bolts.Length; i++)
        {
            if (!m_Bolts[i].m_IsActive)
            {
                return m_Bolts[i];
            }
        }

        return null;
    }

    private void ActivateBolt(BoltData bolt)
    {
        bolt.m_IsActive = true;
        bolt.m_LifetimeTimer = 0f;
        bolt.m_RefreshTimer = 0f;

        ApplyBoltSettings(bolt);
        SetBoltDirection(bolt);
        UpdateBoltPosition(bolt);
        GenerateLightning(bolt);

        SetBoltVisible(bolt, true);

        m_ActiveBoltCount++;
    }

    private void DeactivateBolt(BoltData bolt)
    {
        bolt.m_IsActive = false;

        SetBoltVisible(bolt, false);

        m_ActiveBoltCount--;
    }

    private void SetBoltVisible(
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
            bolt.m_Positions =
                new Vector3[requiredPositionCount];

            bolt.m_CoreLineRenderer.positionCount =
                requiredPositionCount;

            bolt.m_GlowLineRenderer.positionCount =
                requiredPositionCount;
        }

        bolt.m_CoreLineRenderer.startWidth = m_CoreWidth;
        bolt.m_CoreLineRenderer.endWidth =
            m_CoreWidth * m_EndWidthRatio;

        bolt.m_GlowLineRenderer.startWidth = m_GlowWidth;
        bolt.m_GlowLineRenderer.endWidth =
            m_GlowWidth * m_EndWidthRatio;
    }

    private void SetBoltDirection(BoltData bolt)
    {
        if (!m_SphericalMode)
        {
            return;
        }

        bolt.m_Direction = Random.onUnitSphere;
    }

    private void UpdateBoltPosition(BoltData bolt)
    {
        Vector3 centerPosition = GetCenterPosition();

        if (!m_SphericalMode)
        {
            bolt.m_StartPosition = m_UseStartPoint
                ? GetStartPosition(centerPosition)
                : centerPosition;

            bolt.m_EndPosition = m_EndPoint.position;

            return;
        }

        float startRadius = 0f;

        if (m_UseStartPoint && m_StartPoint != null)
        {
            startRadius = Vector3.Distance(
                centerPosition,
                m_StartPoint.position);
        }

        float endRadius = Vector3.Distance(
            centerPosition,
            m_EndPoint.position);

        bolt.m_StartPosition =
            centerPosition +
            bolt.m_Direction * startRadius;

        bolt.m_EndPosition =
            centerPosition +
            bolt.m_Direction * endRadius;
    }

    private Vector3 GetCenterPosition()
    {
        if (m_CenterPoint != null)
        {
            return m_CenterPoint.position;
        }

        if (m_StartPoint != null)
        {
            return m_StartPoint.position;
        }

        return transform.position;
    }

    private Vector3 GetStartPosition(
        Vector3 centerPosition)
    {
        if (m_StartPoint != null)
        {
            return m_StartPoint.position;
        }

        return centerPosition;
    }

    private void GenerateLightning(BoltData bolt)
    {
        bolt.m_Positions[0] =
            bolt.m_StartPosition;

        bolt.m_Positions[^1] =
            bolt.m_EndPosition;

        for (int i = 1; i < m_SegmentCount; i++)
        {
            float progress =
                (float)i / m_SegmentCount;

            Vector3 basePosition =
                Vector3.Lerp(
                    bolt.m_StartPosition,
                    bolt.m_EndPosition,
                    progress);

            Vector3 randomOffset =
                Random.insideUnitSphere *
                m_Displacement;

            bolt.m_Positions[i] =
                basePosition + randomOffset;
        }

        bolt.m_GlowLineRenderer.SetPositions(
            bolt.m_Positions);

        bolt.m_CoreLineRenderer.SetPositions(
            bolt.m_Positions);
    }
}
