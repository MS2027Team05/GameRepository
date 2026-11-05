using UnityEngine;

/// <summary>
/// Triangle境界の検出とEdge種別ごとの処理振り分けを担当します。
/// </summary>
public sealed partial class BallSurfaceSimulation
{
	/// <summary>
	/// Surface方向のTriangle移動を進めます。
	/// </summary>
	private EAdvanceResult AdvanceSurfaceMotion(
		float deltaTime)
	{
		float remainingTime =
			deltaTime;

		int transitionCount =
			0;

		int ignoredEntryEdgeIndex =
			-1;

		while (remainingTime >
			m_MinimumCalculationValue)
		{
			if (transitionCount >=
				m_MaximumTriangleTransitionsPerStep)
			{
				return EAdvanceResult.TransitionLimitReached;
			}

			// Convex Vertexへ到達した後は、
			// SurfacePointをVertexへ固定したまま、
			// Ball中心だけをVertex中心の球面上で進めます。
			//
			// 球面パッチの境界へ到達すると、
			// そのVertexから伸びる実Convex Edge遷移へ戻ります。
			if (m_IsConvexVertexSpherePatch)
			{
				if (!AdvanceConvexVertexSpherePatch(
					ref remainingTime,
					out bool completed))
				{
					return EAdvanceResult.InvalidGeometry;
				}

				if (!completed)
				{
					return EAdvanceResult.Moved;
				}

				ignoredEntryEdgeIndex =
					-1;

				transitionCount++;
				continue;
			}

			// Sharp Convex Edgeへ入った後は、
			// SurfacePointを共有Edge上へ置いたまま、
			// Ball中心の法線方向だけをEdge周りに回転させます。
			//
			// これによりCubeの90度角でも
			//
			// 上面Normal
			//   ↓
			// 円弧状に回転
			//   ↓
			// 側面Normal
			//
			// と連続するため、Heightを保持したまま
			// World位置が飛ばずに隣面へ移行できます。
			if (m_IsConvexEdgeTransition)
			{
				if (!AdvanceConvexEdgeTransition(
					ref remainingTime,
					out bool completed,
					out int completedEntryEdgeIndex))
				{
					return EAdvanceResult.InvalidGeometry;
				}

				if (!completed)
				{
					return EAdvanceResult.Moved;
				}

				ignoredEntryEdgeIndex =
					completedEntryEdgeIndex;

				transitionCount++;
				continue;
			}

			if (!TryGetTriangleWorldData(
				m_CurrentTriangleIndex,
				out Vector3 vertexA,
				out Vector3 vertexB,
				out Vector3 vertexC,
				out Vector3 currentNormal))
			{
				return EAdvanceResult.InvalidGeometry;
			}

			m_SurfacePoint =
				ProjectPointToPlane(
					m_SurfacePoint,
					vertexA,
					currentNormal);

			float currentSpeed =
				m_SurfaceVelocity.magnitude;

			if (currentSpeed <=
				m_MinimumCalculationValue)
			{
				m_SurfaceVelocity =
					Vector3.zero;

				return EAdvanceResult.Moved;
			}

			Vector3 tangentVelocity =
				Vector3.ProjectOnPlane(
					m_SurfaceVelocity,
					currentNormal);

			if (tangentVelocity.sqrMagnitude <=
				m_MinimumCalculationValue *
					m_MinimumCalculationValue)
			{
				m_SurfaceVelocity =
					Vector3.zero;

				return EAdvanceResult.Moved;
			}

			m_SurfaceVelocity =
				tangentVelocity.normalized *
					currentSpeed;

			Vector3 segmentStartPoint =
				m_SurfacePoint;

			Vector3 targetPoint =
				segmentStartPoint +
				m_SurfaceVelocity *
					remainingTime;

			if (!TryCalculateBarycentric(
				segmentStartPoint,
				vertexA,
				vertexB,
				vertexC,
				out Vector3 startBarycentric))
			{
				return EAdvanceResult.InvalidGeometry;
			}

			if (!TryCalculateBarycentric(
				targetPoint,
				vertexA,
				vertexB,
				vertexC,
				out Vector3 endBarycentric))
			{
				return EAdvanceResult.InvalidGeometry;
			}

			if (IsInsideTriangle(
				endBarycentric))
			{
				m_SurfaceDistanceTravelledThisStep +=
					currentSpeed *
					remainingTime;

				m_SurfacePoint =
					ProjectPointToPlane(
						targetPoint,
						vertexA,
						currentNormal);

				return EAdvanceResult.Moved;
			}

			if (!TryFindFirstSurfaceCrossing(
				startBarycentric,
				endBarycentric,
				ignoredEntryEdgeIndex,
				out int crossedEdgeIndex,
				out int simultaneousEdgeIndex,
				out float crossingFraction))
			{
				return EAdvanceResult.InvalidGeometry;
			}

			Vector3 crossingPoint;

			if (simultaneousEdgeIndex >=
					0 &&
				TryGetSharedTriangleVertex(
					crossedEdgeIndex,
					simultaneousEdgeIndex,
					vertexA,
					vertexB,
					vertexC,
					out Vector3 crossingVertex))
			{
				float distanceToVertex =
					Vector3.Distance(
						segmentStartPoint,
						crossingVertex);

				float timeToVertex =
					currentSpeed >
						m_MinimumCalculationValue
						? distanceToVertex /
							currentSpeed
						: 0.0f;

				timeToVertex =
					Mathf.Clamp(
						timeToVertex,
						0.0f,
						remainingTime);

				m_SurfaceDistanceTravelledThisStep +=
					currentSpeed *
						timeToVertex;

				crossingPoint =
					crossingVertex;

				crossingFraction =
					remainingTime >
						m_MinimumCalculationValue
						? timeToVertex /
							remainingTime
						: 0.0f;

				// 2本のEdgeへほぼ同時に到達した場合も、
				// Vertex専用の進行方向へ切り替えません。
				//
				// 「ほんの僅かに片側を先に通った」ものとして、
				// 決定論的に1本だけを先行Edgeへ選びます。
				// その後は通常のEdge遷移を連続して処理するため、
				// Surfaceの展開ルール自体は通常時と同じです。
				crossedEdgeIndex =
					ResolveSimultaneousVertexEdge(
						crossedEdgeIndex,
						simultaneousEdgeIndex);
			}
			else
			{
				float timeToCrossing =
					remainingTime *
						Mathf.Clamp01(
							crossingFraction);

				m_SurfaceDistanceTravelledThisStep +=
					currentSpeed *
						timeToCrossing;

				crossingPoint =
					Vector3.LerpUnclamped(
						segmentStartPoint,
						targetPoint,
						crossingFraction);

				crossingPoint =
					ProjectPointToPlane(
						crossingPoint,
						vertexA,
						currentNormal);
			}

			BallSurfaceMeshData.EEdgeType edgeType =
				m_MeshData.GetEdgeType(
					m_CurrentTriangleIndex,
					crossedEdgeIndex);

			SetLastEdge(
				m_CurrentTriangleIndex,
				crossedEdgeIndex,
				crossingPoint,
				edgeType);

			switch (edgeType)
			{
				case BallSurfaceMeshData.EEdgeType.Boundary:
					m_SurfacePoint =
						crossingPoint;

					return EAdvanceResult.BoundaryEdgeReached;

				case BallSurfaceMeshData.EEdgeType.Invalid:
				case BallSurfaceMeshData.EEdgeType.NonManifold:
					m_SurfacePoint =
						crossingPoint;

					return EAdvanceResult.UnsupportedEdgeReached;
			}

			if (!m_MeshData.TryGetNeighbor(
				m_CurrentTriangleIndex,
				crossedEdgeIndex,
				out int neighborTriangleIndex,
				out int neighborEdgeIndex))
			{
				m_SurfacePoint =
					crossingPoint;

				return EAdvanceResult.UnsupportedEdgeReached;
			}

			if (!TryGetTriangleWorldData(
				neighborTriangleIndex,
				out Vector3 neighborVertexA,
				out _,
				out _,
				out Vector3 neighborNormal))
			{
				return EAdvanceResult.InvalidGeometry;
			}

			if (edgeType ==
				BallSurfaceMeshData.EEdgeType.Concave)
			{
				// ConcaveではBall中心が隣面へ入り込む前に、
				// Sphere半径を考慮して隣面との衝突位置を求めます。
				if (TryHandleConcaveEdgeCollision(
					segmentStartPoint,
					targetPoint,
					crossingPoint,
					crossingFraction,
					currentNormal,
					neighborNormal,
					out float collisionFraction))
				{
					remainingTime *=
						1.0f -
							Mathf.Clamp01(
								collisionFraction);

					ignoredEntryEdgeIndex =
						-1;

					transitionCount++;
					continue;
				}

				// 現在の簡易衝突モデルで安全な衝突点を
				// 求められなかった場合は、従来どおり
				// Edge上で停止側へ倒します。
				m_SurfacePoint =
					crossingPoint;

				return EAdvanceResult.ConcaveEdgeReached;
			}

			if (edgeType ==
				BallSurfaceMeshData.EEdgeType.Convex)
			{
				m_DidTraverseConvexEdgeThisStep =
					true;

				m_SurfacePoint =
					crossingPoint;

				if (!BeginConvexEdgeTransition(
					crossedEdgeIndex,
					neighborTriangleIndex,
					neighborEdgeIndex,
					currentNormal,
					neighborNormal))
				{
					return EAdvanceResult.InvalidGeometry;
				}

				remainingTime *=
					1.0f -
						Mathf.Clamp01(
							crossingFraction);

				transitionCount++;
				continue;
			}

			// Flat / Smooth EdgeはSharpな折れではないため、
			// これまでどおり隣Triangleへ即座に移行します。
			if (edgeType !=
					BallSurfaceMeshData.EEdgeType.Flat &&
				edgeType !=
					BallSurfaceMeshData.EEdgeType.Smooth)
			{
				m_SurfacePoint =
					crossingPoint;

				return EAdvanceResult.UnsupportedEdgeReached;
			}

			if (!TryTransitionFlatOrSmoothEdge(
				crossingPoint,
				currentNormal,
				neighborTriangleIndex,
				neighborVertexA,
				neighborNormal))
			{
				return EAdvanceResult.InvalidGeometry;
			}

			remainingTime *=
				1.0f -
					Mathf.Clamp01(
						crossingFraction);

			ignoredEntryEdgeIndex =
				neighborEdgeIndex;

			transitionCount++;
		}

		return EAdvanceResult.Moved;
	}
	private bool TryFindFirstSurfaceCrossing(
		Vector3 startBarycentric,
		Vector3 endBarycentric,
		int ignoredEntryEdgeIndex,
		out int crossedEdgeIndex,
		out int simultaneousEdgeIndex,
		out float crossingFraction)
	{
		crossedEdgeIndex =
			-1;

		simultaneousEdgeIndex =
			-1;

		crossingFraction =
			float.MaxValue;

		for (int barycentricIndex = 0;
			barycentricIndex < 3;
			barycentricIndex++)
		{
			float startCoordinate =
				GetBarycentricCoordinate(
					startBarycentric,
					barycentricIndex);

			float endCoordinate =
				GetBarycentricCoordinate(
					endBarycentric,
					barycentricIndex);

			if (endCoordinate >=
				-m_BarycentricTolerance)
			{
				continue;
			}

			startCoordinate =
				Mathf.Max(
					0.0f,
					startCoordinate);

			float denominator =
				startCoordinate -
					endCoordinate;

			if (denominator <=
				m_MinimumCalculationValue)
			{
				continue;
			}

			float fraction =
				Mathf.Clamp01(
					startCoordinate /
						denominator);

			int edgeIndex =
				ConvertBarycentricIndexToEdgeIndex(
					barycentricIndex);

			if (edgeIndex ==
					ignoredEntryEdgeIndex &&
				fraction <=
					m_EntryEdgeIgnoreFraction)
			{
				continue;
			}

			if (crossedEdgeIndex < 0 ||
				fraction <
					crossingFraction -
						m_VertexCrossingFractionTolerance)
			{
				crossingFraction =
					fraction;

				crossedEdgeIndex =
					edgeIndex;

				simultaneousEdgeIndex =
					-1;

				continue;
			}

			if (Mathf.Abs(
					fraction -
						crossingFraction) <=
				m_VertexCrossingFractionTolerance &&
				edgeIndex !=
					crossedEdgeIndex)
			{
				simultaneousEdgeIndex =
					edgeIndex;

				crossingFraction =
					Mathf.Min(
						crossingFraction,
						fraction);
			}
		}

		return crossedEdgeIndex >=
			0;
	}

	/// <summary>
	/// 同一Triangleの2本のEdgeが共有する頂点を取得します。
	/// </summary>
	private bool TryGetSharedTriangleVertex(
		int firstEdgeIndex,
		int secondEdgeIndex,
		Vector3 vertexA,
		Vector3 vertexB,
		Vector3 vertexC,
		out Vector3 sharedVertex)
	{
		sharedVertex =
			Vector3.zero;

		if (firstEdgeIndex ==
			secondEdgeIndex)
		{
			return false;
		}

		if ((firstEdgeIndex == 0 &&
				secondEdgeIndex == 1) ||
			(firstEdgeIndex == 1 &&
				secondEdgeIndex == 0))
		{
			sharedVertex =
				vertexB;

			return true;
		}

		if ((firstEdgeIndex == 1 &&
				secondEdgeIndex == 2) ||
			(firstEdgeIndex == 2 &&
				secondEdgeIndex == 1))
		{
			sharedVertex =
				vertexC;

			return true;
		}

		if ((firstEdgeIndex == 2 &&
				secondEdgeIndex == 0) ||
			(firstEdgeIndex == 0 &&
				secondEdgeIndex == 2))
		{
			sharedVertex =
				vertexA;

			return true;
		}

		return false;
	}
	private int ConvertBarycentricIndexToEdgeIndex(
		int barycentricIndex)
	{
		switch (barycentricIndex)
		{
			case 0:
				return 1;

			case 1:
				return 2;

			case 2:
				return 0;

			default:
				return -1;
		}
	}
	private float GetBarycentricCoordinate(
		Vector3 barycentric,
		int index)
	{
		switch (index)
		{
			case 0:
				return barycentric.x;

			case 1:
				return barycentric.y;

			case 2:
				return barycentric.z;

			default:
				return 0.0f;
		}
	}
	private void SetLastEdge(
		int triangleIndex,
		int edgeIndex,
		Vector3 edgePoint,
		BallSurfaceMeshData.EEdgeType edgeType)
	{
		m_LastEdgeTriangleIndex =
			triangleIndex;

		m_LastEdgeIndex =
			edgeIndex;

		m_LastEdgePoint =
			edgePoint;

		m_LastEdgeType =
			edgeType;
	}
	private void ClearLastEdge()
	{
		m_LastEdgeTriangleIndex =
			-1;

		m_LastEdgeIndex =
			-1;

		m_LastEdgePoint =
			Vector3.zero;

		m_LastEdgeType =
			BallSurfaceMeshData.EEdgeType.Boundary;
	}
}
