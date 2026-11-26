using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 未所持のボールが有効範囲外へ出た場合に、
/// Host側で安全な場内位置へ復帰させます。
/// </summary>
/// <remarks>
/// 主な処理の流れ:
/// 1. 場内にいる間は最後に有効だったWorld位置を保存します。
/// 2. 未所持のボールが範囲外へ出たことをHost側で検出します。
/// 3. 最後の有効位置の周囲から、指定Ground Layer上の最も近い復帰候補を探します。
/// 4. Ball Colliderの半径とContact Offsetを考慮してSurface外側へ配置します。
/// 5. 近くに有効なGroundが見つからない場合は初期スポーン地点へ戻します。
///
/// 所持中のボールはプレイヤー側の移動に従うため、この自動復帰の対象外です。
/// </remarks>
[RequireComponent(typeof(BallController))]
public class BallOutOfBoundsRespawner : NetworkBehaviour
{
	[Header("場外復帰")]
	[Tooltip("有効にすると、設定範囲外へ出た未所持のボールを安全な場内位置へ戻します。")]
	[SerializeField] private bool m_IsRespawnEnabled;

	[Header("場外判定範囲(ワールド座標)")]
	[Tooltip("ボールが移動できるX、Y、Z座標の下限です。")]
	[SerializeField] private Vector3 m_MinBounds;

	[Tooltip("ボールが移動できるX、Y、Z座標の上限です。")]
	[SerializeField] private Vector3 m_MaxBounds;

	[Header("場外復帰先")]
	[Tooltip("場外復帰先として探索するGroundのLayerです。")]
	[SerializeField] private LayerMask m_RespawnGroundMask;

	[Tooltip("最後に場内だった位置からGroundを探索する半径です。")]
	[SerializeField] private float m_GroundSearchRadius;

	private const int m_InitialCandidateCount = 64;
	private const int m_MaximumCandidateCount = 4096;
	private const float m_RespawnRetryInterval = 0.5f;
	private Collider[] m_GroundCandidates = new Collider[m_InitialCandidateCount];
	private Collider[] m_PlacementCandidates = new Collider[m_InitialCandidateCount];
	private readonly BallSurfaceSearch m_SurfaceSearch = new BallSurfaceSearch();
	private Rigidbody m_Rigidbody;

	private BallController m_BallController;
	private bool m_AreBoundsValid;
	private float m_NextRespawnAttemptTime;
	private Vector3 m_LastValidPosition;
	private bool m_HasLastValidPosition;

	private void Awake()
	{
		m_BallController = GetComponent<BallController>();
		m_Rigidbody = GetComponent<Rigidbody>();
	}

	public override void OnNetworkSpawn()
	{
		if (!IsServer)
		{
			return;
		}

		m_NextRespawnAttemptTime = 0.0f;
		m_AreBoundsValid = AreBoundsValid() && !IsOutsideBounds(transform.position);
		m_LastValidPosition = transform.position;
		m_HasLastValidPosition = m_AreBoundsValid;

		if (m_IsRespawnEnabled &&
			(m_RespawnGroundMask.value == 0 ||
			m_GroundSearchRadius <= 0.0f))
		{
			Debug.LogWarning(
				"[BallOutOfBoundsRespawner] Ground LayerまたはGround Search Radiusが未設定のため、Groundが見つからない場合は初期スポーン地点へ戻します。",
				this);
		}

		if (m_IsRespawnEnabled && !m_AreBoundsValid)
		{
			Debug.LogError(
				"[BallOutOfBoundsRespawner] 各軸でMin BoundsをMax Boundsより小さくし、初期スポーン地点を範囲内に設定してください。",
				this);
		}
	}

	private void FixedUpdate()
	{
		if (!m_IsRespawnEnabled || !IsSpawned || !IsServer || !m_AreBoundsValid ||
			m_BallController.HasHolder || m_BallController.IsMatchPaused)
		{
			return;
		}

		if (!IsOutsideBounds(transform.position))
		{
			m_LastValidPosition = transform.position;
			m_HasLastValidPosition = true;
			m_NextRespawnAttemptTime = 0.0f;
			return;
		}

		if (Time.fixedTime < m_NextRespawnAttemptTime)
		{
			return;
		}

		// 失敗しても永久に復帰不能にならないよう、次の試行時刻を設定します。
		m_NextRespawnAttemptTime = Time.fixedTime + m_RespawnRetryInterval;
		if (!m_BallController.TryGetRespawnSphere(out Vector3 centerOffset,
			out float radius, out SphereCollider sphere))
		{
			Debug.LogWarning("[BallOutOfBoundsRespawner] 復帰用のSphereColliderを取得できません。", this);
			return;
		}

		Vector3 respawnPosition = Vector3.zero;
		bool hasPosition = m_HasLastValidPosition && TryFindNearestGroundRespawnPosition(
			m_LastValidPosition, centerOffset, radius, sphere, out respawnPosition);
		if (!hasPosition)
		{
			respawnPosition = m_BallController.InitialRespawnPosition;
			if (!IsPlacementClear(respawnPosition, centerOffset, radius, sphere))
			{
				Debug.LogWarning("[BallOutOfBoundsRespawner] 安全な復帰先がないため、次回再試行します。", this);
				return;
			}
		}

		if (!m_BallController.RespawnAtPosition(respawnPosition))
		{
			Debug.LogWarning("[BallOutOfBoundsRespawner] ボールの場外復帰に失敗しました。次回再試行します。", this);
		}
	}

	/// <summary>
	/// Groundごとの最近接点から、安全に配置できる候補を選択します。
	/// </summary>
	private bool TryFindNearestGroundRespawnPosition(Vector3 referencePosition, Vector3 centerOffset,
		float radius, SphereCollider sphere, out Vector3 respawnPosition)
	{
		respawnPosition = Vector3.zero;
		if (!(m_GroundSearchRadius > 0.0f) || float.IsInfinity(m_GroundSearchRadius) ||
			m_RespawnGroundMask.value == 0 ||
			!TryCollectOverlaps(referencePosition, m_GroundSearchRadius, m_RespawnGroundMask,
				ref m_GroundCandidates, out int candidateCount))
		{
			return false;
		}

		float clearance = radius + Mathf.Max(Physics.defaultContactOffset, sphere.contactOffset);
		float nearestDistanceSquared = m_GroundSearchRadius * m_GroundSearchRadius;
		bool found = false;
		for (int index = 0; index < candidateCount; index++)
		{
			Collider ground = m_GroundCandidates[index];
			if (ground == null || (m_Rigidbody != null && ground.attachedRigidbody == m_Rigidbody) ||
				!ground.enabled || !ground.gameObject.activeInHierarchy ||
				ground.bounds.SqrDistance(referencePosition) > nearestDistanceSquared ||
				!m_SurfaceSearch.TryGetCandidateSurface(ground, referencePosition, out BallSurfaceSearch.SearchResult candidate))
			{
				continue;
			}

			Vector3 toPoint = candidate.Point - referencePosition;
			float distanceSquared = toPoint.sqrMagnitude;
			if (!(distanceSquared > 0.00000001f) || !(distanceSquared <= nearestDistanceSquared))
			{
				continue;
			}

			float distance = Mathf.Sqrt(distanceSquared);
			Vector3 point;
			Vector3 normal;
			// 最近接点への短いRayから実際の接触法線を取得します。
			if (ground.Raycast(new Ray(referencePosition, toPoint / distance), out RaycastHit hit,
				distance + clearance))
			{
				point = hit.point;
				normal = hit.normal.normalized;
			}
			else if (candidate.SurfaceSource != null &&
				candidate.SurfaceSource.TryGetTriangleClosestWorldPoint(candidate.TriangleIndex,
					referencePosition, out point, out normal, out _) &&
				Vector3.Dot(referencePosition - point, normal) > 0.0f)
			{
				// Edge付近でRayが外れた場合は、表側からの幾何法線を使用します。
			}
			else
			{
				continue;
			}

			if (!(normal.sqrMagnitude > 0.000001f))
			{
				continue;
			}
			Vector3 position = point + normal * clearance - centerOffset;
			if (!IsPlacementClear(position, centerOffset, radius, sphere))
			{
				continue;
			}

			nearestDistanceSquared = distanceSquared;
			respawnPosition = position;
			found = true;
		}

		System.Array.Clear(m_GroundCandidates, 0, candidateCount);
		return found;
	}

	/// <summary>
	/// 実際の球中心で周囲の衝突対象との重複を確認します。
	/// </summary>
	private bool IsPlacementClear(Vector3 position, Vector3 centerOffset, float radius, SphereCollider sphere)
	{
		Vector3 center = position + centerOffset;
		if (float.IsNaN(center.sqrMagnitude) || float.IsInfinity(center.sqrMagnitude) || IsOutsideBounds(position) ||
			!TryCollectOverlaps(center, radius, ~0, ref m_PlacementCandidates, out int count))
		{
			return false;
		}

		bool clear = true;
		for (int index = 0; index < count; index++)
		{
			Collider other = m_PlacementCandidates[index];
			if (other == null || other == sphere || (m_Rigidbody != null && other.attachedRigidbody == m_Rigidbody) ||
				Physics.GetIgnoreLayerCollision(sphere.gameObject.layer, other.gameObject.layer) ||
				Physics.GetIgnoreCollision(sphere, other))
			{
				continue;
			}
			clear = false;
			break;
		}

		System.Array.Clear(m_PlacementCandidates, 0, count);
		return clear;
	}

	/// <summary>
	/// 配列が満杯の場合は拡張して再探索します。上限到達時は候補を確定しません。
	/// </summary>
	private static bool TryCollectOverlaps(Vector3 center, float radius, int layerMask,
		ref Collider[] candidates, out int count)
	{
		while (true)
		{
			count = Physics.OverlapSphereNonAlloc(center, radius, candidates, layerMask,
				QueryTriggerInteraction.Ignore);
			if (count < candidates.Length)
			{
				return true;
			}
			if (candidates.Length >= m_MaximumCandidateCount)
			{
				System.Array.Clear(candidates, 0, count);
				count = 0;
				return false;
			}
			candidates = new Collider[Mathf.Min(candidates.Length * 2, m_MaximumCandidateCount)];
		}
	}

	/// <summary>
	/// いずれかの軸がワールド座標の有効範囲外か確認します。
	/// </summary>
	private bool IsOutsideBounds(Vector3 position)
	{
		return
			position.x < m_MinBounds.x ||
			position.y < m_MinBounds.y ||
			position.z < m_MinBounds.z ||
			position.x > m_MaxBounds.x ||
			position.y > m_MaxBounds.y ||
			position.z > m_MaxBounds.z;
	}

	/// <summary>
	/// 全軸の下限が上限より小さいことを確認します。
	/// </summary>
	private bool AreBoundsValid()
	{
		return
			m_MinBounds.x < m_MaxBounds.x &&
			m_MinBounds.y < m_MaxBounds.y &&
			m_MinBounds.z < m_MaxBounds.z;
	}

	private void OnDrawGizmosSelected()
	{
		if (!AreBoundsValid())
		{
			return;
		}

		Vector3 center = (m_MinBounds + m_MaxBounds) * 0.5f;
		Vector3 size = m_MaxBounds - m_MinBounds;
		Color previousColor = Gizmos.color;
		Gizmos.color = Color.cyan;
		Gizmos.DrawWireCube(center, size);
		Gizmos.color = previousColor;
	}
}
