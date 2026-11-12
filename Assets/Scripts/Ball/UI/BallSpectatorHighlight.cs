using UnityEngine;

/// <summary>
/// 観戦カメラから見てボールが見失われないよう、強調表示・視認性向上を行うコンポーネント。
/// </summary>
public class BallSpectatorHighlight : MonoBehaviour
{
	[Header("強調表示用マーカー(任意・未設定時は自動パルス)")]
	[SerializeField] private GameObject m_HighlightMarker;

	[Header("距離に応じた最小スケール維持設定")]
	[SerializeField] private bool m_KeepMinimumVisibleScale = true;
	[SerializeField] private float m_ReferenceDistance = 15.0f;
	[SerializeField] private float m_MinScaleMultiplier = 1.0f;
	[SerializeField] private float m_MaxScaleMultiplier = 2.5f;

	private Vector3 m_InitialLocalScale;

	private void Awake()
	{
		m_InitialLocalScale = transform.localScale;
	}

	private void LateUpdate()
	{
		// 観戦カメラがアクティブな場合のみ強調を適用
		Camera spectatorCam = SpectatorDisplayManager.ActiveSpectatorCamera;
		bool isSpectating = spectatorCam != null;

		if (m_HighlightMarker != null)
		{
			m_HighlightMarker.SetActive(isSpectating);
		}

		if (!isSpectating || !m_KeepMinimumVisibleScale)
		{
			transform.localScale = m_InitialLocalScale;
			return;
		}

		float dist = Vector3.Distance(transform.position, spectatorCam.transform.position);
		float refDist = m_ReferenceDistance > 0.001f ? m_ReferenceDistance : 15.0f;
		float scaleMultiplier = Mathf.Clamp(dist / refDist, m_MinScaleMultiplier, m_MaxScaleMultiplier);

		transform.localScale = m_InitialLocalScale * scaleMultiplier;
	}
}
