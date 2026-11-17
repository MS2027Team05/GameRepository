
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 電撃チャージの進捗UIを管理します。
/// </summary>
public class PlayerChargeProgress : MonoBehaviour
{
	[Header("電撃チャージ")]
	[SerializeField] private ElectricAction m_ElectricAction;

	[Header("UI設定")]
	[SerializeField] private RectTransform m_SegmentsRoot;

	[Header("円形配置設定")]
	[SerializeField] private int m_SegmentCount = 20;
	[SerializeField] private float m_Radius = 75f;
	[SerializeField] private float m_SegmentWidth = 5f;
	[SerializeField] private float m_SegmentHeight = 18f;

	[Header("色設定")]
	[SerializeField] private Color m_ActiveColor = Color.cyan;
	[SerializeField] private Color m_InactiveColor = Color.gray;

	private Image[] m_Segments;

	private void Start()
	{
		CreateSegments();
	}

	private void Update()
	{
		if (m_ElectricAction == null)
			return;

		// チャージ中でなければ進捗を0にする
		if (!m_ElectricAction.IsCharging)
		{
			SetProgress(0f);
			return;
		}

		// 電撃チャージの進捗をUIへ反映
		SetProgress(m_ElectricAction.ChargeRatio);
	}

	/// <summary>
	/// 円形に棒を生成します。
	/// </summary>
	private void CreateSegments()
	{
		if (m_SegmentsRoot == null)
			return;

		m_Segments = new Image[m_SegmentCount];

		for (int i = 0; i < m_SegmentCount; i++)
		{
			GameObject segment = new GameObject(
				$"Segment_{i}",
				typeof(RectTransform),
				typeof(Image));

			segment.transform.SetParent(
				m_SegmentsRoot,
				false);

			RectTransform rect =
				segment.GetComponent<RectTransform>();

			rect.sizeDelta = new Vector2(
				m_SegmentWidth,
				m_SegmentHeight);

			float angle = i * (360f / m_SegmentCount);
			float radian = angle * Mathf.Deg2Rad;

			Vector2 direction = new Vector2(
				Mathf.Sin(radian),
				Mathf.Cos(radian));

			rect.anchoredPosition =
				direction * m_Radius;

			rect.localRotation = Quaternion.Euler(
				0f,
				0f,
				-angle);

			m_Segments[i] =
				segment.GetComponent<Image>();

			m_Segments[i].color = m_InactiveColor;
		}
	}

	/// <summary>
	/// チャージ進捗に応じて棒の色を変更します。
	/// progressは0～1で指定します。
	/// </summary>
	public void SetProgress(float progress)
	{
		if (m_Segments == null)
			return;

		progress = Mathf.Clamp01(progress);

		int activeCount = Mathf.RoundToInt(
			progress * m_SegmentCount);

		for (int i = 0; i < m_Segments.Length; i++)
		{
			m_Segments[i].color =
				i < activeCount
				? m_ActiveColor
				: m_InactiveColor;
		}
	}
}
