using UnityEngine;

public sealed class PointToPointLightningBolt : ALightningBolt
{
    [Header("電撃位置の設定")]
    [SerializeField] private Transform m_StartPoint;
    [SerializeField] private Transform m_EndPoint;

    protected override void UpdateBoltPosition(BoltData bolt)
    {
        bolt.m_StartPosition = m_StartPoint != null
            ? m_StartPoint.position
            : transform.position;

        bolt.m_EndPosition = m_EndPoint != null
            ? m_EndPoint.position
            : transform.position;
    }
}
