using UnityEngine;

/// <summary>
/// Sharp Convex EdgeをBall半径に沿って回り込む遷移処理を担当します。
/// </summary>
/// <remarks>
/// 主な処理の流れ:
/// 1. 共有EdgeのWorld位置・方向と前後Surfaceの法線から回転Arcを構築します。
/// 2. Ball中心をEdge軸の周囲へ回転させ、角を貫通せず外側から回り込みます。
/// 3. Vertex球面パッチから入った場合は位置連続性を保つ専用Offsetを使用します。
/// 4. 回り込み中はWorld速度を現在のArc接線方向へ同期します。
/// 5. Arc完了後に隣Triangleへ切り替え、通常Surface移動へ戻します。
/// 6. 1回の角通過で速度減衰が重複しないよう適用済み状態を管理します。
///
/// このファイルは「意図したConvex軌道」の計算を担当します。
/// 同一Mesh内の別壁など、軌道上に存在する非隣接Geometryとの空間衝突は
/// BallPhysicsController.Surface.csのSphereCastで別途判定します。
/// </remarks>
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
	/// 現在FaceからSharp Convex Edgeの回り込み状態へ入ります。
	/// </summary>
	/// <remarks>
	/// Edgeを無限直線ではなく有限Segmentとして扱い、
	/// Ball中心が実際にEdge範囲内へ到達している場合だけ遷移を開始します。
	/// 現在WorldStateからEdge軸・回転方向・開始半径を構築し、以降のArc移動に必要な状態を保存します。
	/// </remarks>
	private bool BeginConvexEdgeTransition(
		int sourceEdgeIndex,
		int targetTriangleIndex,
		int targetEdgeIndex,
		Vector3 sourceNormal,
		Vector3 targetNormal)
	{
		if (!m_HasWorldState)
		{
			return RecordGeometryFailure("NoWorldState");
		}

		if (!TryGetEdgeWorldVertices(
			m_CurrentTriangleIndex,
			sourceEdgeIndex,
			out Vector3 edgeStart,
			out Vector3 edgeEnd))
		{
			return RecordGeometryFailure("EdgeWorldVertices");
		}

		Vector3 edgeVector =
			edgeEnd -
				edgeStart;

		if (edgeVector.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return RecordGeometryFailure("DegenerateEdge");
		}

		float edgeLength =
			edgeVector.magnitude;

		Vector3 edgeDirection =
			edgeVector.normalized;

		Vector3 normalizedSourceNormal =
			sourceNormal.normalized;

		Vector3 normalizedTargetNormal =
			targetNormal.normalized;

		float signedAngle =
			Vector3.SignedAngle(
				normalizedSourceNormal,
				normalizedTargetNormal,
				edgeDirection);

		if (Mathf.Abs(
			signedAngle) <=
			m_MinimumCalculationValue)
		{
			return RecordGeometryFailure("ZeroSignedAngle");
		}

		Vector3 beforeCenter =
			m_WorldCenter;

		float edgeProjectionDistance =
			Vector3.Dot(
				m_WorldCenter -
					edgeStart,
				edgeDirection);

		// Convex Edgeは無限直線ではなく、実際の有限Segmentとして判定します。
		if (edgeProjectionDistance <
				-m_TransitionPositionTolerance ||
			edgeProjectionDistance >
				edgeLength +
					m_TransitionPositionTolerance)
		{
			return RecordGeometryFailure($"EdgeProjectionOutsideSegment Projection={edgeProjectionDistance:F6} EdgeLength={edgeLength:F6}");
		}

		edgeProjectionDistance =
			Mathf.Clamp(
				edgeProjectionDistance,
				0.0f,
				edgeLength);

		Vector3 edgeProjectionPoint =
			edgeStart +
			edgeDirection *
				edgeProjectionDistance;

		Vector3 radialVector =
			m_WorldCenter -
				edgeProjectionPoint;

		float currentRadius =
			radialVector.magnitude;

		if (currentRadius <=
			m_MinimumCalculationValue ||
			currentRadius <
				m_BallRadius -
					m_TransitionPositionTolerance)
		{
			return RecordGeometryFailure($"RadiusTooSmall Radius={currentRadius:F6} BallRadius={m_BallRadius:F6}");
		}

		Vector3 radialDirection =
			radialVector /
			currentRadius;

		float currentAngle =
			Vector3.SignedAngle(
				normalizedSourceNormal,
				radialDirection,
				edgeDirection);

		if (Mathf.Abs(
			currentAngle) >
				m_TransitionAngleTolerance &&
			Mathf.Sign(
				currentAngle) !=
					Mathf.Sign(
						signedAngle))
		{
			// Smooth NormalからSharp Convexへ入る場合の微小な逆方向角だけを許容します。
			// 角度を丸めず、球中心を維持して実際の角度から回転を開始します。
			const float smoothEntryAngleTolerance = 1.0f;
			float sourcePlaneDistance = Vector3.Dot(radialVector, normalizedSourceNormal);
			if (!(Mathf.Abs(currentAngle) <= smoothEntryAngleTolerance) ||
				!(sourcePlaneDistance >= m_BallRadius - m_TransitionPositionTolerance))
			{
				return RecordGeometryFailure($"OppositeAngle CurrentAngle={currentAngle:F6} SignedAngle={signedAngle:F6} SourcePlaneDistance={sourcePlaneDistance:F6}");
			}
		}

		if (Mathf.Abs(
			currentAngle) >
			Mathf.Abs(
				signedAngle) +
			m_TransitionAngleTolerance)
		{
			return RecordGeometryFailure($"AngleOutsideArc CurrentAngle={currentAngle:F6} SignedAngle={signedAngle:F6}");
		}

		float radialSpeed =
			Vector3.Dot(
				m_WorldVelocity,
				radialDirection);

		Vector3 tangentVelocity =
			m_WorldVelocity -
			radialDirection *
				radialSpeed;

		float alongSpeed =
			Vector3.Dot(
				tangentVelocity,
				edgeDirection);

		bool startsAtEdgeStart =
			edgeProjectionDistance <=
				m_TransitionPositionTolerance;

		bool startsAtEdgeEnd =
			edgeProjectionDistance >=
				edgeLength -
					m_TransitionPositionTolerance;

		// Edge端での極小速度は丸め誤差として扱い、実際の端外への移動は従来どおり拒否します。
		const float alongSpeedTolerance = 0.0001f;
		float removedAlongSpeed = 0.0f;
		if ((startsAtEdgeStart || startsAtEdgeEnd) &&
			Mathf.Abs(alongSpeed) <= alongSpeedTolerance)
		{
			removedAlongSpeed = alongSpeed;
			tangentVelocity -= edgeDirection * removedAlongSpeed;
			alongSpeed = 0.0f;
		}

		if ((startsAtEdgeStart &&
				alongSpeed <
					-m_MinimumCalculationValue) ||
			(startsAtEdgeEnd &&
				alongSpeed >
					m_MinimumCalculationValue))
		{
			return RecordGeometryFailure($"AlongSpeedOutsideEdge AlongSpeed={alongSpeed:F6} Projection={edgeProjectionDistance:F6} EdgeLength={edgeLength:F6}");
		}

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
			return RecordGeometryFailure("InvalidCrossDirection");
		}

		crossDirection.Normalize();

		float crossSpeed =
			Vector3.Dot(
				tangentVelocity,
				crossDirection);

		// Target側へ回り込む速度成分が無い場合は、
		// Convexへ状態を切り替えません。
		if (crossSpeed <=
			m_MinimumCalculationValue)
		{
			return RecordGeometryFailure($"CrossSpeedTooSmall CrossSpeed={crossSpeed:F6}");
		}

		// Convex角へ入るときの既存減衰を1回だけ適用します。
		// World速度全体へ同じ倍率を掛けることで、
		// 進行方向を変えずに運動エネルギーだけを減らします。
		float speedRetention =
			ConsumeConvexTraversalSpeedRetention();

		Vector3 retainedWorldVelocity =
			(m_WorldVelocity - edgeDirection * removedAlongSpeed) *
				speedRetention;

		radialSpeed *=
			speedRetention;

		tangentVelocity *=
			speedRetention;

		alongSpeed *=
			speedRetention;

		crossSpeed *=
			speedRetention;

		m_SurfacePoint =
			edgeProjectionPoint;

		m_SurfaceVelocity =
			tangentVelocity;

		m_Height =
			Mathf.Max(
				0.0f,
				currentRadius -
					m_BallRadius);

		m_VerticalVelocity =
			radialSpeed;

		m_ConvexUseCircularCenterOffset =
			true;

		m_IsConvexEdgeTransition =
			true;

		m_ConvexTargetTriangleIndex =
			targetTriangleIndex;

		RecordTraversalTriangles();

		m_ConvexTargetEdgeIndex =
			targetEdgeIndex;

		m_ConvexEdgeDirection =
			edgeDirection;

		m_ConvexEdgeStartPoint =
			edgeStart;

		m_ConvexEdgeEndPoint =
			edgeEnd;

		m_ConvexSourceNormal =
			normalizedSourceNormal;

		m_ConvexTargetNormal =
			normalizedTargetNormal;

		m_ConvexSignedAngle =
			signedAngle;

		m_ConvexCurrentAngle =
			currentAngle;

		m_ConvexAlongSpeed =
			alongSpeed;

		m_ConvexCrossSpeed =
			crossSpeed;

		UpdateConvexTransitionVelocity();

		// 位置はConvex進入前と同一に保ち、Edge端の極小速度を除いた後に
		// 既存Retentionを適用したWorldStateを確定します。
		SetWorldState(
			beforeCenter,
			retainedWorldVelocity);

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
			return RecordGeometryFailure("InvalidMeshOrVertexMotion");
		}

		if (!m_MeshData.TryGetNeighbor(
			edgeReference.TriangleIndex,
			edgeReference.EdgeIndex,
			out int neighborTriangleIndex,
			out int neighborEdgeIndex))
		{
			return RecordGeometryFailure("MissingConvexNeighbor");
		}

		if (!TryGetEdgeWorldVertices(
			edgeReference.TriangleIndex,
			edgeReference.EdgeIndex,
			out Vector3 edgeStart,
			out Vector3 edgeEnd))
		{
			return RecordGeometryFailure("EdgeWorldVertices");
		}

		Vector3 edgeVector =
			edgeEnd -
				edgeStart;

		if (edgeVector.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return RecordGeometryFailure("DegenerateEdge");
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
			return RecordGeometryFailure("TriangleWorldData");
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
			return RecordGeometryFailure("ProjectedTangentTooSmall");
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
			return RecordGeometryFailure("ZeroSignedAngle");
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
			return RecordGeometryFailure("InvalidCrossDirection");
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
			return RecordGeometryFailure("ZeroSignedAngle");
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
			return RecordGeometryFailure("AngleOutsideArc");
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
			return RecordGeometryFailure("InvalidCrossDirection");
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
			return RecordGeometryFailure("ReconstructedSpeedTooSmall");
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

		RecordTraversalTriangles();

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


		UpdateConvexTransitionVelocity();

		return true;
	}

	/// <summary>
	/// Convex Edgeの回り込みArcを残り時間分だけ進めます。
	/// </summary>
	/// <remarks>
	/// Surface速度をEdge軸回りの角速度へ変換してBall中心を回転させます。
	/// Arc終端へ到達した場合は残り時間を保持したまま隣Triangleへ遷移し、同じPhysics Step内の移動を継続します。
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

		// 進行方向を基準に符号付きの進捗を求め、逆方向からの進入時間も消費します。
		float currentAngleProgress =
			m_ConvexCurrentAngle *
				Mathf.Sign(m_ConvexSignedAngle);

		float remainingAngle =
			Mathf.Max(
				0.0f,
				totalAngleMagnitude -
					currentAngleProgress);

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

			if (distanceToVertex <
				-m_TransitionPositionTolerance)
			{
				timeToVertex =
					float.PositiveInfinity;
			}
			else if (distanceToVertex <=
				m_TransitionPositionTolerance)
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

			if (!RefreshWorldStateFromCurrentTraversal())
			{
				return RecordGeometryFailure("RefreshWorldStateAtVertex");
			}

			if (BeginConvexVertexSpherePatch(
				reachedVertexPoint))
			{
				ResetConvexEdgeTransition();

				completed =
					true;

				completedEntryEdgeIndex =
					-1;

				return true;
			}

			// Mixed Convex/Concave vertices may not form a general Convex sphere.
			// Stop only the edge-axis component and consume real time for the
			// remaining circular motion instead of completing it at zero time.
			m_ConvexAlongSpeed =
				0.0f;

			UpdateConvexTransitionVelocity();

			if (remainingTime >
				m_MinimumCalculationValue)
			{
				return AdvanceConvexEdgeTransition(
					ref remainingTime,
					out completed,
					out completedEntryEdgeIndex);
			}

			completed =
				false;

			completedEntryEdgeIndex =
				-1;

			return true;
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
	/// Convex Arcの終端でCurrentTriangleを遷移先へ切り替え、
	/// 通常のSurface移動へ復帰します。
	/// </summary>
	/// <remarks>
	/// Ball中心とWorld速度の連続性を保った状態でSurface表現を遷移先Triangleへ同期します。
	/// Vertex球面パッチから連続してきた場合も、速度減衰を重複適用しない状態を維持します。
	/// </remarks>
	private bool CompleteConvexEdgeTransition(
		out int completedEntryEdgeIndex,
		out bool completed)
	{
		completedEntryEdgeIndex =
			m_ConvexTargetEdgeIndex;

		completed =
			false;

		// Convexを抜ける直前の位置・速度をWorldStateへ確定します。
		// ここではまだConvex状態なので、円弧上の正しい中心位置と
		// 接線速度から再構成できます。
		if (!RefreshWorldStateFromCurrentTraversal())
		{
			return RecordGeometryFailure("RefreshWorldStateBeforeExit");
		}

		Vector3 beforeCenter =
			m_WorldCenter;

		Vector3 beforeVelocity =
			m_WorldVelocity;

		if (!TryGetTriangleWorldData(
			m_ConvexTargetTriangleIndex,
			out Vector3 targetVertexA,
			out _,
			out _,
			out Vector3 targetNormal))
		{
			return RecordGeometryFailure("TargetTriangleWorldData");
		}

		float signedCenterDistance =
			Vector3.Dot(
				beforeCenter -
					targetVertexA,
				targetNormal);

		if (signedCenterDistance <
			m_BallRadius -
				m_TransitionPositionTolerance)
		{
			return RecordGeometryFailure("CenterInsideTargetRadius");
		}

		m_CurrentTriangleIndex =
			m_ConvexTargetTriangleIndex;

		// World中心からTarget Faceへ垂線を下ろしてSurfacePointを作るため、
		// Convex終了時の位置を作り直してワープさせません。
		m_SurfacePoint =
			ProjectPointToPlane(
				beforeCenter,
				targetVertexA,
				targetNormal);

		m_Height =
			Mathf.Max(
				0.0f,
				signedCenterDistance -
					m_BallRadius);

		// World速度をTarget Face基準へ分解するだけで、
		// Convex -> Face切替自体では速度を増減・回転させません。
		m_SurfaceVelocity =
			Vector3.ProjectOnPlane(
				beforeVelocity,
				targetNormal);

		m_VerticalVelocity =
			Vector3.Dot(
				beforeVelocity,
				targetNormal);

		ResetConvexEdgeTransition();

		// Faceへ戻った時点で1回のConvex角通過は終了です。
		// 次のSharp Convexへ入るときは再び1回だけ減速できます。
		m_HasAppliedConvexTraversalSpeedRetention =
			false;

		// Traversal表現だけをFaceへ切り替え、
		// Ball本体のWorld位置・World速度はそのまま保持します。
		SetWorldState(
			beforeCenter,
			beforeVelocity);

		ValidateTransitionWorldState(
			beforeCenter,
			beforeVelocity,
			"Convex -> Surface");

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
