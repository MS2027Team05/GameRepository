using UnityEngine;

/// <summary>
/// BallPhysicsControllerの通常衝突・バウンド処理を定義します。
/// </summary>
public partial class BallPhysicsController
{
	/// <summary>
	/// Colliderへ接触した瞬間に、
	/// バウンドまたは吸着処理を行います。
	/// </summary>
	/// <param name="collision">
	/// Unityから渡される衝突情報です。
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

		if (m_MotionState ==
			EBallMotionState.Surface)
		{
			return;
		}

		if (collision.contactCount <= 0)
		{
			return;
		}

		ContactPoint contact =
			collision.GetContact(0);

		Vector3 incomingVelocity =
			GetIncomingVelocity(
				collision);

		// BallSurfaceSourceが存在する面では、
		// Rigidbody反射ではなくSurface Physicsへ移行します。
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
				$"{name}: BallSurfaceへ接触しましたがSurface Physicsの初期化に失敗したため、接触地点付近で停止します。 Surface={surfaceSource.name}",
				this);

			StopSafelyAtSurfaceContact(
				contact);

			return;
		}

		float collisionSpeed =
			incomingVelocity.magnitude;

		// BallSurfaceSourceを持たない既存AttachSurfaceは、
		// SuperBallUltraSmash側の従来処理を維持します。
		if (IsAttachSurface(
			collision.collider))
		{
			HandleAttachSurfaceCollision(
				collision.collider,
				contact,
				incomingVelocity,
				collisionSpeed);

			return;
		}

		HandleNormalCollision(
			contact,
			incomingVelocity);
	}

	/// <summary>
	/// AttachSurfaceとの接触が継続している間、
	/// 十分低速になったボールをSurfaceへ吸着させます。
	/// </summary>
	/// <remarks>
	/// OnCollisionEnterだけでは、
	/// 浅い角度で接触してそのまま転がり続けた場合に
	/// 低速になった瞬間を検出できません。
	///
	/// そのためCollisionStay中にも停止速度を確認します。
	/// </remarks>
	/// <param name="collision">
	/// 現在継続している衝突情報です。
	/// </param>
	private void OnCollisionStay(
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

		if (m_MotionState !=
			EBallMotionState.AttractingSurface)
		{
			return;
		}

		if (!m_SurfaceGravity.IsLocalSurfaceTarget)
		{
			return;
		}

		if (!IsAttachSurface(
			collision.collider))
		{
			return;
		}

		float currentSpeed =
			m_Rigidbody.linearVelocity.magnitude;

		if (currentSpeed >
			m_SurfaceStopSpeedThreshold)
		{
			return;
		}

		StickToSurface();
	}

	/// <summary>
	/// AttachSurfaceとの衝突を処理します。
	/// </summary>
	/// <remarks>
	/// 十分な速度がある場合はバウンドし、
	/// 接触したLocalSurfaceを新しい重力対象にします。
	///
	/// 低速の場合はバウンドせず、その場へ吸着します。
	/// </remarks>
	private void HandleAttachSurfaceCollision(
		Collider surfaceCollider,
		ContactPoint contact,
		Vector3 incomingVelocity,
		float collisionSpeed)
	{
		if (!m_Bounce.ShouldBounce(
			collisionSpeed,
			m_BounceSpeedThreshold))
		{
			StickToSurface();
			return;
		}

		Vector3 bounceVelocity =
			CalculateBounceVelocity(
				incomingVelocity,
				contact.normal);

		ApplyBounceVelocity(
			bounceVelocity);

		// ステージ全体が1つのColliderでも、
		// 接触地点と法線を基準にLocalSurfaceを設定します。
		m_SurfaceGravity.SetLocalSurface(
			surfaceCollider,
			contact.point,
			contact.normal);

		ChangeMotionState(
			EBallMotionState.AttractingSurface);
	}

	/// <summary>
	/// AttachSurface以外との衝突を処理します。
	/// </summary>
	/// <remarks>
	/// 反射だけを行い、
	/// 現在の疑似重力対象は変更しません。
	/// </remarks>
	private void HandleNormalCollision(
		ContactPoint contact,
		Vector3 incomingVelocity)
	{
		Vector3 bounceVelocity =
			CalculateBounceVelocity(
				incomingVelocity,
				contact.normal);

		ApplyBounceVelocity(
			bounceVelocity);

		if (m_MotionState ==
			EBallMotionState.SeekingSurface)
		{
			ChangeMotionState(
				EBallMotionState.Flying);
		}
	}

	/// <summary>
	/// 現在のInspector設定を使用して、
	/// バウンド後の速度を計算します。
	/// </summary>
	/// <param name="incomingVelocity">
	/// 衝突直前の速度です。
	/// </param>
	/// <param name="surfaceNormal">
	/// 接触面の法線です。
	/// </param>
	/// <returns>
	/// 計算されたバウンド後の速度です。
	/// </returns>
	private Vector3 CalculateBounceVelocity(
		Vector3 incomingVelocity,
		Vector3 surfaceNormal)
	{
		return m_Bounce.CalculateBounceVelocity(
			incomingVelocity,
			surfaceNormal,
			m_BounceNormalRetention,
			m_BounceTangentialRetention,
			m_MinBounceNormalSpeed,
			m_MaxBounceNormalSpeed,
			m_MaxBounceTangentialSpeed);
	}

	/// <summary>
	/// バウンド計算に使用する
	/// 衝突直前の速度を取得します。
	/// </summary>
	/// <remarks>
	/// Unityの衝突解決によって現在速度が変化する可能性があるため、
	/// 基本的には直前のFixedUpdateで保存した速度を使用します。
	/// </remarks>
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

	/// <summary>
	/// 計算済みのバウンド速度をRigidbodyへ反映します。
	/// </summary>
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
}
