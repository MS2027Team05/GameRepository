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

		// 空中飛行時は見つけたSurface上の一点を
		// 固定された重力源として使用します。
		m_SurfaceGravity.SetFixedPoint(
			nearestHit.collider,
			nearestHit.point);

		ChangeMotionState(
			EBallMotionState.AttractingSurface);
	}

	/// <summary>
	/// 現在設定されているSurfaceへ
	/// 疑似重力を適用します。
	/// </summary>
	/// <remarks>
	/// 空中飛行中のFixedPoint重力では
	/// AttractDecelerationを使用します。
	///
	/// 一度SurfaceへバウンドしたLocalSurface重力では
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

		// 空中のスイング状態と、
		// Surface接触後では別の減速値を使用します。
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

		// 慣性を残しながら、
		// 現在のSurface方向へ疑似重力を追加します。
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
