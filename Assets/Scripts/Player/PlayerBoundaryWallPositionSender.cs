using Unity.Netcode;
using UnityEngine;

/// <summary>
/// ローカルプレイヤーの位置を
/// 場外壁用Shaderへ送信します。
/// </summary>
/// <remarks>
/// 各端末ではOwnerのプレイヤーだけが
/// Shaderへ位置を送信します。
/// </remarks>
[DisallowMultipleComponent]
public class PlayerBoundaryWallPositionSender :
	NetworkBehaviour
{
	private static readonly int m_LocalPlayerPositionId =
		Shader.PropertyToID(
			"_LocalPlayerPosition");

	private static readonly int m_LocalPlayerVisibilityEnabledId =
		Shader.PropertyToID(
			"_LocalPlayerVisibilityEnabled");

	/// <summary>
	/// NetworkObject生成時に、
	/// OwnerであればShaderへの位置送信を有効にします。
	/// </summary>
	public override void OnNetworkSpawn()
	{
		if (!IsOwner)
		{
			return;
		}

		Shader.SetGlobalFloat(
			m_LocalPlayerVisibilityEnabledId,
			1.0f);

		UpdateShaderPosition();
	}

	/// <summary>
	/// Ownerのプレイヤー位置を毎フレームShaderへ送ります。
	/// </summary>
	private void LateUpdate()
	{
		if (!IsSpawned ||
			!IsOwner)
		{
			return;
		}

		UpdateShaderPosition();
	}

	/// <summary>
	/// NetworkObject破棄時に
	/// 場外壁の表示を無効にします。
	/// </summary>
	public override void OnNetworkDespawn()
	{
		if (!IsOwner)
		{
			return;
		}

		Shader.SetGlobalFloat(
			m_LocalPlayerVisibilityEnabledId,
			0.0f);
	}

	/// <summary>
	/// 現在のプレイヤー座標をShaderへ送信します。
	/// </summary>
	private void UpdateShaderPosition()
	{
		Vector3 playerPosition =
			transform.position;

		Shader.SetGlobalVector(
			m_LocalPlayerPositionId,
			new Vector4(
				playerPosition.x,
				playerPosition.y,
				playerPosition.z,
				1.0f));
	}
}
