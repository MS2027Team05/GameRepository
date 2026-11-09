// ElectricAction.cs

using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 電撃アクションのチャージ・発射・命中処理を管理します。
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class ElectricAction : NetworkBehaviour, IElectricAction
{
	[Header("チャージ設定")]
	[SerializeField] private float m_ChargeDuration = 1.5f;
	[SerializeField] private float m_RequiredCost = 20.0f;

	[Header("電撃設定")]
	[SerializeField] private float m_StunDuration = 2.0f;
	[SerializeField] private float m_MaxHitDistance = 30.0f;

	private ILockOnProvider m_LockOnProvider;
	private IPlayerResource m_PlayerResource;

	private float m_ChargeTimer;
	private bool m_IsCharging;


	// チャージ進捗ログの出力間隔
	private float m_NextChargeLogTime;

	// 満タンログを一度だけ出すためのフラグ
	private bool m_LoggedChargeFull;



	public bool IsCharging => m_IsCharging;

	public float ChargeRatio =>
		m_ChargeDuration <= 0.0f
			? 1.0f
			: Mathf.Clamp01(m_ChargeTimer / m_ChargeDuration);

	public float RequiredCost => m_RequiredCost;

	private void Awake()
	{
		m_LockOnProvider = GetComponent<ILockOnProvider>();
		m_PlayerResource = GetComponent<IPlayerResource>();
	}

	/// <summary>
	/// 電撃チャージを開始します。
	/// </summary>
	public void BeginCharge()
	{
		if (!IsOwner)
		{
			Debug.LogWarning(
				"[Electric Debug] チャージ開始失敗：Ownerではありません。");
			return;
		}

		m_ChargeTimer = 0.0f;
		m_IsCharging = true;

		m_NextChargeLogTime = 0.0f;
		m_LoggedChargeFull = false;

		Debug.Log(
			$"[Electric Debug] チャージ開始。必要時間={m_ChargeDuration:F2}秒");
	}

	/// <summary>
	/// 電撃チャージを更新します。
	/// </summary>
	public void UpdateCharge(float deltaTime)
	{
		if (!IsOwner || !m_IsCharging)
			return;

		float previousRatio = ChargeRatio;

		m_ChargeTimer += Mathf.Max(0.0f, deltaTime);

		m_ChargeTimer = Mathf.Min(
			m_ChargeTimer,
			Mathf.Max(0.0f, m_ChargeDuration));

		float currentRatio = ChargeRatio;

		// チャージ満タン
		if (currentRatio >= 1.0f)
		{
			if (!m_LoggedChargeFull)
			{
				m_LoggedChargeFull = true;

				Debug.Log(
					"[Electric Debug] チャージ満タン。左クリックを離すと発射します。");
			}

			return;
		}

		// 0.25秒ごとにチャージ進捗を表示
		if (Time.time >= m_NextChargeLogTime)
		{
			m_NextChargeLogTime = Time.time + 0.25f;

			Debug.Log(
				$"[Electric Debug] チャージ中 " +
				$"時間={m_ChargeTimer:F2}/{m_ChargeDuration:F2}秒 " +
				$"進捗={currentRatio * 100.0f:F0}%");
		}
	}

	/// <summary>
	/// チャージをキャンセルします。
	/// </summary>
	public void CancelCharge()
	{
		if (m_IsCharging)
		{
			Debug.Log(
				$"[Electric Debug] チャージキャンセル。 " +
				$"進捗={ChargeRatio * 100.0f:F0}%");
		}

		m_IsCharging = false;
		m_ChargeTimer = 0.0f;

		m_NextChargeLogTime = 0.0f;
		m_LoggedChargeFull = false;
	}
	/// <summary>
	/// チャージ完了後、ロックオン対象へ電撃を発射します。
	/// </summary>
	public bool TryReleaseAndFire()
	{
		Debug.Log(
			$"[Electric Debug] 発射判定開始 " +
			$"IsOwner={IsOwner}, " +
			$"IsCharging={m_IsCharging}, " +
			$"ChargeRatio={ChargeRatio:F2}");

		if (!IsOwner)
		{
			Debug.LogWarning(
				"[Electric Debug] Ownerではないため発射できません。");
			return false;
		}

		if (!m_IsCharging)
		{
			Debug.LogWarning(
				"[Electric Debug] チャージ状態ではありません。");
			return false;
		}

		// チャージが完了していなければ発射しない
		if (ChargeRatio < 1.0f)
		{
			Debug.LogWarning(
				$"[Electric Debug] チャージ不足です。現在の割合={ChargeRatio:F2}");

			CancelCharge();
			return false;
		}

		if (m_LockOnProvider == null)
		{
			Debug.LogError(
				"[Electric Debug] LockOnProviderが取得できていません。");

			CancelCharge();
			return false;
		}

		if (!m_LockOnProvider.HasValidTarget)
		{
			Debug.LogWarning(
				"[Electric Debug] 有効なロックオン対象がありません。");

			CancelCharge();
			return false;
		}

		ELockOnTargetType targetType =
			m_LockOnProvider.CurrentTargetType;

		GameObject target =
			m_LockOnProvider.CurrentTargetObject;

		Debug.Log(
			$"[Electric Debug] 対象確認 " +
			$"TargetType={targetType}, " +
			$"Target={(target != null ? target.name : "NULL")}");

		if (target == null ||
			(targetType != ELockOnTargetType.Ball &&
			 targetType != ELockOnTargetType.EnemyPlayer))
		{
			Debug.LogWarning(
				"[Electric Debug] 対象がボールまたは敵プレイヤーではありません。");

			CancelCharge();
			return false;
		}

		NetworkObject targetNetworkObject =
			FindTargetNetworkObject(target, targetType);

		if (targetNetworkObject == null)
		{
			Debug.LogWarning(
				"[Electric Debug] 対象のNetworkObjectを取得できません。");

			CancelCharge();
			return false;
		}

		if (!targetNetworkObject.IsSpawned)
		{
			Debug.LogWarning(
				"[Electric Debug] 対象のNetworkObjectがSpawnされていません。");

			CancelCharge();
			return false;
		}

		ulong targetId =
			targetNetworkObject.NetworkObjectId;

		m_IsCharging = false;
		m_ChargeTimer = 0.0f;

		Debug.Log(
			$"[Electric Debug] 発射成功。ServerRpc送信 TargetId={targetId}");

		RequestElectricHitServerRpc(targetId, targetType);

		return true;
	}

	/// <summary>
	/// 対象のNetworkObjectを取得します。
	/// </summary>
	private NetworkObject FindTargetNetworkObject(
		GameObject target,
		ELockOnTargetType targetType)
	{
		if (targetType == ELockOnTargetType.Ball)
		{
			BallController ball =
				target.GetComponentInParent<BallController>();

			return ball != null
				? ball.NetworkObject
				: null;
		}

		if (targetType == ELockOnTargetType.EnemyPlayer)
		{
			PlayerController player =
				target.GetComponentInParent<PlayerController>();

			return player != null
				? player.NetworkObject
				: null;
		}

		return null;
	}

	/// <summary>
	/// Host側で電撃命中処理を実行します。
	/// </summary>
	[Rpc(SendTo.Server)]
	private void RequestElectricHitServerRpc(
		ulong targetNetworkObjectId,
		ELockOnTargetType targetType)
	{
		if (!IsServer)
			return;

		if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(
			targetNetworkObjectId,
			out NetworkObject targetNetworkObject))
		{
			return;
		}

		// 攻撃者の位置を取得
		Vector3 attackerPosition = transform.position;

		// 対象までの距離をサーバー側でも確認
		float distance = Vector3.Distance(
			attackerPosition,
			targetNetworkObject.transform.position);

		if (distance > m_MaxHitDistance)
			return;

		// ボールに命中した場合
		if (targetType == ELockOnTargetType.Ball)
		{
			BallController ball =
				targetNetworkObject.GetComponent<BallController>();

			PlayerBallCarrier attackerCarrier =
				GetComponent<PlayerBallCarrier>();

			if (ball == null || attackerCarrier == null)
			{
				Debug.LogWarning(
					"[Electric Debug] ボール奪取失敗：BallControllerまたはPlayerBallCarrierがありません。");
				return;
			}

			// 攻撃者がすでにボールを持っていないことを確認
			if (attackerCarrier.HasBall)
			{
				Debug.LogWarning(
					"[Electric Debug] ボール奪取失敗：すでにボールを所持しています。");
				return;
			}

			Debug.Log(
				"[Electric Debug] ボールに電撃が命中。奪取処理を開始します。");

			ball.TryStealHolder(
				OwnerClientId,
				NetworkObjectId);

			return;
		}

		// 敵プレイヤーに命中した場合
		if (targetType == ELockOnTargetType.EnemyPlayer)
		{
			PlayerController enemy =
				targetNetworkObject.GetComponent<PlayerController>();

			if (enemy == null)
			{
				Debug.LogWarning(
					"[Electric Debug] 敵のPlayerControllerが見つかりません。");
				return;
			}

			// 自分自身には命中させない
			if (enemy == this)
			{
				return;
			}

			PlayerNetworkProfile attackerProfile =
				GetComponent<PlayerNetworkProfile>();

			PlayerNetworkProfile enemyProfile =
				targetNetworkObject.GetComponent<PlayerNetworkProfile>();

			if (attackerProfile == null || enemyProfile == null)
			{
				Debug.LogWarning(
					"[Electric Debug] チーム判定用Profileが見つかりません。");
				return;
			}

			// チーム情報で味方・敵を判定
			if (attackerProfile.Team == ETeamType.None ||
				enemyProfile.Team == ETeamType.None)
			{
				Debug.LogWarning(
					"[Electric Debug] チームが設定されていません。");
				return;
			}

			// 味方には電撃を当てない
			if (attackerProfile.Team == enemyProfile.Team)
			{
				Debug.Log(
					"[Electric Debug] 味方への電撃を無効化しました。");
				return;
			}

			Debug.Log(
				$"[Electric Debug] 敵に電撃が命中。Enemy={enemy.name}");

			// 攻撃側のボール所持状態を確認
			PlayerBallCarrier attackerCarrier =
				GetComponent<PlayerBallCarrier>();

			// 敵側のボール所持状態を確認
			PlayerBallCarrier enemyCarrier =
				enemy.GetComponent<PlayerBallCarrier>();

			if (attackerCarrier == null || enemyCarrier == null)
			{
				Debug.LogWarning(
					"[Electric Debug] PlayerBallCarrierが見つかりません。");
				return;
			}

			// 攻撃側がボールを持っていない場合のみ奪取を試みる
			if (!attackerCarrier.HasBall && enemyCarrier.HasBall)
			{
				BallController[] balls =
					FindObjectsOfType<BallController>();

				foreach (BallController ball in balls)
				{
					if (ball == null || !ball.HasHolder)
					{
						continue;
					}

					// 対象の敵が所持しているボールを検索
					if (ball.CurrentHolderId != enemy.OwnerClientId)
					{
						continue;
					}

					bool stolen = ball.TryStealHolder(
						OwnerClientId,
						NetworkObjectId);

					Debug.Log(
						$"[Electric Debug] 敵からボールを奪取: {stolen}");

					break;
				}
			}

			// ボール奪取の成否にかかわらず敵をスタンさせる
			enemy.ApplyStunOwnerRpc(m_StunDuration);

			Debug.Log(
				$"[Electric Debug] 敵をスタンさせました。Duration={m_StunDuration}");
		}
	}
}
