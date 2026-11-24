using UnityEngine;

/// <summary>
/// 複数Edgeへほぼ同時到達するConvex Vertexの決定論的な選択と、
/// Vertex中心の球面パッチ移動を担当します。
/// </summary>
/// <remarks>
/// 主な処理の流れ:
/// 1. 同時到達したEdge候補を進行方向・Edge種別・EdgeIndexから決定論的に選択します。
/// 2. Convex Vertexでは実Surfaceの1面へ無理に固定せず、Ball中心をVertex中心の球面上へ移します。
/// 3. SurfaceSpeedから球面上の移動距離を求め、半径から回転角へ変換します。
/// 4. 球面パッチ境界へ到達したら接続可能なConvex EdgeまたはTriangleを選びます。
/// 5. Edgeへ戻る場合はConvex遷移へ連続して引き継ぎます。
///
/// 浮動小数点誤差によってFrameごとに遷移先が変わらないよう、
/// Vertexでのタイブレーク規則を明示的に固定しています。
/// </remarks>
public sealed partial class BallSurfaceSimulation
{
	/// <summary>
	/// 球面パッチの境界判定に使用する許容値です。
	/// </summary>
	private const float m_VertexSphereBoundaryTolerance = 0.00001f;

	/// <summary>
	/// 球面境界との交差時刻を二分探索する回数です。
	/// </summary>
	private const int m_VertexSphereBoundarySearchIterations = 16;

	/// <summary>
	/// 球面境界上の方向がConvex Edgeの法線Arc内にあるとみなす
	/// 角度許容値です。
	/// </summary>
	private const float m_VertexSphereArcAngleTolerance = 0.5f;

	private bool m_IsConvexVertexSpherePatch;

	private Vector3 m_ConvexVertexSpherePoint;
	private Vector3 m_ConvexVertexSphereRadialDirection;
	private Vector3 m_ConvexVertexSphereRotationAxis;
	private Vector3 m_ConvexVertexSphereInteriorDirection;

	private BallSurfaceMeshData.ConvexEdgeReference[]
		m_ConvexVertexSphereIncidentEdges;

	/// <summary>
	/// 同時到達した2本のEdgeから、
	/// 先に越えたものとして扱うEdgeを決定します。
	/// </summary>
	/// <remarks>
	/// 浮動小数点誤差に選択を任せるとFrameや環境によって
	/// 左右が変わる可能性があるため、必ず同じ規則で選びます。
	///
	/// Flat / SmoothはTriangle分割上の境界である可能性が高いため
	/// Sharp Edgeより先に処理します。
	/// 同じ種別同士ではEdgeIndexが小さい側を選びます。
	/// </remarks>
	private int ResolveSimultaneousVertexEdge(
		int firstEdgeIndex,
		int secondEdgeIndex)
	{
		return ResolveSimultaneousVertexEdge(
			firstEdgeIndex,
			secondEdgeIndex,
			m_SurfaceVelocity);
	}

	private int ResolveSimultaneousVertexEdge(
		int firstEdgeIndex,
		int secondEdgeIndex,
		Vector3 travelDirection)
	{
		if (firstEdgeIndex <
			0)
		{
			return secondEdgeIndex;
		}

		if (secondEdgeIndex <
			0)
		{
			return firstEdgeIndex;
		}

		if (m_MeshData ==
			null)
		{
			return Mathf.Min(
				firstEdgeIndex,
				secondEdgeIndex);
		}

		BallSurfaceMeshData.EEdgeType firstEdgeType =
			m_MeshData.GetEdgeType(
				m_CurrentTriangleIndex,
				firstEdgeIndex);

		BallSurfaceMeshData.EEdgeType secondEdgeType =
			m_MeshData.GetEdgeType(
				m_CurrentTriangleIndex,
				secondEdgeIndex);

		if (TryGetVertexEdgeExitScore(
				firstEdgeIndex,
				travelDirection,
				out float firstExitScore) &&
			TryGetVertexEdgeExitScore(
				secondEdgeIndex,
				travelDirection,
				out float secondExitScore))
		{
			if (firstExitScore >
				secondExitScore +
					m_MinimumCalculationValue)
			{
				return firstEdgeIndex;
			}

			if (secondExitScore >
				firstExitScore +
					m_MinimumCalculationValue)
			{
				return secondEdgeIndex;
			}
		}

		int firstPriority =
			GetVertexTieBreakPriority(
				firstEdgeType);

		int secondPriority =
			GetVertexTieBreakPriority(
				secondEdgeType);

		if (firstPriority <
			secondPriority)
		{
			return firstEdgeIndex;
		}

		if (secondPriority <
			firstPriority)
		{
			return secondEdgeIndex;
		}

		return Mathf.Min(
			firstEdgeIndex,
			secondEdgeIndex);
	}

	/// <summary>
	/// Returns how strongly the current travel direction exits across an edge.
	/// </summary>
	private bool TryGetVertexEdgeExitScore(
		int edgeIndex,
		Vector3 travelDirection,
		out float exitScore)
	{
		exitScore =
			0.0f;

		if (travelDirection.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return false;
		}

		if (!TryGetTriangleWorldData(
			m_CurrentTriangleIndex,
			out Vector3 vertexA,
			out Vector3 vertexB,
			out Vector3 vertexC,
			out Vector3 currentNormal) ||
			!TryGetEdgeWorldVertices(
				m_CurrentTriangleIndex,
				edgeIndex,
				out Vector3 edgeStart,
				out Vector3 edgeEnd))
		{
			return false;
		}

		Vector3 edgeVector =
			edgeEnd -
				edgeStart;

		if (edgeVector.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return false;
		}

		Vector3 tangentDirection =
			Vector3.ProjectOnPlane(
				travelDirection,
				currentNormal);

		if (tangentDirection.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return false;
		}

		Vector3 edgeDirection =
			edgeVector.normalized;

		Vector3 edgeMidPoint =
			(edgeStart + edgeEnd) *
			0.5f;

		Vector3 triangleCenter =
			(vertexA + vertexB + vertexC) /
			3.0f;

		Vector3 insideDirection =
			Vector3.ProjectOnPlane(
				triangleCenter - edgeMidPoint,
				currentNormal);

		insideDirection -=
			edgeDirection *
				Vector3.Dot(
					insideDirection,
					edgeDirection);

		if (insideDirection.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return false;
		}

		exitScore =
			-Vector3.Dot(
				tangentDirection.normalized,
				insideDirection.normalized);

		return true;
	}

	/// <summary>
	/// Vertex同時到達時のEdge選択優先度を取得します。
	/// 値が小さいほど先に処理します。
	/// </summary>
	private int GetVertexTieBreakPriority(
		BallSurfaceMeshData.EEdgeType edgeType)
	{
		switch (edgeType)
		{
			case BallSurfaceMeshData.EEdgeType.Flat:
				return 0;

			case BallSurfaceMeshData.EEdgeType.Smooth:
				return 1;

			case BallSurfaceMeshData.EEdgeType.Convex:
				return 2;

			case BallSurfaceMeshData.EEdgeType.Concave:
				return 3;

			case BallSurfaceMeshData.EEdgeType.Boundary:
				return 4;

			case BallSurfaceMeshData.EEdgeType.NonManifold:
				return 5;

			case BallSurfaceMeshData.EEdgeType.Invalid:
			default:
				return 6;
		}
	}

	/// <summary>
	/// Face上で2本のEdgeへ同時到達した実Convex Vertexへ直接進入します。
	/// </summary>
	private bool TryBeginDirectConvexVertexSpherePatch(
		Vector3 vertexPoint,
		Vector3 faceNormal)
	{
		if (m_SurfaceSource ==
				null ||
			m_MeshData ==
				null ||
			faceNormal.sqrMagnitude <=
				m_MinimumCalculationValue *
					m_MinimumCalculationValue)
		{
			return false;
		}

		Vector3 localVertexPoint =
			m_SurfaceSource.transform.InverseTransformPoint(
				vertexPoint);

		// 実Convex Vertexでない同時Edge到達は、
		// 従来の決定論的Edge選択へフォールバックします。
		if (!m_MeshData.TryGetIncidentConvexEdges(
			localVertexPoint,
			out BallSurfaceMeshData.ConvexEdgeReference[] incidentEdges) ||
			incidentEdges ==
				null ||
			incidentEdges.Length <
				3)
		{
			return false;
		}

		Vector3 normalizedFaceNormal =
			faceNormal.normalized;

		float offsetDistance =
			Mathf.Max(
				m_BallRadius +
					m_Height,
				m_MinimumCalculationValue);

		Vector3 worldCenter =
			vertexPoint +
			normalizedFaceNormal *
				offsetDistance;

		Vector3 worldVelocity =
			m_SurfaceVelocity +
			normalizedFaceNormal *
				m_VerticalVelocity;

		SetWorldState(
			worldCenter,
			worldVelocity);

		return BeginConvexVertexSpherePatch(
			vertexPoint);
	}

	/// <summary>
	/// FaceまたはConvex Edgeから実Vertexへ到達したWorldStateを使い、
	/// Vertex中心の球面パッチ移動を開始します。
	/// </summary>
	/// <remarks>
	/// Traversal切替のためにBall中心位置や速度方向を作り直さず、
	/// 現在のWorld中心とWorld速度をVertex基準の
	/// Radial / Tangent成分へ分解します。
	///
	/// FaceからVertexへ直接入った場合だけ既存のConvex減速を1回適用し、
	/// Edgeですでに減速済みの場合は同じ角通過中に再適用しません。
	/// </remarks>
	private bool BeginConvexVertexSpherePatch(
		Vector3 vertexPoint)
	{
		if (m_SurfaceSource ==
				null ||
			m_MeshData ==
				null ||
			!m_HasWorldState)
		{
			return false;
		}

		Vector3 localVertexPoint =
			m_SurfaceSource.transform.InverseTransformPoint(
				vertexPoint);

		if (!m_MeshData.TryGetIncidentConvexEdges(
			localVertexPoint,
			out BallSurfaceMeshData.ConvexEdgeReference[] incidentEdges) ||
			incidentEdges ==
				null ||
			incidentEdges.Length <
				3)
		{
			return false;
		}

		if (!TryGetVertexSphereInteriorDirection(
			vertexPoint,
			incidentEdges,
			out Vector3 interiorDirection))
		{
			return false;
		}

		Vector3 beforeCenter =
			m_WorldCenter;

		Vector3 beforeVelocity =
			m_WorldVelocity;

		Vector3 radialVector =
			beforeCenter -
				vertexPoint;

		float currentRadius =
			radialVector.magnitude;

		if (currentRadius <=
				m_MinimumCalculationValue ||
			currentRadius <
				m_BallRadius -
					m_TransitionPositionTolerance)
		{
			return false;
		}

		Vector3 radialDirection =
			radialVector /
				currentRadius;

		float radialSpeed =
			Vector3.Dot(
				beforeVelocity,
				radialDirection);

		Vector3 tangentVelocity =
			beforeVelocity -
			radialDirection *
				radialSpeed;

		if (tangentVelocity.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return false;
		}

		Vector3 rotationAxis =
			Vector3.Cross(
				radialDirection,
				tangentVelocity);

		if (rotationAxis.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return false;
		}

		rotationAxis.Normalize();

		// FaceからVertexへ直接入った場合はここで1回だけ減速します。
		// Edgeですでに減速済みならConsume側が1.0を返すため、
		// Edge -> Vertexの連続遷移では速度を重複して落としません。
		float speedRetention =
			ConsumeConvexTraversalSpeedRetention();

		Vector3 retainedWorldVelocity =
			beforeVelocity *
				speedRetention;

		radialSpeed *=
			speedRetention;

		tangentVelocity *=
			speedRetention;

		m_IsConvexVertexSpherePatch =
			true;

		m_ConvexVertexSpherePoint =
			vertexPoint;

		m_ConvexVertexSphereRadialDirection =
			radialDirection;

		m_ConvexVertexSphereRotationAxis =
			rotationAxis;

		m_ConvexVertexSphereInteriorDirection =
			interiorDirection;

		m_ConvexVertexSphereIncidentEdges =
			incidentEdges;

		m_SurfacePoint =
			vertexPoint;

		m_SurfaceVelocity =
			tangentVelocity;

		m_Height =
			Mathf.Max(
				0.0f,
				currentRadius -
					m_BallRadius);

		m_VerticalVelocity =
			radialSpeed;

		// Traversal表現だけをVertexへ切り替えます。
		// 位置はそのまま、速度は既存Retentionを同方向へ1回だけ適用します。
		SetWorldState(
			beforeCenter,
			retainedWorldVelocity);

		return true;
	}

	/// <summary>
	/// Vertex中心の球面パッチ上をSurface速度に合わせて進めます。
	/// </summary>
	/// <remarks>
	/// 球面上の移動距離を先にSurfaceSpeed * deltaTimeで決め、
	/// その距離を半径で割って回転角へ変換します。
	///
	/// 球面パッチ境界へ到達した場合は、
	/// そのVertexから伸びる実Convex Edgeの円柱遷移へ戻します。
	/// </remarks>
	private bool AdvanceConvexVertexSpherePatch(
		ref float remainingTime,
		out bool completed)
	{
		completed =
			false;

		if (!m_IsConvexVertexSpherePatch)
		{
			completed =
				true;

			return true;
		}

		float surfaceSpeed =
			m_SurfaceVelocity.magnitude;

		if (surfaceSpeed <=
			m_MinimumCalculationValue)
		{
			remainingTime =
				0.0f;

			return true;
		}

		float offsetRadius =
			Mathf.Max(
				m_BallRadius +
					m_Height,
				m_MinimumCalculationValue);

		float requestedAngleRadians =
			surfaceSpeed *
			remainingTime /
			offsetRadius;

		if (requestedAngleRadians <=
			m_MinimumCalculationValue)
		{
			remainingTime =
				0.0f;

			return true;
		}

		bool reachesBoundary =
			TryFindVertexSphereBoundary(
				requestedAngleRadians,
				out BallSurfaceMeshData.ConvexEdgeReference boundaryEdge,
				out float boundaryFraction);

		float stepFraction =
			reachesBoundary
				? Mathf.Clamp01(
					boundaryFraction)
				: 1.0f;

		float stepTime =
			remainingTime *
			stepFraction;

		float angleStepDegrees =
			requestedAngleRadians *
			stepFraction *
			Mathf.Rad2Deg;

		Quaternion rotation =
			Quaternion.AngleAxis(
				angleStepDegrees,
				m_ConvexVertexSphereRotationAxis);

		m_ConvexVertexSphereRadialDirection =
			rotation *
			m_ConvexVertexSphereRadialDirection;

		if (m_ConvexVertexSphereRadialDirection.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return false;
		}

		m_ConvexVertexSphereRadialDirection.Normalize();

		Vector3 rotatedVelocity =
			rotation *
			m_SurfaceVelocity;

		rotatedVelocity =
			Vector3.ProjectOnPlane(
				rotatedVelocity,
				m_ConvexVertexSphereRadialDirection);

		if (rotatedVelocity.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return false;
		}

		m_SurfaceVelocity =
			rotatedVelocity.normalized *
			surfaceSpeed;


		remainingTime -=
			stepTime;

		if (remainingTime <
			0.0f)
		{
			remainingTime =
				0.0f;
		}

		if (!reachesBoundary)
		{
			return true;
		}

		if (!TrySnapVertexSphereDirectionToBoundary(
			boundaryEdge,
			ref m_ConvexVertexSphereRadialDirection))
		{
			return false;
		}

		m_SurfaceVelocity =
			Vector3.ProjectOnPlane(
				m_SurfaceVelocity,
				m_ConvexVertexSphereRadialDirection);

		if (m_SurfaceVelocity.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return false;
		}

		m_SurfaceVelocity =
			m_SurfaceVelocity.normalized *
			surfaceSpeed;

		Vector3 exitRadialDirection =
			m_ConvexVertexSphereRadialDirection;

		Vector3 exitSurfaceVelocity =
			m_SurfaceVelocity;

		Vector3 vertexPoint =
			m_ConvexVertexSpherePoint;

		// Vertex -> Edgeは同じConvex角通過の続きなので、
		// Vertex状態のResetで速度減衰済みフラグを失わないよう保持します。
		bool hasAppliedSpeedRetention =
			m_HasAppliedConvexTraversalSpeedRetention;

		ResetConvexVertexSpherePatch();

		m_HasAppliedConvexTraversalSpeedRetention =
			hasAppliedSpeedRetention;

		if (!BeginConvexEdgeTransitionFromVertexSpherePatch(
			boundaryEdge,
			vertexPoint,
			exitRadialDirection,
			exitSurfaceVelocity))
		{
			return false;
		}

		completed =
			true;

		return true;
	}

	/// <summary>
	/// 現在の球面進行方向が最初に交差するVertex境界Edgeを求めます。
	/// </summary>
	private bool TryFindVertexSphereBoundary(
		float requestedAngleRadians,
		out BallSurfaceMeshData.ConvexEdgeReference boundaryEdge,
		out float boundaryFraction)
	{
		boundaryEdge =
			default;

		boundaryFraction =
			float.MaxValue;

		if (m_ConvexVertexSphereIncidentEdges ==
				null ||
			m_ConvexVertexSphereIncidentEdges.Length ==
				0)
		{
			return false;
		}

		Quaternion fullRotation =
			Quaternion.AngleAxis(
				requestedAngleRadians *
					Mathf.Rad2Deg,
				m_ConvexVertexSphereRotationAxis);

		Vector3 endDirection =
			fullRotation *
			m_ConvexVertexSphereRadialDirection;

		if (endDirection.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return false;
		}

		endDirection.Normalize();

		bool foundBoundary =
			false;

		for (int incidentIndex = 0;
			incidentIndex <
				m_ConvexVertexSphereIncidentEdges.Length;
			incidentIndex++)
		{
			BallSurfaceMeshData.ConvexEdgeReference edgeReference =
				m_ConvexVertexSphereIncidentEdges[
					incidentIndex];

			if (!TryGetVertexSphereBoundaryData(
				edgeReference,
				out Vector3 edgeDirection,
				out Vector3 firstNormal,
				out Vector3 secondNormal))
			{
				continue;
			}

			float interiorSide =
				Vector3.Dot(
					m_ConvexVertexSphereInteriorDirection,
					edgeDirection);

			if (Mathf.Abs(
				interiorSide) <=
				m_VertexSphereBoundaryTolerance)
			{
				continue;
			}

			float interiorSign =
				Mathf.Sign(
					interiorSide);

			float startDistance =
				Vector3.Dot(
					m_ConvexVertexSphereRadialDirection,
					edgeDirection) *
				interiorSign;

			float endDistance =
				Vector3.Dot(
					endDirection,
					edgeDirection) *
				interiorSign;

			bool crossesBoundary =
				(
					startDistance >
						m_VertexSphereBoundaryTolerance &&
					endDistance <=
						m_VertexSphereBoundaryTolerance
				) ||
				(
					startDistance >=
						-m_VertexSphereBoundaryTolerance &&
					endDistance <
						-m_VertexSphereBoundaryTolerance
				);

			if (!crossesBoundary)
			{
				continue;
			}

			float crossingFraction =
				FindVertexSphereBoundaryCrossingFraction(
					requestedAngleRadians,
					edgeDirection,
					interiorSign,
					startDistance);

			Quaternion crossingRotation =
				Quaternion.AngleAxis(
					requestedAngleRadians *
						crossingFraction *
						Mathf.Rad2Deg,
					m_ConvexVertexSphereRotationAxis);

			Vector3 crossingDirection =
				crossingRotation *
				m_ConvexVertexSphereRadialDirection;

			if (crossingDirection.sqrMagnitude <=
				m_MinimumCalculationValue *
					m_MinimumCalculationValue)
			{
				continue;
			}

			crossingDirection.Normalize();

			if (!IsDirectionOnConvexEdgeNormalArc(
				crossingDirection,
				firstNormal,
				secondNormal))
			{
				continue;
			}

			if (!foundBoundary ||
				crossingFraction <
					boundaryFraction -
						m_VertexSphereBoundaryTolerance ||
				(
					Mathf.Abs(
						crossingFraction -
							boundaryFraction) <=
						m_VertexSphereBoundaryTolerance &&
					IsVertexSphereBoundaryDeterministicallyEarlier(
						edgeReference,
						boundaryEdge)
				))
			{
				foundBoundary =
					true;

				boundaryEdge =
					edgeReference;

				boundaryFraction =
					crossingFraction;
			}
		}

		return foundBoundary;
	}

	/// <summary>
	/// 球面上の回転角について、指定境界Planeへ到達する割合を二分探索します。
	/// </summary>
	private float FindVertexSphereBoundaryCrossingFraction(
		float requestedAngleRadians,
		Vector3 edgeDirection,
		float interiorSign,
		float startDistance)
	{
		if (startDistance <=
			m_VertexSphereBoundaryTolerance)
		{
			return 0.0f;
		}

		float lowerFraction =
			0.0f;

		float upperFraction =
			1.0f;

		for (int searchIndex = 0;
			searchIndex <
				m_VertexSphereBoundarySearchIterations;
			searchIndex++)
		{
			float middleFraction =
				(lowerFraction +
					upperFraction) *
				0.5f;

			Quaternion rotation =
				Quaternion.AngleAxis(
					requestedAngleRadians *
						middleFraction *
						Mathf.Rad2Deg,
					m_ConvexVertexSphereRotationAxis);

			Vector3 direction =
				rotation *
				m_ConvexVertexSphereRadialDirection;

			float signedDistance =
				Vector3.Dot(
					direction,
					edgeDirection) *
				interiorSign;

			if (signedDistance >
				0.0f)
			{
				lowerFraction =
					middleFraction;
			}
			else
			{
				upperFraction =
					middleFraction;
			}
		}

		return upperFraction;
	}

	/// <summary>
	/// 指定VertexのConvex Edge境界情報をWorld座標で取得します。
	/// </summary>
	private bool TryGetVertexSphereBoundaryData(
		BallSurfaceMeshData.ConvexEdgeReference edgeReference,
		out Vector3 edgeDirection,
		out Vector3 firstNormal,
		out Vector3 secondNormal)
	{
		edgeDirection =
			Vector3.zero;

		firstNormal =
			Vector3.zero;

		secondNormal =
			Vector3.zero;

		if (m_SurfaceSource ==
				null ||
			m_MeshData ==
				null)
		{
			return false;
		}

		Vector3 otherWorldPoint =
			m_SurfaceSource.transform.TransformPoint(
				edgeReference.OtherLocalVertex);

		edgeDirection =
			otherWorldPoint -
			m_ConvexVertexSpherePoint;

		if (edgeDirection.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return false;
		}

		edgeDirection.Normalize();

		if (!TryGetTriangleWorldData(
			edgeReference.TriangleIndex,
			out _,
			out _,
			out _,
			out firstNormal))
		{
			return false;
		}

		if (!m_MeshData.TryGetNeighbor(
			edgeReference.TriangleIndex,
			edgeReference.EdgeIndex,
			out int neighborTriangleIndex,
			out _))
		{
			return false;
		}

		return TryGetTriangleWorldData(
			neighborTriangleIndex,
			out _,
			out _,
			out _,
			out secondNormal);
	}

	/// <summary>
	/// 指定方向が2面Normal間のConvex Edge Arc上にあるか判定します。
	/// </summary>
	private bool IsDirectionOnConvexEdgeNormalArc(
		Vector3 direction,
		Vector3 firstNormal,
		Vector3 secondNormal)
	{
		float totalAngle =
			Vector3.Angle(
				firstNormal,
				secondNormal);

		float splitAngle =
			Vector3.Angle(
				firstNormal,
				direction) +
			Vector3.Angle(
				direction,
				secondNormal);

		return splitAngle <=
			totalAngle +
			m_VertexSphereArcAngleTolerance;
	}

	/// <summary>
	/// 球面境界方向を実Edgeの法線Planeへ微小補正します。
	/// </summary>
	private bool TrySnapVertexSphereDirectionToBoundary(
		BallSurfaceMeshData.ConvexEdgeReference edgeReference,
		ref Vector3 radialDirection)
	{
		if (!TryGetVertexSphereBoundaryData(
			edgeReference,
			out Vector3 edgeDirection,
			out _,
			out _))
		{
			return false;
		}

		Vector3 projectedDirection =
			Vector3.ProjectOnPlane(
				radialDirection,
				edgeDirection);

		if (projectedDirection.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return false;
		}

		radialDirection =
			projectedDirection.normalized;

		return true;
	}

	/// <summary>
	/// 頂点球面の内側を示す代表方向を取得します。
	/// </summary>
	private bool TryGetVertexSphereInteriorDirection(
		Vector3 vertexPoint,
		BallSurfaceMeshData.ConvexEdgeReference[] incidentEdges,
		out Vector3 interiorDirection)
	{
		interiorDirection =
			Vector3.zero;

		if (incidentEdges ==
				null ||
			incidentEdges.Length ==
				0)
		{
			return false;
		}

		Vector3 normalSum =
			Vector3.zero;

		for (int incidentIndex = 0;
			incidentIndex <
				incidentEdges.Length;
			incidentIndex++)
		{
			BallSurfaceMeshData.ConvexEdgeReference edgeReference =
				incidentEdges[
					incidentIndex];

			if (TryGetTriangleWorldData(
				edgeReference.TriangleIndex,
				out _,
				out _,
				out _,
				out Vector3 firstNormal))
			{
				normalSum +=
					firstNormal;
			}

			if (!m_MeshData.TryGetNeighbor(
				edgeReference.TriangleIndex,
				edgeReference.EdgeIndex,
				out int neighborTriangleIndex,
				out _))
			{
				continue;
			}

			if (TryGetTriangleWorldData(
				neighborTriangleIndex,
				out _,
				out _,
				out _,
				out Vector3 secondNormal))
			{
				normalSum +=
					secondNormal;
			}
		}

		if (normalSum.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return false;
		}

		interiorDirection =
			normalSum.normalized;

		return true;
	}

	/// <summary>
	/// 同時境界到達時の選択を決定論的にします。
	/// </summary>
	private bool IsVertexSphereBoundaryDeterministicallyEarlier(
		BallSurfaceMeshData.ConvexEdgeReference candidate,
		BallSurfaceMeshData.ConvexEdgeReference current)
	{
		if (candidate.TriangleIndex !=
			current.TriangleIndex)
		{
			return candidate.TriangleIndex <
				current.TriangleIndex;
		}

		return candidate.EdgeIndex <
			current.EdgeIndex;
	}

	/// <summary>
	/// Vertex球面パッチ中のBall中心Offsetを取得します。
	/// </summary>
	private Vector3 GetConvexVertexSphereBallCenterOffset()
	{
		float offsetRadius =
			Mathf.Max(
				m_BallRadius +
					m_Height,
				m_MinimumCalculationValue);

		return m_ConvexVertexSphereRadialDirection *
			offsetRadius;
	}

	/// <summary>
	/// Vertex球面パッチ状態を初期化します。
	/// </summary>
	private void ResetConvexVertexSpherePatch()
	{
		m_IsConvexVertexSpherePatch =
			false;

		m_ConvexVertexSpherePoint =
			Vector3.zero;

		m_ConvexVertexSphereRadialDirection =
			Vector3.zero;

		m_ConvexVertexSphereRotationAxis =
			Vector3.zero;

		m_ConvexVertexSphereInteriorDirection =
			Vector3.zero;

		m_ConvexVertexSphereIncidentEdges =
			null;

		// Vertex球面を抜けた時点で、この角通過の減速消費を終了します。
		// その後さらに別のVertexへ到達した場合は、新しい角通過として
		// 再び1回だけ減速できます。
		m_HasAppliedConvexTraversalSpeedRetention =
			false;
	}
}
