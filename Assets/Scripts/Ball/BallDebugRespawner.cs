using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// デバッグ用にボールを初期位置へ戻します。
/// HostのみRキーで実行できます。
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(BallController))]
public class BallDebugRespawner : NetworkBehaviour
{
	private Rigidbody m_Rigidbody;
	private BallController m_BallController;

	private Vector3 m_InitialPosition;
	private Quaternion m_InitialRotation;

	private void Awake()
	{
		m_Rigidbody =
			GetComponent<Rigidbody>();

		m_BallController =
			GetComponent<BallController>();
	}

	public override void OnNetworkSpawn()
	{
		if (!IsServer)
		{
			return;
		}

		// ゲーム開始時の位置をリスポーン位置として保存します。
		m_InitialPosition =
			transform.position;

		m_InitialRotation =
			transform.rotation;
	}

	private void Update()
	{
		// Host側だけ入力を受け付けます。
		if (!IsServer)
		{
			return;
		}

		if (Keyboard.current == null)
		{
			return;
		}

		if (!Keyboard.current.rKey.wasPressedThisFrame)
		{
			return;
		}

		RespawnBall();
	}

	private void RespawnBall()
	{
		Debug.Log(
			"[BallDebugRespawner] ボールをリスポーンします。");

		// 誰かが所持している場合は先に解放します。
		if (m_BallController.HasHolder)
		{
			m_BallController.ReleaseBall();
		}

		// 念のため親子関係が残っている場合は解除します。
		if (transform.parent != null)
		{
			bool removeSucceeded =
				NetworkObject.TryRemoveParent(true);

			if (!removeSucceeded)
			{
				Debug.LogError(
					"[BallDebugRespawner] " +
					"ボールの親解除に失敗しました。");

				return;
			}
		}

		// 移動を完全に止めます。
		m_Rigidbody.linearVelocity =
			Vector3.zero;

		m_Rigidbody.angularVelocity =
			Vector3.zero;

		m_Rigidbody.isKinematic =
			false;

		// ゲーム開始時の位置へ戻します。
		transform.SetPositionAndRotation(
			m_InitialPosition,
			m_InitialRotation);

		Debug.Log(
			$"[BallDebugRespawner] リスポーン完了: " +
			$"Position={m_InitialPosition}");
	}
}
