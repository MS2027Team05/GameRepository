using UnityEngine;

/// <summary>
/// BallPhysicsControllerの飛行・疑似重力・Surface探索処理を定義します。
/// </summary>
public partial class BallPhysicsController
{
	/// <summary>
	/// BallControllerからシュート通知を受け取り、
	/// 通常飛行状態へ移行します。
	/// </summary>
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

	/// <summary>
	/// BallControllerから強制解放通知を受け取り、
	/// AttachSurface探索状態へ移行します。
	/// </summary>
	private void HandleReleased()
	{
		if (!IsServer)
		{
			return;
		}

		m_SurfaceSimulation.Reset();

		BeginSurfaceSeeking();
	}

	/// <summary>
	/// 通常飛行中の速度減衰と
	/// AttachSurface探索開始判定を行います。
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
	/// 飛行中に低コストのSurface探索を実行し、
	/// Surfaceを発見した場合は疑似重力を開始します。
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

		if (!m_SurfaceSearch.TrySearchLowCost(
			m_Rigidbody.position,
			m_AttractSearchDistance,
			m_AttachSurfaceMask,
			out RaycastHit nearestHit))
		{
			return;
		}

		// BallSurfaceSourceを持つObjectでは、
		// 最初にHitしたTriangleを起点としてSurfaceを追跡します。
		//
		// Target Object自体はロックしたまま、
		// 重力対象のSurfaceだけをBall位置に合わせて更新します。
		BallSurfaceSource surfaceSource =
			nearestHit.collider.GetComponent<BallSurfaceSource>();

		bool usesTrackedBallSurface =
			surfaceSource != null &&
			surfaceSource.IsReady &&
			nearestHit.triangleIndex >=
				0 &&
			m_SurfaceGravity.SetTrackedBallSurface(
				surfaceSource,
				nearestHit.triangleIndex);

		if (!usesTrackedBallSurface)
		{
			// BallSurfaceSourceを持たない従来AttachSurfaceは、
			// 互換性のためFixedPoint方式を維持します。
			m_SurfaceGravity.SetFixedPoint(
				nearestHit.collider,
				nearestHit.point);
		}

		ChangeMotionState(
			EBallMotionState.AttractingSurface);
	}

	/// <summary>
	/// 現在設定されているSurfaceへ
	/// 疑似重力を適用します。
	/// </summary>
	/// <remarks>
	/// Flying中はTarget Object内で追跡している
	/// 現在の最近Surface方向へ疑似重力を加えます。
	///
	/// 一度SurfaceへバウンドしたLocalSurfaceでは
	/// SurfaceMoveDecelerationを使用し、
	/// 転がり続けないよう徐々に速度を失わせます。
	/// </remarks>
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

		Vector3 currentVelocity =
			m_Rigidbody.linearVelocity;

		float deceleration =
			m_SurfaceGravity.IsLocalSurfaceTarget
				? m_SurfaceMoveDeceleration
				: m_AttractDeceleration;

		Vector3 dampedVelocity =
			Vector3.MoveTowards(
				currentVelocity,
				Vector3.zero,
				deceleration *
					Time.fixedDeltaTime);

		// 現在追跡している最近Surface方向へ
		// 通常の疑似重力として加速度を加えます。
		Vector3 nextVelocity =
			dampedVelocity +
			gravityDirection.normalized *
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
	/// Surfaceまでの距離から
	/// 現在の疑似重力加速度を計算します。
	/// </summary>
	/// <param name="distance">
	/// Surfaceまでの距離です。
	/// </param>
	/// <returns>
	/// 現在適用する疑似重力加速度です。
	/// </returns>
	private float CalculateAttractionAcceleration(
		float distance)
	{
		float referenceDistance =
			Mathf.Max(
				m_AttractSearchDistance,
				m_MinimumCalculationDistance);

		// 0 = Surfaceから遠い
		// 1 = Surfaceに近い
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

	/// <summary>
	/// 現在の疑似重力を解除して
	/// 通常飛行状態へ戻します。
	/// </summary>
	private void ReleaseSurfaceAttraction()
	{
		Vector3 currentVelocity =
			m_Rigidbody.linearVelocity;

		// スイング後の方向を維持します。
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

	/// <summary>
	/// ボールを空中停止後の
	/// AttachSurface探索状態へ移行します。
	/// </summary>
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

	/// <summary>
	/// 空中停止後に高精度Surface探索を実行し、
	/// 発見したSurfaceへボールを移動させます。
	/// </summary>
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

	/// <summary>
	/// 停止後探索で発見したSurfaceへ向かって
	/// ボールを加速させます。
	/// </summary>
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

	/// <summary>
	/// ボールを現在位置で完全停止させ、
	/// AttachSurfaceへ吸着した状態へ移行します。
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

		m_PrePhysicsVelocity =
			Vector3.zero;

		m_SurfaceGravity.Clear();

		ChangeMotionState(
			EBallMotionState.StuckToSurface);
	}

	/// <summary>
	/// 指定ColliderがAttachSurfaceMaskに
	/// 含まれているか確認します。
	/// </summary>
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
}
