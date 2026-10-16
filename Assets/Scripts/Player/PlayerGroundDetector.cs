using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// プレイヤー足元の地面・曲面・エッジ(角)の物理検知および法線・高低差の計測を担当するコンポーネント。
/// </summary>
[RequireComponent(typeof(CapsuleCollider))]
public class PlayerGroundDetector : MonoBehaviour, IGroundDetector
{
	[Header("接地検知パラメータ")]
	// 想定設定値: 1.5f (足元の地面法線を検知するRaycast/SphereCast距離)
	[SerializeField] private float m_GroundCheckDistance;

	// 想定設定値: Layer 7 (Ground等のマスク、未指定時は全レイヤー対象)
	[SerializeField] private LayerMask m_GroundLayerMask;

	[Header("エッジ(角)接地アシスト設定")]
	// 想定設定値: true (足元キャストが外れても近傍の角や面を捉えて接地維持)
	[SerializeField] private bool m_EnableEdgeGroundedAssist;

	// 想定設定値: 0.6f (足元外れ時に近傍の角や面を探索するサーチ半径)
	[SerializeField] private float m_EdgeSearchRadius;

	[Header("デバッグ可視化設定")]
	// 想定設定値: true (SceneビューおよびGameビューでのデバッグGizmo表示)
	[SerializeField] private bool m_EnableDebugDraw;

	private CapsuleCollider m_CapsuleCollider;
	private bool m_IsGrounded;
	private Vector3 m_CurrentGroundNormal = Vector3.up;
	private float m_GroundGap;
	private float m_LandingSnapTimer;

	private struct DebugRayInfo
	{
		public Vector3 Origin;
		public Vector3 Direction;
		public float Distance;
		public bool Hit;
		public Vector3 HitPoint;
	}

	private readonly List<DebugRayInfo> m_DebugRays = new List<DebugRayInfo>();
	private Vector3 m_DebugSphereCastOrigin;
	private Vector3 m_DebugSphereCastEnd;
	private float m_DebugSphereCastRadius;
	private bool m_DebugSphereCastHit;
	private Vector3 m_DebugSphereCastHitPoint;

	/// <summary>
	/// 地面または壁面に接地しているかを取得します。
	/// </summary>
	public bool IsGrounded => m_IsGrounded;

	/// <summary>
	/// 現在の接地面法線ベクトルを取得します。
	/// </summary>
	public Vector3 CurrentGroundNormal => m_CurrentGroundNormal;

	/// <summary>
	/// 足元理想位置と地面接触点との高低差(浮きギャップ)を取得します。
	/// </summary>
	public float GroundGap => m_GroundGap;

	private void Awake()
	{
		m_CapsuleCollider = GetComponent<CapsuleCollider>();
	}

	/// <summary>
	/// 着地完了時に新しい法線を外部から直接セットし、着地猶予バッファを開始します。
	/// </summary>
	/// <param name="groundNormal">新しい接地面法線</param>
	public void SetLandedNormal(Vector3 groundNormal)
	{
		m_CurrentGroundNormal = groundNormal;
		m_IsGrounded = true;
		m_LandingSnapTimer = 0.25f;
		m_GroundGap = 0f;
	}

	/// <summary>
	/// 指定した移動方向・速度に基づいて足元・角・壁面の接地状態を更新します。
	/// </summary>
	/// <param name="moveDir">現在の進行方向ベクトル</param>
	/// <param name="speed">現在の移動速度</param>
	public void DetectGround(Vector3 moveDir, float speed)
	{
		float footDist = m_CapsuleCollider != null ? m_CapsuleCollider.height * 0.5f : 1.0f;
		float checkDist = footDist + (m_GroundCheckDistance > 0f ? m_GroundCheckDistance : 1.5f);
		int layerMask = m_GroundLayerMask.value == 0 ? ~0 : m_GroundLayerMask.value;

		if (m_EnableDebugDraw)
		{
			m_DebugRays.Clear();
		}

		bool foundGround = false;
		Vector3 detectedNormal = m_CurrentGroundNormal;
		Vector3 detectedPoint = transform.position - transform.up * footDist;

		Vector3 currentMoveDir = moveDir.sqrMagnitude > 0.001f ? moveDir : transform.forward;

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
			float edgeCheckDist = m_EdgeSearchRadius > 0f ? m_EdgeSearchRadius : 0.6f;

			// 加速・高速移動時でも通り過ぎた角を確実に捉えられるよう、背面探索距離を速度連動で拡張
			float backCheckDist = Mathf.Max(edgeCheckDist, speed * Time.fixedDeltaTime * 3.0f);

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
			}
		}
	}

#if UNITY_EDITOR
	private void OnDrawGizmos()
	{
		if (!m_EnableDebugDraw)
		{
			return;
		}

		// 1. 接地面法線(緑)
		Gizmos.color = Color.green;
		Gizmos.DrawRay(transform.position, m_CurrentGroundNormal * 1.5f);

		// 2. 足元 SphereCast の可視化(カプセル形状)
		if (m_DebugSphereCastRadius > 0f)
		{
			Gizmos.color = m_DebugSphereCastHit ? new Color(0f, 1f, 0f, 0.5f) : new Color(1f, 0f, 0f, 0.4f);
			Gizmos.DrawWireSphere(m_DebugSphereCastOrigin, m_DebugSphereCastRadius);
			Gizmos.DrawWireSphere(m_DebugSphereCastEnd, m_DebugSphereCastRadius);

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

		// 3. マルチアングル近接探索 Ray の可視化
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
