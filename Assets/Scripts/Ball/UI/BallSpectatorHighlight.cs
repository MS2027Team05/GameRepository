using UnityEngine;

/// <summary>
/// 観戦Cameraからボールを見失いにくくするための強調Markerを管理します。
/// </summary>
/// <remarks>
/// 主な処理の流れ:
/// 1. Markerの初期Scaleを保存し、SpectatorOnly Layerを子階層へ適用します。
/// 2. 観戦Cameraが有効な間だけMarkerを表示します。
/// 3. Cameraとボールの距離を計算します。
/// 4. 距離に応じてMarkerのScaleを補正し、遠距離でも一定の視認性を保ちます。
/// 5. 観戦終了時はMarkerを非表示にし、初期Scaleへ戻します。
///
/// 通常プレイヤー向けの画面端IndicatorはBallScreenIndicatorが担当します。
/// </remarks>
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

	/// <summary>
	/// Markerの初期Scaleを保存し、観戦専用Layerを子階層へ設定します。
	/// </summary>
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

	/// <summary>
	/// 観戦Cameraの有無と距離に応じてMarkerの表示状態とScaleを更新します。
	/// </summary>
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

	/// <summary>
	/// 指定Transform以下のGameObjectへ同じLayerを再帰的に適用します。
	/// </summary>
	private static void ApplyLayerRecursively(Transform root, int layer)
	{
		root.gameObject.layer = layer;
		for (int i = 0; i < root.childCount; i++)
		{
			ApplyLayerRecursively(root.GetChild(i), layer);
		}
	}
}
