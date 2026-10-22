using Unity.Netcode;
using UnityEngine;

/// <summary>
/// プレイヤーがワールド座標の有効範囲外へ移動したことを検知し、
/// 初期スポーン地点への復帰を要求します。
/// </summary>
public class PlayerOutOfBoundsRespawner : NetworkBehaviour
{
	[Header("場外復帰")]
	[Tooltip("有効にすると、設定範囲外へ出たプレイヤーを初期スポーン地点へ復帰させます。")]
	[SerializeField] private bool m_IsRespawnEnabled;

	[Header("場外判定範囲(ワールド座標)")]
	[Tooltip("プレイヤーが行動できるX、Y、Z座標の下限です。")]
	[SerializeField] private Vector3 m_MinBounds;

	[Tooltip("プレイヤーが行動できるX、Y、Z座標の上限です。")]
	[SerializeField] private Vector3 m_MaxBounds;

	private IPlayerRespawnHandler m_RespawnHandler;
	private Vector3 m_RespawnPosition;
	private Quaternion m_RespawnRotation;
	private bool m_AreBoundsValid;
	private bool m_IsRespawnRequested;

	/// <summary>
	/// ネットワーク生成時の位置と回転を復帰地点として保存します。
	/// </summary>
	public override void OnNetworkSpawn()
	{
		if (!IsOwner)
		{
			return;
		}

		m_RespawnHandler = GetComponent<IPlayerRespawnHandler>();
		m_RespawnPosition = transform.position;
		m_RespawnRotation = transform.rotation;
		m_AreBoundsValid = AreBoundsValid();

		if (!m_IsRespawnEnabled)
		{
			return;
		}

		if (!m_AreBoundsValid)
		{
			Debug.LogError(
				"[PlayerOutOfBoundsRespawner] " +
				"場外判定範囲が不正です。各軸でMin BoundsをMax Boundsより小さく設定してください。",
				this);
		}

		if (m_RespawnHandler == null)
		{
			Debug.LogError(
				"[PlayerOutOfBoundsRespawner] " +
				"IPlayerRespawnHandlerを実装したコンポーネントが見つかりません。",
				this);
		}
	}

	private void FixedUpdate()
	{
		if (!m_IsRespawnEnabled ||
			!IsSpawned ||
			!IsOwner ||
			!m_AreBoundsValid)
		{
			return;
		}

		bool isOutsideBounds = IsOutsideBounds(transform.position);

		if (!isOutsideBounds)
		{
			m_IsRespawnRequested = false;
			return;
		}

		if (m_IsRespawnRequested || m_RespawnHandler == null)
		{
			return;
		}

		m_IsRespawnRequested = true;
		m_RespawnHandler.Respawn(
			m_RespawnPosition,
			m_RespawnRotation);
	}

	/// <summary>
	/// 指定された座標が有効範囲外か確認します。
	/// </summary>
	/// <param name="position">確認対象のワールド座標</param>
	/// <returns>いずれかの軸が範囲外の場合はtrue</returns>
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
