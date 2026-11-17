using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 所持されていないボールの場外をサーバーで検知し、初期スポーン地点へ戻します。
/// </summary>
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

	private BallController m_BallController;
	private bool m_AreBoundsValid;
	private bool m_IsRespawnRequested;

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
			m_IsRespawnRequested = false;
			return;
		}

		if (m_IsRespawnRequested)
		{
			return;
		}

		m_IsRespawnRequested = true;
		if (!m_BallController.RespawnAtInitialPosition())
		{
			Debug.LogWarning("[BallOutOfBoundsRespawner] ボールの場外復帰に失敗しました。", this);
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
