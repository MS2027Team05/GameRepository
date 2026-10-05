using DG.Tweening;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// キャラクターの物理移動・等速直線運動・ホバリング・接地姿勢制御を担当するクラス。
/// PlayerControllerからIGravityMoverインターフェース経由で制御されます。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class GravityMover : NetworkBehaviour, IGravityMover
{
	[Header("移動パラメータ")]
	// 想定設定値: 20.0f (ゲームスピードに応じて要調整)
	[SerializeField] private float m_BaseFallSpeed;

	[Header("姿勢補正アニメーション設定")]
	// 想定設定値: 0.25f (接地時の回転補正にかける秒数)
	[SerializeField] private float m_AlignDuration;

	// 想定設定値: Ease.OutQuad (衝突時の衝撃を和らげる減速イージング)
	[SerializeField] private Ease m_AlignEase;

	[Header("地上移動パラメータ")]
	// 想定設定値: 10.0f (地上歩行移動速度)
	[SerializeField] private float m_GroundMoveSpeed;

	// 想定設定値: 15.0f (地上加減速のレスポンス)
	[SerializeField] private float m_GroundAcceleration;

	// 想定設定値: 15.0f (曲面接地を維持する下向きの吸着力)
	[SerializeField] private float m_GroundStickForce;

	// 想定設定値: 1.5f (足元の地面法線を検知するRaycast距離)
	[SerializeField] private float m_GroundCheckDistance;

	// 想定設定値: 10.0f (歩行中の曲面法線への姿勢追従速度)
	[SerializeField] private float m_SurfaceAlignSpeed;

	// 想定設定値: Groundレイヤー等のマスク(未指定時は全レイヤー対象)
	[SerializeField] private LayerMask m_GroundLayerMask;

	private Rigidbody m_Rigidbody;
	private Vector3 m_CurrentDirection;
	private float m_SpeedMultiplier = 1.0f;
	private bool m_IsFalling;
	private bool m_IsGrounded;
	private Tween m_AlignTween;

	private Vector2 m_MoveInput;
	private Transform m_CameraTransform;
	private Vector3 m_CurrentGroundNormal = Vector3.up;

	/// <summary>
	/// 現在等速直線移動(落下)中であるかを取得します。
	/// </summary>
	public bool IsFalling
	{
		get => m_IsFalling;
	}

	/// <summary>
	/// 地面または壁面に接地しているかを取得します。
	/// </summary>
	public bool IsGrounded
	{
		get => m_IsGrounded;
	}

	private void Awake()
	{
		m_Rigidbody = GetComponent<Rigidbody>();
		if (m_Rigidbody != null)
		{
			// 無重力状態で自前ベクトル推進を行うため物理重力を無効化
			m_Rigidbody.useGravity = false;
		}
	}

	/// <summary>
	/// 指定された方向へ等速直線移動(落下)を開始します。
	/// </summary>
	/// <param name="direction">進行方向ベクトル</param>
	public void StartFalling(Vector3 direction)
	{
		if (direction.sqrMagnitude < 0.0001f)
		{
			return;
		}

		// 進行中の姿勢補正アニメーションがあれば中断
		m_AlignTween?.Kill();

		m_CurrentDirection = direction.normalized;
		m_IsFalling = true;
		m_IsGrounded = false;
		m_MoveInput = Vector2.zero;

		// 進行方向へ即座に向きを設定
		transform.forward = m_CurrentDirection;
	}

	/// <summary>
	/// 落下を停止し、空中で浮遊・静止(ホバリング)させます。
	/// </summary>
	public void StopFalling()
	{
		m_IsFalling = false;
		m_MoveInput = Vector2.zero;

		if (m_Rigidbody != null)
		{
			m_Rigidbody.linearVelocity = Vector3.zero;
			m_Rigidbody.angularVelocity = Vector3.zero;
		}
	}

	/// <summary>
	/// 移動速度の倍率を設定します(ボール所持時のデバフ等)。
	/// </summary>
	/// <param name="multiplier">速度倍率</param>
	public void SetSpeedMultiplier(float multiplier)
	{
		m_SpeedMultiplier = multiplier;
	}

	/// <summary>
	/// 地上移動用の入力ベクトルと基準となるカメラのTransformを設定します。
	/// </summary>
	/// <param name="moveInput">入力ベクトル(WASD/スティック)</param>
	/// <param name="cameraTransform">視点基準となるカメラのTransform</param>
	public void SetMoveInput(Vector2 moveInput, Transform cameraTransform)
	{
		m_MoveInput = moveInput;
		m_CameraTransform = cameraTransform;
	}

	private void FixedUpdate()
	{
		// ネットワーク同期下では所有者(IsOwner)のみ物理挙動を計算
		if (IsSpawned && !IsOwner)
		{
			return;
		}

		if (m_IsFalling && m_Rigidbody != null)
		{
			m_Rigidbody.linearVelocity = m_CurrentDirection * (m_BaseFallSpeed * m_SpeedMultiplier);
			if (m_CurrentDirection != Vector3.zero)
			{
				transform.forward = m_CurrentDirection;
			}
			return;
		}

		// 地上移動・曲面接地制御
		UpdateGroundStatus();
		HandleGroundMovement();
	}

	/// <summary>
	/// 足元Raycastによる最新の曲面法線検知および姿勢追従を行います。
	/// </summary>
	private void UpdateGroundStatus()
	{
		if (m_IsFalling)
		{
			return;
		}

		float checkDist = m_GroundCheckDistance > 0f ? m_GroundCheckDistance : 1.5f;
		int layerMask = m_GroundLayerMask.value == 0 ? ~0 : m_GroundLayerMask.value;

		// キャラクターの足元方向(-transform.up)へRaycast
		if (Physics.Raycast(transform.position, -transform.up, out RaycastHit hit, checkDist, layerMask, QueryTriggerInteraction.Ignore))
		{
			m_IsGrounded = true;
			m_CurrentGroundNormal = hit.normal;

			// Tween動作中でない場合、曲面法線へ徐々に姿勢を補正
			if (m_AlignTween == null || !m_AlignTween.IsActive() || !m_AlignTween.IsPlaying())
			{
				float alignSpeed = m_SurfaceAlignSpeed > 0f ? m_SurfaceAlignSpeed : 10.0f;
				Quaternion targetRotation = Quaternion.FromToRotation(transform.up, m_CurrentGroundNormal) * transform.rotation;
				transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, alignSpeed * Time.fixedDeltaTime);
			}
		}
	}

	/// <summary>
	/// カメラ向きと最新の地面法線に基づき、接平面上の移動と吸着力を適用します。
	/// </summary>
	private void HandleGroundMovement()
	{
		if (m_Rigidbody == null || !m_IsGrounded || m_IsFalling)
		{
			return;
		}

		Vector3 targetVelocity = Vector3.zero;

		if (m_MoveInput.sqrMagnitude > 0.001f && m_CameraTransform != null)
		{
			Vector3 camForward = m_CameraTransform.forward;
			Vector3 camRight = m_CameraTransform.right;

			Vector3 forwardOnPlane = Vector3.ProjectOnPlane(camForward, m_CurrentGroundNormal);
			Vector3 rightOnPlane = Vector3.ProjectOnPlane(camRight, m_CurrentGroundNormal);

			if (forwardOnPlane.sqrMagnitude > 0.0001f)
			{
				forwardOnPlane.Normalize();
			}
			if (rightOnPlane.sqrMagnitude > 0.0001f)
			{
				rightOnPlane.Normalize();
			}

			Vector3 moveDir = (forwardOnPlane * m_MoveInput.y + rightOnPlane * m_MoveInput.x).normalized;
			float speed = m_GroundMoveSpeed > 0f ? m_GroundMoveSpeed : 10.0f;
			targetVelocity = moveDir * (speed * m_SpeedMultiplier);

			// 移動方向を向くよう法線軸周りで回転
			if (moveDir.sqrMagnitude > 0.001f)
			{
				Quaternion targetLook = Quaternion.LookRotation(moveDir, m_CurrentGroundNormal);
				float alignSpeed = m_SurfaceAlignSpeed > 0f ? m_SurfaceAlignSpeed : 10.0f;
				transform.rotation = Quaternion.Slerp(transform.rotation, targetLook, alignSpeed * Time.fixedDeltaTime);
			}
		}

		// 現在のRigidbody速度を接平面成分と法線成分に分解
		Vector3 currentVel = m_Rigidbody.linearVelocity;
		Vector3 normalVel = Vector3.Project(currentVel, m_CurrentGroundNormal);
		Vector3 planeVel = currentVel - normalVel;

		// 接平面上の速度を目標速度へ加減速補間
		float accel = m_GroundAcceleration > 0f ? m_GroundAcceleration : 15.0f;
		Vector3 newPlaneVel = Vector3.MoveTowards(planeVel, targetVelocity, accel * Time.fixedDeltaTime);

		// 曲面からの浮き上がりを防止するダウンフォース(法線の逆方向)
		float stickForce = m_GroundStickForce > 0f ? m_GroundStickForce : 15.0f;
		Vector3 downforce = -m_CurrentGroundNormal * stickForce;

		m_Rigidbody.linearVelocity = newPlaneVel + downforce * Time.fixedDeltaTime;
	}

	private void OnCollisionEnter(Collision collision)
	{
		// ネットワーク同期下では所有者(IsOwner)のみ判定
		if (IsSpawned && !IsOwner)
		{
			return;
		}

		if (collision.contactCount > 0)
		{
			ContactPoint contact = collision.GetContact(0);
			Vector3 hitNormal = contact.normal;

			// 落下を停止してホバリング状態へ遷移
			StopFalling();

			// 既存の姿勢補正アニメーションを中断
			m_AlignTween?.Kill();

			// キャラクターの足元(transform.up)が衝突面の法線を向くように姿勢補正
			Quaternion targetRotation = Quaternion.FromToRotation(transform.up, hitNormal) * transform.rotation;

			if (m_AlignDuration > 0f)
			{
				m_AlignTween = transform.DORotateQuaternion(targetRotation, m_AlignDuration)
					.SetEase(m_AlignEase);
			}
			else
			{
				transform.rotation = targetRotation;
			}

			m_CurrentGroundNormal = hitNormal;
			m_IsGrounded = true;
		}
	}

	private void OnCollisionExit(Collision collision)
	{
		if (IsSpawned && !IsOwner)
		{
			return;
		}

		m_IsGrounded = false;
	}

	public override void OnDestroy()
	{
		m_AlignTween?.Kill();
		base.OnDestroy();
	}
}
