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

	[Header("デバッグ可視化設定")]
	[SerializeField] private bool m_EnableDebugDraw = true;

	// 想定設定値: Groundレイヤー等のマスク(未指定時は全レイヤー対象)
	[SerializeField] private LayerMask m_GroundLayerMask;

	[Header("連続衝突判定設定")]
	// 想定設定値: true (所有者端末のRigidbodyで連続衝突判定を有効化)
	[SerializeField] private bool m_EnableContinuousDetectionForOwner = true;

	[Header("エッジ着地・スタック対策設定")]
	// パターンB: 飛行中のコライダー縮小を有効化(エイム線への忠実な追従)
	[SerializeField] private bool m_EnableSlimColliderInFlight = true;
	// 想定設定値: 0.3f (飛行中のコライダー半径倍率: 通常の30%までスリム化)
	[SerializeField] private float m_FlightColliderRadiusScale;

	// パターンA: 角部接地スタック防止(足元キャストが外れても近傍の角や面を捉えて接地維持)
	[SerializeField] private bool m_EnableEdgeGroundedAssist = true;
	// 想定設定値: 0.6f (足元外れ時に近傍の角や面を探索するサーチ半径)
	[SerializeField] private float m_EdgeSearchRadius;

	[Header("落下中の着地姿勢回転")]
	[SerializeField] private float m_FallingRotationSpeed;

	// 落下中に向かう着地法線
	private Vector3 m_FallingTargetNormal = Vector3.up;

	// 落下中の目標回転
	//private Quaternion m_FallingTargetRotation;

	// 着地面を事前に検出できているか
	private bool m_HasFallingTargetNormal = false;


	private Vector3 m_FallingTargetPoint;
	private bool m_HasFallingTargetPoint = false;

	private Rigidbody m_Rigidbody;
	private CapsuleCollider m_CapsuleCollider;
	private float m_OriginalColliderRadius;
	private Vector3 m_CurrentDirection;
	private float m_SpeedMultiplier = 1.0f;
	private bool m_IsFalling;
	private bool m_IsGrounded;
	private Tween m_AlignTween;
	private bool m_IsAimHovering;

	private Vector2 m_MoveInput;
	private Transform m_CameraTransform;
	private Vector3 m_CurrentGroundNormal = Vector3.up;
	private Vector3 m_LastMoveDirection = Vector3.forward;
	private float m_LandingSnapTimer;
	private float m_GroundGap;
	private float m_LastPlaneSpeed;

	private struct DebugRayInfo
	{
		public Vector3 Origin;
		public Vector3 Direction;
		public float Distance;
		public bool Hit;
		public Vector3 HitPoint;
	}

	private readonly System.Collections.Generic.List<DebugRayInfo> m_DebugRays = new System.Collections.Generic.List<DebugRayInfo>();
	private Vector3 m_DebugSphereCastOrigin;
	private Vector3 m_DebugSphereCastEnd;
	private float m_DebugSphereCastRadius;
	private bool m_DebugSphereCastHit;
	private Vector3 m_DebugSphereCastHitPoint;

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

	/// <summary>
	/// 現在の接地面法線ベクトルを取得します。
	/// </summary>
	public Vector3 CurrentGroundNormal
	{
		get => m_CurrentGroundNormal;
	}

	private void Awake()
	{
		m_Rigidbody = GetComponent<Rigidbody>();
		m_CapsuleCollider = GetComponent<CapsuleCollider>();
		if (m_CapsuleCollider != null)
		{
			m_OriginalColliderRadius = m_CapsuleCollider.radius;
		}

		if (m_Rigidbody != null)
		{
			// 無重力状態で自前ベクトル推進を行うため物理重力を無効化
			m_Rigidbody.useGravity = false;

			// スタンドアロン動作時(ネットワーク未起動時)の連続衝突判定
			if (!NetworkManager.Singleton || !NetworkManager.Singleton.IsListening)
			{
				if (m_EnableContinuousDetectionForOwner)
				{
					m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
				}
			}
		}
	}

	public override void OnNetworkSpawn()
	{
		base.OnNetworkSpawn();

		// ネットワーク接続時は自身の所有端末(IsOwner)のみ連続衝突判定を有効化して負荷を抑制
		if (IsOwner && m_Rigidbody != null && m_EnableContinuousDetectionForOwner)
		{
			m_Rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
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

		if (m_Rigidbody != null)
		{
			// エイムホバリング(isKinematic)から飛行移動を開始する際、
			// 浮遊中のTransform座標をRigidbodyに同期した上で物理演算を再開する
			m_Rigidbody.position = transform.position;
			m_Rigidbody.isKinematic = false;
		}

		// 照準で指定した着地点があれば、その地点へ向かう方向を使う
		if (m_HasFallingTargetPoint)
		{
			Vector3 targetDirection =
				m_FallingTargetPoint - transform.position;

			if (targetDirection.sqrMagnitude > 0.0001f)
			{
				m_CurrentDirection = targetDirection.normalized;
			}
			else
			{
				m_CurrentDirection = direction.normalized;
			}

			m_HasFallingTargetPoint = false;
		}
		else
		{
			m_CurrentDirection = direction.normalized;
		}

		m_IsFalling = true;
		m_IsGrounded = false;
		m_MoveInput = Vector2.zero;

		// パターンB: 飛行中のコライダーをスリム化して角のカス当たりを抑制
		if (m_EnableSlimColliderInFlight && m_CapsuleCollider != null && m_OriginalColliderRadius > 0f)
		{
			float scale = m_FlightColliderRadiusScale > 0f ? m_FlightColliderRadiusScale : 0.3f;
			m_CapsuleCollider.radius = m_OriginalColliderRadius * scale;
		}


		// 進行方向へ即座に向きを設定
		//transform.forward = m_CurrentDirection;
	}

	/// <summary>
	/// 落下先の着地点と着地面の法線を設定します。
	/// マーカーの着地点が決定した時に呼び出します。
	/// </summary>
	public void SetFallingTarget(Vector3 landingPoint, Vector3 landingNormal)
	{
		m_FallingTargetPoint = landingPoint;
		m_HasFallingTargetPoint = true;

		m_FallingTargetNormal = landingNormal.normalized;
		m_HasFallingTargetNormal = true;
	}

	/// <summary>
	/// 落下を停止し、空中で浮遊・静止(ホバリング)させます。
	/// </summary>
	public void StopFalling()
	{
		m_IsFalling = false;
		m_MoveInput = Vector2.zero;

		// パターンB: 停止時にコライダーを元のサイズへ復元
		if (m_CapsuleCollider != null && m_OriginalColliderRadius > 0f)
		{
			m_CapsuleCollider.radius = m_OriginalColliderRadius;
		}

		if (m_Rigidbody != null)
		{
			m_Rigidbody.linearVelocity = Vector3.zero;
			m_Rigidbody.angularVelocity = Vector3.zero;
		}
	}

	/// <summary>
	/// エイム中のホバリング状態を開始します。
	/// 通常の接地判定・地上移動・吸着処理を停止します。
	/// </summary>
	public void StartAimHover()
	{
		m_IsAimHovering = true;
		m_IsFalling = false;
		m_MoveInput = Vector2.zero;

		if (m_Rigidbody != null)
		{
			m_Rigidbody.linearVelocity = Vector3.zero;
			m_Rigidbody.angularVelocity = Vector3.zero;

			// エイム中の浮遊演出(DOTweenによるTransform移動)が物理演算や
			// 補間処理(Interpolate)によって地面へ引き戻されるのを防ぐため、一時的に物理挙動を無効化する
			m_Rigidbody.isKinematic = true;
		}
	}

	/// <summary>
	/// エイム中のホバリング状態を終了します。
	/// 通常の接地判定・地上移動処理へ戻します。
	/// </summary>
	public void StopAimHover()
	{
		m_IsAimHovering = false;
		m_MoveInput = Vector2.zero;

		if (m_Rigidbody != null)
		{
			// エイム浮動終了時、Transformの現在位置をRigidbodyに同期した上で
			// 通常の物理演算(isKinematic = false)に復帰させる
			m_Rigidbody.position = transform.position;
			m_Rigidbody.isKinematic = false;
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

		// エイム中は物理移動・接地処理を停止してホバリング
		if (m_IsAimHovering)
		{
			return;
		}

		if (m_IsFalling && m_Rigidbody != null)
		{
			// 等速直線移動
			m_Rigidbody.linearVelocity =
				m_CurrentDirection *
				(m_BaseFallSpeed * m_SpeedMultiplier);

			// 移動中に少しずつ着地面の向きへ補正
			UpdateFallingRotation();

			return;
		}

		// 地上移動・曲面接地制御
		UpdateGroundStatus();
		HandleGroundMovement();
	}

	/// <summary>
	/// エッジ(角)回り込み遷移を開始します。
	/// <summary>
	/// 地面・エッジ・壁の法線検知および角のマルチアングル接地制御を行います。
	/// 幾何学的テレポート(MovePosition)を行わず、物理速度と吸着力のみで自然に角を回り込ませます。
	/// </summary>
	private void UpdateGroundStatus()
	{
		if (m_Rigidbody == null || m_IsFalling)
		{
			return;
		}

		float footDist = GetFootOffset();
		float checkDist = footDist + (m_GroundCheckDistance > 0f ? m_GroundCheckDistance : 0.6f);
		int layerMask = m_GroundLayerMask.value == 0 ? ~0 : m_GroundLayerMask.value;

		if (m_EnableDebugDraw)
		{
			m_DebugRays.Clear();
		}

		bool foundGround = false;
		Vector3 detectedNormal = m_CurrentGroundNormal;
		Vector3 detectedPoint = transform.position - transform.up * footDist;

		Vector3 currentMoveDir = m_LastMoveDirection.sqrMagnitude > 0.001f
			? m_LastMoveDirection
			: transform.forward;

		// 1. 凹角検知: 進行方向の正面(胸〜腰の高さ)に立ち上がり壁があるかチェック
		Vector3 wallCheckOrigin = transform.position - transform.up * 0.3f;
		bool isWallHit = Physics.Raycast(wallCheckOrigin, currentMoveDir, out RaycastHit wallHit, 0.8f, layerMask, QueryTriggerInteraction.Ignore);
		if (m_EnableDebugDraw)
		{
			Debug.DrawRay(wallCheckOrigin, currentMoveDir * 0.8f, isWallHit ? Color.white : Color.gray, 0f);
		}
		if (isWallHit && Vector3.Dot(wallHit.normal, m_CurrentGroundNormal) < 0.8f)
		{
			foundGround = true;
			detectedNormal = wallHit.normal;
			detectedPoint = wallHit.point;
		}

		// 2. 通常足元検知: SphereCastにより角の縁や曲面でも確実に接地を捕捉
		if (!foundGround)
		{
			float sphereRadius = m_CapsuleCollider != null ? m_CapsuleCollider.radius * 0.85f : 0.4f;
			float castDist = Mathf.Max(0.1f, checkDist - sphereRadius);
			bool isDownHit = Physics.SphereCast(transform.position, sphereRadius, -transform.up, out RaycastHit downHit, castDist, layerMask, QueryTriggerInteraction.Ignore);
			if (m_EnableDebugDraw)
			{
				m_DebugSphereCastOrigin = transform.position;
				m_DebugSphereCastEnd = transform.position - transform.up * castDist;
				m_DebugSphereCastRadius = sphereRadius;
				m_DebugSphereCastHit = isDownHit;
				m_DebugSphereCastHitPoint = isDownHit ? downHit.point : Vector3.zero;
				Debug.DrawRay(transform.position, -transform.up * (sphereRadius + castDist), isDownHit ? Color.green : Color.red, 0f);
			}
			if (isDownHit)
			{
				foundGround = true;
				detectedNormal = downHit.normal;
				detectedPoint = downHit.point;
			}
		}

		// 3. 角・エッジ近傍マルチアングル検知: 足元真下が空中に抜けた際、近接する角の上面・側面・真横・背面を捉えて接地維持
		if (!foundGround && m_EnableEdgeGroundedAssist && m_CapsuleCollider != null)
		{
			Vector3 footOrigin = transform.position - transform.up * (footDist - 0.2f);
			Vector3 centerOrigin = transform.position;
			float edgeCheckDist = m_EdgeSearchRadius > 0f ? m_EdgeSearchRadius : 0.8f;

			// 加速・高速移動時でも通り過ぎた角を確実に捉えられるよう、背面探索距離を速度連動で拡張
			float currentSpeed = (m_GroundMoveSpeed > 0f ? m_GroundMoveSpeed : 10.0f) * m_SpeedMultiplier;
			float backCheckDist = Mathf.Max(edgeCheckDist, currentSpeed * Time.fixedDeltaTime * 3.0f);

			// 足元および腰(中心)からの多方向Ray(斜め下・周囲・真横・背面引き戻し)
			(Vector3 origin, Vector3 dir, float dist)[] checkRays = new (Vector3 origin, Vector3 dir, float dist)[]
			{
				// 足元から斜め下・周囲
				(footOrigin, (-transform.up + currentMoveDir).normalized, edgeCheckDist),
				(footOrigin, (-transform.up - currentMoveDir * 0.6f).normalized, edgeCheckDist),
				(footOrigin, (-transform.up + transform.right * 0.8f).normalized, edgeCheckDist),
				(footOrigin, (-transform.up - transform.right * 0.8f).normalized, edgeCheckDist),
				(footOrigin, -m_CurrentGroundNormal, edgeCheckDist),

				// 背面への引き戻し探索(加速・高速移動時の角飛び出し防止)
				(footOrigin, -currentMoveDir, backCheckDist),
				(footOrigin, (-currentMoveDir - transform.up * 0.5f).normalized, backCheckDist),

				// 腰・中心から水平四方(カス当たりした真横の壁面を捕捉)
				(centerOrigin, transform.right, edgeCheckDist),
				(centerOrigin, -transform.right, edgeCheckDist),
				(centerOrigin, currentMoveDir, edgeCheckDist),
				(centerOrigin, -currentMoveDir, backCheckDist),
				(centerOrigin, (transform.right - transform.up * 0.5f).normalized, edgeCheckDist),
				(centerOrigin, (-transform.right - transform.up * 0.5f).normalized, edgeCheckDist),
			};

			float closestDist = float.MaxValue;
			RaycastHit bestHit = default;
			bool foundEdge = false;

			for (int i = 0; i < checkRays.Length; i++)
			{
				Vector3 origin = checkRays[i].origin;
				Vector3 dir = checkRays[i].dir;
				float dist = checkRays[i].dist;
				bool isHit = Physics.Raycast(origin, dir, out RaycastHit hit, dist, layerMask, QueryTriggerInteraction.Ignore);

				if (m_EnableDebugDraw)
				{
					m_DebugRays.Add(new DebugRayInfo
					{
						Origin = origin,
						Direction = dir,
						Distance = dist,
						Hit = isHit,
						HitPoint = isHit ? hit.point : Vector3.zero
					});
				}

				if (isHit && hit.distance < closestDist)
				{
					closestDist = hit.distance;
					bestHit = hit;
					foundEdge = true;
				}
			}

			if (foundEdge)
			{
				foundGround = true;
				detectedNormal = bestHit.normal;
				detectedPoint = bestHit.point;
			}
		}

		// デバッグ表示: 現在の法線(緑線)と進行方向(青線)
		if (m_EnableDebugDraw)
		{
			Debug.DrawRay(transform.position, m_CurrentGroundNormal * 1.5f, Color.green, 0f);
			Debug.DrawRay(transform.position, currentMoveDir * 1.0f, Color.blue, 0f);
		}

		bool wasGrounded = m_IsGrounded;

		// 接地状態と目標法線の更新
		if (foundGround)
		{
			m_IsGrounded = true;
			m_LandingSnapTimer = 0f;

			// 足元理想位置と地面接触点との高低差(浮きギャップ)を計測
			Vector3 footPos = transform.position - transform.up * footDist;
			m_GroundGap = Vector3.Dot(footPos - detectedPoint, m_CurrentGroundNormal);

			// 角を乗り越える際(法線角度差大)は機敏に追従、通常歩行時は滑らかに追従
			float dot = Vector3.Dot(m_CurrentGroundNormal, detectedNormal);
			float normalAlignSpeed = dot < 0.7f ? 35.0f : 15.0f;
			m_CurrentGroundNormal = Vector3.Slerp(m_CurrentGroundNormal, detectedNormal, normalAlignSpeed * Time.fixedDeltaTime).normalized;
		}
		else
		{
			m_GroundGap = 0f;
			if (m_LandingSnapTimer > 0f)
			{
				// 着地直後の猶予バッファ: 接地を維持しつつ、直前の法線逆方向から真の床面をリカバリー探索
				m_LandingSnapTimer -= Time.fixedDeltaTime;
				m_IsGrounded = true;

				// 体が傾いていても真下の床面を再捕捉できるよう下向きに探索
				if (Physics.Raycast(transform.position, -m_CurrentGroundNormal, out RaycastHit recoveryHit, footDist + 0.8f, layerMask, QueryTriggerInteraction.Ignore))
				{
					float dot = Vector3.Dot(m_CurrentGroundNormal, recoveryHit.normal);
					float normalAlignSpeed = dot < 0.7f ? 25.0f : 15.0f;
					m_CurrentGroundNormal = Vector3.Slerp(m_CurrentGroundNormal, recoveryHit.normal, normalAlignSpeed * Time.fixedDeltaTime).normalized;
				}
			}
			else
			{
				m_IsGrounded = false;
				if (wasGrounded && m_EnableDebugDraw)
				{
					Debug.LogWarning($"[GravityMover-Debug] 接地ロスト発生: Pos={transform.position}, 前回の法線={m_CurrentGroundNormal}");
				}
			}
		}
	}

	private void HandleGroundMovement()
	{
		if (m_Rigidbody == null || m_IsFalling)
		{
			return;
		}

		if (!m_IsGrounded)
		{
			m_LastPlaneSpeed = 0f;
			// フェールセーフ: 未接地かつ非落下時、直前の歩行慣性で空中滑走しないよう速やかに減速(ドラッグ制動)
			float dragRate = m_GroundAcceleration > 0f ? m_GroundAcceleration * 2.5f : 30.0f;
			m_Rigidbody.linearVelocity = Vector3.MoveTowards(m_Rigidbody.linearVelocity, Vector3.zero, dragRate * Time.fixedDeltaTime);

			if (m_MoveInput.sqrMagnitude > 0.001f && m_EnableDebugDraw)
			{
				Debug.LogWarning($"[GravityMover-Debug] 接地ロスト中に入力検知(スタック疑い): Pos={transform.position}, Up={transform.up}, CurrentGroundNormal={m_CurrentGroundNormal}, Input={m_MoveInput}");
			}
			return;
		}

		Vector3 targetVelocity = Vector3.zero;
		Vector3 moveDir = Vector3.zero;

		if (m_MoveInput.sqrMagnitude > 0.001f && m_CameraTransform != null)
		{
			// 通常歩行時: カメラ基準のTPS移動
			Vector3 camForward = m_CameraTransform.forward;
			Vector3 camRight = m_CameraTransform.right;

			Vector3 forwardOnPlane = Vector3.ProjectOnPlane(camForward, m_CurrentGroundNormal);
			Vector3 rightOnPlane = Vector3.ProjectOnPlane(camRight, m_CurrentGroundNormal);

			// カメラの視線が壁の法線とほぼ重なって forwardOnPlane が消失した場合、直前の進行方向を接平面へ投影して前進を維持
			if (forwardOnPlane.sqrMagnitude < 0.01f && m_LastMoveDirection.sqrMagnitude > 0.01f)
			{
				forwardOnPlane = Vector3.ProjectOnPlane(m_LastMoveDirection, m_CurrentGroundNormal);
			}

			if (forwardOnPlane.sqrMagnitude > 0.0001f)
			{
				forwardOnPlane.Normalize();
			}
			if (rightOnPlane.sqrMagnitude > 0.0001f)
			{
				rightOnPlane.Normalize();
			}

			moveDir = (forwardOnPlane * m_MoveInput.y + rightOnPlane * m_MoveInput.x).normalized;
			if (moveDir.sqrMagnitude > 0.001f)
			{
				m_LastMoveDirection = moveDir;
			}

			float speed = m_GroundMoveSpeed > 0f ? m_GroundMoveSpeed : 10.0f;
			targetVelocity = moveDir * (speed * m_SpeedMultiplier);
		}

		// 姿勢回転の一本化: ロール(斜めの傾き)のない垂直90度姿勢を適用
		ApplyGroundRotation(moveDir);

		// 現在のRigidbody速度を接平面成分と法線成分に分解
		Vector3 currentVel = m_Rigidbody.linearVelocity;
		Vector3 normalVel = Vector3.Project(currentVel, m_CurrentGroundNormal);
		Vector3 planeVel = currentVel - normalVel;

		// スピード維持の運動量転送(角・エッジを跨いだ際、直前の走破スピードの数値をそのまま新面の進行方向へ転送し、失速と空中投げ出しを完全防止)
		if (m_MoveInput.sqrMagnitude > 0.001f && moveDir.sqrMagnitude > 0.001f)
		{
			float targetSpeed = (m_GroundMoveSpeed > 0f ? m_GroundMoveSpeed : 10.0f) * m_SpeedMultiplier;
			float currentForwardSpeed = Vector3.Dot(planeVel, moveDir);

			// 角で法線が急変した際、直前の勢い(スピード)を新面の進行方向へ直接転送して100%のスピードを維持
			if (currentForwardSpeed < targetSpeed * 0.85f && m_LastPlaneSpeed > targetSpeed * 0.5f)
			{
				planeVel = moveDir * targetSpeed;
			}
		}

		// 接平面上の速度を目標速度へ加減速補間
		float accel = m_GroundAcceleration > 0f ? m_GroundAcceleration : 15.0f;
		Vector3 newPlaneVel = Vector3.MoveTowards(planeVel, targetVelocity, accel * Time.fixedDeltaTime);
		m_LastPlaneSpeed = newPlaneVel.magnitude;

		// 曲面・角部回り込み時の求心吸着スナップ(浮き上がりを瞬時に相殺して角へ吸着)
		float snapRate = 25.0f;
		float snapSpeed = Mathf.Clamp(m_GroundGap * snapRate, -3.0f, 15.0f);
		Vector3 snapVelocity = -m_CurrentGroundNormal * snapSpeed;

		// 地面への安定吸着力(ダウンフォース)の適用
		float stickForce = m_GroundStickForce > 0f ? m_GroundStickForce : 15.0f;
		Vector3 downforce = -m_CurrentGroundNormal * stickForce;

		m_Rigidbody.linearVelocity = newPlaneVel + snapVelocity + downforce * Time.fixedDeltaTime;
	}

	private void ApplyGroundRotation(Vector3 moveDir)
	{
		if (m_AlignTween != null && m_AlignTween.IsActive() && m_AlignTween.IsPlaying())
		{
			return;
		}

		Vector3 targetForward;

		if (moveDir.sqrMagnitude > 0.001f)
		{
			targetForward = moveDir;
		}
		else
		{
			// 静止時は現在の正面向きを接平面上に投影
			targetForward = Vector3.ProjectOnPlane(transform.forward, m_CurrentGroundNormal);
			if (targetForward.sqrMagnitude < 0.001f)
			{
				targetForward = Vector3.ProjectOnPlane(-transform.up, m_CurrentGroundNormal);
			}
		}

		if (targetForward.sqrMagnitude > 0.0001f && m_CurrentGroundNormal.sqrMagnitude > 0.0001f)
		{
			targetForward.Normalize();
			Quaternion targetRotation = Quaternion.LookRotation(targetForward, m_CurrentGroundNormal);
			float alignSpeed = m_SurfaceAlignSpeed > 0f ? m_SurfaceAlignSpeed : 10.0f;
			Quaternion newRotation = Quaternion.Slerp(m_Rigidbody.rotation, targetRotation, alignSpeed * Time.fixedDeltaTime);
			m_Rigidbody.MoveRotation(newRotation);
		}
	}

	/// <summary>
	/// コライダー寸法に基づき、オブジェクト中心から足元までの距離を取得します。
	/// </summary>
	private float GetFootOffset()
	{
		if (m_CapsuleCollider != null)
		{
			return m_CapsuleCollider.height * 0.5f;
		}
		return 1.0f;
	}

	private void OnCollisionEnter(Collision collision)
	{
		// ネットワーク同期下では所有者(IsOwner)のみ判定
		if (IsSpawned && !IsOwner)
		{
			return;
		}

		// 落下移動中のみ衝突着地処理を実行(歩行中の壁接触による誤着地防止)
		if (m_IsFalling && collision.contactCount > 0)
		{
			ContactPoint contact = collision.GetContact(0);
			Vector3 hitNormal = contact.normal;

			if (m_EnableDebugDraw)
			{
				Debug.Log($"[GravityMover-Debug] 着地検出: hitNormal={hitNormal}, 目標Normal={m_FallingTargetNormal}, hasTargetNormal={m_HasFallingTargetNormal}, 接触物体={collision.gameObject.name}, contactPoint={contact.point}, playerPos={transform.position}");
			}

			// 落下を停止してホバリング状態へ遷移(コライダーサイズも復元)
			StopFalling();

			Vector3 finalGroundNormal = hitNormal;
			Vector3 snapPoint = contact.point;

			// 1. 目標面近接判定(カス当たり救済): 接触点周囲に本来の目標面が存在する場合は目標面を採用
			if (m_HasFallingTargetNormal)
			{
				float dot = Vector3.Dot(hitNormal, m_FallingTargetNormal);
				if (dot < 0.6f)
				{
					int layerMask = m_GroundLayerMask.value == 0 ? ~0 : m_GroundLayerMask.value;
					Vector3 checkOrigin = contact.point + m_FallingTargetNormal * 0.3f;
					if (Physics.Raycast(checkOrigin, -m_FallingTargetNormal, out RaycastHit targetHit, 0.8f, layerMask, QueryTriggerInteraction.Ignore))
					{
						if (Vector3.Dot(targetHit.normal, m_FallingTargetNormal) > 0.7f)
						{
							finalGroundNormal = targetHit.normal;
							snapPoint = targetHit.point;
							if (m_EnableDebugDraw)
							{
								Debug.LogWarning($"[GravityMover-Debug] カス当たり補正: hitNormal={hitNormal}を補正し、近接目標面={finalGroundNormal}へ着地");
							}
						}
					}
				}
			}

			// 2. 移動中の回転姿勢を着地面法線へ完全に合わせる
			Vector3 targetForward =
				Vector3.ProjectOnPlane(
					transform.forward,
					finalGroundNormal
				);

			if (targetForward.sqrMagnitude < 0.001f)
			{
				targetForward =
					Vector3.ProjectOnPlane(
						m_CurrentDirection,
						finalGroundNormal
					);
			}

			if (targetForward.sqrMagnitude > 0.001f)
			{
				targetForward.Normalize();

				Quaternion targetRotation =
					Quaternion.LookRotation(
						targetForward,
						finalGroundNormal
					);
				m_Rigidbody.MoveRotation(targetRotation);
			}

			// 3. 3D全方位足元サーフェススナップ: 接触面直上にカプセルの底面を密着配置し、空中に浮くスタックを物理的に根絶
			Vector3 targetPos = snapPoint + finalGroundNormal * (GetFootOffset() + 0.02f);
			m_Rigidbody.position = targetPos;
			m_Rigidbody.linearVelocity = Vector3.zero;
			m_GroundGap = 0f;

			// 落下中の着地法線情報をクリア
			m_HasFallingTargetNormal = false;

			m_CurrentGroundNormal = finalGroundNormal;
			m_IsGrounded = true;
			m_LandingSnapTimer = 0.25f;
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

	// 追加
	private void UpdateFallingRotation()
	{
		if (!m_HasFallingTargetNormal)
			return;

		// 着地面に沿うように、移動方向を補正する
		Vector3 targetForward = Vector3.ProjectOnPlane(
			m_CurrentDirection,
			m_FallingTargetNormal
		);

		// 移動方向が着地面とほぼ平行でない場合の補助処理
		if (targetForward.sqrMagnitude < 0.001f)
		{
			targetForward = Vector3.ProjectOnPlane(
				transform.forward,
				m_FallingTargetNormal
			);
		}

		if (targetForward.sqrMagnitude < 0.001f)
			return;

		targetForward.Normalize();

		// 着地面の法線に合わせた目標回転
		Quaternion targetRotation = Quaternion.LookRotation(
			targetForward,
			m_FallingTargetNormal
		);

		// 回転速度
		float speed = m_FallingRotationSpeed > 0f
			? m_FallingRotationSpeed
			: 4.0f;

		// Rigidbodyを使って徐々に回転させる
		m_Rigidbody.MoveRotation(
			Quaternion.Slerp(
				m_Rigidbody.rotation,
				targetRotation,
				speed * Time.fixedDeltaTime
			)
		);
	}


	public override void OnDestroy()
	{
		m_AlignTween?.Kill();
		base.OnDestroy();
	}

#if UNITY_EDITOR
	private void OnDrawGizmos()
	{
		if (!m_EnableDebugDraw)
		{
			return;
		}

		// 1. 接地面法線(緑)と前進方向(青)
		Gizmos.color = Color.green;
		Gizmos.DrawRay(transform.position, m_CurrentGroundNormal * 1.5f);
		Gizmos.color = Color.blue;
		Gizmos.DrawRay(transform.position, m_LastMoveDirection * 1.0f);

		// 2. 足元 SphereCast の可視化(始点球と終点球を外周4本線で結んだカプセル形状)
		if (m_DebugSphereCastRadius > 0f)
		{
			Gizmos.color = m_DebugSphereCastHit ? new Color(0f, 1f, 0f, 0.5f) : new Color(1f, 0f, 0f, 0.4f);
			Gizmos.DrawWireSphere(m_DebugSphereCastOrigin, m_DebugSphereCastRadius);
			Gizmos.DrawWireSphere(m_DebugSphereCastEnd, m_DebugSphereCastRadius);

			// 外周4本の母線を描画してカプセル形状を表現
			Vector3 rOffset = transform.right * m_DebugSphereCastRadius;
			Vector3 fOffset = transform.forward * m_DebugSphereCastRadius;
			Gizmos.DrawLine(m_DebugSphereCastOrigin + rOffset, m_DebugSphereCastEnd + rOffset);
			Gizmos.DrawLine(m_DebugSphereCastOrigin - rOffset, m_DebugSphereCastEnd - rOffset);
			Gizmos.DrawLine(m_DebugSphereCastOrigin + fOffset, m_DebugSphereCastEnd + fOffset);
			Gizmos.DrawLine(m_DebugSphereCastOrigin - fOffset, m_DebugSphereCastEnd - fOffset);

			if (m_DebugSphereCastHit)
			{
				Gizmos.color = Color.cyan;
				Gizmos.DrawSphere(m_DebugSphereCastHitPoint, 0.08f);
			}
		}

		// 3. マルチアングル近接探索 Ray の可視化(ヒットは黄色線+マゼンタ球、未ヒットは半透明黄線)
		for (int i = 0; i < m_DebugRays.Count; i++)
		{
			DebugRayInfo ray = m_DebugRays[i];
			if (ray.Hit)
			{
				Gizmos.color = Color.yellow;
				Gizmos.DrawLine(ray.Origin, ray.HitPoint);
				Gizmos.color = Color.magenta;
				Gizmos.DrawSphere(ray.HitPoint, 0.06f);
			}
			else
			{
				Gizmos.color = new Color(1f, 0.92f, 0.016f, 0.2f);
				Gizmos.DrawLine(ray.Origin, ray.Origin + ray.Direction * ray.Distance);
			}
		}
	}
#endif
}


