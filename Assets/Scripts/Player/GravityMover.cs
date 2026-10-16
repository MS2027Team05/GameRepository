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

		bool foundGround = false;
		Vector3 detectedNormal = m_CurrentGroundNormal;

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
		}

		// 2. 通常足元検知: SphereCastにより角の縁や曲面でも確実に接地を捕捉
		if (!foundGround)
		{
			float sphereRadius = m_CapsuleCollider != null ? m_CapsuleCollider.radius * 0.85f : 0.4f;
			float castDist = Mathf.Max(0.1f, checkDist - sphereRadius);
			bool isDownHit = Physics.SphereCast(transform.position, sphereRadius, -transform.up, out RaycastHit downHit, castDist, layerMask, QueryTriggerInteraction.Ignore);
			if (m_EnableDebugDraw)
			{
				Debug.DrawRay(transform.position, -transform.up * (sphereRadius + castDist), isDownHit ? Color.green : Color.red, 0f);
			}
			if (isDownHit)
			{
				foundGround = true;
				detectedNormal = downHit.normal;
			}
		}

		// 3. 角・エッジ近傍マルチアングル検知: 足元真下が空中に抜けた瞬間、近接する角の上面や側面を捉えて接地を維持
		if (!foundGround && m_EnableEdgeGroundedAssist && m_CapsuleCollider != null)
		{
			Vector3 footOrigin = transform.position - transform.up * (footDist - 0.2f);
			float edgeCheckDist = m_EdgeSearchRadius > 0f ? m_EdgeSearchRadius : 0.8f;

			Vector3[] edgeCheckDirections = new Vector3[]
			{
				// 進行方向の斜め下(これから進む側面や下りエッジ)
				(-transform.up + currentMoveDir).normalized,
				// 進行方向の斜め手前(踏み外した直前の上面・エッジ)
				(-transform.up - currentMoveDir * 0.6f).normalized,
				// 直前の接地面の逆方向(上面)
				-m_CurrentGroundNormal,
				// 進行方向の真後ろ(通り過ぎた側面)
				-currentMoveDir,
				// 進行方向の左右斜め下(斜め進入時のエッジ捕捉)
				(-transform.up + transform.right * 0.7f).normalized,
				(-transform.up - transform.right * 0.7f).normalized,
			};

			float closestDist = float.MaxValue;
			RaycastHit bestHit = default;
			bool foundEdge = false;

			for (int i = 0; i < edgeCheckDirections.Length; i++)
			{
				Vector3 dir = edgeCheckDirections[i];
				if (Physics.Raycast(footOrigin, dir, out RaycastHit hit, edgeCheckDist, layerMask, QueryTriggerInteraction.Ignore))
				{
					if (m_EnableDebugDraw)
					{
						Debug.DrawLine(footOrigin, hit.point, Color.yellow, 0f);
					}

					if (hit.distance < closestDist)
					{
						closestDist = hit.distance;
						bestHit = hit;
						foundEdge = true;
					}
				}
			}

			if (foundEdge)
			{
				foundGround = true;
				detectedNormal = bestHit.normal;
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
			// 角を乗り越える際(法線角度差大)は機敏に追従、通常歩行時は滑らかに追従
			float dot = Vector3.Dot(m_CurrentGroundNormal, detectedNormal);
			float normalAlignSpeed = dot < 0.7f ? 25.0f : 15.0f;
			m_CurrentGroundNormal = Vector3.Slerp(m_CurrentGroundNormal, detectedNormal, normalAlignSpeed * Time.fixedDeltaTime).normalized;
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

	private void HandleGroundMovement()
	{
		if (m_Rigidbody == null || m_IsFalling)
		{
			return;
		}

		if (!m_IsGrounded)
		{
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

			if (forwardOnPlane.sqrMagnitude > 0.0001f)
			{
				forwardOnPlane.Normalize();
			}
			if (rightOnPlane.sqrMagnitude > 0.0001f)
			{
				rightOnPlane.Normalize();
			}

			moveDir = (forwardOnPlane * m_MoveInput.y + rightOnPlane * m_MoveInput.x).normalized;
			m_LastMoveDirection = moveDir;

			float speed = m_GroundMoveSpeed > 0f ? m_GroundMoveSpeed : 10.0f;
			targetVelocity = moveDir * (speed * m_SpeedMultiplier);
		}

		// 姿勢回転の一本化: ロール(斜めの傾き)のない垂直90度姿勢を適用
		ApplyGroundRotation(moveDir);

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

			// 移動中の回転でほぼ着地姿勢になっているが、
			// 最後に衝突面の法線へ完全に合わせる
			Vector3 targetForward =
				Vector3.ProjectOnPlane(
					transform.forward,
					hitNormal
				);

			if (targetForward.sqrMagnitude < 0.001f)
			{
				targetForward =
					Vector3.ProjectOnPlane(
						m_CurrentDirection,
						hitNormal
					);
			}

			if (targetForward.sqrMagnitude > 0.001f)
			{
				targetForward.Normalize();

				Quaternion targetRotation =
					Quaternion.LookRotation(
						targetForward,
						hitNormal
					);
				m_Rigidbody.MoveRotation(targetRotation);
			}

			// 落下中の着地法線情報をクリア
			m_HasFallingTargetNormal = false;

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
}


