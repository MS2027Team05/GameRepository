using System.Collections.Generic;
using UnityEngine;

public sealed class SphereSurfaceLightningBolt : ALightningBolt
{
	[Header("球面の設定")]
	[Tooltip("ボールの中心。未設定の場合は自身のTransformを使用します。")]
	[SerializeField] private Transform m_CenterPoint;

	[Tooltip("中心Transformのローカル座標での半径。球形を保つ均等スケールを想定します。")]
	[Min(0f)]
	[SerializeField] private float m_Radius;

	[Tooltip("中心Transformのローカル座標で、表面から電撃を浮かせる距離。")]
	[Min(0f)]
	[SerializeField] private float m_SurfaceOffset;

	[Header("球面上の電撃の設定")]
	[Tooltip("電撃が球面上に描く円弧の角度(度)。")]
	[Range(0f, 180f)]
	[SerializeField] private float m_ArcAngle;

	[Tooltip("電撃が球面上を移動する角速度(度/秒)。負の値で逆方向に移動します。")]
	[SerializeField] private float m_CrawlSpeed;

	private readonly Dictionary<BoltData, SurfaceBoltData> m_SurfaceBolts =
		new Dictionary<BoltData, SurfaceBoltData>();

	private sealed class SurfaceBoltData
	{
		public Vector3[] m_LocalDirections;
		public Vector3 m_StartDirection;
		public Vector3 m_RotationAxis;
	}

	protected override bool CanSpawnBolt()
	{
		return m_Radius > 0f && m_ArcAngle > 0f;
	}

	protected override void InitializeBolt(BoltData bolt)
	{
		if (!m_SurfaceBolts.TryGetValue(bolt, out SurfaceBoltData surfaceBolt))
		{
			surfaceBolt = new SurfaceBoltData();
			m_SurfaceBolts.Add(bolt, surfaceBolt);
		}

		// 配列は初回使用時と分割数の変更時のみ確保し、電撃の再生時に再利用します。
		if (surfaceBolt.m_LocalDirections == null ||
			surfaceBolt.m_LocalDirections.Length != bolt.m_Positions.Length)
		{
			surfaceBolt.m_LocalDirections = new Vector3[bolt.m_Positions.Length];
		}

		surfaceBolt.m_StartDirection = Random.onUnitSphere;

		Vector3 referenceAxis = Mathf.Abs(surfaceBolt.m_StartDirection.y) < 0.99f
			? Vector3.up
			: Vector3.right;

		Vector3 tangent = Vector3.Cross(surfaceBolt.m_StartDirection, referenceAxis).normalized;
		surfaceBolt.m_RotationAxis =
			Quaternion.AngleAxis(Random.Range(0f, 360f), surfaceBolt.m_StartDirection) * tangent;
	}

	protected override void UpdateBoltPosition(BoltData bolt)
	{
		// 始点と終点を含む全頂点を球面上で生成するため、直線用の端点更新は行いません。
	}

	protected override void GenerateLightning(BoltData bolt)
	{
		SurfaceBoltData surfaceBolt = m_SurfaceBolts[bolt];
		int segmentCount = surfaceBolt.m_LocalDirections.Length - 1;
		float radius = Mathf.Max(m_Radius, Mathf.Epsilon);

		for (int i = 0; i <= segmentCount; i++)
		{
			float progress = segmentCount > 0 ? (float)i / segmentCount : 0f;
			Vector3 direction =
				Quaternion.AngleAxis(m_ArcAngle * progress, surfaceBolt.m_RotationAxis) *
				surfaceBolt.m_StartDirection;

			if (i > 0 && i < segmentCount)
			{
				Vector3 offset = Vector3.ProjectOnPlane(
					Random.insideUnitSphere * Displacement, direction);

				// 接線方向に揺らした後で正規化し、球面からの距離を一定に保ちます。
				direction = (direction + offset / radius).normalized;
			}

			surfaceBolt.m_LocalDirections[i] = direction;
		}
	}

	protected override void UpdateBoltTransform(BoltData bolt)
	{
		SurfaceBoltData surfaceBolt = m_SurfaceBolts[bolt];
		Transform centerPoint = m_CenterPoint != null ? m_CenterPoint : transform;
		Quaternion crawlRotation = Quaternion.AngleAxis(
			m_CrawlSpeed * bolt.m_LifetimeTimer, surfaceBolt.m_RotationAxis);
		float surfaceRadius = Mathf.Max(0f, m_Radius) + Mathf.Max(0f, m_SurfaceOffset);

		for (int i = 0; i < bolt.m_Positions.Length; i++)
		{
			Vector3 localPosition =
				crawlRotation * surfaceBolt.m_LocalDirections[i] * surfaceRadius;
			bolt.m_Positions[i] = centerPoint.TransformPoint(localPosition);
		}

		bolt.m_StartPosition = bolt.m_Positions[0];
		bolt.m_EndPosition = bolt.m_Positions[^1];
		bolt.m_GlowLineRenderer.SetPositions(bolt.m_Positions);
		bolt.m_CoreLineRenderer.SetPositions(bolt.m_Positions);
	}
}
