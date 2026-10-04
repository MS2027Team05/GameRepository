using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ボール取得テスト用の仮プレイヤー移動処理です。
/// OwnerのプレイヤーのみWASDでXZ平面を移動します。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class TemporaryPlayerMover : NetworkBehaviour, IGravityMover
{
	[Header("仮移動設定")]
	[SerializeField] private float m_MoveSpeed;

	private Rigidbody m_Rigidbody;
	private float m_SpeedMultiplier = 1.0f;

	private void Awake()
	{
		m_Rigidbody = GetComponent<Rigidbody>();
	}

	private void FixedUpdate()
	{
		if (!IsOwner)
		{
			return;
		}

		if (Keyboard.current == null)
		{
			return;
		}

		Vector3 moveDirection = Vector3.zero;

		if (Keyboard.current.wKey.isPressed)
		{
			moveDirection += Vector3.forward;
		}

		if (Keyboard.current.sKey.isPressed)
		{
			moveDirection += Vector3.back;
		}

		if (Keyboard.current.aKey.isPressed)
		{
			moveDirection += Vector3.left;
		}

		if (Keyboard.current.dKey.isPressed)
		{
			moveDirection += Vector3.right;
		}

		if (moveDirection.sqrMagnitude > 1.0f)
		{
			moveDirection.Normalize();
		}

		Vector3 moveVelocity =
			moveDirection * (m_MoveSpeed * m_SpeedMultiplier);

		m_Rigidbody.linearVelocity = moveVelocity;
	}

	/// <summary>
	/// 本来の重力移動用メソッドです。
	/// 今回の仮移動では使用しません。
	/// </summary>
	public void StartFalling(Vector3 direction)
	{
	}

	/// <summary>
	/// 本来の重力移動停止用メソッドです。
	/// 今回の仮移動では使用しません。
	/// </summary>
	public void StopFalling()
	{
	}

	/// <summary>
	/// ボール所持状態などによる移動速度倍率を設定します。
	/// </summary>
	public void SetSpeedMultiplier(float multiplier)
	{
		m_SpeedMultiplier = multiplier;
	}
}
