using UnityEngine;

/// <summary>
/// 着地地点に表示するリング型マーカーを制御します。
/// 地面の法線に合わせて回転し、3重リングを表示します。
/// </summary>
public class LandingMarkerEffect : MonoBehaviour
{
	[Header("リング")]
	[SerializeField] private LineRenderer m_InnerRing;
	[SerializeField] private LineRenderer m_MiddleRing;
	[SerializeField] private LineRenderer m_OuterRing;

	[Header("リング設定")]
	[SerializeField] private int m_Segments = 48;

	[SerializeField] private float m_InnerRadius = 0.42f;
	[SerializeField] private float m_MiddleRadius = 0.60f;
	[SerializeField] private float m_OuterRadius = 0.85f;

	[Header("リング太さ")]
	[SerializeField] private float m_InnerWidth = 0.055f;
	[SerializeField] private float m_MiddleWidth = 0.025f;
	[SerializeField] private float m_OuterWidth = 0.008f;

	[Header("リング明るさ・透明度")]
	[SerializeField] private Color m_InnerColor = new Color(0.1f, 0.7f, 1.0f, 1.0f);
	[SerializeField] private Color m_MiddleColor = new Color(0.1f, 0.7f, 1.0f, 0.65f);
	[SerializeField] private Color m_OuterColor = new Color(0.1f, 0.7f, 1.0f, 0.25f);

	[Header("アニメーション")]
	[SerializeField] private bool m_EnablePulse = true;
	[SerializeField] private float m_PulseSpeed = 3.0f;
	[SerializeField] private float m_PulseAmount = 0.08f;

	private float m_PulseTime;

	private void Awake()
	{
		SetupLineRenderer(
			m_InnerRing,
			m_InnerWidth,
			m_InnerColor);

		SetupLineRenderer(
			m_MiddleRing,
			m_MiddleWidth,
			m_MiddleColor);

		SetupLineRenderer(
			m_OuterRing,
			m_OuterWidth,
			m_OuterColor);

		CreateRing(
			m_InnerRing,
			m_InnerRadius);

		CreateRing(
			m_MiddleRing,
			m_MiddleRadius);

		CreateRing(
			m_OuterRing,
			m_OuterRadius);
	}

	private void Update()
	{
		if (!m_EnablePulse)
		{
			return;
		}

		m_PulseTime += Time.deltaTime * m_PulseSpeed;

		float pulse =
			1.0f + Mathf.Sin(m_PulseTime) * m_PulseAmount;

		CreateRing(
			m_InnerRing,
			m_InnerRadius * pulse);

		CreateRing(
			m_MiddleRing,
			m_MiddleRadius * pulse);

		// 外周リングは動かさず、安定して見えるようにする
		CreateRing(
			m_OuterRing,
			m_OuterRadius);
	}

	/// <summary>
	/// LineRendererの基本設定。
	/// </summary>
	private void SetupLineRenderer(
		LineRenderer line,
		float width,
		Color color)
	{
		if (line == null)
		{
			return;
		}

		line.loop = true;
		line.useWorldSpace = false;
		line.positionCount = m_Segments;

		// リングごとの太さ
		line.startWidth = width;
		line.endWidth = width;

		// リングごとの明るさ・透明度
		line.startColor = color;
		line.endColor = color;
	}

	/// <summary>
	/// XZ平面上にリングを作成します。
	/// </summary>
	private void CreateRing(
		LineRenderer line,
		float radius)
	{
		if (line == null)
		{
			return;
		}

		line.positionCount = m_Segments;

		for (int i = 0; i < m_Segments; i++)
		{
			float angle =
				Mathf.PI * 2.0f * i / m_Segments;

			float x =
				Mathf.Cos(angle) * radius;

			float z =
				Mathf.Sin(angle) * radius;

			line.SetPosition(
				i,
				new Vector3(x, 0.0f, z));
		}
	}

	/// <summary>
	/// マーカーを表示します。
	/// </summary>
	public void Show()
	{
		SetVisible(true);
	}

	/// <summary>
	/// マーカーを非表示にします。
	/// </summary>
	public void Hide()
	{
		SetVisible(false);
	}

	private void SetVisible(bool visible)
	{
		if (m_InnerRing != null)
		{
			m_InnerRing.enabled = visible;
		}

		if (m_MiddleRing != null)
		{
			m_MiddleRing.enabled = visible;
		}

		if (m_OuterRing != null)
		{
			m_OuterRing.enabled = visible;
		}
	}

	/// <summary>
	/// 着地地点と地面の向きを設定します。
	/// </summary>
	public void SetLandingPosition(
		Vector3 position,
		Vector3 normal)
	{
		const float markerOffset = 0.03f;

		transform.position =
			position + normal * markerOffset;

		transform.rotation =
			Quaternion.FromToRotation(
				Vector3.up,
				normal);
	}
}
