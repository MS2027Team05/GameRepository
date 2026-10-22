using Unity.Netcode;
using UnityEngine;

/// <summary>
/// ボールのFree FlightとSurface Physicsを管理します。
/// </summary>
/// <remarks>
/// BallSurfaceへ接触する前はRigidbody Physicsを使用し、
/// BallSurfaceへ実際に接触した後は
/// BallSurfaceSimulationへ移行します。
///
/// Surface Physicsでは、
///
/// SurfaceVelocity
///     Surface上を進む速度。
///
/// Height / VerticalVelocity
///     Surfaceから離れる高さ方向の運動。
///
/// として分離して計算します。
/// </remarks>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SphereCollider))]
[RequireComponent(typeof(BallController))]
public class BallPhysicsController : NetworkBehaviour
{
	private enum EBallMotionState
	{
		Idle,
		Flying,
		AttractingSurface,
		SeekingSurface,
		Surface
	}

	// =====================================================================
	// Free Flight
	// =====================================================================

	[Header("Free Flight")]

	[Tooltip(
		"シュート後の減速度です。")]
	[Min(0.0f)]
	[SerializeField] private float m_FlightDeceleration;

	[Tooltip(
		"この速度以下になった場合に停止として扱います。")]
	[Min(0.0f)]
	[SerializeField] private float m_StopSpeedThreshold;

	// =====================================================================
	// Free Flight Ground Search
	// =====================================================================

	[Header("Free Flight Ground Search")]

	[Tooltip(
		"接触前の疑似重力探索対象となるGround Layerです。")]
	[SerializeField] private LayerMask m_AttachSurfaceMask;

	[Tooltip(
		"この速度以下になるとGround探索を開始します。")]
	[Min(0.0f)]
	[SerializeField] private float m_AttractStartSpeed;

	[Tooltip(
		"飛行中のGround探索距離です。")]
	[Min(0.0f)]
	[SerializeField] private float m_AttractSearchDistance;

	[Tooltip(
		"疑似重力対象を維持する最大距離です。")]
	[Min(0.0f)]
	[SerializeField] private float m_AttractReleaseDistance;

	[Tooltip(
		"Ground探索間隔です。")]
	[Min(0.0f)]
	[SerializeField] private float m_AttractSearchInterval;

	[Tooltip(
		"疑似重力の最低加速度です。")]
	[Min(0.0f)]
	[SerializeField] private float m_AttractMinAcceleration;

	[Tooltip(
		"疑似重力の最大加速度です。")]
	[Min(0.0f)]
	[SerializeField] private float m_AttractMaxAcceleration;

	[Tooltip(
		"Surfaceへ近づいた際の加速度曲線です。")]
	[Min(0.01f)]
	[SerializeField] private float m_AttractAccelerationCurve;

	[Tooltip(
		"疑似重力中の速度減衰です。")]
	[Min(0.0f)]
	[SerializeField] private float m_AttractDeceleration;

	[Tooltip(
		"疑似重力中の最高速度です。0なら制限しません。")]
	[Min(0.0f)]
	[SerializeField] private float m_AttractMaxSpeed;

	// =====================================================================
	// Free Flight Collision
	// =====================================================================

	[Header("Free Flight Collision")]

	[Range(0.0f, 1.0f)]
	[SerializeField] private float m_CollisionNormalRetention;

	[Range(0.0f, 1.0f)]
	[SerializeField] private float m_CollisionTangentialRetention;

	[Min(0.0f)]
	[SerializeField] private float m_MinCollisionNormalSpeed;

	[Min(0.0f)]
	[SerializeField] private float m_MaxCollisionNormalSpeed;

	// =====================================================================
	// Surface Physics
	// =====================================================================

	[Header("Surface Physics")]

	[Tooltip(
		"Surface法線とは逆方向へ作用する重力加速度です。")]
	[Min(0.0f)]
	[SerializeField] private float m_SurfaceGravityAcceleration;

	[Tooltip(
		"この法線方向衝突速度未満では再バウンドしません。")]
	[Min(0.0f)]
	[SerializeField] private float m_SurfaceBounceSpeedThreshold;

	[Tooltip(
		"バウンド後に残す法線方向速度の割合です。")]
	[Range(0.0f, 1.0f)]
	[SerializeField] private float m_SurfaceBounceNormalRetention;

	[Tooltip(
		"バウンド時に残すSurface方向速度の割合です。")]
	[Range(0.0f, 1.0f)]
	[SerializeField] private float m_SurfaceBounceTangentialRetention;

	[Tooltip(
		"バウンド時に最低限保証する法線方向速度です。")]
	[Min(0.0f)]
	[SerializeField] private float m_SurfaceMinBounceNormalSpeed;

	[Tooltip(
		"バウンド時の最大法線方向速度です。0なら制限しません。")]
	[Min(0.0f)]
	[SerializeField] private float m_SurfaceMaxBounceNormalSpeed;

	[Tooltip(
		"バウンド終了後、Surface上を移動しているボールの減速度です。")]
	[Min(0.0f)]
	[SerializeField] private float m_SurfaceGroundDeceleration;

	[Tooltip(
		"接地中、この速度以下になればSurface方向の移動を停止します。")]
	[Min(0.0f)]
	[SerializeField] private float m_SurfaceStopSpeedThreshold;

	// =====================================================================
	// Stopped Ground Search
	// =====================================================================

	[Header("Stopped Ground Search")]

	[Min(0.0f)]
	[SerializeField] private float m_SurfaceSearchRadius;

	[Min(0.0f)]
	[SerializeField] private float m_SurfaceSearchInterval;

	[Min(0.0f)]
	[SerializeField] private float m_SurfaceSeekSpeed;

	[Min(0.0f)]
	[SerializeField] private float m_SurfaceSeekAcceleration;

	private const float m_MinimumCalculationDistance = 0.001f;

	private Rigidbody m_Rigidbody;
	private SphereCollider m_BallCollider;
	private BallController m_BallController;

	private BallSurfaceGravity m_SurfaceGravity;
	private BallSurfaceSearch m_SurfaceSearch;
	private BallBounce m_Bounce;
	private BallSurfaceSimulation m_SurfaceSimulation;

	private EBallMotionState m_MotionState;

	private Vector3 m_FlightDirection;
	private Vector3 m_PrePhysicsVelocity;

	private float m_NextSurfaceSearchTime;
	private float m_NextAttractSearchTime;

	/// <summary>
	/// ComponentとSurface Physicsを初期化します。
	/// </summary>
	private void Awake()
	{
		m_Rigidbody =
			GetComponent<Rigidbody>();

		m_BallCollider =
			GetComponent<SphereCollider>();

		m_BallController =
			GetComponent<BallController>();

		m_SurfaceGravity =
			new BallSurfaceGravity();

		m_SurfaceSearch =
			new BallSurfaceSearch();

		m_Bounce =
			new BallBounce();

		m_SurfaceSimulation =
			new BallSurfaceSimulation();

		ConfigureSurfaceSimulation();

		m_Rigidbody.useGravity =
			false;
	}

	/// <summary>
	/// InspectorのSurface Physics設定を
	/// Simulationへ反映します。
	/// </summary>
	private void ConfigureSurfaceSimulation()
	{
		m_SurfaceSimulation.ConfigurePhysics(
			m_SurfaceGravityAcceleration,
			m_SurfaceBounceSpeedThreshold,
			m_SurfaceBounceNormalRetention,
			m_SurfaceBounceTangentialRetention,
			m_SurfaceMinBounceNormalSpeed,
			m_SurfaceMaxBounceNormalSpeed,
			m_SurfaceGroundDeceleration,
			m_SurfaceStopSpeedThreshold);
	}

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

			m_PrePhysicsVelocity =
				Vector3.zero;

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

			case EBallMotionState.Surface:
				UpdateSurfaceSimulation();
				break;

			case EBallMotionState.Idle:
				break;
		}

		if (m_MotionState ==
			EBallMotionState.Surface)
		{
			m_PrePhysicsVelocity =
				Vector3.zero;

			return;
		}

		m_PrePhysicsVelocity =
			m_Rigidbody.linearVelocity;
	}

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

		if (m_MotionState ==
			EBallMotionState.Surface)
		{
			return;
		}

		if (collision.contactCount <=
			0)
		{
			return;
		}

		ContactPoint contact =
			collision.GetContact(0);

		Vector3 incomingVelocity =
			GetIncomingVelocity(
				collision);

		BallSurfaceSource surfaceSource =
			collision.collider.GetComponent<BallSurfaceSource>();

		if (surfaceSource != null)
		{
			if (TryEnterSurfaceSimulation(
				surfaceSource,
				contact,
				incomingVelocity))
			{
				return;
			}

			Debug.LogWarning(
				$"{name}: BallSurfaceへ接触しましたがSurface Physicsの初期化に失敗しました。 Surface={surfaceSource.name}",
				this);
		}

		HandleNormalCollision(
			contact,
			incomingVelocity);
	}

	/// <summary>
	/// BallSurfaceへロックしてSurface Physicsを開始します。
	/// </summary>
	private bool TryEnterSurfaceSimulation(
		BallSurfaceSource surfaceSource,
		ContactPoint contact,
		Vector3 incomingVelocity)
	{
		if (surfaceSource == null ||
			!surfaceSource.IsReady)
		{
			return false;
		}

		if (!surfaceSource.TryGetSurfaceHit(
			contact.point,
			contact.normal,
			out RaycastHit surfaceHit))
		{
			return false;
		}

		if (surfaceHit.triangleIndex <
			0)
		{
			return false;
		}

		// Play中にInspectorを調整した場合にも
		// 最新値を反映できるよう、Surface開始時に再設定します。
		ConfigureSurfaceSimulation();

		if (!m_SurfaceSimulation.TryInitialize(
			surfaceSource,
			surfaceHit.triangleIndex,
			surfaceHit.point,
			incomingVelocity))
		{
			return false;
		}

		// Dynamic Rigidbodyの間に速度を停止します。
		m_Rigidbody.linearVelocity =
			Vector3.zero;

		m_Rigidbody.angularVelocity =
			Vector3.zero;

		m_Rigidbody.isKinematic =
			true;

		m_FlightDirection =
			Vector3.zero;

		m_PrePhysicsVelocity =
			Vector3.zero;

		m_SurfaceGravity.Clear();

		ChangeMotionState(
			EBallMotionState.Surface);

		ApplySurfacePosition(
			true);

		return true;
	}

	private void UpdateSurfaceSimulation()
	{
		if (!m_SurfaceSimulation.IsInitialized)
		{
			BeginSurfaceSeeking();

			return;
		}

		BallSurfaceSimulation.EAdvanceResult result =
			m_SurfaceSimulation.Advance(
				Time.fixedDeltaTime);

		switch (result)
		{
			case BallSurfaceSimulation.EAdvanceResult.Moved:
				ApplySurfacePosition(
					false);
				break;

			case BallSurfaceSimulation.EAdvanceResult.ConcaveEdgeReached:
			case BallSurfaceSimulation.EAdvanceResult.BoundaryEdgeReached:
			case BallSurfaceSimulation.EAdvanceResult.UnsupportedEdgeReached:

				ApplySurfacePosition(
					false);
				break;

			case BallSurfaceSimulation.EAdvanceResult.NotInitialized:
				BeginSurfaceSeeking();
				break;

			case BallSurfaceSimulation.EAdvanceResult.InvalidGeometry:
			case BallSurfaceSimulation.EAdvanceResult.TransitionLimitReached:

				Debug.LogWarning(
					$"{name}: Surface Physicsを継続できませんでした。 Result={result}",
					this);

				BeginSurfaceSeeking();
				break;
		}
	}

	/// <summary>
	/// Surface Physicsの座標をWorld座標へ戻します。
	/// </summary>
	private void ApplySurfacePosition(
		bool immediate)
	{
		if (!m_SurfaceSimulation.TryGetCurrentSurfaceNormal(
			out Vector3 surfaceNormal))
		{
			return;
		}

		float ballRadius =
			GetBallWorldRadius();

		Vector3 targetColliderCenter =
			m_SurfaceSimulation.SurfacePoint +
			surfaceNormal *
			(
				ballRadius +
				m_SurfaceSimulation.Height
			);

		Vector3 colliderCenterOffset =
			transform.TransformVector(
				m_BallCollider.center);

		Vector3 targetRigidbodyPosition =
			targetColliderCenter -
			colliderCenterOffset;

		if (immediate)
		{
			m_Rigidbody.position =
				targetRigidbodyPosition;
		}
		else
		{
			m_Rigidbody.MovePosition(
				targetRigidbodyPosition);
		}
	}

	private float GetBallWorldRadius()
	{
		Vector3 lossyScale =
			m_BallCollider.transform.lossyScale;

		float maximumScale =
			Mathf.Max(
				Mathf.Abs(
					lossyScale.x),
				Mathf.Abs(
					lossyScale.y),
				Mathf.Abs(
					lossyScale.z));

		return m_BallCollider.radius *
			maximumScale;
	}

	private void HandleNormalCollision(
		ContactPoint contact,
		Vector3 incomingVelocity)
	{
		Vector3 bounceVelocity =
			m_Bounce.CalculateBounceVelocity(
				incomingVelocity,
				contact.normal,
				m_CollisionNormalRetention,
				m_CollisionTangentialRetention,
				m_MinCollisionNormalSpeed,
				m_MaxCollisionNormalSpeed);

		ApplyBounceVelocity(
			bounceVelocity);

		if (m_MotionState ==
			EBallMotionState.SeekingSurface)
		{
			ChangeMotionState(
				EBallMotionState.Flying);
		}
	}

	private Vector3 GetIncomingVelocity(
		Collision collision)
	{
		if (m_PrePhysicsVelocity.sqrMagnitude >
			Mathf.Epsilon)
		{
			return m_PrePhysicsVelocity;
		}

		Vector3 currentVelocity =
			m_Rigidbody.linearVelocity;

		if (currentVelocity.sqrMagnitude >
			Mathf.Epsilon)
		{
			return currentVelocity;
		}

		float relativeSpeed =
			collision.relativeVelocity.magnitude;

		if (relativeSpeed <=
			Mathf.Epsilon)
		{
			return Vector3.zero;
		}

		ContactPoint contact =
			collision.GetContact(0);

		return -contact.normal *
			relativeSpeed;
	}

	private void ApplyBounceVelocity(
		Vector3 bounceVelocity)
	{
		m_SurfaceSimulation.Reset();

		m_Rigidbody.isKinematic =
			false;

		m_Rigidbody.linearVelocity =
			bounceVelocity;

		m_PrePhysicsVelocity =
			bounceVelocity;

		if (bounceVelocity.sqrMagnitude >
			Mathf.Epsilon)
		{
			m_FlightDirection =
				bounceVelocity.normalized;
		}
		else
		{
			m_FlightDirection =
				Vector3.zero;
		}
	}

	private void HandleShot()
	{
		if (!IsServer)
		{
			return;
		}

		m_SurfaceSimulation.Reset();

		m_Rigidbody.isKinematic =
			false;

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

		m_PrePhysicsVelocity =
			currentVelocity;

		m_NextAttractSearchTime =
			Time.time;

		m_SurfaceGravity.Clear();

		ChangeMotionState(
			EBallMotionState.Flying);
	}

	private void HandleReleased()
	{
		if (!IsServer)
		{
			return;
		}

		m_SurfaceSimulation.Reset();

		BeginSurfaceSeeking();
	}

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

		if (!m_SurfaceSearch.TrySearchLowCost(
			m_Rigidbody.position,
			m_AttractSearchDistance,
			m_AttachSurfaceMask,
			out RaycastHit nearestHit))
		{
			return;
		}

		m_SurfaceGravity.SetFixedPoint(
			nearestHit.collider,
			nearestHit.point);

		ChangeMotionState(
			EBallMotionState.AttractingSurface);
	}

	private void UpdateSurfaceAttraction()
	{
		if (!m_SurfaceGravity.IsTargetValid())
		{
			ReleaseSurfaceAttraction();

			return;
		}

		float surfaceTrackDistance =
			Mathf.Max(
				m_AttractSearchDistance,
				m_AttractReleaseDistance);

		if (!m_SurfaceGravity.TryGetGravityData(
			m_Rigidbody.position,
			surfaceTrackDistance,
			out Vector3 gravityDirection,
			out float distance))
		{
			ReleaseSurfaceAttraction();

			return;
		}

		if (m_AttractReleaseDistance >
				0.0f &&
			distance >
				m_AttractReleaseDistance)
		{
			ReleaseSurfaceAttraction();

			return;
		}

		if (gravityDirection.sqrMagnitude <=
			Mathf.Epsilon)
		{
			return;
		}

		float attractionAcceleration =
			CalculateAttractionAcceleration(
				distance);

		Vector3 dampedVelocity =
			Vector3.MoveTowards(
				m_Rigidbody.linearVelocity,
				Vector3.zero,
				m_AttractDeceleration *
					Time.fixedDeltaTime);

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

	private float CalculateAttractionAcceleration(
		float distance)
	{
		float referenceDistance =
			Mathf.Max(
				m_AttractSearchDistance,
				m_MinimumCalculationDistance);

		float proximity =
			1.0f -
			Mathf.Clamp01(
				distance /
					referenceDistance);

		float curvePower =
			Mathf.Max(
				m_AttractAccelerationCurve,
				0.01f);

		float curvedProximity =
			Mathf.Pow(
				proximity,
				curvePower);

		return Mathf.Lerp(
			m_AttractMinAcceleration,
			m_AttractMaxAcceleration,
			curvedProximity);
	}

	private void ReleaseSurfaceAttraction()
	{
		Vector3 currentVelocity =
			m_Rigidbody.linearVelocity;

		if (currentVelocity.sqrMagnitude >
			Mathf.Epsilon)
		{
			m_FlightDirection =
				currentVelocity.normalized;
		}

		m_SurfaceGravity.Clear();

		m_NextAttractSearchTime =
			Time.time +
				m_AttractSearchInterval;

		ChangeMotionState(
			EBallMotionState.Flying);
	}

	private void BeginSurfaceSeeking()
	{
		m_SurfaceSimulation.Reset();

		m_Rigidbody.isKinematic =
			false;

		m_Rigidbody.linearVelocity =
			Vector3.zero;

		m_Rigidbody.angularVelocity =
			Vector3.zero;

		m_FlightDirection =
			Vector3.zero;

		m_PrePhysicsVelocity =
			Vector3.zero;

		m_SurfaceGravity.Clear();

		m_NextSurfaceSearchTime =
			Time.time;

		ChangeMotionState(
			EBallMotionState.SeekingSurface);
	}

	private void UpdateSurfaceSeeking()
	{
		if (!m_SurfaceGravity.IsTargetValid())
		{
			m_SurfaceGravity.Clear();

			if (Time.time <
				m_NextSurfaceSearchTime)
			{
				return;
			}

			m_NextSurfaceSearchTime =
				Time.time +
					m_SurfaceSearchInterval;

			if (!m_SurfaceSearch.TrySearchHighPrecision(
				m_Rigidbody.position,
				m_SurfaceSearchRadius,
				m_AttachSurfaceMask,
				out RaycastHit nearestHit))
			{
				m_Rigidbody.linearVelocity =
					Vector3.zero;

				return;
			}

			m_SurfaceGravity.SetFixedPoint(
				nearestHit.collider,
				nearestHit.point);
		}

		MoveTowardTargetSurface();
	}

	private void MoveTowardTargetSurface()
	{
		float surfaceTrackDistance =
			Mathf.Max(
				m_SurfaceSearchRadius,
				m_AttractReleaseDistance);

		if (!m_SurfaceGravity.TryGetGravityData(
			m_Rigidbody.position,
			surfaceTrackDistance,
			out Vector3 surfaceDirection,
			out float distance))
		{
			return;
		}

		if (distance <=
			m_MinimumCalculationDistance)
		{
			m_Rigidbody.linearVelocity =
				Vector3.zero;

			m_SurfaceGravity.Clear();

			m_NextSurfaceSearchTime =
				Time.time +
					m_SurfaceSearchInterval;

			return;
		}

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

	private void ResetInternalState()
	{
		ChangeMotionState(
			EBallMotionState.Idle);

		m_FlightDirection =
			Vector3.zero;

		m_PrePhysicsVelocity =
			Vector3.zero;

		m_SurfaceGravity.Clear();

		m_SurfaceSimulation.Reset();
	}

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

		m_PrePhysicsVelocity =
			Vector3.zero;

		m_SurfaceGravity.Clear();

		m_SurfaceSimulation.Reset();

		m_NextSurfaceSearchTime =
			0.0f;

		m_NextAttractSearchTime =
			0.0f;

		ChangeMotionState(
			EBallMotionState.Idle);
	}
}
