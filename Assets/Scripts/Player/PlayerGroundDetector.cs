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
	private Vector3 m_TargetGroundNormal = Vector3.up;
	private float m_NormalHysteresisTimer;
	private float m_GroundGap;
	private float m_LandingSnapTimer;

	public struct DebugRayInfo
	{
		public Vector3 Origin { get; set; }
		public Vector3 Direction { get; set; }
		public float Distance { get; set; }
		public bool Hit { get; set; }
		public Vector3 HitPoint { get; set; }
		public Vector3 HitNormal { get; set; }
	}

	private readonly List<DebugRayInfo> m_DebugRays = new List<DebugRayInfo>();
	private readonly Collider[] m_OverlapColliderBuffer = new Collider[16];
	private Vector3 m_DebugSphereCastOrigin;
	private Vector3 m_DebugSphereCastEnd;
	private float m_DebugSphereCastRadius;
	private bool m_DebugSphereCastHit;
	private Vector3 m_DebugSphereCastHitPoint;
	private Vector3 m_LastDetectedNormal = Vector3.up;
	private Vector3 m_LastDetectedPoint;
	private int m_LastDetectionStep; // 0:なし, 1:凹角壁, 2:真下SphereCast, 3:角近傍幾何学的面検知

	/// <summary>
	/// 地面または壁面に接地しているかを取得します。
	/// </summary>
	public bool IsGrounded { get => m_IsGrounded; }

	/// <summary>
	/// 現在の接地面法線ベクトルを取得します。
	/// </summary>
	public Vector3 CurrentGroundNormal { get => m_CurrentGroundNormal; }

	/// <summary>
	/// 足元理想位置と地面接触点との高低差(浮きギャップ)を取得します。
	/// </summary>
	public float GroundGap { get => m_GroundGap; }

	/// <summary>
	/// 診断・検証用: 直近フレームで実行されたRaycast一覧を取得します。
	/// </summary>
	public IReadOnlyList<DebugRayInfo> DebugRays { get => m_DebugRays; }

	/// <summary>
	/// 診断・検証用: 直近フレームで接地を検出した判定ステップ(0:なし, 1:凹角壁, 2:足元SphereCast, 3:角近傍Ray)を取得します。
	/// </summary>
	public int LastDetectionStep { get => m_LastDetectionStep; }

	/// <summary>
	/// 診断・検証用: 直近フレームで検出された面の法線を取得します。
	/// </summary>
	public Vector3 LastDetectedNormal { get => m_LastDetectedNormal; }

	/// <summary>
	/// 診断・検証用: 直近フレームで検出された面の接触点を取得します。
	/// </summary>
	public Vector3 LastDetectedPoint { get => m_LastDetectedPoint; }

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

		m_DebugRays.Clear();

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
			m_LastDetectionStep = 1;
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
				m_LastDetectionStep = 2;
			}
		}

		// 3. 角・エッジ近傍の幾何学的最近接面検知 (ClosestPoint方式)
		// 足元が空中の場合、周囲のGroundコライダーから足元までの最短表面座標を算出して接地を維持する
		// 接地面の取得にRaycastを廃止しClosestPoint(GJK/EPA)を採用して信頼できるように
		if (!foundGround && m_EnableEdgeGroundedAssist && m_CapsuleCollider != null)
		{
			Vector3 footBottom = transform.position - transform.up * footDist;
			float searchRadius = m_EdgeSearchRadius;

			int hitCount = Physics.OverlapSphereNonAlloc(footBottom, searchRadius, m_OverlapColliderBuffer, layerMask, QueryTriggerInteraction.Ignore);

			float closestDist = float.MaxValue;
			Collider bestCollider = null;
			Vector3 bestClosestPoint = Vector3.zero;

			for (int i = 0; i < hitCount; i++)
			{
				Collider col = m_OverlapColliderBuffer[i];
				if (col == null || col == m_CapsuleCollider || col.gameObject == gameObject)
				{
					continue;
				}

				Vector3 closest = col.ClosestPoint(footBottom);
				float dist = Vector3.Distance(footBottom, closest);
				if (dist < closestDist)
				{
					closestDist = dist;
					bestCollider = col;
					bestClosestPoint = closest;
				}
			}

			if (bestCollider != null && closestDist <= searchRadius)
			{
				Vector3 toFoot = footBottom - bestClosestPoint;
				Vector3 surfaceNormal = Vector3.up;

				// 最近接点の正確な面法線を取得 (外側から微小Raycastを照射してポリゴン面法線を特定)
				if (toFoot.sqrMagnitude > 0.0001f)
				{
					Vector3 rayDir = -toFoot.normalized;
					Vector3 rayOrigin = bestClosestPoint + toFoot.normalized * 0.15f;
					if (bestCollider.Raycast(new Ray(rayOrigin, rayDir), out RaycastHit surfaceHit, 0.35f))
					{
						surfaceNormal = surfaceHit.normal;
					}
					else
					{
						surfaceNormal = toFoot.normalized;
					}
				}
				else
				{
					surfaceNormal = bestCollider.transform.up;
				}

				foundGround = true;
				detectedNormal = surfaceNormal;
				detectedPoint = bestClosestPoint;
				m_LastDetectionStep = 3;

				// 診断およびデバッグ用可視化情報
				m_DebugRays.Add(new DebugRayInfo
				{
					Origin = footBottom,
					Direction = (bestClosestPoint - footBottom).normalized,
					Distance = closestDist,
					Hit = true,
					HitPoint = bestClosestPoint,
					HitNormal = surfaceNormal
				});
			}
		}

		if (!foundGround)
		{
			m_LastDetectionStep = 0;
		}

		m_LastDetectedNormal = detectedNormal;
		m_LastDetectedPoint = detectedPoint;

		// デバッグ表示: 現在の法線(緑線)と進行方向(青線)
		if (m_EnableDebugDraw)
		{
			Debug.DrawRay(transform.position, m_CurrentGroundNormal * 1.5f, Color.green, 0f);
			Debug.DrawRay(transform.position, currentMoveDir * 1.0f, Color.blue, 0f);
		}

		// 接地状態と目標法線の更新 (ヒステリシス・ダンピングアルゴリズム)
		if (foundGround)
		{
			m_IsGrounded = true;
			m_LandingSnapTimer = 0f;

			// 足元理想位置と地面接触点との高低差(浮きギャップ)を計測
			Vector3 footPos = transform.position - transform.up * footDist;
			m_GroundGap = Vector3.Dot(footPos - detectedPoint, m_CurrentGroundNormal);

			// 【アルゴリズム: 法線遷移ヒステリシス (Normal Transition Hysteresis)】
			// 直方体の稜線上において、上面(Step 2)と側面(Step 3)が微小な位置揺らぎで
			// 毎フレーム交互に切り替わるチャタリング振動を防止します。
			// 新たに検出された面が直前の面と大きく異なる(内積 dot < 0.75f = 角度差約41度以上)場合、
			// 0.12秒間のヒステリシスホールド時間を設定し、過渡期における古い面への急な跳ね返りを抑制します。
			float dotNewTarget = Vector3.Dot(m_TargetGroundNormal, detectedNormal);
			if (dotNewTarget < 0.75f)
			{
				m_NormalHysteresisTimer = 0.12f;
				m_TargetGroundNormal = detectedNormal;
			}
			else if (m_NormalHysteresisTimer > 0f)
			{
				m_NormalHysteresisTimer -= Time.fixedDeltaTime;
			}
			else
			{
				m_TargetGroundNormal = detectedNormal;
			}

			// 【アルゴリズム: 二乗イージング法線Slerp (Adaptive Slerp Damping)】
			// 法線角度差が大きい過渡期はダンピングを効かせて急激な姿勢反転を抑え、
			// 平坦面付近では即座に追従する可変レートを適用してカクつきのない円弧軌道を描きます。
			float dotCurrent = Vector3.Dot(m_CurrentGroundNormal, m_TargetGroundNormal);
			float normalAlignSpeed = dotCurrent < 0.7f ? 16.0f : 20.0f;
			m_CurrentGroundNormal = Vector3.Slerp(m_CurrentGroundNormal, m_TargetGroundNormal, normalAlignSpeed * Time.fixedDeltaTime).normalized;
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
