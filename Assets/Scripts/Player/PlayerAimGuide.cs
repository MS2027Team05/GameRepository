using UnityEngine;

/// <summary>
/// エイムモード時のレティクル表示・軌道予測線描画を担当するモジュール。
/// </summary>
public class PlayerAimGuide : MonoBehaviour, IAimGuide
{
	[Header("照準UI")]
	[SerializeField] private GameObject m_AimReticle;

	[Header("予測線")]
	[SerializeField] private LineRenderer m_AimLine;

	[Header("着地マーカー")]
	[SerializeField] private GameObject m_LandingMarker;

	[Header("Raycast設定")]
	[SerializeField] private LayerMask m_AimTargetLayers;
	[SerializeField] private float m_MaxAimDistance = 100.0f;

	private bool m_IsAiming;

	private void Awake()
	{
		SetAimGuideActive(false);
	}

	public void ShowAimGuide(bool show)
	{
		m_IsAiming = show;
		SetAimGuideActive(show);

		if (!show)
		{
			ClearAimLine();
		}
	}

	public void UpdateAimDirection(Vector3 direction)
	{
		if (!m_IsAiming)
		{
			return;
		}

		if (direction.sqrMagnitude <= 0.0f)
		{
			return;
		}

		// 実際に画面を描画しているMain Cameraを使用します。
		Vector3 rayOrigin;

		if (Camera.main != null)
		{
			rayOrigin = Camera.main.transform.position;
		}
		else
		{
			rayOrigin = transform.position;
		}

		Vector3 normalizedDirection = direction.normalized;

		// カメラの正面方向へRaycastします。
		if (Physics.Raycast(
				rayOrigin,
				normalizedDirection,
				out RaycastHit hit,
				m_MaxAimDistance,
				m_AimTargetLayers,
				QueryTriggerInteraction.Ignore))
		{
			// プレイヤーから着地点まで予測線を描画します。
			UpdateAimLine(transform.position, hit.point);

			// 着地点にマーカーを表示します。
			UpdateLandingMarker(hit.point, hit.normal);
		}
		else
		{
			// 何にも当たらない場合は、
			// 予測線と着地マーカーを非表示にします。
			ClearAimLine();
		}
	}

	private void SetAimGuideActive(bool active)
	{
		if (m_AimReticle != null)
		{
			m_AimReticle.SetActive(active);
		}

		if (m_AimLine != null)
		{
			m_AimLine.enabled = active;
		}

		if (m_LandingMarker != null)
		{
			m_LandingMarker.SetActive(active);
		}
	}

	private void UpdateAimLine(
		Vector3 startPoint,
		Vector3 endPoint)
	{
		if (m_AimLine == null)
		{
			return;
		}

		m_AimLine.positionCount = 2;
		m_AimLine.SetPosition(0, startPoint);
		m_AimLine.SetPosition(1, endPoint);
	}

	private void UpdateLandingMarker(
		Vector3 position,
		Vector3 normal)
	{
		if (m_LandingMarker == null)
		{
			return;
		}

		// 地面に完全に重ならないよう、
		// 法線方向へ少し浮かせる。
		const float markerOffset = 0.06f;

		m_LandingMarker.SetActive(true);

		m_LandingMarker.transform.position =
			position + normal * markerOffset;

		// マーカーの上方向を、
		// 接地面の法線方向へ合わせる。
		m_LandingMarker.transform.rotation =
			Quaternion.FromToRotation(
				Vector3.up,
				normal);
	}

	private void HideLandingMarker()
	{
		if (m_LandingMarker != null)
		{
			m_LandingMarker.SetActive(false);
		}
	}

	private void ClearAimLine()
	{
		if (m_AimLine == null)
		{
			return;
		}

		m_AimLine.positionCount = 0;
		HideLandingMarker();
	}
}
