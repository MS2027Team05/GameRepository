using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// デバッグ用にHostからボールを初期位置へ戻します。
/// Rキーでリスポーンします。
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NetworkTransform))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(BallController))]
public class BallDebugRespawner : NetworkBehaviour
{
	private Rigidbody m_Rigidbody;
	private NetworkTransform m_NetworkTransform;
	private BallController m_BallController;

	private Vector3 m_InitialPosition;
	private Quaternion m_InitialRotation;

	private void Awake()
	{
		m_Rigidbody =
			GetComponent<Rigidbody>();

		m_NetworkTransform =
			GetComponent<NetworkTransform>();

		m_BallController =
			GetComponent<BallController>();
	}

	public override void OnNetworkSpawn()
	{
		if (!IsServer)
		{
			return;
		}

		// リスポーン処理を行うHost側だけで
		// 初期位置と初期回転を保存します。
		m_InitialPosition =
			transform.position;

		m_InitialRotation =
			transform.rotation;
	}

	private void Update()
	{
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

	/// <summary>
	/// ボールを初期位置へリスポーンさせます。
	/// </summary>
	private void RespawnBall()
	{
		if (!IsServer)
		{
			return;
		}

		// 誰かが所持している場合は、
		// 先に通常のボール解放処理を行います。
		if (m_BallController != null &&
			m_BallController.HasHolder)
		{
			m_BallController.ReleaseBall();
		}

		// 念のため親が残っている場合は解除します。
		if (transform.parent != null)
		{
			bool removeSucceeded =
				NetworkObject.TryRemoveParent(true);

			if (!removeSucceeded)
			{
				Debug.LogWarning(
					"[BallDebugRespawner] " +
					"ボールの親解除に失敗しました。");

				return;
			}
		}

		// リスポーン後に以前の速度が残らないようにします。
		m_Rigidbody.linearVelocity =
			Vector3.zero;

		m_Rigidbody.angularVelocity =
			Vector3.zero;

		m_Rigidbody.isKinematic =
			false;

		// HostのTransformだけを書き換えるのではなく、
		// NetworkTransformへ即時移動として通知します。
		// これによりClient側の補間もリセットされます。
		m_NetworkTransform.Teleport(
			m_InitialPosition,
			m_InitialRotation,
			transform.localScale);

		Debug.Log(
			$"[BallDebugRespawner] " +
			$"ボールをリスポーンしました。 " +
			$"Position={m_InitialPosition}");
	}
}
