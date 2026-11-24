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
	[Tooltip("有効にすると、設定範囲外へ出た未所持のボールを初期スポーン地点へ戻します。")]
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

	private const int m_MaxGroundCandidateCount =
		64;

	private readonly Collider[] m_GroundCandidates =
		new Collider[m_MaxGroundCandidateCount];

	private BallController m_BallController;
	private bool m_AreBoundsValid;
	private bool m_IsRespawnRequested;
	private Vector3 m_LastValidPosition;
	private bool m_HasLastValidPosition;

	private void Awake()
	{
		m_BallController = GetComponent<BallController>();
	}

	public override void OnNetworkSpawn()
	{
		if (!IsServer)
		{
			return;
		}

		m_IsRespawnRequested = false;
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
			m_LastValidPosition =
				transform.position;

			m_HasLastValidPosition =
				true;

			m_IsRespawnRequested = false;
			return;
		}

		if (m_IsRespawnRequested)
		{
			return;
		}

		m_IsRespawnRequested = true;

		bool respawnSucceeded =
			false;

		if (m_HasLastValidPosition &&
			TryFindNearestGroundRespawnPosition(
				m_LastValidPosition,
				out Vector3 respawnPosition))
		{
			respawnSucceeded =
				m_BallController.RespawnAtPosition(
					respawnPosition);
		}
		else
		{
			Debug.LogWarning(
				"[BallOutOfBoundsRespawner] 近くのGroundを取得できなかったため、初期スポーン地点へ戻します。",
				this);

			respawnSucceeded =
				m_BallController.RespawnAtInitialPosition();
		}

		if (!respawnSucceeded)
		{
			Debug.LogWarning(
				"[BallOutOfBoundsRespawner] ボールの場外復帰に失敗しました。",
				this);
		}
	}

	/// <summary>
	/// 最後に場内だった位置の周囲から最も近いGround Surfaceを探し、
	/// Ball Colliderが埋まらない復帰World座標を取得します。
	/// </summary>
	/// <remarks>
	/// OverlapSphereNonAllocで候補Colliderを列挙し、各ColliderのClosestPointから実SurfaceへRaycastします。
	/// 最終位置はBall半径とPhysics.defaultContactOffsetの分だけSurface法線方向へ離します。
	/// 範囲外になる候補は採用しません。
	/// </remarks>
	private bool TryFindNearestGroundRespawnPosition(
		Vector3 referencePosition,
		out Vector3 respawnPosition)
	{
		respawnPosition =
			default;

		if (m_GroundSearchRadius <=
				0.0f ||
			m_RespawnGroundMask.value ==
				0)
		{
			return false;
		}

		int candidateCount =
			Physics.OverlapSphereNonAlloc(
				referencePosition,
				m_GroundSearchRadius,
				m_GroundCandidates,
				m_RespawnGroundMask,
				QueryTriggerInteraction.Ignore);

		if (candidateCount <=
			0)
		{
			return false;
		}

		float ballRadius =
			Mathf.Max(
				m_BallController.GetPhysicsColliderWorldRadius(),
				0.0f);

		float surfaceClearance =
			ballRadius +
			Mathf.Max(
				Physics.defaultContactOffset,
				0.0f);

		float nearestDistanceSquared =
			float.PositiveInfinity;

		bool hasRespawnPosition =
			false;

		for (int candidateIndex = 0;
			candidateIndex < candidateCount;
			candidateIndex++)
		{
			Collider groundCollider =
				m_GroundCandidates[candidateIndex];

			if (groundCollider == null ||
				!groundCollider.enabled ||
				!groundCollider.gameObject.activeInHierarchy)
			{
				continue;
			}

			Vector3 closestPoint =
				groundCollider.ClosestPoint(
					referencePosition);

			Vector3 toSurface =
				closestPoint -
				referencePosition;

			float surfaceDistanceSquared =
				toSurface.sqrMagnitude;

			if (surfaceDistanceSquared <=
					Mathf.Epsilon ||
				surfaceDistanceSquared >=
					nearestDistanceSquared)
			{
				continue;
			}

			float surfaceDistance =
				Mathf.Sqrt(
					surfaceDistanceSquared);

			Vector3 surfaceDirection =
				toSurface /
				surfaceDistance;

			if (!groundCollider.Raycast(
				new Ray(
					referencePosition,
					surfaceDirection),
				out RaycastHit groundHit,
				surfaceDistance +
					surfaceClearance))
			{
				continue;
			}

			if (groundHit.normal.sqrMagnitude <=
				Mathf.Epsilon)
			{
				continue;
			}

			Vector3 candidateRespawnPosition =
				groundHit.point +
				groundHit.normal.normalized *
					surfaceClearance;

			if (IsOutsideBounds(
				candidateRespawnPosition))
			{
				continue;
			}

			nearestDistanceSquared =
				surfaceDistanceSquared;

			respawnPosition =
				candidateRespawnPosition;

			hasRespawnPosition =
				true;
		}

		return hasRespawnPosition;
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
