using UnityEngine;

/// <summary>
/// Concave EdgeでBall中心を維持したまま
/// 隣Surfaceへ遷移する処理を担当します。
/// </summary>
public sealed partial class BallSurfaceSimulation
{
	/// <summary>
	/// Concave Edgeへ到達する前に、
	/// BallRadius + Height分だけ手前の位置で隣Surfaceへ遷移します。
	/// </summary>
	/// <remarks>
	/// SurfacePoint自体は旧面から新面へ切り替わりますが、
	/// Ball中心位置は同じWorld座標を維持します。
	///
	/// Height / VerticalVelocityはそのまま保持し、
	/// SurfaceVelocityだけを新しいSurface法線へ回転させます。
	/// </remarks>
	private bool TryTransitionConcaveEdge(
		Vector3 segmentStartPoint,
		Vector3 targetPoint,
		Vector3 edgePoint,
		float crossingFraction,
		Vector3 currentNormal,
		int neighborTriangleIndex,
		int neighborEdgeIndex,
		Vector3 neighborNormal,
		out float transitionFraction,
		out int completedEntryEdgeIndex)
	{
		transitionFraction =
			0.0f;

		completedEntryEdgeIndex =
			-1;

		float centerOffset =
			Mathf.Max(
				m_BallRadius +
					m_Height,
				m_MinimumCalculationValue);

		Vector3 startCenter =
			segmentStartPoint +
			currentNormal *
				centerOffset;

		Vector3 edgeCenter =
			edgePoint +
			currentNormal *
				centerOffset;

		float startClearance =
			Vector3.Dot(
				startCenter -
					edgePoint,
				neighborNormal) -
			centerOffset;

		float edgeClearance =
			Vector3.Dot(
				edgeCenter -
					edgePoint,
				neighborNormal) -
			centerOffset;

		// 実Edgeへ到達しても隣面から十分離れている場合は、
		// Concave遷移として扱えません。
		if (edgeClearance >
			m_MinimumCalculationValue)
		{
			return false;
		}

		float fractionToEdge;

		if (startClearance <=
			m_MinimumCalculationValue)
		{
			// すでに有限半径分の遷移位置へ到達している場合は、
			// このStepの開始地点で即座に座標系を切り替えます。
			fractionToEdge =
				0.0f;
		}
		else
		{
			float clearanceDifference =
				startClearance -
					edgeClearance;

			if (clearanceDifference <=
				m_MinimumCalculationValue)
			{
				return false;
			}

			fractionToEdge =
				Mathf.Clamp01(
					startClearance /
						clearanceDifference);
		}

		transitionFraction =
			Mathf.Clamp01(
				crossingFraction *
					fractionToEdge);

		Vector3 transitionSurfacePoint =
			Vector3.LerpUnclamped(
				segmentStartPoint,
				targetPoint,
				transitionFraction);

		transitionSurfacePoint =
			ProjectPointToPlane(
				transitionSurfacePoint,
				segmentStartPoint,
				currentNormal);

		Vector3 ballCenter =
			transitionSurfacePoint +
			currentNormal *
				centerOffset;

		if (!TryRotateConcaveSurfaceVelocity(
			m_SurfaceVelocity,
			currentNormal,
			neighborNormal,
			out Vector3 neighborSurfaceVelocity))
		{
			return false;
		}

		if (!TryResolveConcaveTargetTriangle(
			ballCenter,
			centerOffset,
			edgePoint,
			neighborTriangleIndex,
			neighborEdgeIndex,
			neighborSurfaceVelocity,
			out int resolvedTriangleIndex,
			out Vector3 resolvedSurfacePoint,
			out Vector3 resolvedSurfaceVelocity,
			out int resolvedEntryEdgeIndex))
		{
			return false;
		}

		m_CurrentTriangleIndex =
			resolvedTriangleIndex;

		m_SurfacePoint =
			resolvedSurfacePoint;

		m_SurfaceVelocity =
			resolvedSurfaceVelocity;

		completedEntryEdgeIndex =
			resolvedEntryEdgeIndex;

		return true;
	}

	/// <summary>
	/// Ball中心を維持できるSurfacePointを含むTriangleを解決します。
	/// </summary>
	/// <remarks>
	/// Concave Edge直後のTriangleだけでなく、
	/// 同一Surface上のFlat / Smooth Triangleを必要な分だけ辿ります。
	///
	/// Convex / Concave / Boundaryなど別のSharp Edgeへ到達した場合は、
	/// 1回のConcave遷移として安全に解決できないためfalseを返します。
	/// </remarks>
	private bool TryResolveConcaveTargetTriangle(
		Vector3 ballCenter,
		float centerOffset,
		Vector3 entryPoint,
		int initialTriangleIndex,
		int initialEntryEdgeIndex,
		Vector3 initialSurfaceVelocity,
		out int resolvedTriangleIndex,
		out Vector3 resolvedSurfacePoint,
		out Vector3 resolvedSurfaceVelocity,
		out int resolvedEntryEdgeIndex)
	{
		resolvedTriangleIndex =
			-1;

		resolvedSurfacePoint =
			Vector3.zero;

		resolvedSurfaceVelocity =
			Vector3.zero;

		resolvedEntryEdgeIndex =
			-1;

		int currentTriangleIndex =
			initialTriangleIndex;

		int currentEntryEdgeIndex =
			initialEntryEdgeIndex;

		Vector3 currentStartPoint =
			entryPoint;

		Vector3 currentSurfaceVelocity =
			initialSurfaceVelocity;

		for (int transitionIndex = 0;
			transitionIndex <
				m_MaximumTriangleTransitionsPerStep;
			transitionIndex++)
		{
			if (!TryGetTriangleWorldData(
				currentTriangleIndex,
				out Vector3 vertexA,
				out Vector3 vertexB,
				out Vector3 vertexC,
				out Vector3 currentNormal))
			{
				return false;
			}

			Vector3 targetSurfacePoint =
				ballCenter -
				currentNormal *
					centerOffset;

			targetSurfacePoint =
				ProjectPointToPlane(
					targetSurfacePoint,
					vertexA,
					currentNormal);

			if (!TryCalculateBarycentric(
				targetSurfacePoint,
				vertexA,
				vertexB,
				vertexC,
				out Vector3 targetBarycentric))
			{
				return false;
			}

			if (IsInsideTriangle(
				targetBarycentric))
			{
				float surfaceSpeed =
					currentSurfaceVelocity.magnitude;

				Vector3 tangentVelocity =
					Vector3.ProjectOnPlane(
						currentSurfaceVelocity,
						currentNormal);

				if (surfaceSpeed >
						m_MinimumCalculationValue &&
					tangentVelocity.sqrMagnitude <=
						m_MinimumCalculationValue *
							m_MinimumCalculationValue)
				{
					return false;
				}

				resolvedTriangleIndex =
					currentTriangleIndex;

				resolvedSurfacePoint =
					targetSurfacePoint;

				resolvedSurfaceVelocity =
					surfaceSpeed >
						m_MinimumCalculationValue
						? tangentVelocity.normalized *
							surfaceSpeed
						: Vector3.zero;

				resolvedEntryEdgeIndex =
					currentEntryEdgeIndex;

				return true;
			}

			Vector3 projectedStartPoint =
				ProjectPointToPlane(
					currentStartPoint,
					vertexA,
					currentNormal);

			if (!TryCalculateBarycentric(
				projectedStartPoint,
				vertexA,
				vertexB,
				vertexC,
				out Vector3 startBarycentric))
			{
				return false;
			}

			if (!IsInsideTriangle(
				startBarycentric))
			{
				return false;
			}

			if (!TryFindFirstSurfaceCrossing(
				startBarycentric,
				targetBarycentric,
				currentEntryEdgeIndex,
				out int crossedEdgeIndex,
				out int simultaneousEdgeIndex,
				out float crossingFraction))
			{
				return false;
			}

			if (simultaneousEdgeIndex >=
				0)
			{
				crossedEdgeIndex =
					ResolveSimultaneousVertexEdge(
						crossedEdgeIndex,
						simultaneousEdgeIndex);
			}

			BallSurfaceMeshData.EEdgeType edgeType =
				m_MeshData.GetEdgeType(
					currentTriangleIndex,
					crossedEdgeIndex);

			if (edgeType !=
					BallSurfaceMeshData.EEdgeType.Flat &&
				edgeType !=
					BallSurfaceMeshData.EEdgeType.Smooth)
			{
				return false;
			}

			if (!m_MeshData.TryGetNeighbor(
				currentTriangleIndex,
				crossedEdgeIndex,
				out int nextTriangleIndex,
				out int nextEntryEdgeIndex))
			{
				return false;
			}

			if (!TryGetTriangleWorldData(
				nextTriangleIndex,
				out _,
				out _,
				out _,
				out Vector3 nextNormal))
			{
				return false;
			}

			Vector3 crossingPoint =
				Vector3.LerpUnclamped(
					projectedStartPoint,
					targetSurfacePoint,
					Mathf.Clamp01(
						crossingFraction));

			crossingPoint =
				ProjectPointToPlane(
					crossingPoint,
					vertexA,
					currentNormal);

			if (!TryRotateConcaveSurfaceVelocity(
				currentSurfaceVelocity,
				currentNormal,
				nextNormal,
				out Vector3 nextSurfaceVelocity))
			{
				return false;
			}

			currentTriangleIndex =
				nextTriangleIndex;

			currentEntryEdgeIndex =
				nextEntryEdgeIndex;

			currentStartPoint =
				crossingPoint;

			currentSurfaceVelocity =
				nextSurfaceVelocity;
		}

		return false;
	}

	/// <summary>
	/// Surface方向速度の大きさを維持したまま、
	/// 新しいSurface法線へ速度方向を回転させます。
	/// </summary>
	private bool TryRotateConcaveSurfaceVelocity(
		Vector3 sourceVelocity,
		Vector3 sourceNormal,
		Vector3 targetNormal,
		out Vector3 rotatedVelocity)
	{
		rotatedVelocity =
			Vector3.zero;

		float surfaceSpeed =
			sourceVelocity.magnitude;

		if (surfaceSpeed <=
			m_MinimumCalculationValue)
		{
			return false;
		}

		Quaternion transitionRotation =
			Quaternion.FromToRotation(
				sourceNormal,
				targetNormal);

		Vector3 nextVelocity =
			transitionRotation *
				sourceVelocity;

		nextVelocity =
			Vector3.ProjectOnPlane(
				nextVelocity,
				targetNormal);

		if (nextVelocity.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return false;
		}

		rotatedVelocity =
			nextVelocity.normalized *
				surfaceSpeed;

		return true;
	}
}
