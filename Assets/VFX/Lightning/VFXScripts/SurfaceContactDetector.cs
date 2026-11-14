using System;
using UnityEngine;
using UnityEngine.Serialization;

[DefaultExecutionOrder(100)]
[RequireComponent(typeof(SurfaceRaycaster))]
public sealed class SurfaceContactDetector : MonoBehaviour
{
	[Header("接触判定設定")]
	[FormerlySerializedAs("groundDistance")]
	[FormerlySerializedAs("m_GroundDistance")]
	[SerializeField] private float m_ContactDistance;

	[Header("デバッグ")]
	[FormerlySerializedAs("debugIsGrounded")]
	[FormerlySerializedAs("m_DebugIsGrounded")]
	[SerializeField] private bool m_DebugIsContacting;

	[FormerlySerializedAs("showDebugGizmos")]
	[SerializeField] private bool m_ShowDebugGizmos;

	[FormerlySerializedAs("showDebugLog")]
	[SerializeField] private bool m_ShowDebugLog;

	private SurfaceRaycaster m_SurfaceRaycaster;

	public bool IsContacting { get; private set; }

	public event Action<RaycastHit> ContactEntered;
	public event Action ContactExited;

	public bool TryGetContact(out RaycastHit hitInfo)
	{
		if (m_SurfaceRaycaster == null)
		{
			hitInfo = default;
			return false;
		}

		hitInfo = m_SurfaceRaycaster.HitInfo;
		return IsContacting && m_SurfaceRaycaster.HasHit;
	}

	private void Awake()
	{
		m_SurfaceRaycaster = GetComponent<SurfaceRaycaster>();
	}

	private void LateUpdate()
	{
		bool wasContacting = IsContacting;

		if (!m_SurfaceRaycaster.HasHit)
		{
			IsContacting = false;
		}
		else
		{
			float distance = m_SurfaceRaycaster.HitInfo.distance;
			IsContacting = distance <= m_ContactDistance;
		}

		m_DebugIsContacting = IsContacting;

		if (!wasContacting && IsContacting)
		{
			ContactEntered?.Invoke(m_SurfaceRaycaster.HitInfo);

			if (m_ShowDebugLog)
			{
				Debug.Log(
					$"{gameObject.name}: 接触しました",
					this);
			}
		}
		else if (wasContacting && !IsContacting)
		{
			ContactExited?.Invoke();

			if (m_ShowDebugLog)
			{
				Debug.Log(
					$"{gameObject.name}: 接触面から離れました",
					this);
			}
		}
	}

	private void OnDrawGizmos()
	{
		if (!m_ShowDebugGizmos)
		{
			return;
		}

		Gizmos.color = m_DebugIsContacting
			? Color.green
			: Color.red;

		Gizmos.DrawWireSphere(transform.position, 0.05f);
	}
}
