using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// デバッグ用にサーバーからCキーでボールを初期位置へ戻します。
/// </summary>
[RequireComponent(typeof(BallController))]
public class BallDebugRespawner : NetworkBehaviour
{
	private BallController m_BallController;

	private void Awake()
	{
		m_BallController = GetComponent<BallController>();
	}

	private void Update()
	{
		if (!IsSpawned || !IsServer || Keyboard.current == null)
		{
			return;
		}

		if (!Keyboard.current.cKey.wasPressedThisFrame)
		{
			return;
		}

		if (!m_BallController.RespawnAtInitialPosition())
		{
			Debug.LogWarning("[BallDebugRespawner] ボールの初期位置への復帰に失敗しました。", this);
		}
	}
}
