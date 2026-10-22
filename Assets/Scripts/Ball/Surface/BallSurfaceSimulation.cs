using UnityEngine;

/// <summary>
/// BallSurface上でのボールの移動計算を担当します。
/// </summary>
/// <remarks>
/// このクラスはMonoBehaviourではありません。
///
/// Surface Physicsではボールの運動を、
///
/// ・Surface接線方向
/// ・Surface法線方向
///
/// の2つへ分離して管理します。
///
/// Surface接線方向:
/// CurrentTriangle
/// SurfacePoint
/// SurfaceVelocity
///
/// Surface法線方向:
/// Height
/// VerticalVelocity
///
/// これにより、Surfaceそのものを平面へ展開した空間上で
/// ボールをバウンドさせ、その結果を3D Surfaceへ
/// 折り戻すような挙動を実現します。
/// </remarks>
public sealed class BallSurfaceSimulation
{
	/// <summary>
	/// 1回のSurface移動結果です。
	/// </summary>
	public enum EAdvanceResult
	{
		Moved,

		NotInitialized,

		ConcaveEdgeReached,

		BoundaryEdgeReached,

		UnsupportedEdgeReached,

		InvalidGeometry,

		TransitionLimitReached
	}

	/// <summary>
	/// Barycentric座標判定の許容値です。
	/// </summary>
	private const float m_BarycentricTolerance = 0.0001f;

	/// <summary>
	/// Neighbor Triangleへ入った直後、
	/// 同じEdgeへ即座に戻ることを防ぐ許容値です。
	/// </summary>
	private const float m_EntryEdgeIgnoreFraction = 0.0001f;

	/// <summary>
	/// 計算上0として扱う最小値です。
	/// </summary>
	private const float m_MinimumCalculationValue = 0.000001f;

	/// <summary>
	/// Surfaceへ接地したと判断する高さです。
	/// </summary>
	private const float m_MinimumHeight = 0.0001f;

	/// <summary>
	/// 1Physics Step内で通過可能な
	/// Triangle数の最大値です。
	/// </summary>
	private const int m_MaximumTriangleTransitionsPerStep = 128;

	private BallSurfaceSource m_SurfaceSource;
	private BallSurfaceMeshData m_MeshData;

	private int m_CurrentTriangleIndex;

	private Vector3 m_SurfacePoint;
	private Vector3 m_SurfaceVelocity;

	private float m_Height;
	private float m_VerticalVelocity;

	private bool m_IsInitialized;

	private int m_LastEdgeTriangleIndex;
	private int m_LastEdgeIndex;

	private Vector3 m_LastEdgePoint;

	private BallSurfaceMeshData.EEdgeType m_LastEdgeType;

	// ---------------------------------------------------------------------
	// Surface Physics Settings
	// ---------------------------------------------------------------------

	/// <summary>
	/// Surfaceへ向かう重力加速度です。
	/// </summary>
	private float m_GravityAcceleration;

	/// <summary>
	/// この衝突速度未満では再バウンドしません。
	/// </summary>
	private float m_BounceSpeedThreshold;

	/// <summary>
	/// 法線方向速度の保持率です。
	/// </summary>
	private float m_BounceNormalRetention;

	/// <summary>
	/// バウンド時のSurface接線方向速度保持率です。
	/// </summary>
	private float m_BounceTangentialRetention;

	/// <summary>
	/// バウンド時の最低法線方向速度です。
	/// </summary>
	private float m_MinBounceNormalSpeed;

	/// <summary>
	/// バウンド時の最大法線方向速度です。
	/// </summary>
	private float m_MaxBounceNormalSpeed;

	/// <summary>
	/// Surfaceへ接地した後の減速度です。
	/// </summary>
	private float m_GroundedDeceleration;

	/// <summary>
	/// Surface上で停止したと判定する速度です。
	/// </summary>
	private float m_GroundedStopSpeedThreshold;

	/// <summary>
	/// Surface Simulationが初期化済みか取得します。
	/// </summary>
	public bool IsInitialized
	{
		get
		{
			return m_IsInitialized;
		}
	}

	/// <summary>
	/// 現在ロックしているSurfaceを取得します。
	/// </summary>
	public BallSurfaceSource SurfaceSource
	{
		get
		{
			return m_SurfaceSource;
		}
	}

	/// <summary>
	/// 現在のTriangle Indexです。
	/// </summary>
	public int CurrentTriangleIndex
	{
		get
		{
			return m_CurrentTriangleIndex;
		}
	}

	/// <summary>
	/// Surface上の基準点です。
	/// </summary>
	public Vector3 SurfacePoint
	{
		get
		{
			return m_SurfacePoint;
		}
	}

	/// <summary>
	/// Surface接線方向の速度です。
	/// </summary>
	public Vector3 SurfaceVelocity
	{
		get
		{
			return m_SurfaceVelocity;
		}
	}

	/// <summary>
	/// Surface上の速度です。
	/// </summary>
	public float SurfaceSpeed
	{
		get
		{
			return m_SurfaceVelocity.magnitude;
		}
	}

	/// <summary>
	/// Surfaceからの高さです。
	/// </summary>
	public float Height
	{
		get
		{
			return m_Height;
		}
	}

	/// <summary>
	/// Surface法線方向速度です。
	///
	/// 正:
	/// Surfaceから離れる方向。
	///
	/// 負:
	/// Surfaceへ向かう方向。
	/// </summary>
	public float VerticalVelocity
	{
		get
		{
			return m_VerticalVelocity;
		}
	}

	/// <summary>
	/// Surfaceへ接地しているか取得します。
	/// </summary>
	public bool IsGrounded
	{
		get
		{
			return m_Height <=
					m_MinimumHeight &&
				m_VerticalVelocity <=
					0.0f;
		}
	}

	/// <summary>
	/// 直近のAdvanceでEdgeへ到達したか取得します。
	/// </summary>
	public bool HasLastEdge
	{
		get
		{
			return m_LastEdgeIndex >=
				0;
		}
	}

	public BallSurfaceMeshData.EEdgeType LastEdgeType
	{
		get
		{
			return m_LastEdgeType;
		}
	}

	public Vector3 LastEdgePoint
	{
		get
		{
			return m_LastEdgePoint;
		}
	}

	public int LastEdgeTriangleIndex
	{
		get
		{
			return m_LastEdgeTriangleIndex;
		}
	}

	public int LastEdgeIndex
	{
		get
		{
			return m_LastEdgeIndex;
		}
	}

	/// <summary>
	/// 未初期化状態で生成します。
	/// </summary>
	public BallSurfaceSimulation()
	{
		Reset();
	}

	/// <summary>
	/// Surface Physicsのパラメータを設定します。
	/// </summary>
	public void ConfigurePhysics(
		float gravityAcceleration,
		float bounceSpeedThreshold,
		float bounceNormalRetention,
		float bounceTangentialRetention,
		float minBounceNormalSpeed,
		float maxBounceNormalSpeed,
		float groundedDeceleration,
		float groundedStopSpeedThreshold)
	{
		m_GravityAcceleration =
			Mathf.Max(
				0.0f,
				gravityAcceleration);

		m_BounceSpeedThreshold =
			Mathf.Max(
				0.0f,
				bounceSpeedThreshold);

		m_BounceNormalRetention =
			Mathf.Clamp01(
				bounceNormalRetention);

		m_BounceTangentialRetention =
			Mathf.Clamp01(
				bounceTangentialRetention);

		m_MinBounceNormalSpeed =
			Mathf.Max(
				0.0f,
				minBounceNormalSpeed);

		m_MaxBounceNormalSpeed =
			Mathf.Max(
				0.0f,
				maxBounceNormalSpeed);

		m_GroundedDeceleration =
			Mathf.Max(
				0.0f,
				groundedDeceleration);

		m_GroundedStopSpeedThreshold =
			Mathf.Max(
				0.0f,
				groundedStopSpeedThreshold);
	}

	/// <summary>
	/// Collisionの入射速度からSurface Physicsを開始します。
	/// </summary>
	public bool TryInitialize(
		BallSurfaceSource surfaceSource,
		int triangleIndex,
		Vector3 surfacePoint,
		Vector3 incomingVelocity)
	{
		Reset();

		if (surfaceSource == null ||
			!surfaceSource.IsReady)
		{
			return false;
		}

		BallSurfaceMeshData meshData =
			surfaceSource.MeshData;

		if (meshData == null ||
			triangleIndex < 0 ||
			triangleIndex >=
				meshData.TriangleCount)
		{
			return false;
		}

		m_SurfaceSource =
			surfaceSource;

		m_MeshData =
			meshData;

		m_CurrentTriangleIndex =
			triangleIndex;

		if (!TryGetTriangleWorldData(
			triangleIndex,
			out Vector3 vertexA,
			out Vector3 vertexB,
			out Vector3 vertexC,
			out Vector3 surfaceNormal))
		{
			Reset();

			return false;
		}

		Vector3 projectedPoint =
			ProjectPointToPlane(
				surfacePoint,
				vertexA,
				surfaceNormal);

		if (!TryCalculateBarycentric(
			projectedPoint,
			vertexA,
			vertexB,
			vertexC,
			out Vector3 barycentric))
		{
			Reset();

			return false;
		}

		if (!IsInsideTriangle(
			barycentric))
		{
			Reset();

			return false;
		}

		m_SurfacePoint =
			projectedPoint;

		// 入射速度をSurface方向と法線方向へ分離します。
		m_SurfaceVelocity =
			Vector3.ProjectOnPlane(
				incomingVelocity,
				surfaceNormal);

		float incomingNormalSpeed =
			Vector3.Dot(
				incomingVelocity,
				surfaceNormal);

		float impactSpeed =
			Mathf.Max(
				0.0f,
				-incomingNormalSpeed);

		m_Height =
			0.0f;

		m_VerticalVelocity =
			0.0f;

		m_IsInitialized =
			true;

		// 初回接触も通常のSurface Bounceとして処理します。
		HandleSurfaceImpact(
			impactSpeed);

		return true;
	}

	/// <summary>
	/// Surface Physicsを1Physics Step進めます。
	/// </summary>
	public EAdvanceResult Advance(
		float deltaTime)
	{
		ClearLastEdge();

		if (!m_IsInitialized ||
			m_SurfaceSource == null ||
			m_MeshData == null)
		{
			return EAdvanceResult.NotInitialized;
		}

		if (deltaTime <=
			0.0f)
		{
			return EAdvanceResult.Moved;
		}

		// 高さ方向を更新します。
		AdvanceVerticalMotion(
			deltaTime);

		// 完全に接地した場合だけSurface摩擦を適用します。
		if (IsGrounded)
		{
			ApplyGroundedDeceleration(
				deltaTime);
		}

		// -----------------------------------------------------------------
		// Surface方向移動
		// -----------------------------------------------------------------

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

			Vector3 targetPoint =
				m_SurfacePoint +
				m_SurfaceVelocity *
					remainingTime;

			if (!TryCalculateBarycentric(
				m_SurfacePoint,
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
				m_SurfacePoint =
					ProjectPointToPlane(
						targetPoint,
						vertexA,
						currentNormal);

				return EAdvanceResult.Moved;
			}

			if (!TryFindFirstCrossedEdge(
				startBarycentric,
				endBarycentric,
				ignoredEntryEdgeIndex,
				out int crossedEdgeIndex,
				out float crossingFraction))
			{
				return EAdvanceResult.InvalidGeometry;
			}

			Vector3 crossingPoint =
				Vector3.LerpUnclamped(
					m_SurfacePoint,
					targetPoint,
					crossingFraction);

			crossingPoint =
				ProjectPointToPlane(
					crossingPoint,
					vertexA,
					currentNormal);

			BallSurfaceMeshData.EEdgeType edgeType =
				m_MeshData.GetEdgeType(
					m_CurrentTriangleIndex,
					crossedEdgeIndex);

			SetLastEdge(
				m_CurrentTriangleIndex,
				crossedEdgeIndex,
				crossingPoint,
				edgeType);

			m_SurfacePoint =
				crossingPoint;

			switch (edgeType)
			{
				case BallSurfaceMeshData.EEdgeType.Boundary:
					return EAdvanceResult.BoundaryEdgeReached;

				case BallSurfaceMeshData.EEdgeType.Concave:
					return EAdvanceResult.ConcaveEdgeReached;

				case BallSurfaceMeshData.EEdgeType.Invalid:
				case BallSurfaceMeshData.EEdgeType.NonManifold:
					return EAdvanceResult.UnsupportedEdgeReached;

				case BallSurfaceMeshData.EEdgeType.Flat:
				case BallSurfaceMeshData.EEdgeType.Smooth:
				case BallSurfaceMeshData.EEdgeType.Convex:
					break;

				default:
					return EAdvanceResult.UnsupportedEdgeReached;
			}

			if (!m_MeshData.TryGetNeighbor(
				m_CurrentTriangleIndex,
				crossedEdgeIndex,
				out int neighborTriangleIndex,
				out int neighborEdgeIndex))
			{
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

			float surfaceSpeed =
				m_SurfaceVelocity.magnitude;

			Quaternion transitionRotation =
				Quaternion.FromToRotation(
					currentNormal,
					neighborNormal);

			Vector3 nextVelocity =
				transitionRotation *
					m_SurfaceVelocity;

			nextVelocity =
				Vector3.ProjectOnPlane(
					nextVelocity,
					neighborNormal);

			if (nextVelocity.sqrMagnitude <=
				m_MinimumCalculationValue *
					m_MinimumCalculationValue)
			{
				return EAdvanceResult.InvalidGeometry;
			}

			nextVelocity =
				nextVelocity.normalized *
					surfaceSpeed;

			m_CurrentTriangleIndex =
				neighborTriangleIndex;

			m_SurfaceVelocity =
				nextVelocity;

			m_SurfacePoint =
				ProjectPointToPlane(
					crossingPoint,
					neighborVertexA,
					neighborNormal);

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

	/// <summary>
	/// Surface法線方向の物理を更新します。
	/// </summary>
	private void AdvanceVerticalMotion(
		float deltaTime)
	{
		// 完全に接地して停止している場合は、
		// 毎Frame重力によってSurfaceへ押し込む必要はありません。
		if (m_Height <=
				m_MinimumHeight &&
			m_VerticalVelocity <=
				0.0f)
		{
			m_Height =
				0.0f;

			m_VerticalVelocity =
				0.0f;

			return;
		}

		float previousVerticalVelocity =
			m_VerticalVelocity;

		float nextVerticalVelocity =
			previousVerticalVelocity -
			m_GravityAcceleration *
				deltaTime;

		// 等加速度運動として、
		// Step内の平均速度を使用してHeightを更新します。
		float averageVerticalVelocity =
			(
				previousVerticalVelocity +
				nextVerticalVelocity
			) *
			0.5f;

		float nextHeight =
			m_Height +
			averageVerticalVelocity *
				deltaTime;

		if (nextHeight >
			m_MinimumHeight)
		{
			m_Height =
				nextHeight;

			m_VerticalVelocity =
				nextVerticalVelocity;

			return;
		}

		// Surfaceへ到達しました。
		m_Height =
			0.0f;

		float impactSpeed =
			Mathf.Max(
				0.0f,
				-nextVerticalVelocity);

		m_VerticalVelocity =
			0.0f;

		HandleSurfaceImpact(
			impactSpeed);
	}

	/// <summary>
	/// Surfaceへ衝突した際のバウンドを処理します。
	/// </summary>
	private void HandleSurfaceImpact(
		float impactSpeed)
	{
		// Surfaceへ触れた際には接線方向にも
		// エネルギー損失を適用します。
		m_SurfaceVelocity *=
			m_BounceTangentialRetention;

		// 十分な衝突速度が残っていなければ
		// 再バウンドせず接地します。
		if (impactSpeed <
			m_BounceSpeedThreshold)
		{
			m_VerticalVelocity =
				0.0f;

			return;
		}

		float bounceSpeed =
			impactSpeed *
			m_BounceNormalRetention;

		if (m_MinBounceNormalSpeed >
			0.0f)
		{
			bounceSpeed =
				Mathf.Max(
					bounceSpeed,
					m_MinBounceNormalSpeed);
		}

		if (m_MaxBounceNormalSpeed >
			0.0f)
		{
			bounceSpeed =
				Mathf.Min(
					bounceSpeed,
					m_MaxBounceNormalSpeed);
		}

		m_VerticalVelocity =
			bounceSpeed;
	}

	/// <summary>
	/// 接地中のSurface方向速度を減速します。
	/// </summary>
	private void ApplyGroundedDeceleration(
		float deltaTime)
	{
		float currentSpeed =
			m_SurfaceVelocity.magnitude;

		if (currentSpeed <=
			m_GroundedStopSpeedThreshold)
		{
			m_SurfaceVelocity =
				Vector3.zero;

			return;
		}

		if (m_GroundedDeceleration <=
			0.0f)
		{
			return;
		}

		float nextSpeed =
			Mathf.MoveTowards(
				currentSpeed,
				0.0f,
				m_GroundedDeceleration *
					deltaTime);

		if (nextSpeed <=
			m_GroundedStopSpeedThreshold)
		{
			m_SurfaceVelocity =
				Vector3.zero;

			return;
		}

		m_SurfaceVelocity =
			m_SurfaceVelocity.normalized *
				nextSpeed;
	}

	/// <summary>
	/// 現在Triangleのワールド法線を取得します。
	/// </summary>
	public bool TryGetCurrentSurfaceNormal(
		out Vector3 surfaceNormal)
	{
		surfaceNormal =
			Vector3.zero;

		if (!m_IsInitialized)
		{
			return false;
		}

		return TryGetTriangleWorldData(
			m_CurrentTriangleIndex,
			out _,
			out _,
			out _,
			out surfaceNormal);
	}

	/// <summary>
	/// Surface Simulationを初期状態へ戻します。
	/// </summary>
	public void Reset()
	{
		m_SurfaceSource =
			null;

		m_MeshData =
			null;

		m_CurrentTriangleIndex =
			-1;

		m_SurfacePoint =
			Vector3.zero;

		m_SurfaceVelocity =
			Vector3.zero;

		m_Height =
			0.0f;

		m_VerticalVelocity =
			0.0f;

		m_IsInitialized =
			false;

		ClearLastEdge();
	}

	/// <summary>
	/// TriangleのWorld座標とWorld法線を取得します。
	/// </summary>
	private bool TryGetTriangleWorldData(
		int triangleIndex,
		out Vector3 vertexA,
		out Vector3 vertexB,
		out Vector3 vertexC,
		out Vector3 surfaceNormal)
	{
		vertexA =
			Vector3.zero;

		vertexB =
			Vector3.zero;

		vertexC =
			Vector3.zero;

		surfaceNormal =
			Vector3.zero;

		if (m_SurfaceSource == null ||
			m_MeshData == null ||
			triangleIndex < 0 ||
			triangleIndex >=
				m_MeshData.TriangleCount)
		{
			return false;
		}

		m_MeshData.GetTriangleLocalVertices(
			triangleIndex,
			out Vector3 localVertexA,
			out Vector3 localVertexB,
			out Vector3 localVertexC);

		Transform surfaceTransform =
			m_SurfaceSource.transform;

		vertexA =
			surfaceTransform.TransformPoint(
				localVertexA);

		vertexB =
			surfaceTransform.TransformPoint(
				localVertexB);

		vertexC =
			surfaceTransform.TransformPoint(
				localVertexC);

		Vector3 edgeAB =
			vertexB -
			vertexA;

		Vector3 edgeAC =
			vertexC -
			vertexA;

		Vector3 normal =
			Vector3.Cross(
				edgeAB,
				edgeAC);

		if (normal.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return false;
		}

		surfaceNormal =
			normal.normalized;

		return true;
	}

	/// <summary>
	/// PointをPlane上へ投影します。
	/// </summary>
	private Vector3 ProjectPointToPlane(
		Vector3 point,
		Vector3 planePoint,
		Vector3 planeNormal)
	{
		float distance =
			Vector3.Dot(
				point -
					planePoint,
				planeNormal);

		return point -
			planeNormal *
				distance;
	}

	/// <summary>
	/// World座標をTriangleのBarycentric座標へ変換します。
	/// </summary>
	private bool TryCalculateBarycentric(
		Vector3 point,
		Vector3 vertexA,
		Vector3 vertexB,
		Vector3 vertexC,
		out Vector3 barycentric)
	{
		barycentric =
			Vector3.zero;

		Vector3 vector0 =
			vertexB -
			vertexA;

		Vector3 vector1 =
			vertexC -
			vertexA;

		Vector3 vector2 =
			point -
			vertexA;

		float dot00 =
			Vector3.Dot(
				vector0,
				vector0);

		float dot01 =
			Vector3.Dot(
				vector0,
				vector1);

		float dot11 =
			Vector3.Dot(
				vector1,
				vector1);

		float dot20 =
			Vector3.Dot(
				vector2,
				vector0);

		float dot21 =
			Vector3.Dot(
				vector2,
				vector1);

		float denominator =
			dot00 *
				dot11 -
			dot01 *
				dot01;

		if (Mathf.Abs(
			denominator) <=
			m_MinimumCalculationValue)
		{
			return false;
		}

		float inverseDenominator =
			1.0f /
			denominator;

		float coordinateB =
			(
				dot11 *
					dot20 -
				dot01 *
					dot21
			) *
			inverseDenominator;

		float coordinateC =
			(
				dot00 *
					dot21 -
				dot01 *
					dot20
			) *
			inverseDenominator;

		float coordinateA =
			1.0f -
			coordinateB -
			coordinateC;

		barycentric =
			new Vector3(
				coordinateA,
				coordinateB,
				coordinateC);

		return true;
	}

	/// <summary>
	/// Barycentric座標がTriangle内にあるか確認します。
	/// </summary>
	private bool IsInsideTriangle(
		Vector3 barycentric)
	{
		return barycentric.x >=
				-m_BarycentricTolerance &&
			barycentric.y >=
				-m_BarycentricTolerance &&
			barycentric.z >=
				-m_BarycentricTolerance;
	}

	/// <summary>
	/// 移動中に最初に越えるEdgeを取得します。
	/// </summary>
	private bool TryFindFirstCrossedEdge(
		Vector3 startBarycentric,
		Vector3 endBarycentric,
		int ignoredEntryEdgeIndex,
		out int crossedEdgeIndex,
		out float crossingFraction)
	{
		crossedEdgeIndex =
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
				startCoordinate /
				denominator;

			fraction =
				Mathf.Clamp01(
					fraction);

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

			if (fraction >=
				crossingFraction)
			{
				continue;
			}

			crossingFraction =
				fraction;

			crossedEdgeIndex =
				edgeIndex;
		}

		return crossedEdgeIndex >=
			0;
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
