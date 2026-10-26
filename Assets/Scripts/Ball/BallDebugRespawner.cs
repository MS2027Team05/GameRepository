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
	private BallPhysicsController m_BallPhysicsController;

	private Vector3 m_InitialPosition;
	private Quaternion m_InitialRotation;

	/// <summary>
	/// 使用するComponentの参照を取得します。
	/// </summary>
	private void Awake()
	{
		m_Rigidbody =
			GetComponent<Rigidbody>();

		m_NetworkTransform =
			GetComponent<NetworkTransform>();

		m_BallController =
			GetComponent<BallController>();

		m_BallPhysicsController =
			GetComponent<BallPhysicsController>();
	}

	/// <summary>
	/// Host側で初期位置と初期回転を保存します。
	/// </summary>
	public override void OnNetworkSpawn()
	{
		if (!IsServer)
		{
			return;
		}

		m_InitialPosition =
			transform.position;

		m_InitialRotation =
			transform.rotation;
	}

	/// <summary>
	/// Host側でデバッグ用リスポーン入力を確認します。
	/// </summary>
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
	/// ボールを初期位置へ戻し、
	/// 所持状態と物理状態を初期化します。
	/// </summary>
	private void RespawnBall()
	{
		if (!IsServer)
		{
			return;
		}

		// 所持中の場合は、
		// 先に通常の解放処理を通します。
		if (m_BallController != null &&
			m_BallController.HasHolder)
		{
			m_BallController.ReleaseBall();
		}

		// NetworkObjectがPlayerなどの子になっている場合は
		// 親子関係を解除します。
		if (transform.parent != null)
		{
			bool removeSucceeded =
				NetworkObject.TryRemoveParent(
					true);

			if (!removeSucceeded)
			{
				Debug.LogWarning(
					"[BallDebugRespawner] " +
					"ボールの親解除に失敗しました。");

				return;
			}
		}

		// Surface Physics中はRigidbodyがKinematicです。
		//
		// Kinematic RigidbodyへlinearVelocityや
		// angularVelocityを設定するとUnityから警告が出るため、
		// 必ず先にDynamic状態へ戻します。
		m_Rigidbody.isKinematic =
			false;

		m_Rigidbody.useGravity =
			false;

		m_Rigidbody.linearVelocity =
			Vector3.zero;

		m_Rigidbody.angularVelocity =
			Vector3.zero;

		// NetworkTransform経由で初期位置へ戻します。
		m_NetworkTransform.Teleport(
			m_InitialPosition,
			m_InitialRotation,
			transform.localScale);

		// BallPhysicsController内部の
		// Surface Physics状態も初期化します。
		if (m_BallPhysicsController != null)
		{
			m_BallPhysicsController.ResetMotionState();
		}
	}
}
