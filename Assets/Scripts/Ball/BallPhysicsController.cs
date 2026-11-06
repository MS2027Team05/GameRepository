using Unity.Netcode;
using UnityEngine;

/// <summary>
/// フリー状態のボールに関する物理挙動全体を管理します。
/// </summary>
/// <remarks>
/// このクラスはボール物理の司令塔として、
/// 状態遷移、Rigidbodyへの速度反映、衝突処理、
/// 各専門クラスへの処理委譲を担当します。
///
/// 主な処理の流れ:
/// 1. シュート後は射出方向を維持しながら減速します。
/// 2. 一定速度以下になるとAttachSurfaceを探索します。
/// 3. 発見したSurface上の一点へ疑似重力を掛けます。
/// 4. AttachSurfaceへ高速で接触した場合はバウンドします。
/// 5. バウンド後は接触したLocalSurfaceを地面として扱います。
/// 6. LocalSurface上では専用の減速を適用します。
/// 7. 接触状態で十分低速になった場合はSurfaceへ吸着します。
/// 8. AttachSurface以外への衝突は反射のみ行います。
/// 9. 物理状態の確定はHost側だけで行います。
///
/// 専門処理:
/// BallBounce
///     バウンド判定・反射速度計算を担当します。
///
/// BallSurfaceGravity
///     固定点重力・接触後のSurface重力を担当します。
///
/// BallSurfaceSearch
///     AttachSurfaceの周辺探索を担当します。
/// </remarks>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SphereCollider))]
[RequireComponent(typeof(BallController))]
public class BallPhysicsController : NetworkBehaviour
{
	/// <summary>
	/// ボールの現在の移動状態です。
	/// </summary>
	private enum EBallMotionState
	{
		/// <summary>
		/// 所持中やリスポーン直後など、
		/// 自由移動していない状態です。
		/// </summary>
		Idle,

		/// <summary>
		/// シュート後に通常飛行している状態です。
		/// </summary>
		Flying,

		/// <summary>
		/// AttachSurfaceから疑似重力を受けている状態です。
		/// </summary>
		AttractingSurface,

		/// <summary>
		/// 空中停止後にAttachSurfaceを探索し、
		/// Surfaceへ向かっている状態です。
		/// </summary>
		SeekingSurface,

		/// <summary>
		/// BallSurface上でSurface Physicsを実行している状態です。
		/// </summary>
		Surface,

		/// <summary>
		/// AttachSurfaceへ吸着して
		/// 完全停止している状態です。
		/// </summary>
		StuckToSurface
	}

	[Header("シュート後の移動")]

	[Tooltip(
		"大きいほどシュート後に早く減速します。")]
	[Min(0.0f)]
	[SerializeField] private float m_FlightDeceleration;

	[Tooltip(
		"大きいほど早い速度で停止扱いになります。")]
	[Min(0.0f)]
	[SerializeField] private float m_StopSpeedThreshold;

	[Header("吸着・バウンド可能な面")]

	[Tooltip(
		"疑似重力・バウンド・吸着の対象となるLayerです。複数選択できます。")]
	[SerializeField] private LayerMask m_AttachSurfaceMask;

	[Header("バウンド")]

	[Tooltip(
		"AttachSurfaceへ接触した時、この速度以上ならバウンドします。")]
	[Min(0.0f)]
	[SerializeField] private float m_BounceSpeedThreshold;

	[Tooltip(
		"面から跳ね返る方向に残す速度割合です。")]
	[Range(0.0f, 1.0f)]
	[SerializeField] private float m_BounceNormalRetention;

	[Tooltip(
		"接触面に沿う方向に残す速度割合です。")]
	[Range(0.0f, 1.0f)]
	[SerializeField] private float m_BounceTangentialRetention;

	[Tooltip(
		"浅い角度で衝突した場合でも最低限保証する跳ね返り速度です。")]
	[Min(0.0f)]
	[SerializeField] private float m_MinBounceNormalSpeed;

	[Tooltip(
		"バウンド時に面から離れる方向の最大速度です。0なら制限しません。")]
	[Min(0.0f)]
	[SerializeField] private float m_MaxBounceNormalSpeed;

	[Tooltip(
		"バウンド時に接触面に沿う方向の最大速度です。0なら制限しません。")]
	[Min(0.0f)]
	[SerializeField] private float m_MaxBounceTangentialSpeed;

	[Header("飛行中・バウンド後の疑似重力")]

	[Tooltip(
		"この速度以下になると飛行中のAttachSurface探索を開始します。")]
	[Min(0.0f)]
	[SerializeField] private float m_AttractStartSpeed;

	[Tooltip(
		"飛行中にAttachSurfaceを探す最大距離です。")]
	[Min(0.0f)]
	[SerializeField] private float m_AttractSearchDistance;

	[Tooltip(
		"疑似重力を維持する最大距離です。")]
	[Min(0.0f)]
	[SerializeField] private float m_AttractReleaseDistance;

	[Tooltip(
		"飛行中にAttachSurfaceを再探索する間隔です。")]
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
		"Surfaceへ近づいた時の加速度変化を調整します。")]
	[Min(0.01f)]
	[SerializeField] private float m_AttractAccelerationCurve;

	[Tooltip(
		"空中で固定点重力を受けている間の速度減衰量です。")]
	[Min(0.0f)]
	[SerializeField] private float m_AttractDeceleration;

	[Tooltip(
		"疑似重力中の最高速度です。0なら制限しません。")]
	[Min(0.0f)]
	[SerializeField] private float m_AttractMaxSpeed;

	[Header("Surface上の移動")]

	[Tooltip(
		"一度Surfaceへバウンドした後、移動速度を減衰させる量です。")]
	[Min(0.0f)]
	[SerializeField] private float m_SurfaceMoveDeceleration;

	[Tooltip(
		"Surfaceと接触中にこの速度以下になると吸着して停止します。")]
	[Min(0.0f)]
	[SerializeField] private float m_SurfaceStopSpeedThreshold;

	[Header("BallSurface Physics")]

	[Tooltip(
		"Surface法線とは逆方向へ作用する重力加速度です。")]
	[Min(0.0f)]
	[SerializeField] private float m_SurfaceGravityAcceleration;

	[Tooltip(
		"この法線方向衝突速度未満では再バウンドしません。")]
	[Min(0.0f)]
	[SerializeField] private float m_SurfaceBounceSpeedThreshold;

	[Tooltip(
		"Surface Physicsのバウンド後に残す法線方向速度の割合です。")]
	[Range(0.0f, 1.0f)]
	[SerializeField] private float m_SurfaceBounceNormalRetention;

	[Tooltip(
		"Surface Physicsのバウンド時に残すSurface方向速度の割合です。")]
	[Range(0.0f, 1.0f)]
	[SerializeField] private float m_SurfaceBounceTangentialRetention;

	[Tooltip(
		"Surface Physicsのバウンド時に最低限保証する法線方向速度です。")]
	[Min(0.0f)]
	[SerializeField] private float m_SurfaceMinBounceNormalSpeed;

	[Tooltip(
		"Surface Physicsのバウンド時の最大法線方向速度です。0なら制限しません。")]
	[Min(0.0f)]
	[SerializeField] private float m_SurfaceMaxBounceNormalSpeed;

	[Tooltip(
		"Surface Physicsのバウンド時の最大Surface方向速度です。0なら制限しません。")]
	[Min(0.0f)]
	[SerializeField] private float m_SurfaceMaxBounceTangentialSpeed;

	[Tooltip(
		"Surface Physicsで接地後にSurface方向速度を減衰させる量です。")]
	[Min(0.0f)]
	[SerializeField] private float m_SurfaceGroundDeceleration;

	[Header("BallSurface Physics Debug")]

	[Tooltip(
		"ONにするとバウンドごとの滞空時間・World移動距離・Surface移動距離をConsoleへ出力します。")]
	[SerializeField] private bool m_EnableBounceDistanceDebug;

	[Header("停止後のAttachSurface探索")]

	[Tooltip(
		"停止後にAttachSurfaceを探す最大距離です。")]
	[Min(0.0f)]
	[SerializeField] private float m_SurfaceSearchRadius;

	[Tooltip(
		"停止後のAttachSurface再探索間隔です。")]
	[Min(0.0f)]
	[SerializeField] private float m_SurfaceSearchInterval;

	[Tooltip(
		"停止後にAttachSurfaceへ向かう速度です。")]
	[Min(0.0f)]
	[SerializeField] private float m_SurfaceSeekSpeed;

	[Tooltip(
		"停止後にAttachSurfaceへ向かう加速度です。")]
	[Min(0.0f)]
	[SerializeField] private float m_SurfaceSeekAcceleration;

	/// <summary>
	/// 距離計算時の0除算を防ぐ最小距離です。
	/// </summary>
	private const float m_MinimumCalculationDistance = 0.001f;

	private Rigidbody m_Rigidbody;
	private SphereCollider m_BallCollider;
	private BallController m_BallController;

	private BallSurfaceGravity m_SurfaceGravity;
	private BallSurfaceSearch m_SurfaceSearch;
	private BallBounce m_Bounce;
	private BallSurfaceSimulation m_SurfaceSimulation;

	private EBallMotionState m_MotionState;

	/// <summary>
	/// 通常飛行中に維持する進行方向です。
	/// </summary>
	private Vector3 m_FlightDirection;

	/// <summary>
	/// Physicsシミュレーション直前の速度です。
	/// 衝突時の入射速度取得に使用します。
	/// </summary>
	private Vector3 m_PrePhysicsVelocity;

	private float m_NextSurfaceSearchTime;
	private float m_NextAttractSearchTime;

	private bool m_HasBounceDistanceDebugSample;
	private bool m_BounceDebugCrossedConvexEdge;

	private Vector3 m_PreviousBounceDebugWorldPosition;

	private float m_BounceDebugWorldDistance;
	private float m_BounceDebugSurfaceDistance;
	private float m_BounceDebugAirTime;
	private float m_BounceDebugStartSurfaceSpeed;
	private float m_BounceDebugStartVerticalSpeed;

	private int m_BounceDebugSequence;

	/// <summary>
	/// 必要なComponentを取得し、
	/// ボール物理用の各専門クラスを生成します。
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

		// Unity標準重力ではなく
		// 独自の疑似重力を使用します。
		m_Rigidbody.useGravity =
			false;
	}

	/// <summary>
	/// InspectorのSurface Physics設定をSimulationへ反映します。
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
			m_SurfaceMaxBounceTangentialSpeed,
			m_SurfaceGroundDeceleration,
			m_SurfaceStopSpeedThreshold,
			GetBallWorldRadius());
	}

	/// <summary>
	/// NetworkObjectのSpawn時に、
	/// シュート・強制解放通知を登録します。
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
	/// NetworkObjectのDespawn時に、
	/// 登録した通知を解除します。
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
	/// Host側で現在のMotionStateに応じた
	/// ボール物理を更新します。
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

		// 所持中は自由移動を行わないため、
		// 内部の物理状態をIdleへ戻します。
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
			case EBallMotionState.StuckToSurface:
				break;
		}

		if (m_MotionState ==
			EBallMotionState.Surface)
		{
			m_PrePhysicsVelocity =
				Vector3.zero;

			return;
		}

		// Unityの衝突解決前の速度として使用するため、
		// FixedUpdateで決定した速度を保存します。
		m_PrePhysicsVelocity =
			m_Rigidbody.linearVelocity;
	}

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

	/// <summary>
	/// ボールの現在のMotionStateを変更します。
	/// </summary>


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

		// Transform Scaleなどが変わった場合にも
		// 現在のBall半径をSurface Simulationへ反映します。
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

	/// <summary>
	/// Surface Physicsを1Physics Step進めます。
	/// </summary>
	private void UpdateSurfaceSimulation()
	{
		if (!m_SurfaceSimulation.IsInitialized)
		{
			Debug.LogWarning(
				$"{name}: Surface Physicsが未初期化の状態になったため、現在位置で停止します。",
				this);

			StopSurfaceMotionSafely();

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
			case BallSurfaceSimulation.EAdvanceResult.InvalidGeometry:
			case BallSurfaceSimulation.EAdvanceResult.TransitionLimitReached:
				Debug.LogWarning(
					$"{name}: Surface Physicsを継続できないため、現在位置で停止します。 Result={result}",
					this);

				StopSurfaceMotionSafely();
				break;
		}
	}

	/// <summary>
	/// Surface Physicsの座標をWorld座標へ戻します。
	/// </summary>
	private void ApplySurfacePosition(
		bool immediate)
	{
		if (!m_SurfaceSimulation.TryGetCurrentBallCenterOffset(
			out Vector3 ballCenterOffset))
		{
			return;
		}

		Vector3 targetColliderCenter =
			m_SurfaceSimulation.SurfacePoint +
			ballCenterOffset;

		Vector3 colliderCenterOffset =
			transform.TransformVector(
				m_BallCollider.center);

		Vector3 targetRigidbodyPosition =
			targetColliderCenter -
			colliderCenterOffset;

		MeasureBounceDistance(
			targetRigidbodyPosition,
			immediate);

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

	/// <summary>
	/// バウンド1回分の移動距離を計測します。
	/// </summary>
	/// <remarks>
	/// 1回のバウンド開始から次の着地までについて、
	///
	/// ・AirTime
	/// ・WorldDistance
	/// ・SurfaceDistance
	/// ・StartSurfaceSpeed
	/// ・StartVerticalSpeed
	/// ・CrossedConvex
	///
	/// を集計します。
	///
	/// WorldDistanceは実際にWorld上でボール中心が進んだ経路長です。
	/// SurfaceDistanceはSimulationが展開Surface上で
	/// 実際に消費した経路長です。
	///
	/// Sharp Convex Edgeを越えたバウンドだけ距離が不自然に変化するかを
	/// バウンド単位で比較するための一時デバッグです。
	/// </remarks>
	private void MeasureBounceDistance(
		Vector3 targetRigidbodyPosition,
		bool immediate)
	{
		if (!m_EnableBounceDistanceDebug)
		{
			ResetBounceDistanceDebug();

			return;
		}

		int currentBounceSequence =
			m_SurfaceSimulation.BounceSequence;

		// 初回Surface接触でバウンドが成立しなかった場合は
		// 計測対象が存在しません。
		if (currentBounceSequence <=
			0)
		{
			ResetBounceDistanceDebug();

			return;
		}

		if (immediate ||
			!m_HasBounceDistanceDebugSample)
		{
			BeginBounceDistanceDebug(
				currentBounceSequence,
				targetRigidbodyPosition);

			return;
		}

		m_BounceDebugWorldDistance +=
			Vector3.Distance(
				m_PreviousBounceDebugWorldPosition,
				targetRigidbodyPosition);

		m_BounceDebugSurfaceDistance +=
			m_SurfaceSimulation.SurfaceDistanceTravelledThisStep;

		m_BounceDebugAirTime +=
			Time.fixedDeltaTime;

		if (m_SurfaceSimulation.DidTraverseConvexEdgeThisStep)
		{
			m_BounceDebugCrossedConvexEdge =
				true;
		}

		m_PreviousBounceDebugWorldPosition =
			targetRigidbodyPosition;

		if (!m_SurfaceSimulation.DidSurfaceImpactThisStep)
		{
			return;
		}

		// このStep内で着地したので、
		// 直前まで追跡していた1バウンド分を出力します。
		Debug.Log(
			$"[BounceDistanceDebug] " +
			$"Bounce={m_BounceDebugSequence}, " +
			$"AirTime={m_BounceDebugAirTime:F3}, " +
			$"WorldDistance={m_BounceDebugWorldDistance:F3}, " +
			$"SurfaceDistance={m_BounceDebugSurfaceDistance:F3}, " +
			$"StartSurfaceSpeed={m_BounceDebugStartSurfaceSpeed:F3}, " +
			$"StartVerticalSpeed={m_BounceDebugStartVerticalSpeed:F3}, " +
			$"CrossedConvex={m_BounceDebugCrossedConvexEdge}",
			this);

		if (currentBounceSequence >
			m_BounceDebugSequence)
		{
			// 着地後に再バウンドした場合は、
			// 同じ位置から次のバウンド計測を開始します。
			BeginBounceDistanceDebug(
				currentBounceSequence,
				targetRigidbodyPosition);

			return;
		}

		// 再バウンドしなかった場合は計測終了です。
		ResetBounceDistanceDebug();
	}

	/// <summary>
	/// 新しいバウンドの距離計測を開始します。
	/// </summary>
	private void BeginBounceDistanceDebug(
		int bounceSequence,
		Vector3 worldPosition)
	{
		m_HasBounceDistanceDebugSample =
			true;

		m_BounceDebugSequence =
			bounceSequence;

		m_PreviousBounceDebugWorldPosition =
			worldPosition;

		m_BounceDebugWorldDistance =
			0.0f;

		m_BounceDebugSurfaceDistance =
			0.0f;

		m_BounceDebugAirTime =
			0.0f;

		m_BounceDebugStartSurfaceSpeed =
			m_SurfaceSimulation.LastBounceSurfaceSpeed;

		m_BounceDebugStartVerticalSpeed =
			m_SurfaceSimulation.LastBounceVerticalSpeed;

		m_BounceDebugCrossedConvexEdge =
			false;
	}

	/// <summary>
	/// バウンド距離計測用の一時状態を初期化します。
	/// </summary>
	private void ResetBounceDistanceDebug()
	{
		m_HasBounceDistanceDebugSample =
			false;

		m_BounceDebugCrossedConvexEdge =
			false;

		m_PreviousBounceDebugWorldPosition =
			Vector3.zero;

		m_BounceDebugWorldDistance =
			0.0f;

		m_BounceDebugSurfaceDistance =
			0.0f;

		m_BounceDebugAirTime =
			0.0f;

		m_BounceDebugStartSurfaceSpeed =
			0.0f;

		m_BounceDebugStartVerticalSpeed =
			0.0f;

		m_BounceDebugSequence =
			0;
	}

	/// <summary>
	/// SphereColliderの実ワールド半径を取得します。
	/// </summary>
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

	/// <summary>
	/// BallSurfaceへの接触は成立したものの、
	/// Triangleを取得できずSurface Physicsを開始できなかった場合に、
	/// 接触地点の外側へボールを置いて安全に停止します。
	/// </summary>
	private void StopSafelyAtSurfaceContact(
		ContactPoint contact)
	{
		Vector3 targetRigidbodyPosition =
			m_Rigidbody.position;

		bool hasSafePosition =
			false;

		Vector3 contactNormal =
			contact.normal;

		if (contactNormal.sqrMagnitude >
			Mathf.Epsilon)
		{
			contactNormal.Normalize();

			Vector3 colliderCenterOffset =
				transform.TransformVector(
					m_BallCollider.center);

			Vector3 currentColliderCenter =
				m_Rigidbody.position +
				colliderCenterOffset;

			// Contact Normalの向きに依存せず、
			// 現在のBall中心が存在する側へ必ず退避させます。
			if (Vector3.Dot(
				currentColliderCenter -
					contact.point,
				contactNormal) <
				0.0f)
			{
				contactNormal =
					-contactNormal;
			}

			float safeCenterDistance =
				GetBallWorldRadius() +
				m_MinimumCalculationDistance;

			Vector3 targetColliderCenter =
				contact.point +
				contactNormal *
					safeCenterDistance;

			targetRigidbodyPosition =
				targetColliderCenter -
				colliderCenterOffset;

			hasSafePosition =
				true;
		}

		StopSurfaceMotionSafely();

		if (hasSafePosition)
		{
			m_Rigidbody.position =
				targetRigidbodyPosition;
		}
	}

	/// <summary>
	/// Surface Physicsを安全停止し、
	/// 周辺Surfaceの再探索を行わず現在位置を維持します。
	/// </summary>
	private void StopSurfaceMotionSafely()
	{
		m_SurfaceSimulation.Reset();

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

		ResetBounceDistanceDebug();

		ChangeMotionState(
			EBallMotionState.Idle);
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

	/// <summary>
	/// 所持開始時などに、
	/// 内部の物理状態を初期化します。
	/// </summary>
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

		ResetBounceDistanceDebug();
	}

	/// <summary>
	/// デバッグリスポーン時などに、
	/// ボールを静止した通常のフリー状態へ戻します。
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

		m_PrePhysicsVelocity =
			Vector3.zero;

		m_SurfaceGravity.Clear();

		m_SurfaceSimulation.Reset();

		ResetBounceDistanceDebug();

		m_NextSurfaceSearchTime =
			0.0f;

		m_NextAttractSearchTime =
			0.0f;

		ChangeMotionState(
			EBallMotionState.Idle);
	}
}
