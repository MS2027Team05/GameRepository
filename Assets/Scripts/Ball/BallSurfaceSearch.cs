using UnityEngine;

/// <summary>
/// ボール周辺のAttachSurface探索を担当します。
/// </summary>
/// <remarks>
/// 飛行中は26方向の軽量探索、
/// 停止後はFibonacci Sphereを利用した64方向探索を行います。
/// </remarks>
public sealed class BallSurfaceSearch
{
	private const int m_StoppedSearchRayCount = 64;
	private const float m_GoldenAngle = 2.39996323f;

	/// <summary>
	/// 飛行中用の軽量な26方向探索を行い、
	/// 最も近いAttachSurfaceを取得します。
	/// </summary>
	/// <param name="origin">
	/// 探索開始位置です。
	/// </param>
	/// <param name="searchDistance">
	/// 探索する最大距離です。
	/// </param>
	/// <param name="surfaceMask">
	/// 探索対象Layerです。
	/// </param>
	/// <param name="nearestHit">
	/// 最も近かったSurface情報です。
	/// </param>
	/// <returns>
	/// Surfaceを発見した場合はtrueです。
	/// </returns>
	public bool TrySearchLowCost(
		Vector3 origin,
		float searchDistance,
		LayerMask surfaceMask,
		out RaycastHit nearestHit)
	{
		nearestHit =
			default;

		if (searchDistance <=
			0.0f)
		{
			return false;
		}

		float nearestDistance =
			float.MaxValue;

		bool hasHit =
			false;

		for (int x = -1;
			x <= 1;
			x++)
		{
			for (int y = -1;
				y <= 1;
				y++)
			{
				for (int z = -1;
					z <= 1;
					z++)
				{
					if (x == 0 &&
						y == 0 &&
						z == 0)
					{
						continue;
					}

					Vector3 direction =
						new Vector3(
							x,
							y,
							z).normalized;

					if (!Physics.Raycast(
						origin,
						direction,
						out RaycastHit hit,
						searchDistance,
						surfaceMask,
						QueryTriggerInteraction.Ignore))
					{
						continue;
					}

					if (hit.distance >=
						nearestDistance)
					{
						continue;
					}

					nearestDistance =
						hit.distance;

					nearestHit =
						hit;

					hasHit =
						true;
				}
			}
		}

		return hasHit;
	}

	/// <summary>
	/// 停止後用に球面へ均等な64方向探索を行い、
	/// 最も近いAttachSurfaceを取得します。
	/// </summary>
	/// <param name="origin">
	/// 探索開始位置です。
	/// </param>
	/// <param name="searchDistance">
	/// 探索する最大距離です。
	/// </param>
	/// <param name="surfaceMask">
	/// 探索対象Layerです。
	/// </param>
	/// <param name="nearestHit">
	/// 最も近かったSurface情報です。
	/// </param>
	/// <returns>
	/// Surfaceを発見した場合はtrueです。
	/// </returns>
	public bool TrySearchHighPrecision(
		Vector3 origin,
		float searchDistance,
		LayerMask surfaceMask,
		out RaycastHit nearestHit)
	{
		nearestHit =
			default;

		if (searchDistance <=
			0.0f)
		{
			return false;
		}

		float nearestDistance =
			float.MaxValue;

		bool hasHit =
			false;

		for (int i = 0;
			i < m_StoppedSearchRayCount;
			i++)
		{
			Vector3 direction =
				GetFibonacciSphereDirection(
					i,
					m_StoppedSearchRayCount);

			if (!Physics.Raycast(
				origin,
				direction,
				out RaycastHit hit,
				searchDistance,
				surfaceMask,
				QueryTriggerInteraction.Ignore))
			{
				continue;
			}

			if (hit.distance >=
				nearestDistance)
			{
				continue;
			}

			nearestDistance =
				hit.distance;

			nearestHit =
				hit;

			hasHit =
				true;
		}

		return hasHit;
	}

	/// <summary>
	/// Fibonacci Sphereを使用して、
	/// 球面上へ均等に配置された探索方向を取得します。
	/// </summary>
	private Vector3 GetFibonacciSphereDirection(
		int index,
		int count)
	{
		float normalizedIndex =
			(index + 0.5f) /
			count;

		float y =
			1.0f -
			2.0f *
			normalizedIndex;

		float horizontalRadius =
			Mathf.Sqrt(
				Mathf.Max(
					0.0f,
					1.0f -
					y * y));

		float angle =
			m_GoldenAngle *
			index;

		float x =
			Mathf.Cos(
				angle) *
			horizontalRadius;

		float z =
			Mathf.Sin(
				angle) *
			horizontalRadius;

		return new Vector3(
			x,
			y,
			z);
	}
}
