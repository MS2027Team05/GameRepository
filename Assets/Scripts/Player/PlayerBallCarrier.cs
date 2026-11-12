using Unity.Netcode;
using UnityEngine;

/// <summary>
/// プレイヤー側のボール所持処理を担当します。
/// </summary>
/// <remarks>
/// 主な処理の流れ:
/// 1. Ownerがボールとの接触を検出します。
/// 2. ServerRpcでHostへ取得を要求します。
/// 3. HostがBallControllerへ取得処理を依頼します。
/// 4. シュート・強制解放もHost側で所持状態を確認して確定します。
/// 5. ボール所持状態の変化をPlayerControllerへ通知します。
/// </remarks>
[RequireComponent(typeof(NetworkObject))]
public class PlayerBallCarrier :
	NetworkBehaviour,
	IBallCarrier
{
	[Header("ボール所持位置")]
	[SerializeField] private Transform m_BallHoldPoint;

	[Header("追尾パス設定")]
	[Tooltip("追尾パスのチャージ完了に必要な時間です(秒)。")]
	[SerializeField] private float m_PassChargeDuration = 1.0f;

	private PlayerController m_PlayerController;
	private PlayerResourceController m_PlayerResourceController;
	private bool m_HasBall;
	private bool m_IsPassCharging;
	private float m_PassChargeRatio;

	/// <summary>
	/// このプレイヤーが現在ボールを所持しているか取得します。
	/// </summary>
	public bool HasBall
	{
		get
		{
			return m_HasBall;
		}
	}

	/// <summary>
	/// 追尾パスの長押しチャージ中であるか取得します。
	/// </summary>
	public bool IsPassCharging
	{
		get
		{
			return m_IsPassCharging;
		}
	}

	/// <summary>
	/// 追尾パスのチャージ進捗率(0.0〜1.0)を取得します。
	/// </summary>
	public float PassChargeRatio
	{
		get
		{
			return m_PassChargeRatio;
		}
	}

	/// <summary>
	/// 追尾パスのチャージが満タンに達しているか取得します。
	/// </summary>
	public bool IsPassChargeFull
	{
		get
		{
			return m_PassChargeRatio >= 1.0f;
		}
	}

	/// <summary>
	/// ボールを所持中に配置する位置を取得します。
	/// </summary>
	public Transform BallHoldPoint
	{
		get
		{
			return m_BallHoldPoint;
		}
	}

	/// <summary>
	/// 使用するコンポーネントの参照を取得します。
	/// </summary>
	private void Awake()
	{
		m_PlayerController =
			GetComponent<PlayerController>();
		m_PlayerResourceController = GetComponent<PlayerResourceController>();
	}

	/// <summary>
	/// Owner側でボールとの接触を検出し、
	/// Hostへ取得要求を送信します。
	/// </summary>
	/// <param name="other">
	/// 接触したColliderです。
	/// </param>
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

		// ゴール判定など別用途のColliderでは
		// キャッチ処理を開始しません。
		if (!ballController.IsCatchCollider(other))
		{
			return;
		}

		// NetworkObjectとしてSpawnされる前のボールには
		// ServerRpcによる取得要求を行いません。
		if (!ballController.IsSpawned)
		{
			return;
		}

		RequestPickupServerRpc(
			ballController.NetworkObjectId);
	}

	/// <summary>
	/// Host上で指定されたボールの取得を試みます。
	/// </summary>
	/// <param name="ballNetworkObjectId">
	/// 取得対象となるボールのNetworkObjectIdです。
	/// </param>
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

		// 最終的な取得可否はBallController側で判定します。
		ballController.TrySetHolder(
			OwnerClientId,
			NetworkObjectId);
	}

	/// <summary>
	/// Hostが味方からのパス受取を確認したときに、対象Ownerへボーナス付与を通知します。
	/// </summary>
	public void NotifyFriendlyPassReceivedServer()
	{
		if (!IsServer || !IsSpawned)
		{
			return;
		}

		ReceiveFriendlyPassBonusOwnerRpc();
	}

	[Rpc(SendTo.Owner)]
	private void ReceiveFriendlyPassBonusOwnerRpc()
	{
		if (!IsOwner)
		{
			return;
		}

		m_PlayerResourceController?.ApplyFriendlyPassBonus();
	}

	/// <summary>
	/// 所持中のボールを指定方向へ射出するようHostへ要求します。
	/// </summary>
	/// <param name="direction">
	/// ボールを射出する方向です。
	/// </param>
	public void Shoot(
		Vector3 direction)
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
	/// Host上でこのプレイヤーが所持するボールを取得し、
	/// 指定方向へのシュートを試みます。
	/// </summary>
	/// <param name="shootDirection">
	/// ボールを射出する方向です。
	/// </param>
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

		if (!TryGetOwnedBallServer(
			out BallController ballController))
		{
			Debug.LogWarning(
				"[PlayerBallCarrier] " +
				"シュート対象のボールが見つかりません。");

			return;
		}

		if (!ballController.TryShoot(
			shootDirection))
		{
			Debug.LogWarning(
				"[PlayerBallCarrier] " +
				"ボールのシュート処理に失敗しました。");
		}
	}

	/// <summary>
	/// タックルなどの外部処理から、
	/// このプレイヤーが所持するボールを強制的に解放します。
	/// </summary>
	public void ForceReleaseBall()
	{
		if (!IsSpawned)
		{
			return;
		}

		// Host自身から呼ばれた場合は、
		// ServerRpcを経由せず直接解放します。
		if (IsServer)
		{
			ReleaseOwnedBallServer();
			return;
		}

		// Clientからの解放要求は、
		// 対象プレイヤー本人のOwnerだけが送信できます。
		if (IsOwner)
		{
			RequestReleaseBallServerRpc();
		}
	}

	/// <summary>
	/// OwnerからHostへボールの強制解放を要求します。
	/// </summary>
	[ServerRpc]
	private void RequestReleaseBallServerRpc()
	{
		ReleaseOwnedBallServer();
	}

	/// <summary>
	/// Host上でこのプレイヤーが所持しているボールを探し、
	/// 見つかった場合は解放します。
	/// </summary>
	private void ReleaseOwnedBallServer()
	{
		if (!IsServer)
		{
			return;
		}

		if (!TryGetOwnedBallServer(
			out BallController ballController))
		{
			return;
		}

		ballController.ReleaseBall();
	}

	/// <summary>
	/// Host上で、このプレイヤーが現在所持している
	/// ボールを取得します。
	/// </summary>
	/// <param name="ballController">
	/// 所持中のボールです。
	/// 見つからない場合はnullになります。
	/// </param>
	/// <returns>
	/// 所持中のボールが見つかった場合はtrue、
	/// 見つからなかった場合はfalseです。
	/// </returns>
	private bool TryGetOwnedBallServer(
		out BallController ballController)
	{
		ballController = null;

		if (!IsServer)
		{
			return false;
		}

		// Spawn済みオブジェクトから、
		// このプレイヤーを所持者としているボールを探します。
		foreach (NetworkObject networkObject in
			NetworkManager.SpawnManager.SpawnedObjectsList)
		{
			BallController candidate =
				networkObject.GetComponent<BallController>();

			if (candidate == null)
			{
				continue;
			}

			if (!candidate.HasHolder)
			{
				continue;
			}

			if (candidate.CurrentHolderId !=
				OwnerClientId)
			{
				continue;
			}

			ballController = candidate;

			return true;
		}

		return false;
	}

	/// <summary>
	/// このプレイヤーのボール所持状態を更新し、
	/// Ownerの場合はPlayerControllerへ状態変化を通知します。
	/// </summary>
	/// <param name="hasBall">
	/// ボールを所持している場合はtrue、
	/// 所持していない場合はfalseです。
	/// </param>
	public void SetBallState(
		bool hasBall)
	{
		if (m_HasBall == hasBall)
		{
			return;
		}

		m_HasBall = hasBall;

		if (!hasBall)
		{
			CancelPassCharge();
		}

		// 移動速度などプレイヤー固有の変更は、
		// そのプレイヤーを操作するOwner側だけに通知します。
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

	// =========================================================================
	// 追尾パスアクション用インターフェース窓口 (PG3担当: 本実装対象)
	// =========================================================================

	/// <summary>
	/// 長押しによる追尾パスチャージおよびキャンセル案内UIの表示を開始します(PG3本実装対象)。
	/// </summary>
	public void BeginPassCharge()
	{
		if (!IsOwner || !m_HasBall)
		{
			return;
		}

		m_IsPassCharging = true;
		m_PassChargeRatio = 0.0f;

		Debug.Log("[PlayerBallCarrier] パスチャージ開始");
	}

	/// <summary>
	/// 追尾パスチャージの状態を毎フレーム更新します(PG3本実装対象)。
	/// </summary>
	/// <param name="deltaTime">経過時間(秒)</param>
	public void UpdatePassCharge(float deltaTime)
	{
		if (!IsOwner || !m_IsPassCharging || !m_HasBall)
		{
			return;
		}

		// チャージ時間が0以下の場合は即座に満タン
		if (m_PassChargeDuration <= 0.0f)
		{
			m_PassChargeRatio = 1.0f;
			return;
		}

		// チャージ進捗を更新
		m_PassChargeRatio += deltaTime / m_PassChargeDuration;

		// 0～1の範囲に制限
		m_PassChargeRatio = Mathf.Clamp01(m_PassChargeRatio);
	}

	/// <summary>
	/// 追尾パスチャージをキャンセルし、関連UIを非表示にします(PG3本実装対象)。
	/// </summary>
	public void CancelPassCharge()
	{
		if (!m_IsPassCharging)
		{
			return;
		}

		m_IsPassCharging = false;
		m_PassChargeRatio = 0.0f;

		Debug.Log("[PlayerBallCarrier] パスチャージキャンセル");
	}

	/// <summary>
	/// ロックオン対象(味方プレイヤーまたは相手ゴール)へ向けて追尾パスを射出します(PG3本実装対象)。
	/// </summary>
	/// <param name="targetObject">追尾対象のGameObject</param>
	/// <param name="targetType">追尾対象の種類</param>

	/// <summary>
	/// ロックオン対象への追尾パスを要求する。
	/// 味方はNetworkObject、ゴールは座標で指定する。
	/// </summary>
	public void ShootHoming(
		GameObject targetObject,
		ELockOnTargetType targetType)
	{
		CancelPassCharge();

		if (!IsOwner || !HasBall || targetObject == null)
		{
			return;
		}

		// ---------------------------------
		// ゴールの場合は座標で追尾する
		// ---------------------------------
		if (targetType == ELockOnTargetType.EnemyGoal)
		{
			Vector3 targetPosition =
				targetObject.transform.position;

			Debug.Log(
				$"[PlayerBallCarrier] ゴールへの追尾パス要求: " +
				$"{targetObject.name} / Position={targetPosition}"
			);

			RequestHomingPassToPositionServerRpc(
				targetPosition
			);

			return;
		}

		// ---------------------------------
		// 味方プレイヤーはNetworkObjectで追尾
		// ---------------------------------
		NetworkObject targetNetworkObject =
			targetObject.GetComponentInParent<NetworkObject>();

		if (targetNetworkObject == null)
		{
			Debug.LogWarning(
				"[PlayerBallCarrier] 味方プレイヤーにNetworkObjectがありません。",
				targetObject
			);

			return;
		}

		RequestHomingPassServerRpc(
			targetNetworkObject.NetworkObjectId
		);
	}


	/// <summary>
	/// サーバーに追尾パスの開始を要求する。
	/// </summary>
	[ServerRpc]
	private void RequestHomingPassServerRpc(ulong targetNetworkObjectId)
	{
		if (!IsServer || !HasBall)
		{
			return;
		}

		if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(
			targetNetworkObjectId,
			out NetworkObject targetNetworkObject))
		{
			Debug.LogWarning(
				"[PlayerBallCarrier] パス対象のNetworkObjectが見つかりません。");
			return;
		}

		if (!TryGetOwnedBallServer(out BallController ballController))
		{
			Debug.LogWarning(
				"[PlayerBallCarrier] 所持しているボールが見つかりません。");
			return;
		}

		bool success = ballController.TryStartHomingPass(
			targetNetworkObject.transform);

		if (!success)
		{
			Debug.LogWarning(
				"[PlayerBallCarrier] 追尾パスを開始できませんでした。");
		}
	}



	/// <summary>
	/// ゴールの座標を指定して追尾パスを開始する。
	/// </summary>
	[ServerRpc]
	private void RequestHomingPassToPositionServerRpc(
		Vector3 targetPosition)
	{
		if (!IsServer || !HasBall)
		{
			return;
		}

		if (!TryGetOwnedBallServer(
			out BallController ballController))
		{
			Debug.LogWarning(
				"[PlayerBallCarrier] 所持しているボールが見つかりません。"
			);

			return;
		}

		bool success =
			ballController.TryStartHomingPass(targetPosition);

		if (!success)
		{
			Debug.LogWarning(
				"[PlayerBallCarrier] ゴールへの追尾パスを開始できませんでした。"
			);
		}
		else
		{
			Debug.Log(
				"[PlayerBallCarrier] ゴールへの追尾パス開始成功"
			);
		}
	}



}
