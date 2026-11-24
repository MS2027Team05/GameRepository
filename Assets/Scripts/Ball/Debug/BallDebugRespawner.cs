using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 開発中の確認用として、Hostからボールを初期位置へ戻します。
/// </summary>
/// <remarks>
/// 主な処理の流れ:
/// 1. NetworkObjectがSpawn済みかつServerであることを確認します。
/// 2. Input SystemでCキーの押下を検出します。
/// 3. BallController.RespawnAtInitialPositionを呼び出します。
///
/// 実ゲームの場外復帰処理とは分離されたデバッグ専用コンポーネントです。
/// </remarks>
[RequireComponent(typeof(BallController))]
public class BallDebugRespawner : NetworkBehaviour
{
	private BallController m_BallController;

	/// <summary>
	/// デバッグ復帰に使用するBallControllerを取得します。
	/// </summary>
	private void Awake()
	{
		m_BallController = GetComponent<BallController>();
	}

	/// <summary>
	/// Server上でCキーが押されたFrameだけ初期位置へのリスポーンを実行します。
	/// </summary>
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
