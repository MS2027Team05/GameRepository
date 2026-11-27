using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

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
	[Tooltip("男性モデルのRig:Human_Ball_Jointを指定します。描画は位置・回転のオフセットなしで追従します。")]
	[FormerlySerializedAs("m_BallHoldPoint")]
	[SerializeField] private Transform m_ManBallHoldPoint;
	[Tooltip("女性モデルのRig:Human_Ball_Jointを指定します。描画は位置・回転のオフセットなしで追従します。")]
	[SerializeField] private Transform m_WomanBallHoldPoint;

	private const float m_PickupRetryInterval = 0.10f;
	private const float m_LocalPickupSuppressDuration = 0.25f;

	[Header("追尾パス設定")]
	[Tooltip("追尾パスのチャージ完了に必要な時間です(秒)。")]
	[SerializeField] private float m_PassChargeDuration;

	private PlayerController m_PlayerController;
	private PlayerResourceController m_PlayerResourceController;
	private PlayerAppearance m_PlayerAppearance;
	private PlayerNetworkProfile m_PlayerNetworkProfile;
	private bool m_HasBall;
	private bool m_IsPassCharging;
	private float m_PassChargeRatio;
	private float m_NextPickupRetryTime;
	private float m_LocalPickupSuppressUntil;

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
	/// 表示中の男女モデルに対応する所持ジョイントを取得します。
	/// 外見の初期反映前は、同期済みのキャラクター種別を使用します。
	/// </summary>
	public Transform BallHoldPoint
	{
		get
		{
			ECharacterType characterType = ECharacterType.Man;
			if (m_PlayerAppearance != null && m_PlayerAppearance.HasAppliedCharacterType)
			{
				characterType = m_PlayerAppearance.CurrentCharacterType;
			}
			else if (m_PlayerNetworkProfile != null)
			{
				characterType = m_PlayerNetworkProfile.CharacterType;
			}

			// 未登録の場合は、非表示側のモデルへ装着せず取得を拒否します。
			return characterType switch
			{
				ECharacterType.Man => m_ManBallHoldPoint,
				ECharacterType.Woman => m_WomanBallHoldPoint,
				_ => null
			};
		}
	}

	public override void OnNetworkSpawn()
	{
		ResetCarrierState();
	}

	public override void OnNetworkDespawn()
	{
		ResetCarrierState();
	}

	/// <summary>
	/// ネットワーク生成・破棄時にボール所持とチャージ・取得抑制の内部状態を初期化します。
	/// </summary>
	public void ResetCarrierState()
	{
		m_HasBall = false;
		ResetActionState();
	}

	/// <summary>
	/// リスタートや復帰時にパスチャージ状態と再取得抑制タイマーを初期化します。
	/// </summary>
	public void ResetActionState()
	{
		m_IsPassCharging = false;
		m_PassChargeRatio = 0.0f;
		m_NextPickupRetryTime = 0.0f;
		m_LocalPickupSuppressUntil = 0.0f;
	}

	/// <summary>
	/// 使用するコンポーネントの参照を取得します。
	/// </summary>
	private void Awake()
	{
		m_PlayerController =
			GetComponent<PlayerController>();
		m_PlayerResourceController = GetComponent<PlayerResourceController>();
		m_PlayerAppearance = GetComponent<PlayerAppearance>();
		m_PlayerNetworkProfile = GetComponent<PlayerNetworkProfile>();
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
		TryRequestPickup(
			other,
			true);
	}

	/// <summary>
	/// 再取得禁止時間中にキャッチ範囲へ入り続けていた場合も、
	/// 範囲外へ出ることなく一定間隔で再取得を試します。
	/// </summary>
	/// <param name="other">
	/// 接触中のColliderです。
	/// </param>
	private void OnTriggerStay(
		Collider other)
	{
		TryRequestPickup(
			other,
			false);
	}

	/// <summary>
	/// Owner側から指定ColliderのBall取得を要求します。
	/// </summary>
	/// <param name="other">
	/// 接触対象のColliderです。
	/// </param>
	/// <param name="ignoreRetryInterval">
	/// Triggerへ入った瞬間など、再試行間隔を無視する場合はtrueです。
	/// </param>
	private void TryRequestPickup(
		Collider other,
		bool ignoreRetryInterval)
	{
		if (!IsOwner ||
			m_HasBall ||
			Time.time < m_LocalPickupSuppressUntil)
		{
			return;
		}

		if (!ignoreRetryInterval &&
			Time.time <
				m_NextPickupRetryTime)
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

		m_NextPickupRetryTime =
			Time.time +
			m_PickupRetryInterval;

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

		m_LocalPickupSuppressUntil =
			Time.time +
			m_LocalPickupSuppressDuration;

		RequestShootServerRpc(
			direction.normalized,
			false,
			Vector3.zero);
	}

	/// <summary>
	/// 所持中のボールを照準座標へ射出するようHostへ要求します。
	/// </summary>
	public void ShootAt(Vector3 targetPoint, Vector3 fallbackDirection)
	{
		if (!IsOwner || !m_HasBall || fallbackDirection.sqrMagnitude <= 0.000001f ||
			float.IsNaN(fallbackDirection.sqrMagnitude) || float.IsInfinity(fallbackDirection.sqrMagnitude) ||
			float.IsNaN(targetPoint.sqrMagnitude) || float.IsInfinity(targetPoint.sqrMagnitude))
		{
			return;
		}

		m_LocalPickupSuppressUntil =
			Time.time +
			m_LocalPickupSuppressDuration;

		RequestShootServerRpc(fallbackDirection.normalized, true, targetPoint);
	}

	/// <summary>
	/// Hostで所持権を確認し、通常シュートの方向または照準座標を処理します。
	/// </summary>
	[ServerRpc]
	private void RequestShootServerRpc(Vector3 shootDirection, bool useTargetPoint, Vector3 targetPoint)
	{
		float directionSquared = shootDirection.sqrMagnitude;
		if (directionSquared <= 0.000001f || float.IsNaN(directionSquared) || float.IsInfinity(directionSquared) ||
			(useTargetPoint && (float.IsNaN(targetPoint.sqrMagnitude) || float.IsInfinity(targetPoint.sqrMagnitude))))
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

		bool succeeded = useTargetPoint
			? ballController.TryShootAt(targetPoint, shootDirection)
			: ballController.TryShoot(shootDirection);
		if (!succeeded)
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
	/// ロックオン対象(味方プレイヤーまたは相手ゴール)へ向けて追尾パスを射出します。
	/// 味方はNetworkObject、ゴールは座標で指定します。
	/// </summary>
	/// <param name="targetObject">追尾対象のGameObject</param>
	/// <param name="targetType">追尾対象の種類</param>
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

			m_LocalPickupSuppressUntil =
				Time.time +
				m_LocalPickupSuppressDuration;

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

		m_LocalPickupSuppressUntil =
			Time.time +
			m_LocalPickupSuppressDuration;

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
