using System.Collections.Generic;
using UnityEngine;

public sealed class TrailLightningBolt : ALightningBolt
{
    [Header("軌跡の追跡設定")]
    [Tooltip("移動経路を記録する対象。未設定の場合は自身のTransformを使用します。")]
    [SerializeField] private Transform m_TargetPoint;

    [Tooltip("経路に新しい点を記録する最小移動距離(ワールド単位)。")]
    [Min(0f)]
    [SerializeField] private float m_SampleDistance;

    [Tooltip("記録しておく経路の最大長(ワールド単位)。")]
    [Min(0f)]
    [SerializeField] private float m_MaxTrailLength;

    [Tooltip("記録できる点の上限。上限に達した場合は古い点から破棄します。")]
    [Min(2)]
    [SerializeField] private int m_MaxSampleCount;

    [Tooltip("1フレームでこの距離以上移動した場合、経路を区切ります(ワールド単位)。")]
    [Min(0f)]
    [SerializeField] private float m_TeleportDistance;

    [Header("軌跡上の電撃設定")]
    [Tooltip("新しい位置から後方へ描く電撃1本の長さ(ワールド単位)。経路が短い場合はその長さに合わせます。")]
    [Min(0f)]
    [SerializeField] private float m_BoltLength;

    private readonly Dictionary<BoltData, Vector3[]> m_BoltPaths =
        new Dictionary<BoltData, Vector3[]>();

    private LightningTrailPath m_Path;
    private Transform m_PreviousTarget;
    private Vector3 m_PreviousPosition;
    private float m_LastMovementTime;
    private bool m_HasPreviousPosition;
    private bool m_HasPendingTrail;
    private bool m_IsMoving;
    private BallController m_BallController;

    private void Awake()
    {
        // ボール配下の軌跡だけ投球状態で制限し、単独のVFXテストは従来どおり動作させます。
        m_BallController = GetComponentInParent<BallController>();
    }

    protected override void UpdateSource()
    {
        if (m_BallController != null && !m_BallController.IsShotInFlight)
        {
            ResetTracking();
            return;
        }

        if (m_SampleDistance <= 0f || m_MaxTrailLength <= 0f ||
            m_MaxSampleCount < 2 || m_TeleportDistance <= 0f || m_BoltLength <= 0f)
        {
            ResetTracking();
            return;
        }

        Transform target = m_TargetPoint != null ? m_TargetPoint : transform;
        Vector3 position = target.position;

        if (m_Path == null || m_Path.Capacity != m_MaxSampleCount)
        {
            m_Path = new LightningTrailPath(m_MaxSampleCount);
            m_HasPreviousPosition = false;
        }

        if (!m_HasPreviousPosition || m_PreviousTarget != target)
        {
            BeginPath(target, position);
            return;
        }

        Vector3 frameMovement = position - m_PreviousPosition;
        m_PreviousPosition = position;

        if (frameMovement.sqrMagnitude >= m_TeleportDistance * m_TeleportDistance)
        {
            BeginPath(target, position);
            return;
        }

        if (frameMovement.sqrMagnitude <= 0f)
        {
            m_HasPendingTrail = false;

            // 物理更新の合間の静止フレームで経路を切らないよう、更新間隔分の猶予を設けます。
            float stopDelay = Time.fixedDeltaTime + Time.deltaTime;

            if (m_IsMoving && Time.time - m_LastMovementTime > stopDelay)
            {
                BeginPath(target, position);
            }

            return;
        }

        m_IsMoving = true;
        m_LastMovementTime = Time.time;

        if ((position - m_Path.LatestPosition).sqrMagnitude < m_SampleDistance * m_SampleDistance)
        {
            return;
        }

        m_Path.Add(position, m_MaxTrailLength);
        m_HasPendingTrail = true;
    }

    protected override bool CanSpawnBolt()
    {
        return m_IsMoving && m_HasPendingTrail && m_Path != null &&
            m_Path.Count > 1 && m_Path.Length > 0f;
    }

    protected override bool CanPlayPrefabEffect()
    {
        // 経路のサンプリング間隔でプレハブが明滅しないよう、移動状態で判定します。
        return m_IsMoving;
    }

    protected override void UpdatePrefabEffectTransform(Transform effectTransform)
    {
        Transform target = m_TargetPoint != null ? m_TargetPoint : transform;
        effectTransform.SetPositionAndRotation(target.position, target.rotation);
    }

    protected override void InitializeBolt(BoltData bolt)
    {
        if (!m_BoltPaths.TryGetValue(bolt, out Vector3[] positions) ||
            positions.Length != bolt.m_Positions.Length)
        {
            positions = new Vector3[bolt.m_Positions.Length];
            m_BoltPaths[bolt] = positions;
        }

        // 発生時の経路を固定し、循環バッファ更新後も表示位置を維持します。
        m_Path.CopyRecentPath(positions, m_BoltLength);
        m_HasPendingTrail = false;
    }

    protected override void UpdateBoltPosition(BoltData bolt)
    {
        Vector3[] positions = m_BoltPaths[bolt];
        bolt.m_StartPosition = positions[0];
        bolt.m_EndPosition = positions[^1];
    }

    protected override void GenerateLightning(BoltData bolt)
    {
        Vector3[] positions = m_BoltPaths[bolt];
        int segmentCount = positions.Length - 1;

        for (int i = 0; i < positions.Length; i++)
        {
            float progress = segmentCount > 0 ? (float)i / segmentCount : 0f;
            float offsetRatio = 4f * progress * (1f - progress);
            bolt.m_Positions[i] = positions[i] +
                Random.insideUnitSphere * (Displacement * offsetRatio);
        }

        bolt.m_GlowLineRenderer.SetPositions(bolt.m_Positions);
        bolt.m_CoreLineRenderer.SetPositions(bolt.m_Positions);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        ResetTracking();
    }

    private void BeginPath(Transform target, Vector3 position)
    {
        ClearActiveBolts();
        m_Path.Reset(position);
        m_PreviousTarget = target;
        m_PreviousPosition = position;
        m_LastMovementTime = Time.time;
        m_HasPreviousPosition = true;
        m_HasPendingTrail = false;
        m_IsMoving = false;
        ResetSpawnCycle();
    }

    private void ResetTracking()
    {
        ClearActiveBolts();
        m_Path?.Reset(Vector3.zero);
        m_HasPreviousPosition = false;
        m_HasPendingTrail = false;
        m_IsMoving = false;
        ResetSpawnCycle();
    }
}
