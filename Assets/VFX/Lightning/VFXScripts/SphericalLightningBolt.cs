using UnityEngine;

public sealed class SphericalLightningBolt : ALightningBolt
{
    [Header("電撃位置の設定")]
    [SerializeField] private Transform m_CenterPoint;
    [SerializeField] private Transform m_StartRadiusPoint;
    [SerializeField] private Transform m_EndRadiusPoint;

    protected override void InitializeBolt(BoltData bolt)
    {
        bolt.m_Direction = Random.onUnitSphere;
    }

    protected override void UpdateBoltPosition(BoltData bolt)
    {
        Vector3 centerPosition = m_CenterPoint != null
            ? m_CenterPoint.position
            : transform.position;

        float startRadius = m_StartRadiusPoint != null
            ? Vector3.Distance(centerPosition, m_StartRadiusPoint.position)
            : 0f;

        float endRadius = m_EndRadiusPoint != null
            ? Vector3.Distance(centerPosition, m_EndRadiusPoint.position)
            : 0f;

        bolt.m_StartPosition = centerPosition + bolt.m_Direction * startRadius;
        bolt.m_EndPosition = centerPosition + bolt.m_Direction * endRadius;
    }
}
