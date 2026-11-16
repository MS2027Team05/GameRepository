using UnityEngine;
using UnityEngine.Serialization;

public sealed class SurfaceRaycaster : MonoBehaviour
{
	[Header("Raycast設定")]
	[FormerlySerializedAs("rayDistance")]
	[SerializeField] private float m_RayDistance;

	[FormerlySerializedAs("groundLayer")]
	[SerializeField] private LayerMask m_GroundLayer;

	public bool HasHit { get; private set; }
	public RaycastHit HitInfo { get; private set; }

	public bool TryGetHit(out RaycastHit hitInfo)
	{
		hitInfo = HitInfo;
		return HasHit;
	}

	private void LateUpdate()
	{
		HasHit = Physics.Raycast(
			transform.position,
			-transform.up,
			out RaycastHit hit,
			m_RayDistance,
			m_GroundLayer,
			QueryTriggerInteraction.Ignore);

		HitInfo = hit;
	}

	private void OnDrawGizmosSelected()
	{
		Gizmos.color = Color.yellow;
		Gizmos.DrawRay(
			transform.position,
			-transform.up * m_RayDistance);
	}
}
