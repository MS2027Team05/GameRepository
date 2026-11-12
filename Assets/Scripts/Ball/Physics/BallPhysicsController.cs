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
public partial class BallPhysicsController : NetworkBehaviour
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

	/// <summary>
	/// Surface Physics中の障害物検出で再利用するHitバッファ数です。
	/// </summary>
	private const int m_SurfaceObstacleHitBufferSize = 32;

	private Rigidbody m_Rigidbody;
	private SphereCollider m_BallCollider;
	private BallController m_BallController;

	private BallSurfaceGravity m_SurfaceGravity;
	private BallSurfaceSearch m_SurfaceSearch;
	private BallBounce m_Bounce;
	private BallSurfaceSimulation m_SurfaceSimulation;

	private RaycastHit[] m_SurfaceObstacleHits;
	private int m_BallCollisionMask;

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

		m_SurfaceObstacleHits =
			new RaycastHit[m_SurfaceObstacleHitBufferSize];

		m_BallCollisionMask =
			BuildBallCollisionMask();

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

		if (m_BallController == null ||
			m_BallController.IsMatchPaused)
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


		m_NextSurfaceSearchTime =
			0.0f;

		m_NextAttractSearchTime =
			0.0f;

		ChangeMotionState(
			EBallMotionState.Idle);
	}
}
