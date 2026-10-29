using UnityEngine;

/// <summary>
/// Sharp Convex Edgeの回り込み処理と表示Offset計算を担当します。
/// </summary>
public sealed partial class BallSurfaceSimulation
{
	/// <summary>
	/// Vertex球面パッチから入ったEdgeでは、
	/// 球面との位置連続性を保つため真円Offsetを使用します。
	/// </summary>
	private bool m_ConvexUseCircularCenterOffset;

	/// <summary>
	/// 現在のConvex角通過で速度減衰をすでに適用したかどうかです。
	/// </summary>
	/// <remarks>
	/// EdgeからVertex球面へ連続して移行する場合も、
	/// 1回の角通過として減速を重複適用しません。
	/// </remarks>
	private bool m_HasAppliedConvexTraversalSpeedRetention;

	/// <summary>
	/// Sharp Convex Edgeの円弧移動を開始します。
	/// </summary>
	private bool BeginConvexEdgeTransition(
		int sourceEdgeIndex,
		int targetTriangleIndex,
		int targetEdgeIndex,
		Vector3 sourceNormal,
		Vector3 targetNormal)
	{
		if (!TryGetEdgeWorldVertices(
			m_CurrentTriangleIndex,
			sourceEdgeIndex,
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

		Vector3 edgeDirection =
			edgeVector.normalized;

		float signedAngle =
			Vector3.SignedAngle(
				sourceNormal,
				targetNormal,
				edgeDirection);

		if (Mathf.Abs(
			signedAngle) <=
			m_MinimumCalculationValue)
		{
			return false;
		}

		float angleSign =
			Mathf.Sign(
				signedAngle);

		Vector3 crossDirection =
			Vector3.Cross(
				edgeDirection,
				sourceNormal) *
			angleSign;

		if (crossDirection.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return false;
		}

		crossDirection.Normalize();

		float alongSpeed =
			Vector3.Dot(
				m_SurfaceVelocity,
				edgeDirection);

		Vector3 perpendicularVelocity =
			m_SurfaceVelocity -
			edgeDirection *
				alongSpeed;

		float crossSpeed =
			perpendicularVelocity.magnitude;

		if (crossSpeed <=
			m_MinimumCalculationValue)
		{
			return false;
		}

		// MeshのWindingや数値誤差によって
		// 交差方向と円弧方向が反対になっていないか確認します。
		if (Vector3.Dot(
			perpendicularVelocity,
			crossDirection) <
			0.0f)
		{
			return false;
		}

		// Sharp Convex Edgeへ入った瞬間だけ、
		// Surface方向の速度を少し落とします。
		//
		// 円弧移動中に毎Frame減速するのではないため、
		// 一度の角通過で連続的に速度が失われることはありません。
		float speedRetention =
			ConsumeConvexTraversalSpeedRetention();

		float retainedAlongSpeed =
			alongSpeed *
			speedRetention;

		float retainedCrossSpeed =
			crossSpeed *
			speedRetention;

		m_SurfaceVelocity *=
			speedRetention;

		m_ConvexUseCircularCenterOffset =
			false;

		m_IsConvexEdgeTransition =
			true;

		m_ConvexTargetTriangleIndex =
			targetTriangleIndex;

		m_ConvexTargetEdgeIndex =
			targetEdgeIndex;

		m_ConvexEdgeDirection =
			edgeDirection;

		// 円弧中にSurfacePointが本物のEdge端を越えないよう、
		// EdgeのWorld端点を保持します。
		m_ConvexEdgeStartPoint =
			edgeStart;

		m_ConvexEdgeEndPoint =
			edgeEnd;

		m_ConvexSourceNormal =
			sourceNormal.normalized;

		m_ConvexTargetNormal =
			targetNormal.normalized;

		m_ConvexSignedAngle =
			signedAngle;

		m_ConvexCurrentAngle =
			0.0f;

		m_ConvexAlongSpeed =
			retainedAlongSpeed;

		m_ConvexCrossSpeed =
			retainedCrossSpeed;

		UpdateConvexTransitionVelocity();

		return true;
	}

	/// <summary>
	/// Vertex球面パッチからConvex Edgeの円柱遷移へ接続します。
	/// </summary>
	/// <remarks>
	/// 通常のFace → Edge遷移と異なり、すでにEdge Arcの途中へ
	/// 到達しているため、現在のRadial DirectionからCurrentAngleを復元します。
	///
	/// Vertex球面で速度保持済みのため、
	/// m_ConvexEdgeSpeedRetentionはここでは再適用しません。
	/// </remarks>
	private bool BeginConvexEdgeTransitionFromVertexSpherePatch(
		BallSurfaceMeshData.ConvexEdgeReference edgeReference,
		Vector3 vertexPoint,
		Vector3 radialDirection,
		Vector3 tangentVelocity)
	{
		if (m_MeshData ==
				null ||
			radialDirection.sqrMagnitude <=
				m_MinimumCalculationValue *
					m_MinimumCalculationValue ||
			tangentVelocity.sqrMagnitude <=
				m_MinimumCalculationValue *
					m_MinimumCalculationValue)
		{
			return false;
		}

		if (!m_MeshData.TryGetNeighbor(
			edgeReference.TriangleIndex,
			edgeReference.EdgeIndex,
			out int neighborTriangleIndex,
			out int neighborEdgeIndex))
		{
			return false;
		}

		if (!TryGetEdgeWorldVertices(
			edgeReference.TriangleIndex,
			edgeReference.EdgeIndex,
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

		Vector3 edgeDirection =
			edgeVector.normalized;

		if (!TryGetTriangleWorldData(
			edgeReference.TriangleIndex,
			out _,
			out _,
			out _,
			out Vector3 firstNormal) ||
			!TryGetTriangleWorldData(
				neighborTriangleIndex,
				out _,
				out _,
				out _,
				out Vector3 secondNormal))
		{
			return false;
		}

		radialDirection.Normalize();

		float surfaceSpeed =
			tangentVelocity.magnitude;

		tangentVelocity =
			Vector3.ProjectOnPlane(
				tangentVelocity,
				radialDirection);

		if (tangentVelocity.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return false;
		}

		tangentVelocity =
			tangentVelocity.normalized *
			surfaceSpeed;

		float firstToSecondSignedAngle =
			Vector3.SignedAngle(
				firstNormal,
				secondNormal,
				edgeDirection);

		if (Mathf.Abs(
			firstToSecondSignedAngle) <=
			m_MinimumCalculationValue)
		{
			return false;
		}

		Vector3 firstToSecondCrossDirection =
			Vector3.Cross(
				edgeDirection,
				radialDirection) *
			Mathf.Sign(
				firstToSecondSignedAngle);

		if (firstToSecondCrossDirection.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return false;
		}

		firstToSecondCrossDirection.Normalize();

		float firstToSecondCrossVelocity =
			Vector3.Dot(
				tangentVelocity,
				firstToSecondCrossDirection);

		bool useFirstAsSource;

		if (firstToSecondCrossVelocity >
			m_MinimumCalculationValue)
		{
			useFirstAsSource =
				true;
		}
		else if (firstToSecondCrossVelocity <
			-m_MinimumCalculationValue)
		{
			useFirstAsSource =
				false;
		}
		else
		{
			// Edge方向へほぼ真っすぐ進む対称ケースでは、
			// Triangle Indexで決定論的にSource側を選びます。
			useFirstAsSource =
				edgeReference.TriangleIndex <=
				neighborTriangleIndex;
		}

		int sourceTriangleIndex =
			useFirstAsSource
				? edgeReference.TriangleIndex
				: neighborTriangleIndex;

		int targetTriangleIndex =
			useFirstAsSource
				? neighborTriangleIndex
				: edgeReference.TriangleIndex;

		int targetEdgeIndex =
			useFirstAsSource
				? neighborEdgeIndex
				: edgeReference.EdgeIndex;

		Vector3 sourceNormal =
			useFirstAsSource
				? firstNormal
				: secondNormal;

		Vector3 targetNormal =
			useFirstAsSource
				? secondNormal
				: firstNormal;

		float signedAngle =
			Vector3.SignedAngle(
				sourceNormal,
				targetNormal,
				edgeDirection);

		if (Mathf.Abs(
			signedAngle) <=
			m_MinimumCalculationValue)
		{
			return false;
		}

		float currentAngleMagnitude =
			Vector3.Angle(
				sourceNormal,
				radialDirection);

		float totalAngleMagnitude =
			Mathf.Abs(
				signedAngle);

		if (currentAngleMagnitude >
			totalAngleMagnitude +
				m_VertexSphereArcAngleTolerance)
		{
			return false;
		}

		float currentAngle =
			Mathf.Min(
				currentAngleMagnitude,
				totalAngleMagnitude) *
			Mathf.Sign(
				signedAngle);

		Vector3 crossDirection =
			Vector3.Cross(
				edgeDirection,
				radialDirection) *
			Mathf.Sign(
				signedAngle);

		if (crossDirection.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return false;
		}

		crossDirection.Normalize();

		float alongSpeed =
			Vector3.Dot(
				tangentVelocity,
				edgeDirection);

		float crossSpeed =
			Mathf.Max(
				0.0f,
				Vector3.Dot(
					tangentVelocity,
					crossDirection));

		float reconstructedSpeed =
			Mathf.Sqrt(
				alongSpeed *
					alongSpeed +
				crossSpeed *
					crossSpeed);

		if (reconstructedSpeed <=
			m_MinimumCalculationValue)
		{
			return false;
		}

		float speedScale =
			surfaceSpeed /
			reconstructedSpeed;

		alongSpeed *=
			speedScale;

		crossSpeed *=
			speedScale;

		m_CurrentTriangleIndex =
			sourceTriangleIndex;

		m_SurfacePoint =
			vertexPoint;

		m_IsConvexEdgeTransition =
			true;

		m_ConvexUseCircularCenterOffset =
			true;

		m_ConvexTargetTriangleIndex =
			targetTriangleIndex;

		m_ConvexTargetEdgeIndex =
			targetEdgeIndex;

		m_ConvexEdgeDirection =
			edgeDirection;

		m_ConvexEdgeStartPoint =
			edgeStart;

		m_ConvexEdgeEndPoint =
			edgeEnd;

		m_ConvexSourceNormal =
			sourceNormal.normalized;

		m_ConvexTargetNormal =
			targetNormal.normalized;

		m_ConvexSignedAngle =
			signedAngle;

		m_ConvexCurrentAngle =
			currentAngle;

		m_ConvexAlongSpeed =
			alongSpeed;

		m_ConvexCrossSpeed =
			crossSpeed;

		m_DidTraverseConvexEdgeThisStep =
			true;

		UpdateConvexTransitionVelocity();

		return true;
	}

	/// <summary>
	/// Sharp Convex Edgeの円弧移動を進めます。
	/// </summary>
	/// <remarks>
	/// 円弧完了までの時間だけでなく、
	/// Edge方向速度によって実Edge端のVertexへ到達する時間も計算します。
	///
	/// Vertexへ先に到達した場合はSurfacePointをVertexへ固定し、
	/// Ball中心の運動をVertex球面パッチへ引き継ぎます。
	/// 球面パッチを出た後は、到達した実Convex Edgeの
	/// 円柱遷移へ連続的に戻ります。
	/// </remarks>
	private bool AdvanceConvexEdgeTransition(
		ref float remainingTime,
		out bool completed,
		out int completedEntryEdgeIndex)
	{
		completed =
			false;

		completedEntryEdgeIndex =
			-1;

		if (!m_IsConvexEdgeTransition)
		{
			completed =
				true;

			return true;
		}

		float offsetRadius =
			Mathf.Max(
				m_BallRadius +
					m_Height,
				m_MinimumCalculationValue);

		float totalAngleMagnitude =
			Mathf.Abs(
				m_ConvexSignedAngle);

		float currentAngleMagnitude =
			Mathf.Abs(
				m_ConvexCurrentAngle);

		float remainingAngle =
			Mathf.Max(
				0.0f,
				totalAngleMagnitude -
					currentAngleMagnitude);

		if (remainingAngle <=
			m_MinimumCalculationValue)
		{
			return CompleteConvexEdgeTransition(
				out completedEntryEdgeIndex,
				out completed);
		}

		float angularSpeed =
			m_ConvexCrossSpeed >
				m_MinimumCalculationValue
				? m_ConvexCrossSpeed /
					offsetRadius *
					Mathf.Rad2Deg
				: 0.0f;

		float timeToComplete =
			angularSpeed >
				m_MinimumCalculationValue
				? remainingAngle /
					angularSpeed
				: float.PositiveInfinity;

		float timeToVertex =
			float.PositiveInfinity;

		Vector3 reachedVertexPoint =
			Vector3.zero;

		if (Mathf.Abs(
				m_ConvexAlongSpeed) >
			m_MinimumCalculationValue)
		{
			bool movesTowardEdgeEnd =
				m_ConvexAlongSpeed >
				0.0f;

			reachedVertexPoint =
				movesTowardEdgeEnd
					? m_ConvexEdgeEndPoint
					: m_ConvexEdgeStartPoint;

			Vector3 alongDirection =
				m_ConvexEdgeDirection *
				Mathf.Sign(
					m_ConvexAlongSpeed);

			float distanceToVertex =
				Vector3.Dot(
					reachedVertexPoint -
						m_SurfacePoint,
					alongDirection);

			if (distanceToVertex <=
				m_MinimumCalculationValue)
			{
				timeToVertex =
					0.0f;
			}
			else
			{
				timeToVertex =
					distanceToVertex /
					Mathf.Abs(
						m_ConvexAlongSpeed);
			}
		}

		float stepTime =
			Mathf.Min(
				remainingTime,
				Mathf.Min(
					timeToComplete,
					timeToVertex));

		float convexSurfaceSpeed =
			Mathf.Sqrt(
				m_ConvexAlongSpeed *
					m_ConvexAlongSpeed +
				m_ConvexCrossSpeed *
					m_ConvexCrossSpeed);

		if (convexSurfaceSpeed <=
			m_MinimumCalculationValue)
		{
			remainingTime =
				0.0f;

			return true;
		}

		m_SurfaceDistanceTravelledThisStep +=
			convexSurfaceSpeed *
			stepTime;

		m_SurfacePoint +=
			m_ConvexEdgeDirection *
				m_ConvexAlongSpeed *
				stepTime;

		if (angularSpeed >
			m_MinimumCalculationValue)
		{
			float angleStep =
				angularSpeed *
				stepTime *
				Mathf.Sign(
					m_ConvexSignedAngle);

			m_ConvexCurrentAngle +=
				angleStep;
		}

		remainingTime -=
			stepTime;

		if (remainingTime <
			0.0f)
		{
			remainingTime =
				0.0f;
		}

		bool reachedVertex =
			timeToVertex <=
				stepTime +
					m_MinimumCalculationValue &&
			timeToVertex <=
				timeToComplete +
					m_MinimumCalculationValue;

		if (reachedVertex)
		{
			m_SurfacePoint =
				reachedVertexPoint;

			UpdateConvexTransitionVelocity();

			if (TryGetCurrentBallCenterOffset(
				out Vector3 currentBallCenterOffset) &&
				BeginConvexVertexSpherePatch(
					reachedVertexPoint,
					currentBallCenterOffset,
					m_SurfaceVelocity))
			{
				ResetConvexEdgeTransition();

				completed =
					true;

				completedEntryEdgeIndex =
					-1;

				return true;
			}

			// Convex Vertexとして成立しないMeshでは、
			// 従来通り現在Edgeだけを完了して通常Triangleへ戻します。
			m_ConvexCurrentAngle =
				m_ConvexSignedAngle;

			UpdateConvexTransitionVelocity();

			return CompleteConvexEdgeTransition(
				out completedEntryEdgeIndex,
				out completed);
		}

		bool reachedTarget =
			timeToComplete <=
				stepTime +
					m_MinimumCalculationValue;

		if (reachedTarget)
		{
			m_ConvexCurrentAngle =
				m_ConvexSignedAngle;
		}

		UpdateConvexTransitionVelocity();

		if (!reachedTarget)
		{
			return true;
		}

		return CompleteConvexEdgeTransition(
			out completedEntryEdgeIndex,
			out completed);
	}

	/// <summary>
	/// Convex Edgeの円弧移動を終了し、
	/// Neighbor Triangleへ移行します。
	/// </summary>
	private bool CompleteConvexEdgeTransition(
		out int completedEntryEdgeIndex,
		out bool completed)
	{
		completedEntryEdgeIndex =
			m_ConvexTargetEdgeIndex;

		completed =
			false;

		if (!TryGetTriangleWorldData(
			m_ConvexTargetTriangleIndex,
			out Vector3 targetVertexA,
			out _,
			out _,
			out Vector3 targetNormal))
		{
			return false;
		}

		m_CurrentTriangleIndex =
			m_ConvexTargetTriangleIndex;

		m_SurfacePoint =
			ProjectPointToPlane(
				m_SurfacePoint,
				targetVertexA,
				targetNormal);

		m_SurfaceVelocity =
			Vector3.ProjectOnPlane(
				m_SurfaceVelocity,
				targetNormal);

		float expectedSpeed =
			Mathf.Sqrt(
				m_ConvexAlongSpeed *
					m_ConvexAlongSpeed +
				m_ConvexCrossSpeed *
					m_ConvexCrossSpeed);

		if (expectedSpeed >
				m_MinimumCalculationValue &&
			m_SurfaceVelocity.sqrMagnitude >
				m_MinimumCalculationValue *
					m_MinimumCalculationValue)
		{
			m_SurfaceVelocity =
				m_SurfaceVelocity.normalized *
					expectedSpeed;
		}

		ResetConvexEdgeTransition();

		// Faceへ戻った時点で1回のConvex角通過は終了です。
		// 次に別のSharp Convexへ入るときは、再び1回だけ減速できます。
		m_HasAppliedConvexTraversalSpeedRetention =
			false;

		completed =
			true;

		return true;
	}

	/// <summary>
	/// 現在のConvex角通過に対する速度減衰倍率を取得します。
	/// </summary>
	/// <remarks>
	/// 1回目だけm_ConvexEdgeSpeedRetentionを返し、
	/// 同じEdge → Vertex球面などの連続遷移では1.0を返します。
	/// </remarks>
	private float ConsumeConvexTraversalSpeedRetention()
	{
		if (m_HasAppliedConvexTraversalSpeedRetention)
		{
			return 1.0f;
		}

		m_HasAppliedConvexTraversalSpeedRetention =
			true;

		return m_ConvexEdgeSpeedRetention;
	}

	/// <summary>
	/// 現在のConvex Edge円弧上の法線を取得します。
	/// </summary>
	private Vector3 GetConvexTransitionNormal()
	{
		Quaternion rotation =
			Quaternion.AngleAxis(
				m_ConvexCurrentAngle,
				m_ConvexEdgeDirection);

		Vector3 transitionNormal =
			rotation *
				m_ConvexSourceNormal;

		if (transitionNormal.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return m_ConvexSourceNormal;
		}

		return transitionNormal.normalized;
	}

	/// <summary>
	/// Convex Edge円弧上の現在速度を更新します。
	/// </summary>
	private void UpdateConvexTransitionVelocity()
	{
		Vector3 transitionNormal =
			GetConvexTransitionNormal();

		float angleSign =
			Mathf.Sign(
				m_ConvexSignedAngle);

		Vector3 crossDirection =
			Vector3.Cross(
				m_ConvexEdgeDirection,
				transitionNormal) *
			angleSign;

		if (crossDirection.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return;
		}

		crossDirection.Normalize();

		m_SurfaceVelocity =
			m_ConvexEdgeDirection *
				m_ConvexAlongSpeed +
			crossDirection *
				m_ConvexCrossSpeed;
	}

	/// <summary>
	/// 指定EdgeのWorld座標を取得します。
	/// </summary>
	private bool TryGetEdgeWorldVertices(
		int triangleIndex,
		int edgeIndex,
		out Vector3 edgeStart,
		out Vector3 edgeEnd)
	{
		edgeStart =
			Vector3.zero;

		edgeEnd =
			Vector3.zero;

		if (m_SurfaceSource == null ||
			m_MeshData == null)
		{
			return false;
		}

		m_MeshData.GetEdgeLocalVertices(
			triangleIndex,
			edgeIndex,
			out Vector3 localEdgeStart,
			out Vector3 localEdgeEnd);

		Transform surfaceTransform =
			m_SurfaceSource.transform;

		edgeStart =
			surfaceTransform.TransformPoint(
				localEdgeStart);

		edgeEnd =
			surfaceTransform.TransformPoint(
				localEdgeEnd);

		return (
			edgeEnd -
				edgeStart
			).sqrMagnitude >
			m_MinimumCalculationValue *
				m_MinimumCalculationValue;
	}

	/// <summary>
	/// Convex Edge遷移状態を初期化します。
	/// </summary>
	private void ResetConvexEdgeTransition()
	{
		m_IsConvexEdgeTransition =
			false;

		m_ConvexUseCircularCenterOffset =
			false;

		m_ConvexTargetTriangleIndex =
			-1;

		m_ConvexTargetEdgeIndex =
			-1;

		m_ConvexEdgeDirection =
			Vector3.zero;

		m_ConvexEdgeStartPoint =
			Vector3.zero;

		m_ConvexEdgeEndPoint =
			Vector3.zero;

		m_ConvexSourceNormal =
			Vector3.zero;

		m_ConvexTargetNormal =
			Vector3.zero;

		m_ConvexSignedAngle =
			0.0f;

		m_ConvexCurrentAngle =
			0.0f;

		m_ConvexAlongSpeed =
			0.0f;

		m_ConvexCrossSpeed =
			0.0f;
	}

	/// <summary>
	/// 現在のConvex Edge用Ball中心Offsetを取得します。
	/// </summary>
	/// <remarks>
	/// 通常のFace → Edge遷移では従来のFlattened Bezierを使用します。
	/// Vertex球面パッチから接続されたEdgeだけは、
	/// 球面との半径連続性を保つため真円Offsetを使用します。
	/// </remarks>
	private Vector3 GetConvexBallCenterOffset()
	{
		float offsetRadius =
			Mathf.Max(
				m_BallRadius +
					m_Height,
				m_MinimumCalculationValue);

		if (m_ConvexUseCircularCenterOffset)
		{
			return GetConvexTransitionNormal() *
				offsetRadius;
		}

		return GetFlattenedConvexBallCenterOffset();
	}

	/// <summary>
	/// Sharp Convex Edge上のBall中心Offsetを、
	/// 円弧より少し内側へ潰したBezierカーブとして取得します。
	/// </summary>
	private Vector3 GetFlattenedConvexBallCenterOffset()
	{
		float offsetRadius =
			Mathf.Max(
				m_BallRadius +
					m_Height,
				m_MinimumCalculationValue);

		float totalAngleMagnitude =
			Mathf.Abs(
				m_ConvexSignedAngle);

		if (totalAngleMagnitude <=
			m_MinimumCalculationValue)
		{
			return m_ConvexSourceNormal *
				offsetRadius;
		}

		float progress =
			Mathf.Clamp01(
				Mathf.Abs(
					m_ConvexCurrentAngle) /
				totalAngleMagnitude);

		float angleSign =
			Mathf.Sign(
				m_ConvexSignedAngle);

		Vector3 sourceTangent =
			Vector3.Cross(
				m_ConvexEdgeDirection,
				m_ConvexSourceNormal) *
			angleSign;

		Vector3 targetTangent =
			Vector3.Cross(
				m_ConvexEdgeDirection,
				m_ConvexTargetNormal) *
			angleSign;

		if (sourceTangent.sqrMagnitude <=
				m_MinimumCalculationValue *
					m_MinimumCalculationValue ||
			targetTangent.sqrMagnitude <=
				m_MinimumCalculationValue *
					m_MinimumCalculationValue)
		{
			return GetConvexTransitionNormal() *
				offsetRadius;
		}

		sourceTangent.Normalize();
		targetTangent.Normalize();

		// 円弧を3次Bezierで近似するときの標準Handle長を求め、
		// Flatten倍率で短くすることで中央をEdge側へ潰します。
		float clampedAngleDegrees =
			Mathf.Min(
				totalAngleMagnitude,
				179.0f);

		float angleRadians =
			clampedAngleDegrees *
			Mathf.Deg2Rad;

		float circularHandleLength =
			4.0f /
				3.0f *
			Mathf.Tan(
				angleRadians *
					0.25f) *
			offsetRadius;

		float flattenedHandleLength =
			circularHandleLength *
			m_ConvexCurveFlatten;

		Vector3 point0 =
			m_ConvexSourceNormal *
				offsetRadius;

		Vector3 point1 =
			point0 +
			sourceTangent *
				flattenedHandleLength;

		Vector3 point3 =
			m_ConvexTargetNormal *
				offsetRadius;

		Vector3 point2 =
			point3 -
			targetTangent *
				flattenedHandleLength;

		return EvaluateCubicBezier(
			point0,
			point1,
			point2,
			point3,
			progress);
	}
}
