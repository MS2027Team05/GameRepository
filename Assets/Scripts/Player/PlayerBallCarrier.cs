using Unity.Netcode;
using UnityEngine;

/// <summary>
/// プレイヤー側のボール所持処理を担当します。
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class PlayerBallCarrier :
	NetworkBehaviour,
	IBallCarrier
{
	[Header("ボール所持位置")]
	[SerializeField] private Transform m_BallHoldPoint;

	private PlayerController m_PlayerController;
	private bool m_HasBall;

	public bool HasBall =>
		m_HasBall;

	public Transform BallHoldPoint =>
		m_BallHoldPoint;

	private void Awake()
	{
		m_PlayerController =
			GetComponent<PlayerController>();
	}

	private void OnTriggerEnter(
		Collider other)
	{
		if (!IsOwner)
		{
			return;
		}

		if (m_HasBall)
		{
			return;
		}

		BallController ballController =
			other.GetComponentInParent<BallController>();

		if (ballController == null)
		{
			return;
		}

		if (!ballController.IsCatchCollider(other))
		{
			return;
		}

		if (!ballController.IsSpawned)
		{
			return;
		}

		RequestPickupServerRpc(
			ballController.NetworkObjectId);
	}

	[ServerRpc]
	private void RequestPickupServerRpc(
		ulong ballNetworkObjectId)
	{
		if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(
			ballNetworkObjectId,
			out NetworkObject ballNetworkObject))
		{
			return;
		}

		BallController ballController =
			ballNetworkObject.GetComponent<BallController>();

		if (ballController == null)
		{
			return;
		}

		bool pickupSucceeded =
			ballController.TrySetHolder(
				OwnerClientId,
				NetworkObjectId);

		Debug.Log(
			$"[PlayerBallCarrier] キャッチ結果: " +
			$"{pickupSucceeded}");
	}

	/// <summary>
	/// 所持中のボールを指定方向へ射出します。
	/// </summary>
	public void Shoot(Vector3 direction)
	{
		if (!IsOwner)
		{
			return;
		}

		if (!m_HasBall)
		{
			return;
		}

		if (direction.sqrMagnitude <=
			Mathf.Epsilon)
		{
			return;
		}

		RequestShootServerRpc(
			direction.normalized);
	}

	/// <summary>
	/// OwnerからHostへシュートを要求します。
	/// </summary>
	[ServerRpc]
	private void RequestShootServerRpc(
		Vector3 shootDirection)
	{
		if (shootDirection.sqrMagnitude <=
			Mathf.Epsilon)
		{
			return;
		}

		shootDirection.Normalize();

		foreach (NetworkObject networkObject in
			NetworkManager.SpawnManager.SpawnedObjectsList)
		{
			BallController ballController =
				networkObject.GetComponent<BallController>();

			if (ballController == null)
			{
				continue;
			}

			if (!ballController.HasHolder)
			{
				continue;
			}

			if (ballController.CurrentHolderId !=
				OwnerClientId)
			{
				continue;
			}

			bool shootSucceeded =
				ballController.TryShoot(
					shootDirection);

			Debug.Log(
				$"[PlayerBallCarrier] シュート結果: " +
				$"{shootSucceeded}");

			return;
		}

		Debug.LogWarning(
			"[PlayerBallCarrier] " +
			"シュート対象のボールが見つかりません。");
	}

	public void ForceReleaseBall()
	{
		if (!IsSpawned)
		{
			return;
		}

		if (IsServer)
		{
			ReleaseOwnedBallServer();
			return;
		}

		if (IsOwner)
		{
			RequestReleaseBallServerRpc();
		}
	}

	[ServerRpc]
	private void RequestReleaseBallServerRpc()
	{
		ReleaseOwnedBallServer();
	}

	private void ReleaseOwnedBallServer()
	{
		if (!IsServer)
		{
			return;
		}

		foreach (NetworkObject networkObject in
			NetworkManager.SpawnManager.SpawnedObjectsList)
		{
			BallController ballController =
				networkObject.GetComponent<BallController>();

			if (ballController == null)
			{
				continue;
			}

			if (!ballController.HasHolder)
			{
				continue;
			}

			if (ballController.CurrentHolderId !=
				OwnerClientId)
			{
				continue;
			}

			ballController.ReleaseBall();

			return;
		}
	}

	public void SetBallState(bool hasBall)
	{
		if (m_HasBall == hasBall)
		{
			return;
		}

		m_HasBall = hasBall;

		Debug.Log(
			$"[PlayerBallCarrier] SetBallState: " +
			$"ClientId={OwnerClientId}, " +
			$"HasBall={m_HasBall}");

		if (!IsOwner)
		{
			return;
		}

		if (m_PlayerController == null)
		{
			return;
		}

		m_PlayerController.NotifyBallStateChanged(
			hasBall);
	}
}
