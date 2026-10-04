using UnityEngine;

public class LightningBolt : MonoBehaviour
{
    [Header("電撃位置の設定")]
    [SerializeField] private Transform centerPoint;
    [SerializeField] private Transform startPoint;
    [SerializeField] private Transform endPoint;

    [Header("電撃生成モード")]
    [SerializeField] private bool sphericalMode;
    [SerializeField] private bool useStartPoint;

    [Header("電撃本数の設定")]
    [SerializeField] private int boltCount;
    [SerializeField] private int maxActiveBoltCount;

    [Header("電撃形状の設定")]
    [SerializeField] private int segmentCount;
    [SerializeField] private float displacement;

    [Header("電撃の見た目設定")]
    [SerializeField] private Material coreMaterial;
    [SerializeField] private Material glowMaterial;
    [SerializeField] private float coreWidth;
    [SerializeField] private float glowWidth;

    [Range(0f, 1f)]
    [SerializeField] private float endWidthRatio;

    [Header("電撃アニメーションの設定")]
    [SerializeField] private float refreshInterval;
    [SerializeField] private float lifetime;
    [SerializeField] private float spawnInterval;

    private BoltData[] bolts;
    private Transform boltContainer;
    private float spawnTimer;
    private int activeBoltCount;

    private class BoltData
    {
        public LineRenderer CoreLineRenderer;
        public LineRenderer GlowLineRenderer;

        public Vector3[] Positions;
        public Vector3 StartPosition;
        public Vector3 EndPosition;
        public Vector3 Direction;

        public float LifetimeTimer;
        public float RefreshTimer;

        public bool IsActive;
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

        boltContainer = containerObject.transform;
    }

    private void CreateBoltPool()
    {
        bolts = new BoltData[boltCount];

        for (int i = 0; i < boltCount; i++)
        {
            GameObject boltObject =
                new GameObject($"LightningBolt_{i}");

            boltObject.transform.SetParent(boltContainer);
            boltObject.transform.localPosition = Vector3.zero;
            boltObject.transform.localRotation = Quaternion.identity;
            boltObject.transform.localScale = Vector3.one;

            LineRenderer glowLineRenderer =
                CreateLineRenderer(
                    boltObject.transform,
                    "Glow",
                    glowMaterial,
                    glowWidth);

            LineRenderer coreLineRenderer =
                CreateLineRenderer(
                    boltObject.transform,
                    "Core",
                    coreMaterial,
                    coreWidth);

            bolts[i] = new BoltData
            {
                CoreLineRenderer = coreLineRenderer,
                GlowLineRenderer = glowLineRenderer,
                Positions = new Vector3[segmentCount + 1],
                IsActive = false
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

        lineRenderer.positionCount = segmentCount + 1;
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth * endWidthRatio;
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
        for (int i = 0; i < bolts.Length; i++)
        {
            BoltData bolt = bolts[i];

            if (!bolt.IsActive)
            {
                continue;
            }

            bolt.LifetimeTimer += Time.deltaTime;
            bolt.RefreshTimer += Time.deltaTime;

            if (bolt.LifetimeTimer >= lifetime)
            {
                DeactivateBolt(bolt);
                continue;
            }

            if (bolt.RefreshTimer < refreshInterval)
            {
                continue;
            }

            bolt.RefreshTimer = 0f;

            UpdateBoltPosition(bolt);
            GenerateLightning(bolt);
        }
    }

    private void UpdateSpawn()
    {
        if (activeBoltCount >= maxActiveBoltCount)
        {
            return;
        }

        spawnTimer += Time.deltaTime;

        if (spawnTimer < spawnInterval)
        {
            return;
        }

        BoltData availableBolt = GetAvailableBolt();

        if (availableBolt == null)
        {
            return;
        }

        spawnTimer = 0f;

        ActivateBolt(availableBolt);
    }

    private BoltData GetAvailableBolt()
    {
        for (int i = 0; i < bolts.Length; i++)
        {
            if (!bolts[i].IsActive)
            {
                return bolts[i];
            }
        }

        return null;
    }

    private void ActivateBolt(BoltData bolt)
    {
        bolt.IsActive = true;
        bolt.LifetimeTimer = 0f;
        bolt.RefreshTimer = 0f;

        ApplyBoltSettings(bolt);
        SetBoltDirection(bolt);
        UpdateBoltPosition(bolt);
        GenerateLightning(bolt);

        SetBoltVisible(bolt, true);

        activeBoltCount++;
    }

    private void DeactivateBolt(BoltData bolt)
    {
        bolt.IsActive = false;

        SetBoltVisible(bolt, false);

        activeBoltCount--;
    }

    private void SetBoltVisible(
        BoltData bolt,
        bool isVisible)
    {
        bolt.CoreLineRenderer.enabled = isVisible;
        bolt.GlowLineRenderer.enabled = isVisible;
    }

    private void ApplyBoltSettings(BoltData bolt)
    {
        int requiredPositionCount = segmentCount + 1;

        if (bolt.Positions.Length != requiredPositionCount)
        {
            bolt.Positions =
                new Vector3[requiredPositionCount];

            bolt.CoreLineRenderer.positionCount =
                requiredPositionCount;

            bolt.GlowLineRenderer.positionCount =
                requiredPositionCount;
        }

        bolt.CoreLineRenderer.startWidth = coreWidth;
        bolt.CoreLineRenderer.endWidth =
            coreWidth * endWidthRatio;

        bolt.GlowLineRenderer.startWidth = glowWidth;
        bolt.GlowLineRenderer.endWidth =
            glowWidth * endWidthRatio;
    }

    private void SetBoltDirection(BoltData bolt)
    {
        if (!sphericalMode)
        {
            return;
        }

        bolt.Direction = Random.onUnitSphere;
    }

    private void UpdateBoltPosition(BoltData bolt)
    {
        Vector3 centerPosition = GetCenterPosition();

        if (!sphericalMode)
        {
            bolt.StartPosition = useStartPoint
                ? GetStartPosition(centerPosition)
                : centerPosition;

            bolt.EndPosition = endPoint.position;

            return;
        }

        float startRadius = 0f;

        if (useStartPoint && startPoint != null)
        {
            startRadius = Vector3.Distance(
                centerPosition,
                startPoint.position);
        }

        float endRadius = Vector3.Distance(
            centerPosition,
            endPoint.position);

        bolt.StartPosition =
            centerPosition +
            bolt.Direction * startRadius;

        bolt.EndPosition =
            centerPosition +
            bolt.Direction * endRadius;
    }

    private Vector3 GetCenterPosition()
    {
        if (centerPoint != null)
        {
            return centerPoint.position;
        }

        if (startPoint != null)
        {
            return startPoint.position;
        }

        return transform.position;
    }

    private Vector3 GetStartPosition(
        Vector3 centerPosition)
    {
        if (startPoint != null)
        {
            return startPoint.position;
        }

        return centerPosition;
    }

    private void GenerateLightning(BoltData bolt)
    {
        bolt.Positions[0] =
            bolt.StartPosition;

        bolt.Positions[^1] =
            bolt.EndPosition;

        for (int i = 1; i < segmentCount; i++)
        {
            float progress =
                (float)i / segmentCount;

            Vector3 basePosition =
                Vector3.Lerp(
                    bolt.StartPosition,
                    bolt.EndPosition,
                    progress);

            Vector3 randomOffset =
                Random.insideUnitSphere *
                displacement;

            bolt.Positions[i] =
                basePosition + randomOffset;
        }

        bolt.GlowLineRenderer.SetPositions(
            bolt.Positions);

        bolt.CoreLineRenderer.SetPositions(
            bolt.Positions);
    }
}