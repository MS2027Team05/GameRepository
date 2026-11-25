using System.Collections.Generic;
using UnityEngine;

public sealed class CubeSurfaceLightningBolt : ALightningBolt
{
    [Header("キューブ表面の設定")]
    [Tooltip("対象のTransform。未設定の場合は自身のTransformを使用します。")]
    [SerializeField] private Transform m_CenterPoint;
    [Tooltip("対象Transformのローカル座標での中心。")]
    [SerializeField] private Vector3 m_LocalCenter;
    [Tooltip("対象Transformのローカル座標での縦横奥行き。各成分を正の値に設定します。")]
    [SerializeField] private Vector3 m_Size;
    [Tooltip("ローカル座標で各面を外側に広げる距離。非均等スケールではワールド空間の距離も変化します。")]
    [Min(0f)]
    [SerializeField] private float m_SurfaceOffset;

    [Header("表面上の電流の設定")]
    [Tooltip("ローカル座標での電流の経路長。揺らぎによる長さの変化は含みません。")]
    [Min(0f)]
    [SerializeField] private float m_PathLength;
    [Tooltip("ローカル座標での移動速度。負の値で逆方向に移動します。")]
    [SerializeField] private float m_CrawlSpeed;

    private readonly Dictionary<BoltData, SurfaceBoltData> m_SurfaceBolts =
        new Dictionary<BoltData, SurfaceBoltData>();

    private sealed class SurfaceBoltData
    {
        public readonly List<Vector3> m_Points = new List<Vector3>();
        public Vector3[] m_Offsets;
        public Vector3 m_Position;
        public Vector3 m_Normal;
        public Vector3 m_Direction;
        public float m_LastLifetime;
    }

    protected override bool CanSpawnBolt()
    {
        return m_Size.x > 0f && m_Size.y > 0f && m_Size.z > 0f && m_PathLength > 0f;
    }

    protected override void InitializeBolt(BoltData bolt)
    {
        if (!m_SurfaceBolts.TryGetValue(bolt, out SurfaceBoltData surface))
        {
            surface = new SurfaceBoltData();
            m_SurfaceBolts.Add(bolt, surface);
        }

        if (surface.m_Offsets == null || surface.m_Offsets.Length != bolt.m_Positions.Length)
        {
            surface.m_Offsets = new Vector3[bolt.m_Positions.Length];
        }

        Vector3 halfSize = GetHalfSize();
        int axis = Random.Range(0, 3);
        float sign = Random.value < 0.5f ? -1f : 1f;
        surface.m_Normal = Vector3.zero;
        surface.m_Normal[axis] = sign;
        surface.m_Position = new Vector3(
            Random.Range(-halfSize.x, halfSize.x),
            Random.Range(-halfSize.y, halfSize.y),
            Random.Range(-halfSize.z, halfSize.z));
        surface.m_Position[axis] = halfSize[axis] * sign;
        Vector3 tangent = Vector3.zero;
        tangent[(axis + 1) % 3] = 1f;
        surface.m_Direction = Quaternion.AngleAxis(Random.Range(0f, 360f), surface.m_Normal) * tangent;
        surface.m_LastLifetime = 0f;
    }

    protected override void UpdateBoltPosition(BoltData bolt)
    {
        // 全頂点は面上の経路から算出します。
    }

    protected override void GenerateLightning(BoltData bolt)
    {
        Vector3[] offsets = m_SurfaceBolts[bolt].m_Offsets;
        for (int i = 0; i < offsets.Length; i++)
        {
            offsets[i] = i == 0 || i == offsets.Length - 1
                ? Vector3.zero
                : Random.insideUnitSphere * Mathf.Max(0f, Displacement);
        }
    }

    protected override void UpdateBoltTransform(BoltData bolt)
    {
        SurfaceBoltData surface = m_SurfaceBolts[bolt];
        Vector3 halfSize = GetHalfSize();
        float movement = m_CrawlSpeed * Mathf.Max(0f, bolt.m_LifetimeTimer - surface.m_LastLifetime);
        surface.m_LastLifetime = bolt.m_LifetimeTimer;
        surface.m_Position = CubeSurfaceLightningPath.ClampToFace(
            surface.m_Position, surface.m_Normal, halfSize);

        Vector3 crawlDirection = movement < 0f ? -surface.m_Direction : surface.m_Direction;
        CubeSurfaceLightningPath.Advance(ref surface.m_Position, ref surface.m_Normal,
            ref crawlDirection, Mathf.Abs(movement), halfSize, null);
        surface.m_Direction = movement < 0f ? -crawlDirection : crawlDirection;

        List<Vector3> points = surface.m_Points;
        points.Clear();
        Vector3 position = surface.m_Position;
        Vector3 normal = surface.m_Normal;
        Vector3 direction = surface.m_Direction;
        points.Add(position);
        int segmentCount = Mathf.Max(1, surface.m_Offsets.Length - 1);
        float stepLength = Mathf.Max(0f, m_PathLength) / segmentCount;

        for (int i = 1; i <= segmentCount; i++)
        {
            // 面の境界を必ず頂点として挿入し、隣接面を結ぶ線の内部への食い込みを防ぎます。
            CubeSurfaceLightningPath.Advance(ref position, ref normal, ref direction,
                stepLength, halfSize, points);
            Vector3 offset = surface.m_Offsets[Mathf.Min(i, surface.m_Offsets.Length - 1)];
            points.Add(CubeSurfaceLightningPath.ClampToFace(position + offset, normal, halfSize));
        }

        Transform center = m_CenterPoint != null ? m_CenterPoint : transform;
        bolt.m_CoreLineRenderer.positionCount = points.Count;
        bolt.m_GlowLineRenderer.positionCount = points.Count;
        for (int i = 0; i < points.Count; i++)
        {
            Vector3 worldPosition = center.TransformPoint(m_LocalCenter + points[i]);
            bolt.m_CoreLineRenderer.SetPosition(i, worldPosition);
            bolt.m_GlowLineRenderer.SetPosition(i, worldPosition);
        }

        bolt.m_StartPosition = center.TransformPoint(m_LocalCenter + points[0]);
        bolt.m_EndPosition = center.TransformPoint(m_LocalCenter + points[points.Count - 1]);
    }

    private Vector3 GetHalfSize()
    {
        // 実行中にサイズが変更された場合も、退化した面によるゼロ距離の反復を避けます。
        float offset = Mathf.Max(0f, m_SurfaceOffset);
        return new Vector3(Mathf.Max(0.0001f, m_Size.x * 0.5f + offset),
            Mathf.Max(0.0001f, m_Size.y * 0.5f + offset),
            Mathf.Max(0.0001f, m_Size.z * 0.5f + offset));
    }
}
