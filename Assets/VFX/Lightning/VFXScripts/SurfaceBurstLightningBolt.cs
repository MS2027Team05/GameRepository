using UnityEngine;

public sealed class SurfaceBurstLightningBolt : ALightningBolt
{
    [Header("接触判定の設定")]
    [SerializeField] private SurfaceContactDetector m_ContactDetector;

    [Header("放電範囲の設定")]
    [SerializeField] private float m_StartRadius;
    [SerializeField] private float m_Height;
    [SerializeField] private float m_SpreadRadius;

    private RaycastHit m_ActivationHit;
    private bool m_HasPlayRequest;
    private bool m_HasPlayed;

    protected override bool ReusesDeactivatedBolts => false;

    public void Play()
    {
        if (m_ContactDetector == null ||
            !m_ContactDetector.TryGetContact(out RaycastHit hitInfo))
        {
            return;
        }

        Play(hitInfo);
    }

    public void Play(RaycastHit hitInfo)
    {
        if (m_HasPlayed)
        {
            return;
        }

        m_ActivationHit = hitInfo;
        m_HasPlayRequest = true;
        m_HasPlayed = true;
    }

    protected override bool CanSpawnBolt()
    {
        return m_HasPlayRequest;
    }

    protected override void InitializeBolt(BoltData bolt)
    {
        Vector3 surfaceNormal = GetSurfaceNormal();

        Vector3 referenceAxis = Mathf.Abs(Vector3.Dot(surfaceNormal, Vector3.up)) < 0.99f
            ? Vector3.up
            : Vector3.right;

        Vector3 tangent = Vector3.Cross(surfaceNormal, referenceAxis).normalized;
        Vector3 bitangent = Vector3.Cross(surfaceNormal, tangent);
        float angle = Random.Range(0f, Mathf.PI * 2f);
        Vector3 radialDirection =
            tangent * Mathf.Cos(angle) +
            bitangent * Mathf.Sin(angle);

        bolt.m_StartPosition =
            m_ActivationHit.point + radialDirection * m_StartRadius;

        bolt.m_EndPosition =
            m_ActivationHit.point +
            surfaceNormal * m_Height +
            radialDirection * (m_StartRadius + m_SpreadRadius);
    }

    protected override Vector3 AdjustRandomOffset(Vector3 randomOffset)
    {
        return Vector3.ProjectOnPlane(randomOffset, GetSurfaceNormal());
    }

    protected override void UpdateBoltPosition(BoltData bolt)
    {
    }

    private void OnEnable()
    {
        if (m_ContactDetector != null)
        {
            m_ContactDetector.ContactEntered += Play;
            m_ContactDetector.ContactExited += ResetForNextContact;
        }
    }

    private void OnDisable()
    {
        if (m_ContactDetector != null)
        {
            m_ContactDetector.ContactEntered -= Play;
            m_ContactDetector.ContactExited -= ResetForNextContact;
        }
    }

    private void ResetForNextContact()
    {
        m_HasPlayRequest = false;
        m_HasPlayed = false;
        ResetSpawnCycle();
    }

    private Vector3 GetSurfaceNormal()
    {
        return m_ActivationHit.normal.sqrMagnitude > Mathf.Epsilon
            ? m_ActivationHit.normal.normalized
            : transform.up;
    }
}
