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

	[Header("エッジ・角部回り込み設定")]
	// 想定設定値: 0.6f (進行方向へのエッジ先読み距離)
	[SerializeField] private float m_EdgeAheadDistance;

	// 想定設定値: 1.2f (エッジ外側から側面を検知する折り返し距離)
	[SerializeField] private float m_EdgeWrapCheckDistance;

	// 想定設定値: 25.0f (エッジ回り込み時の姿勢追従速度)
	[SerializeField] private float m_EdgeAlignSpeed;

	[Header("デバッグ可視化設定")]
	[SerializeField] private bool m_EnableDebugDraw = true;

	// 想定設定値: Groundレイヤー等のマスク(未指定時は全レイヤー対象)
	[SerializeField] private LayerMask m_GroundLayerMask;

	private Rigidbody m_Rigidbody;
	private CapsuleCollider m_CapsuleCollider;
	private Vector3 m_CurrentDirection;
	private float m_SpeedMultiplier = 1.0f;
	private bool m_IsFalling;
	private bool m_IsGrounded;
	private Tween m_AlignTween;

	private Vector2 m_MoveInput;
	private Transform m_CameraTransform;
	private Vector3 m_CurrentGroundNormal = Vector3.up;
	private Vector3 m_LastMoveDirection = Vector3.forward;
	private Vector3 m_EdgeStartForward = Vector3.forward;
	private Vector3 m_EdgeTargetForward = Vector3.forward;
	private Vector3 m_EdgeStartNormal = Vector3.up;
	private Vector3 m_EdgeTargetNormal = Vector3.up;
	private float m_EdgeTransitionDuration = 0.35f;
	private float m_EdgeTransitionTimer;

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
	/// エッジ(角)回り込み遷移を開始します。
	/// </summary>
	/// <param name="targetNormal">移行先の新面の法線</param>
	private void StartEdgeTransition(Vector3 targetNormal)
	{
		Quaternion edgeRot = Quaternion.FromToRotation(m_CurrentGroundNormal, targetNormal);
		m_EdgeStartForward = transform.forward;
		m_EdgeTargetForward = (edgeRot * transform.forward).normalized;
		m_EdgeStartNormal = m_CurrentGroundNormal;
		m_EdgeTargetNormal = targetNormal;
		m_EdgeTransitionDuration = 0.35f;
		m_EdgeTransitionTimer = m_EdgeTransitionDuration;
	}

	/// <summary>
	/// マルチレイによる曲面・エッジ(凸角)・壁(凹角)の法線検知および円弧回り込み制御を行います。
	/// </summary>
	private void UpdateGroundStatus()
	{
		if (m_Rigidbody == null || m_IsFalling)
		{
			return;
		}

		// エッジ回り込み遷移中の処理
		if (m_EdgeTransitionTimer > 0f)
		{
			m_EdgeTransitionTimer -= Time.fixedDeltaTime;
			float t = Mathf.Clamp01(1.0f - (m_EdgeTransitionTimer / m_EdgeTransitionDuration));

			// 法線を円弧補間(Slerp)して角への滑らかな吸着向心力を生成
			m_CurrentGroundNormal = Vector3.Slerp(m_EdgeStartNormal, m_EdgeTargetNormal, t).normalized;
			m_IsGrounded = true;
			return;
		}

		float checkDist = m_GroundCheckDistance > 0f ? m_GroundCheckDistance : 1.5f;
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
			StartEdgeTransition(wallHit.normal);
		}

		// 2. 凸角検知: 移動中に足元の面が途切れるエッジ(角の縁)に到達した場合の死角なし多方向検知
		if (!foundGround && m_MoveInput.sqrMagnitude > 0.001f)
		{
			float aheadDist = m_EdgeAheadDistance > 0f ? m_EdgeAheadDistance : 0.6f;
			float wrapDist = m_EdgeWrapCheckDistance > 0f ? m_EdgeWrapCheckDistance : 1.2f;

			// A. 斜め前下(45度) Raycast: 角の頂点・側面の切り替わりを死角なく捕捉
			Vector3 diagDir = (currentMoveDir - transform.up).normalized;
			bool isDiagHit = Physics.Raycast(transform.position, diagDir, out RaycastHit diagHit, checkDist * 1.4f, layerMask, QueryTriggerInteraction.Ignore);
			if (m_EnableDebugDraw)
		{
				Debug.DrawRay(transform.position, diagDir * (checkDist * 1.4f), isDiagHit ? Color.magenta : Color.gray, 0f);
			}

			if (isDiagHit && Vector3.Dot(diagHit.normal, m_CurrentGroundNormal) < 0.7f)
			{
				foundGround = true;
				detectedNormal = diagHit.normal;
				StartEdgeTransition(diagHit.normal);
			}
			else
			{
				// B. 先読みチェック
				Vector3 aheadOrigin = transform.position + currentMoveDir * aheadDist;
				bool hasAheadGround = Physics.Raycast(aheadOrigin, -transform.up, out RaycastHit _, checkDist, layerMask, QueryTriggerInteraction.Ignore);
				if (m_EnableDebugDraw)
				{
					Debug.DrawRay(aheadOrigin, -transform.up * checkDist, hasAheadGround ? Color.cyan : Color.red, 0f);
				}

				if (!hasAheadGround)
				{
					// 足元より確実に下(-transform.up * 1.2f)から内側へ折り返しRaycastを照射
					Vector3 wrapOrigin = transform.position + currentMoveDir * aheadDist - transform.up * 1.2f;
					Vector3 wrapDirection = -currentMoveDir;

					bool isWrapHit = Physics.Raycast(wrapOrigin, wrapDirection, out RaycastHit wrapHit, wrapDist, layerMask, QueryTriggerInteraction.Ignore);
					if (m_EnableDebugDraw)
					{
						Debug.DrawRay(wrapOrigin, wrapDirection * wrapDist, isWrapHit ? Color.yellow : Color.blue, 0f);
					}
					if (isWrapHit && Vector3.Dot(wrapHit.normal, m_CurrentGroundNormal) < 0.8f)
					{
						foundGround = true;
						detectedNormal = wrapHit.normal;
						StartEdgeTransition(wrapHit.normal);
					}
				}
			}
		}

		// 3. 通常足元検知: キャラクター直下(-transform.up)へRaycast
		if (!foundGround)
		{
			bool isDownHit = Physics.Raycast(transform.position, -transform.up, out RaycastHit downHit, checkDist, layerMask, QueryTriggerInteraction.Ignore);
			if (m_EnableDebugDraw)
			{
				Debug.DrawRay(transform.position, -transform.up * checkDist, isDownHit ? Color.green : Color.red, 0f);
			}
			if (isDownHit)
			{
				foundGround = true;
				detectedNormal = downHit.normal;
			}
		}

		// デバッグ表示: 現在の法線(緑線)と進行方向(青線)
		if (m_EnableDebugDraw)
		{
			Debug.DrawRay(transform.position, m_CurrentGroundNormal * 1.5f, Color.green, 0f);
			Debug.DrawRay(transform.position, currentMoveDir * 1.0f, Color.blue, 0f);
		}

		// 接地状態と目標法線の更新
		if (foundGround)
		{
			m_IsGrounded = true;
			m_CurrentGroundNormal = detectedNormal;
		}
	}

	/// <summary>
	/// 接平面上の移動・吸着力・一本化された姿勢回転を適用します。
	/// </summary>
	private void HandleGroundMovement()
	{
		if (m_Rigidbody == null || !m_IsGrounded || m_IsFalling)
		{
			return;
		}

		Vector3 targetVelocity = Vector3.zero;
		Vector3 moveDir = Vector3.zero;

		// エッジ通過中は円弧軌道で角の外側を包むように回り込む速度ベクトルを適用
		if (m_EdgeTransitionTimer > 0f)
		{
			float t = Mathf.Clamp01(1.0f - (m_EdgeTransitionTimer / m_EdgeTransitionDuration));
			moveDir = Vector3.Slerp(m_EdgeStartForward, m_EdgeTargetForward, t).normalized;
			m_LastMoveDirection = moveDir;

			float speed = m_GroundMoveSpeed > 0f ? m_GroundMoveSpeed : 10.0f;
			targetVelocity = moveDir * (speed * m_SpeedMultiplier);
		}
		else if (m_MoveInput.sqrMagnitude > 0.001f && m_CameraTransform != null)
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

			// エッジ回り込み中は素早く旋回し、通常歩行中は自然に追従
			float alignSpeed = (m_EdgeTransitionTimer > 0f)
				? (m_EdgeAlignSpeed > 0f ? m_EdgeAlignSpeed : 25.0f)
				: (m_SurfaceAlignSpeed > 0f ? m_SurfaceAlignSpeed : 10.0f);

			Quaternion newRotation = Quaternion.Slerp(transform.rotation, targetRotation, alignSpeed * Time.fixedDeltaTime);

			if (m_EdgeTransitionTimer > 0f && m_Rigidbody != null)
			{
				// 足元ピボット回転補正:
				// コライダーの寸法(高さ)を考慮し、足元の接地点を中心に外側へ円弧を描いて腰を持ち上げる
				// これにより、カプセル下部が角や床の内部へめり込んで物理衝突でロックされるのを完全に防止
				float footOffset = GetFootOffset();
				Vector3 currentFootPos = transform.position - transform.up * footOffset;
				Vector3 newUp = newRotation * Vector3.up;
				Vector3 adjustedCenterPos = currentFootPos + newUp * footOffset;

				transform.rotation = newRotation;
				m_Rigidbody.position = adjustedCenterPos;
			}
			else
			{
				transform.rotation = newRotation;
			}
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
