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

	private const string m_SpectatorOnlyLayerName = "SpectatorOnly";

	private Vector3 m_MarkerInitialLocalScale;
	private bool m_HasMarkerInitialScale;

	private void Awake()
	{
		if (m_HighlightMarker != null)
		{
			m_MarkerInitialLocalScale = m_HighlightMarker.transform.localScale;
			m_HasMarkerInitialScale = true;

			int spectatorLayer = LayerMask.NameToLayer(m_SpectatorOnlyLayerName);
			if (spectatorLayer >= 0)
			{
				ApplyLayerRecursively(m_HighlightMarker.transform, spectatorLayer);
			}
		}
	}

	private void LateUpdate()
	{
		if (m_HighlightMarker == null) return;

		// 観戦カメラがアクティブな場合のみ強調マーカーを表示
		Camera spectatorCam = SpectatorDisplayManager.ActiveSpectatorCamera;
		bool isSpectating = spectatorCam != null;

		if (m_HighlightMarker.activeSelf != isSpectating)
		{
			m_HighlightMarker.SetActive(isSpectating);
		}

		if (!m_HasMarkerInitialScale) return;

		if (!isSpectating || !m_KeepMinimumVisibleScale)
		{
			m_HighlightMarker.transform.localScale = m_MarkerInitialLocalScale;
			return;
		}

		float dist = Vector3.Distance(transform.position, spectatorCam.transform.position);
		float refDist = m_ReferenceDistance > 0.001f ? m_ReferenceDistance : 15.0f;
		float scaleMultiplier = Mathf.Clamp(dist / refDist, m_MinScaleMultiplier, m_MaxScaleMultiplier);

		m_HighlightMarker.transform.localScale = m_MarkerInitialLocalScale * scaleMultiplier;
	}

	private static void ApplyLayerRecursively(Transform root, int layer)
	{
		root.gameObject.layer = layer;
		for (int i = 0; i < root.childCount; i++)
		{
			ApplyLayerRecursively(root.GetChild(i), layer);
		}
	}
}
