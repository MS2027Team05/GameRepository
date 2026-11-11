using UnityEngine;

// ワールド座標の移動経路を固定長の循環バッファで保持します。
internal sealed class LightningTrailPath
{
	private readonly Vector3[] m_Points;
	private int m_FirstIndex;
	private int m_Count;
	private float m_Length;

	public LightningTrailPath(int capacity)
	{
		m_Points = new Vector3[Mathf.Max(2, capacity)];
	}

	public int Capacity => m_Points.Length;
	public int Count => m_Count;
	public float Length => m_Length;
	public Vector3 LatestPosition => GetPoint(m_Count - 1);

	public void Reset(Vector3 position)
	{
		m_FirstIndex = 0;
		m_Count = 1;
		m_Length = 0f;
		m_Points[0] = position;
	}

	public void Add(Vector3 position, float maxLength)
	{
		if (m_Count == 0)
		{
			Reset(position);
			return;
		}

		float distance = Vector3.Distance(LatestPosition, position);

		if (distance <= 0f)
		{
			return;
		}

		if (m_Count == m_Points.Length)
		{
			RemoveFirst();
		}

		m_Points[(m_FirstIndex + m_Count) % m_Points.Length] = position;
		m_Count++;
		m_Length += distance;

		Trim(Mathf.Max(0f, maxLength));
	}

	public void CopyRecentPath(Vector3[] positions, float requestedLength)
	{
		if (m_Count == 0 || positions.Length == 0)
		{
			return;
		}

		float length = Mathf.Clamp(requestedLength, 0f, m_Length);
		int newerIndex = m_Count - 1;
		float traversedLength = 0f;
		float edgeLength = newerIndex > 0
			? Vector3.Distance(GetPoint(newerIndex), GetPoint(newerIndex - 1))
			: 0f;

		for (int i = 0; i < positions.Length; i++)
		{
			float distance = positions.Length > 1
				? length * i / (positions.Length - 1)
				: 0f;

			while (newerIndex > 1 && traversedLength + edgeLength < distance)
			{
				traversedLength += edgeLength;
				newerIndex--;
				edgeLength = Vector3.Distance(GetPoint(newerIndex), GetPoint(newerIndex - 1));
			}

			positions[i] = newerIndex > 0 && edgeLength > 0f
				? Vector3.Lerp(
					GetPoint(newerIndex),
					GetPoint(newerIndex - 1),
					(distance - traversedLength) / edgeLength)
				: GetPoint(newerIndex);
		}
	}

	private Vector3 GetPoint(int index)
	{
		return m_Points[(m_FirstIndex + index) % m_Points.Length];
	}

	private void RemoveFirst()
	{
		m_Length -= Vector3.Distance(GetPoint(0), GetPoint(1));
		m_FirstIndex = (m_FirstIndex + 1) % m_Points.Length;
		m_Count--;
	}

	private void Trim(float maxLength)
	{
		while (m_Count > 1 && m_Length > maxLength)
		{
			float firstEdgeLength = Vector3.Distance(GetPoint(0), GetPoint(1));
			float excessLength = m_Length - maxLength;

			if (firstEdgeLength <= excessLength)
			{
				RemoveFirst();
				continue;
			}

			// 最古の線分の途中で切り、記録距離を指定した最大長に収めます。
			m_Points[m_FirstIndex] = Vector3.Lerp(
				GetPoint(0), GetPoint(1), excessLength / firstEdgeLength);
			m_Length = maxLength;
		}
	}
}
