using Unity.Netcode;
using UnityEngine;

/// <summary>
/// フリー状態のボールの移動とGroundへの吸着を管理します。
/// </summary>
/// <remarks>
/// 主な処理の流れ:
/// 1. シュート後は射出方向を維持しながら減速します。
/// 2. 一定速度以下になると、一定間隔で周囲のGroundを探索します。
/// 3. Groundが見つかった場合、その表面上の一点を重力源として固定します。
/// 4. Groundへ近づくほど疑似重力を強くし、
///    スイングバイしながら最後に強く吸着する動きを作ります。
/// 5. 重力源から一定距離以上離れた場合は疑似重力を解除します。
/// 6. Groundへ実際に衝突すると、その場で固定されます。
/// 7. 空中で停止した場合は64方向探索で近くのGroundを探します。
/// 8. 物理状態の確定はHost側だけで行います。
/// </remarks>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(BallController))]
public class BallPhysicsController : NetworkBehaviour
{
	private enum EBallMotionState
	{
		Idle,
		Flying,
		AttractingSurface,
		SeekingSurface,
		StuckToSurface
	}

	[Header("シュート後の移動")]

	[Tooltip(
		"大きいほどシュート後に早く減速します。")]
	[Min(0.0f)]
	// 推奨値: 12.0
	[SerializeField] private float m_FlightDeceleration;

	[Tooltip(
		"大きいほど早い速度で停止扱いになります。")]
	[Min(0.0f)]
	// 推奨値: 0.5
	[SerializeField] private float m_StopSpeedThreshold;

	[Header("吸着可能な面")]

	[Tooltip(
		"ボールが吸着できるLayerです。")]
	[SerializeField] private LayerMask m_AttachSurfaceMask;

	[Header("飛行中の疑似重力")]

	[Tooltip(
		"大きいほど早い段階からGroundを探し始めます。")]
	[Min(0.0f)]
	// 推奨値: 9.0
	[SerializeField] private float m_AttractStartSpeed;

	[Tooltip(
		"大きいほど遠くのGroundを発見できます。")]
	[Min(0.0f)]
	// 推奨値: 18.0
	[SerializeField] private float m_AttractSearchDistance;

	[Tooltip(
		"大きいほど遠くまでGroundの引力を受け続けます。")]
	[Min(0.0f)]
	// 推奨値: 24.0
	[SerializeField] private float m_AttractReleaseDistance;

	[Tooltip(
		"大きいほどGroundを探す間隔が長くなります。")]
	[Min(0.0f)]
	// 推奨値: 0.2
	[SerializeField] private float m_AttractSearchInterval;

	[Tooltip(
		"大きいほど遠くにいる時から強く引っ張られます。")]
	[Min(0.0f)]
	// 推奨値: 4.0
	[SerializeField] private float m_AttractMinAcceleration;

	[Tooltip(
		"大きいほどGround直前で強く加速します。")]
	[Min(0.0f)]
	// 推奨値: 45.0
	[SerializeField] private float m_AttractMaxAcceleration;

	[Tooltip(
		"大きいほどGround直前で急激に加速します。")]
	[Min(0.01f)]
	// 推奨値: 2.0
	[SerializeField] private float m_AttractAccelerationCurve;

	[Tooltip(
		"大きいほど元の進行方向の勢いが早く失われます。")]
	[Min(0.0f)]
	// 推奨値: 0.0
	[SerializeField] private float m_AttractDeceleration;

	[Tooltip(
		"大きいほど吸引中の最高速度が上がります。0で制限しません。")]
	[Min(0.0f)]
	// 推奨値: 30.0
	[SerializeField] private float m_AttractMaxSpeed;

	[Header("停止後のGround探索")]

	[Tooltip(
		"大きいほど停止後に遠くのGroundを探します。")]
	[Min(0.0f)]
	// 推奨値: 100.0
	[SerializeField] private float m_SurfaceSearchRadius;

	[Tooltip(
		"大きいほど停止後の再探索間隔が長くなります。")]
	[Min(0.0f)]
	// 推奨値: 0.25
	[SerializeField] private float m_SurfaceSearchInterval;

	[Tooltip(
		"大きいほど停止後にGroundへ速く向かいます。")]
	[Min(0.0f)]
	// 推奨値: 4.0
	[SerializeField] private float m_SurfaceSeekSpeed;

	[Tooltip(
		"大きいほど停止後にGroundへ素早く加速します。")]
	[Min(0.0f)]
	// 推奨値: 10.0
	[SerializeField] private float m_SurfaceSeekAcceleration;

	private const int m_StoppedSearchRayCount = 64;
	private const float m_GoldenAngle = 2.39996323f;
	private const float m_MinimumCalculationDistance = 0.001f;

	private Rigidbody m_Rigidbody;
	private BallController m_BallController;

	private EBallMotionState m_MotionState;

	private Vector3 m_FlightDirection;

	private Collider m_TargetSurfaceCollider;
	private Vector3 m_TargetSurfacePoint;
	private bool m_HasTargetSurface;

	private float m_NextSurfaceSearchTime;
	private float m_NextAttractSearchTime;

	/// <summary>
	/// 必要なコンポーネントを取得し、
	/// 通常の重力を無効化します。
	/// </summary>
	private void Awake()
	{
		m_Rigidbody =
			GetComponent<Rigidbody>();

		m_BallController =
			GetComponent<BallController>();

		m_Rigidbody.useGravity =
			false;
	}

	/// <summary>
	/// ネットワークSpawn時にボール状態の通知を登録します。
	/// </summary>
	public override void OnNetworkSpawn()
	{
		if (m_BallController == null)
		{
			return;
		}

		m_BallController.OnShot +=
			HandleShot;

		m_BallController.OnReleased +=
			HandleReleased;
	}

	/// <summary>
	/// ネットワークDespawn時に通知を解除します。
	/// </summary>
	public override void OnNetworkDespawn()
	{
		if (m_BallController == null)
		{
			return;
		}

		m_BallController.OnShot -=
			HandleShot;

		m_BallController.OnReleased -=
			HandleReleased;
	}

	/// <summary>
	/// Host側で現在の状態に応じた物理挙動を更新します。
	/// </summary>
	private void FixedUpdate()
	{
		if (!IsServer)
		{
			return;
		}

		if (m_BallController == null)
		{
			return;
		}

		if (m_BallController.HasHolder)
		{
			if (m_MotionState !=
				EBallMotionState.Idle)
			{
				ResetInternalState();
			}

			return;
		}

		switch (m_MotionState)
		{
			case EBallMotionState.Flying:
				UpdateFlying();
				break;

			case EBallMotionState.AttractingSurface:
				UpdateSurfaceAttraction();
				break;

			case EBallMotionState.SeekingSurface:
				UpdateSurfaceSeeking();
				break;

			case EBallMotionState.Idle:
			case EBallMotionState.StuckToSurface:
				break;
		}
	}

	/// <summary>
	/// Groundへ衝突した場合、
	/// ボールをその場へ固定します。
	/// </summary>
	/// <param name="collision">
	/// 衝突情報です。
	/// </param>
	private void OnCollisionEnter(
		Collision collision)
	{
		if (!IsServer)
		{
			return;
		}

		if (m_BallController == null ||
			m_BallController.HasHolder)
		{
			return;
		}

		if (!IsAttachSurface(
			collision.collider))
		{
			return;
		}

		StickToSurface();
	}

	/// <summary>
	/// シュートされたボールを飛行状態へ移行します。
	/// </summary>
	private void HandleShot()
	{
		if (!IsServer)
		{
			return;
		}

		Vector3 currentVelocity =
			m_Rigidbody.linearVelocity;

		if (currentVelocity.sqrMagnitude <=
			Mathf.Epsilon)
		{
			BeginSurfaceSeeking();
			return;
		}

		m_FlightDirection =
			currentVelocity.normalized;

		m_NextAttractSearchTime =
			Time.time;

		ClearTargetSurface();

		ChangeMotionState(
			EBallMotionState.Flying);
	}

	/// <summary>
	/// ボールが所持者から解放された場合、
	/// Ground探索状態へ移行します。
	/// </summary>
	private void HandleReleased()
	{
		if (!IsServer)
		{
			return;
		}

		BeginSurfaceSeeking();
	}

	/// <summary>
	/// シュート方向を維持しながら減速します。
	/// 一定速度以下になるとGroundを探索します。
	/// </summary>
	private void UpdateFlying()
	{
		float currentSpeed =
			m_Rigidbody.linearVelocity.magnitude;

		if (currentSpeed <=
			m_StopSpeedThreshold)
		{
			BeginSurfaceSeeking();
			return;
		}

		if (currentSpeed <=
			m_AttractStartSpeed)
		{
			TryBeginSurfaceAttraction();

			if (m_MotionState ==
				EBallMotionState.AttractingSurface)
			{
				return;
			}
		}

		float nextSpeed =
			Mathf.MoveTowards(
				currentSpeed,
				0.0f,
				m_FlightDeceleration *
				Time.fixedDeltaTime);

		m_Rigidbody.linearVelocity =
			m_FlightDirection *
			nextSpeed;

		if (nextSpeed <=
			m_StopSpeedThreshold)
		{
			BeginSurfaceSeeking();
		}
	}

	/// <summary>
	/// 低速飛行中に26方向でGroundを探索し、
	/// 見つかった地点を重力源として固定します。
	/// </summary>
	private void TryBeginSurfaceAttraction()
	{
		if (Time.time <
			m_NextAttractSearchTime)
		{
			return;
		}

		m_NextAttractSearchTime =
			Time.time +
			m_AttractSearchInterval;

		if (!TrySearchNearestSurfaceLowCost(
			m_AttractSearchDistance))
		{
			return;
		}

		ChangeMotionState(
			EBallMotionState.AttractingSurface);
	}

	/// <summary>
	/// Ground上の固定地点へ疑似重力を加えます。
	/// Groundへ近づくほど加速度が強くなります。
	/// </summary>
	private void UpdateSurfaceAttraction()
	{
		if (!IsTargetSurfaceValid())
		{
			ReleaseSurfaceAttraction();
			return;
		}

		Vector3 toGravityPoint =
			m_TargetSurfacePoint -
			m_Rigidbody.position;

		float distance =
			toGravityPoint.magnitude;

		if (m_AttractReleaseDistance >
			0.0f &&
			distance >
			m_AttractReleaseDistance)
		{
			ReleaseSurfaceAttraction();
			return;
		}

		if (distance <=
			m_MinimumCalculationDistance)
		{
			return;
		}

		Vector3 gravityDirection =
			toGravityPoint /
			distance;

		float attractionAcceleration =
			CalculateAttractionAcceleration(
				distance);

		Vector3 currentVelocity =
			m_Rigidbody.linearVelocity;

		Vector3 dampedVelocity =
			Vector3.MoveTowards(
				currentVelocity,
				Vector3.zero,
				m_AttractDeceleration *
				Time.fixedDeltaTime);

		// 現在の慣性は残したまま、
		// Ground方向へ加速度だけを加えます。
		Vector3 nextVelocity =
			dampedVelocity +
			gravityDirection *
			attractionAcceleration *
			Time.fixedDeltaTime;

		if (m_AttractMaxSpeed >
			0.0f &&
			nextVelocity.magnitude >
			m_AttractMaxSpeed)
		{
			nextVelocity =
				nextVelocity.normalized *
				m_AttractMaxSpeed;
		}

		m_Rigidbody.linearVelocity =
			nextVelocity;
	}

	/// <summary>
	/// Groundまでの距離から疑似重力の強さを求めます。
	/// Groundへ近づくほどMinからMaxへ加速します。
	/// </summary>
	/// <param name="distance">
	/// 重力源までの距離です。
	/// </param>
	/// <returns>
	/// 現在距離で使用する加速度です。
	/// </returns>
	private float CalculateAttractionAcceleration(
		float distance)
	{
		float referenceDistance =
			Mathf.Max(
				m_AttractSearchDistance,
				m_MinimumCalculationDistance);

		// 0 = 遠い
		// 1 = Groundに近い
		float proximity =
			1.0f -
			Mathf.Clamp01(
				distance /
				referenceDistance);

		float curvePower =
			Mathf.Max(
				m_AttractAccelerationCurve,
				0.01f);

		// Curveを大きくすると、
		// Ground直前まで弱く、最後に一気に強くなります。
		float curvedProximity =
			Mathf.Pow(
				proximity,
				curvePower);

		return Mathf.Lerp(
			m_AttractMinAcceleration,
			m_AttractMaxAcceleration,
			curvedProximity);
	}

	/// <summary>
	/// 疑似重力を解除し、
	/// 現在の進行方向を維持した飛行へ戻します。
	/// </summary>
	private void ReleaseSurfaceAttraction()
	{
		Vector3 currentVelocity =
			m_Rigidbody.linearVelocity;

		// スイングバイ後の方向を維持するため、
		// 現在速度を新しい飛行方向として保存します。
		if (currentVelocity.sqrMagnitude >
			Mathf.Epsilon)
		{
			m_FlightDirection =
				currentVelocity.normalized;
		}

		ClearTargetSurface();

		m_NextAttractSearchTime =
			Time.time +
			m_AttractSearchInterval;

		ChangeMotionState(
			EBallMotionState.Flying);
	}

	/// <summary>
	/// 完全停止したボールをGround探索状態へ移行します。
	/// </summary>
	private void BeginSurfaceSeeking()
	{
		m_Rigidbody.isKinematic =
			false;

		m_Rigidbody.linearVelocity =
			Vector3.zero;

		m_Rigidbody.angularVelocity =
			Vector3.zero;

		m_FlightDirection =
			Vector3.zero;

		ClearTargetSurface();

		m_NextSurfaceSearchTime =
			Time.time;

		ChangeMotionState(
			EBallMotionState.SeekingSurface);
	}

	/// <summary>
	/// 停止後に64方向で周囲のGroundを探索し、
	/// 見つかった場合はその表面へ移動します。
	/// </summary>
	private void UpdateSurfaceSeeking()
	{
		if (!IsTargetSurfaceValid())
		{
			ClearTargetSurface();

			if (Time.time <
				m_NextSurfaceSearchTime)
			{
				return;
			}

			m_NextSurfaceSearchTime =
				Time.time +
				m_SurfaceSearchInterval;

			if (!TrySearchNearestSurfaceHighPrecision(
				m_SurfaceSearchRadius))
			{
				m_Rigidbody.linearVelocity =
					Vector3.zero;

				return;
			}
		}

		MoveTowardTargetSurface();
	}

	/// <summary>
	/// 飛行中用の軽量な26方向探索を行います。
	/// </summary>
	/// <param name="searchDistance">
	/// 探索距離です。
	/// </param>
	/// <returns>
	/// Groundを発見した場合はtrueです。
	/// </returns>
	private bool TrySearchNearestSurfaceLowCost(
		float searchDistance)
	{
		ClearTargetSurface();

		if (searchDistance <=
			0.0f)
		{
			return false;
		}

		float nearestDistance =
			float.MaxValue;

		RaycastHit nearestHit =
			default;

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
						m_Rigidbody.position,
						direction,
						out RaycastHit hit,
						searchDistance,
						m_AttachSurfaceMask,
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

		return ApplySurfaceSearchResult(
			hasHit,
			nearestHit);
	}

	/// <summary>
	/// 停止後用に球面へ均等な64方向のRaycastを行い、
	/// 最も近いGroundを探索します。
	/// </summary>
	/// <param name="searchDistance">
	/// 探索距離です。
	/// </param>
	/// <returns>
	/// Groundを発見した場合はtrueです。
	/// </returns>
	private bool TrySearchNearestSurfaceHighPrecision(
		float searchDistance)
	{
		ClearTargetSurface();

		if (searchDistance <=
			0.0f)
		{
			return false;
		}

		float nearestDistance =
			float.MaxValue;

		RaycastHit nearestHit =
			default;

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
				m_Rigidbody.position,
				direction,
				out RaycastHit hit,
				searchDistance,
				m_AttachSurfaceMask,
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

		return ApplySurfaceSearchResult(
			hasHit,
			nearestHit);
	}

	/// <summary>
	/// Fibonacci Sphereを使って、
	/// 球面上へ均等に配置された探索方向を求めます。
	/// </summary>
	/// <param name="index">
	/// 現在のRay番号です。
	/// </param>
	/// <param name="count">
	/// 全Ray数です。
	/// </param>
	/// <returns>
	/// 正規化済み探索方向です。
	/// </returns>
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

	/// <summary>
	/// Ground探索結果を現在の対象として保存します。
	/// </summary>
	/// <param name="hasHit">
	/// Groundを検出したかどうかです。
	/// </param>
	/// <param name="nearestHit">
	/// 最も近かったGroundです。
	/// </param>
	/// <returns>
	/// Groundを保存できた場合はtrueです。
	/// </returns>
	private bool ApplySurfaceSearchResult(
		bool hasHit,
		RaycastHit nearestHit)
	{
		if (!hasHit)
		{
			return false;
		}

		m_TargetSurfaceCollider =
			nearestHit.collider;

		m_TargetSurfacePoint =
			nearestHit.point;

		m_HasTargetSurface =
			true;

		return true;
	}

	/// <summary>
	/// 停止後に発見したGroundへ加速しながら移動します。
	/// </summary>
	private void MoveTowardTargetSurface()
	{
		if (!m_HasTargetSurface)
		{
			return;
		}

		Vector3 surfaceDirection =
			m_TargetSurfacePoint -
			m_Rigidbody.position;

		if (surfaceDirection.sqrMagnitude <=
			Mathf.Epsilon)
		{
			m_Rigidbody.linearVelocity =
				Vector3.zero;

			ClearTargetSurface();

			m_NextSurfaceSearchTime =
				Time.time +
				m_SurfaceSearchInterval;

			return;
		}

		surfaceDirection.Normalize();

		Vector3 targetVelocity =
			surfaceDirection *
			m_SurfaceSeekSpeed;

		if (m_SurfaceSeekAcceleration <=
			0.0f)
		{
			m_Rigidbody.linearVelocity =
				targetVelocity;

			return;
		}

		m_Rigidbody.linearVelocity =
			Vector3.MoveTowards(
				m_Rigidbody.linearVelocity,
				targetVelocity,
				m_SurfaceSeekAcceleration *
				Time.fixedDeltaTime);
	}

	/// <summary>
	/// ボールを現在位置で停止させ、
	/// Groundへ固定します。
	/// </summary>
	private void StickToSurface()
	{
		m_Rigidbody.linearVelocity =
			Vector3.zero;

		m_Rigidbody.angularVelocity =
			Vector3.zero;

		m_Rigidbody.isKinematic =
			true;

		m_FlightDirection =
			Vector3.zero;

		ClearTargetSurface();

		ChangeMotionState(
			EBallMotionState.StuckToSurface);
	}

	/// <summary>
	/// 現在保存しているGround対象を解除します。
	/// </summary>
	private void ClearTargetSurface()
	{
		m_TargetSurfaceCollider =
			null;

		m_TargetSurfacePoint =
			Vector3.zero;

		m_HasTargetSurface =
			false;
	}

	/// <summary>
	/// Colliderが吸着対象Layerに含まれているか確認します。
	/// </summary>
	/// <param name="targetCollider">
	/// 確認対象です。
	/// </param>
	/// <returns>
	/// 吸着可能な場合はtrueです。
	/// </returns>
	private bool IsAttachSurface(
		Collider targetCollider)
	{
		if (targetCollider == null)
		{
			return false;
		}

		int targetLayerMask =
			1 <<
			targetCollider.gameObject.layer;

		return (m_AttachSurfaceMask.value &
			targetLayerMask) != 0;
	}

	/// <summary>
	/// 保存しているGround対象が有効か確認します。
	/// </summary>
	/// <returns>
	/// 有効な場合はtrueです。
	/// </returns>
	private bool IsTargetSurfaceValid()
	{
		return m_HasTargetSurface &&
			m_TargetSurfaceCollider != null &&
			m_TargetSurfaceCollider.enabled &&
			m_TargetSurfaceCollider.gameObject.activeInHierarchy;
	}

	/// <summary>
	/// ボールの移動状態を変更します。
	/// </summary>
	/// <param name="newState">
	/// 変更後の状態です。
	/// </param>
	private void ChangeMotionState(
		EBallMotionState newState)
	{
		if (m_MotionState ==
			newState)
		{
			return;
		}

		m_MotionState =
			newState;
	}

	/// <summary>
	/// 所持開始時などに内部状態を初期化します。
	/// </summary>
	private void ResetInternalState()
	{
		ChangeMotionState(
			EBallMotionState.Idle);

		m_FlightDirection =
			Vector3.zero;

		ClearTargetSurface();
	}


	/// <summary>
	/// デバッグリスポーン時などに、
	/// ボールを通常のフリー状態へ戻します。
	/// </summary>
	public void ResetMotionState()
	{
		if (!IsServer)
		{
			return;
		}

		m_Rigidbody.useGravity =
			false;

		m_Rigidbody.isKinematic =
			false;

		m_Rigidbody.linearVelocity =
			Vector3.zero;

		m_Rigidbody.angularVelocity =
			Vector3.zero;

		m_FlightDirection =
			Vector3.zero;

		ClearTargetSurface();

		m_NextSurfaceSearchTime =
			0.0f;

		m_NextAttractSearchTime =
			0.0f;

		ChangeMotionState(
			EBallMotionState.Idle);
	}
}

