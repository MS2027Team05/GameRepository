using UnityEngine;

/// <summary>
/// 地上・曲面・壁面でのカメラ基準歩行移動・運動量転送・ダウンフォース・姿勢回転を担当するコンポーネント。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PlayerSurfaceMover : MonoBehaviour, ISurfaceMover
{
	[Header("地上移動パラメータ")]
	// 想定設定値: 10.0f (地上歩行移動速度)
	[SerializeField] private float m_GroundMoveSpeed;

	// 想定設定値: 15.0f (地上加減速のレスポンス)
	[SerializeField] private float m_GroundAcceleration;

	// 想定設定値: 15.0f (曲面接地を維持する下向きの吸着力)
	[SerializeField] private float m_GroundStickForce;

	// 想定設定値: 10.0f (歩行中の曲面法線への姿勢追従速度)
	[SerializeField] private float m_SurfaceAlignSpeed;

	[Header("デバッグ表示設定")]
	// 想定設定値: true
	[SerializeField] private bool m_EnableDebugDraw;

	private Rigidbody m_Rigidbody;
	private IGroundDetector m_GroundDetector;
	private Vector2 m_MoveInput;
	private Transform m_CameraTransform;
	private float m_SpeedMultiplier = 1.0f;
	private Vector3 m_LastMoveDirection = Vector3.forward;
	private float m_LastPlaneSpeed;
	private Vector3 m_LastSnapVelocity;
	private Vector3 m_LastDownforce;
	private float m_LastNormalDiff;

	/// <summary>
	/// 現在の接平面進行方向ベクトルを取得します。
	/// </summary>
	public Vector3 CurrentMoveDirection { get => m_LastMoveDirection; }

	/// <summary>
	/// 現在の接平面走破速度の絶対値を取得します。
	/// </summary>
	public float CurrentPlaneSpeed { get => m_LastPlaneSpeed; }

	/// <summary>
	/// 診断・検証用: 直近フレームで適用されたスナップ速度を取得します。
	/// </summary>
	public Vector3 LastSnapVelocity { get => m_LastSnapVelocity; }

	/// <summary>
	/// 診断・検証用: 直近フレームで適用されたダウンフォースを取得します。
	/// </summary>
	public Vector3 LastDownforce { get => m_LastDownforce; }

	/// <summary>
	/// 診断・検証用: 直近フレームの法線角度差(0:平坦〜1:直角)を取得します。
	/// </summary>
	public float LastNormalDiff { get => m_LastNormalDiff; }

	/// <summary>
	/// 診断・検証用: 直近フレームの入力ベクトル(WASD)を取得します。
	/// </summary>
	public Vector2 CurrentMoveInput { get => m_MoveInput; }

	private void Awake()
	{
		m_Rigidbody = GetComponent<Rigidbody>();
		m_GroundDetector = GetComponent<IGroundDetector>();
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

	/// <summary>
	/// 移動速度の倍率を設定します(ボール所持時のデバフ等)。
	/// </summary>
	/// <param name="multiplier">速度倍率(通常時は1.0f、所持時は0.7f等)</param>
	public void SetSpeedMultiplier(float multiplier)
	{
		m_SpeedMultiplier = multiplier;
	}

	/// <summary>
	/// 地上・曲面移動および姿勢回転をFixedUpdateタイミングで更新します。
	/// </summary>
	public void UpdateSurfaceMovement()
	{
		if (m_Rigidbody == null || m_Rigidbody.isKinematic)
		{
			return;
		}

		bool isGrounded = m_GroundDetector != null && m_GroundDetector.IsGrounded;
		Vector3 groundNormal = m_GroundDetector != null ? m_GroundDetector.CurrentGroundNormal : Vector3.up;
		float groundGap = m_GroundDetector != null ? m_GroundDetector.GroundGap : 0f;

		if (!isGrounded)
		{
			m_LastPlaneSpeed = 0f;
			// 未接地かつ非落下時: 直前の歩行慣性で空中滑走しないようドラッグ制動
			float dragRate = m_GroundAcceleration > 0f ? m_GroundAcceleration * 2.5f : 30.0f;
			m_Rigidbody.linearVelocity = Vector3.MoveTowards(m_Rigidbody.linearVelocity, Vector3.zero, dragRate * Time.fixedDeltaTime);
			return;
		}

		Vector3 targetVelocity = Vector3.zero;
		Vector3 moveDir = Vector3.zero;

		if (m_MoveInput.sqrMagnitude > 0.001f && m_CameraTransform != null)
		{
			Vector3 camForward = m_CameraTransform.forward;
			Vector3 camRight = m_CameraTransform.right;

			Vector3 forwardOnPlane = Vector3.ProjectOnPlane(camForward, groundNormal);
			Vector3 rightOnPlane = Vector3.ProjectOnPlane(camRight, groundNormal);

			// カメラの視線が壁の法線と重なって消失した場合、直前の進行方向を接平面へ投影して前進を維持
			if (forwardOnPlane.sqrMagnitude < 0.01f && m_LastMoveDirection.sqrMagnitude > 0.01f)
			{
				forwardOnPlane = Vector3.ProjectOnPlane(m_LastMoveDirection, groundNormal);
			}

			if (forwardOnPlane.sqrMagnitude > 0.0001f)
			{
				forwardOnPlane.Normalize();
			}
			if (rightOnPlane.sqrMagnitude > 0.0001f)
			{
				rightOnPlane.Normalize();
			}

			Vector3 rawMoveDir = (forwardOnPlane * m_MoveInput.y + rightOnPlane * m_MoveInput.x).normalized;

			// 【アルゴリズム: 接平面移動ベクトルの慣性平滑化 (Tangent Plane Inertial Smoothing)】
			// 角を回り込む際、法線の回転に伴ってカメラの投影ベクトルが直角にねじれてガタつくのを防止します。
			// 直前の進行方向 m_LastMoveDirection を現在の新法線接平面上に再投影(ProjectOnPlane)して基準を保ち、
			// 急角を跨ぐ過渡期(normalDiff > 0.05f)は角速度制限(Slerp)をかけて滑らかに進行ベクトルを追従させます。
			if (m_LastMoveDirection.sqrMagnitude > 0.001f)
			{
				Vector3 lastOnPlane = Vector3.ProjectOnPlane(m_LastMoveDirection, groundNormal).normalized;
				if (lastOnPlane.sqrMagnitude > 0.001f)
				{
					// 平坦地では素早いレスポンス(40.0f)、角回り込み過渡期は滑らかな円弧補間(18.0f)を適用
					float turnRate = m_LastNormalDiff > 0.05f ? 18.0f : 40.0f;
					moveDir = Vector3.Slerp(lastOnPlane, rawMoveDir, turnRate * Time.fixedDeltaTime).normalized;
				}
				else
				{
					moveDir = rawMoveDir;
				}
			}
			else
			{
				moveDir = rawMoveDir;
			}

			if (moveDir.sqrMagnitude > 0.001f)
			{
				m_LastMoveDirection = moveDir;
			}

			float speed = m_GroundMoveSpeed > 0f ? m_GroundMoveSpeed : 10.0f;
			targetVelocity = moveDir * (speed * m_SpeedMultiplier);
		}

		// 姿勢回転: ロール(斜めの傾き)のない垂直90度姿勢を適用
		ApplyGroundRotation(moveDir, groundNormal);

		// 現在のRigidbody速度を接平面成分と法線成分に分解
		Vector3 currentVel = m_Rigidbody.linearVelocity;
		Vector3 normalVel = Vector3.Project(currentVel, groundNormal);
		Vector3 planeVel = currentVel - normalVel;

		// スピード維持の運動量転送(角を跨いだ際、直前の走破スピードの数値を新面へ直接転送して失速と飛び出しを防止)
		if (m_MoveInput.sqrMagnitude > 0.001f && moveDir.sqrMagnitude > 0.001f)
		{
			float targetSpeed = (m_GroundMoveSpeed > 0f ? m_GroundMoveSpeed : 10.0f) * m_SpeedMultiplier;
			float currentForwardSpeed = Vector3.Dot(planeVel, moveDir);

			if (currentForwardSpeed < targetSpeed * 0.85f && m_LastPlaneSpeed > targetSpeed * 0.5f)
			{
				planeVel = moveDir * targetSpeed;
			}
		}

		// 接平面上の速度を加減速補間
		float accel = m_GroundAcceleration > 0f ? m_GroundAcceleration : 15.0f;
		Vector3 newPlaneVel = Vector3.MoveTowards(planeVel, targetVelocity, accel * Time.fixedDeltaTime);
		m_LastPlaneSpeed = newPlaneVel.magnitude;

		// 曲面・角回り込み時の求心吸着スナップ
		// 速度に連動した適度な向心力を持たせつつ、急激な階段状付与を抑えて滑らかな円弧軌道を描く
		float currentSpeed = newPlaneVel.magnitude;
		float normalDiff = 1.0f - Mathf.Clamp01(Vector3.Dot(transform.up, groundNormal)); // 0(平坦)〜1(直角)
		m_LastNormalDiff = normalDiff;
		float maxSnapSpeed = Mathf.Max(15.0f, currentSpeed * 1.5f);

		float snapRate = 22.0f;
		float snapSpeed = Mathf.Clamp(groundGap * snapRate, -2.5f, maxSnapSpeed);

		// 角を跨ぐ過渡期(浮き上がり防止)は、法線差に応じて緩やかに向心引き込みをアシスト
		// 低速移動時でも角から空中に抜け出さないよう、法線差に応じた最低限のベース向心吸着力を保証
		if (normalDiff > 0.05f)
		{
			float baseCentripetal = normalDiff * 4.5f;
			// 二乗イージングにより、高速時は速度連動で引き込みを強化
			float smoothCentripetal = Mathf.Max(baseCentripetal, currentSpeed * (normalDiff * normalDiff) * 0.5f);
			snapSpeed = Mathf.Max(snapSpeed, smoothCentripetal);
		}

		Vector3 snapVelocity = -groundNormal * snapSpeed;
		m_LastSnapVelocity = snapVelocity;

		// 地面への安定吸着力(ダウンフォース)
		float stickForce = m_GroundStickForce > 0f ? m_GroundStickForce : 15.0f;
		Vector3 downforce = -groundNormal * stickForce;
		m_LastDownforce = downforce;

		m_Rigidbody.linearVelocity = newPlaneVel + snapVelocity + downforce * Time.fixedDeltaTime;
	}

	private void ApplyGroundRotation(Vector3 moveDir, Vector3 groundNormal)
	{
		Vector3 targetForward;

		if (moveDir.sqrMagnitude > 0.001f)
		{
			targetForward = moveDir;
		}
		else
		{
			targetForward = Vector3.ProjectOnPlane(transform.forward, groundNormal);
			if (targetForward.sqrMagnitude < 0.001f)
			{
				targetForward = Vector3.ProjectOnPlane(-transform.up, groundNormal);
			}
		}

		if (targetForward.sqrMagnitude > 0.0001f && groundNormal.sqrMagnitude > 0.0001f)
		{
			targetForward.Normalize();
			Quaternion targetRotation = Quaternion.LookRotation(targetForward, groundNormal);
			float baseAlignSpeed = m_SurfaceAlignSpeed > 0f ? m_SurfaceAlignSpeed : 10.0f;

			// 角回り込み時でも急変させず、心地よい円弧を描く範囲(最大16.0f程度)で滑らかに姿勢追従
			float normalDiff = 1.0f - Mathf.Clamp01(Vector3.Dot(transform.up, groundNormal));
			float alignSpeed = baseAlignSpeed + normalDiff * 6.0f;

			Quaternion newRotation = Quaternion.Slerp(m_Rigidbody.rotation, targetRotation, alignSpeed * Time.fixedDeltaTime);
			m_Rigidbody.MoveRotation(newRotation);
		}
	}
}
