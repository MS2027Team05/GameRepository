using Unity.Netcode;
using UnityEngine;

/// <summary>
/// フリー状態のボールにゲーム向けの物理補正を加えます。
/// このコンポーネントを外すと、通常のRigidbody挙動に戻ります。
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(BallController))]
public class BallPhysicsController : NetworkBehaviour
{
	[Header("フリー状態のボール調整")]

	[Tooltip(
		"フリー状態のボールが1秒間にどれだけ減速するかを表します。" +
		"単位はおおよそm/s²です。0にすると減速補正を行いません。")]
	[Min(0.0f)]
	[SerializeField] private float m_FreeBallDeceleration;

	[Tooltip(
		"フリー状態のボールの最高速度です。" +
		"0にすると最高速度の制限を行いません。" +
		"BallControllerのShoot Powerと同程度を推奨します。")]
	[Min(0.0f)]
	[SerializeField] private float m_MaxFreeBallSpeed;

	[Tooltip(
		"ボールの回転速度を1秒間にどれだけ減衰させるかを表します。" +
		"大きいほど回転が早く収まります。0にすると回転補正を行いません。")]
	[Min(0.0f)]
	[SerializeField] private float m_AngularDeceleration;

	[Header("シュート中の重力調整")]

	[Tooltip(
		"シュートしてから最初に何かへ衝突するまでの重力倍率です。" +
		"0で重力なし、0.5で通常の半分、1で通常の重力になります。")]
	[Range(0.0f, 1.0f)]
	[SerializeField] private float m_ShotGravityMultiplier;

	[Header("衝突時の調整")]

	[Tooltip(
		"地面や壁などへ衝突した直後に残す速度の割合です。" +
		"0で完全停止、0.7で衝突前の70%、1で速度を減らしません。")]
	[Range(0.0f, 1.0f)]
	[SerializeField] private float m_CollisionSpeedMultiplier;

	private Rigidbody m_Rigidbody;
	private BallController m_BallController;

	private bool m_IsShotFlying;

	private void Awake()
	{
		m_Rigidbody =
			GetComponent<Rigidbody>();

		m_BallController =
			GetComponent<BallController>();
	}

	public override void OnNetworkSpawn()
	{
		if (m_BallController != null)
		{
			m_BallController.OnShot +=
				HandleShot;
		}
	}

	public override void OnNetworkDespawn()
	{
		if (m_BallController != null)
		{
			m_BallController.OnShot -=
				HandleShot;
		}
	}

	private void FixedUpdate()
	{
		// ボールの物理計算はHostだけが行います。
		if (!IsServer)
		{
			return;
		}

		// 所持状態になった場合は、
		// シュート中の状態も終了します。
		if (m_Rigidbody.isKinematic)
		{
			m_IsShotFlying = false;

			return;
		}

		if (m_IsShotFlying)
		{
			ApplyShotGravity();
		}

		LimitLinearSpeed();
		ApplyLinearDeceleration();
		ApplyAngularDeceleration();
	}

	/// <summary>
	/// シュートされた瞬間から、
	/// 最初の衝突までシュート中として扱います。
	/// </summary>
	private void HandleShot()
	{
		if (!IsServer)
		{
			return;
		}

		m_IsShotFlying = true;
	}

	/// <summary>
	/// シュート中だけ通常より弱い重力に補正します。
	/// RigidbodyのUse Gravityは有効なまま使用します。
	/// </summary>
	private void ApplyShotGravity()
	{
		if (!m_Rigidbody.useGravity)
		{
			return;
		}

		float gravityMultiplier =
			Mathf.Max(
				0.0f,
				m_ShotGravityMultiplier);

		// Rigidbodyには標準重力が既に適用されるため、
		// 指定倍率との差分だけを追加して補正します。
		Vector3 gravityCorrection =
			Physics.gravity *
			(gravityMultiplier - 1.0f);

		m_Rigidbody.AddForce(
			gravityCorrection,
			ForceMode.Acceleration);
	}

	/// <summary>
	/// ボールが設定された最高速度を超えないようにします。
	/// </summary>
	private void LimitLinearSpeed()
	{
		if (m_MaxFreeBallSpeed <= 0.0f)
		{
			return;
		}

		float currentSpeed =
			m_Rigidbody.linearVelocity.magnitude;

		if (currentSpeed <= m_MaxFreeBallSpeed)
		{
			return;
		}

		m_Rigidbody.linearVelocity =
			m_Rigidbody.linearVelocity.normalized *
			m_MaxFreeBallSpeed;
	}

	/// <summary>
	/// フリー状態のボールを徐々に減速させます。
	/// </summary>
	private void ApplyLinearDeceleration()
	{
		if (m_FreeBallDeceleration <= 0.0f)
		{
			return;
		}

		m_Rigidbody.linearVelocity =
			Vector3.MoveTowards(
				m_Rigidbody.linearVelocity,
				Vector3.zero,
				m_FreeBallDeceleration *
				Time.fixedDeltaTime);
	}

	/// <summary>
	/// ボールの回転速度を徐々に抑えます。
	/// </summary>
	private void ApplyAngularDeceleration()
	{
		if (m_AngularDeceleration <= 0.0f)
		{
			return;
		}

		m_Rigidbody.angularVelocity =
			Vector3.MoveTowards(
				m_Rigidbody.angularVelocity,
				Vector3.zero,
				m_AngularDeceleration *
				Time.fixedDeltaTime);
	}

	/// <summary>
	/// 最初の衝突でシュート中の重力補正を終了し、
	/// 衝突後の速度も少し落とします。
	/// </summary>
	private void OnCollisionEnter(
		Collision collision)
	{
		if (!IsServer)
		{
			return;
		}

		if (m_Rigidbody.isKinematic)
		{
			return;
		}

		// 地面・壁・障害物など、
		// 何かに衝突した時点で通常重力へ戻します。
		m_IsShotFlying = false;

		float multiplier =
			Mathf.Clamp01(
				m_CollisionSpeedMultiplier);

		m_Rigidbody.linearVelocity *=
			multiplier;
	}
}
