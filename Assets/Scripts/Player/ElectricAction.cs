// ElectricAction.cs

using System.Collections;
using System;
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
	[Tooltip("チャージしていない間のゲージ減少速度（1秒あたりの割合）")]
	[SerializeField] private float m_ChargeDecaySpeed = 0.25f;

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

	/// <summary>
	/// サーバー側で敵プレイヤーへスタンを適用した際に通知します(攻撃者ClientId, 被弾者ClientId)。
	/// </summary>
	public static event Action<ulong, ulong> OnServerEnemyStunned;

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

		// 進捗はリセットせず、前回の続きから開始
		m_IsCharging = true;

		m_NextChargeLogTime = 0.0f;
		m_LoggedChargeFull = false;

		Debug.Log(
			$"[Electric Debug] チャージ開始。現在の進捗={ChargeRatio * 100.0f:F0}%");
	}

	/// <summary>
	/// 電撃チャージを更新します。
	/// 有効なロックオン対象が存在するときだけチャージを進めます。
	/// </summary>
	public void UpdateCharge(float deltaTime)
	{
		if (!IsOwner)
			return;

		// チャージ中でない場合は徐々に減少
		if (!m_IsCharging)
		{
			m_ChargeTimer -=
				Mathf.Max(0.0f, deltaTime) *
				m_ChargeDecaySpeed *
				m_ChargeDuration;

			m_ChargeTimer = Mathf.Max(0.0f, m_ChargeTimer);

			return;
		}

		// 有効な対象が存在するか確認
		if (m_LockOnProvider == null ||
			!m_LockOnProvider.HasValidTarget)
		{
			return;
		}

		// 電撃の対象として有効な種類か確認
		ELockOnTargetType targetType =
			m_LockOnProvider.CurrentTargetType;

		if (targetType != ELockOnTargetType.Ball &&
			targetType != ELockOnTargetType.EnemyPlayer)
		{
			return;
		}

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

		// 発射成功時はチャージ進捗をリセット
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

		PlayerBallCarrier attackerCarrier =
			GetComponent<PlayerBallCarrier>();

		PlayerNetworkProfile attackerProfile =
			GetComponent<PlayerNetworkProfile>();

		if (attackerCarrier == null || attackerProfile == null)
			return;

		// 攻撃側がすでにボールを持っている場合は奪取できません。
		if (attackerCarrier.HasBall)
			return;


		// ボールに命中した場合
		if (targetType == ELockOnTargetType.Ball)
		{
			BallController ball =
				targetNetworkObject.GetComponent<BallController>();

			if (ball == null)
				return;

			// 誰も所持していないボールの場合
			// スタン処理は行わず、そのまま引き寄せます。
			if (!ball.HasHolder)
			{
				Debug.Log(
					"[Electric Debug] 未所持ボールに電撃が命中。引き寄せを開始します。");

				bool pullStarted = ball.TryStealHolder(
					OwnerClientId,
					NetworkObjectId);

				if (!pullStarted)
				{
					Debug.Log(
						"[Electric Debug] 未所持ボールの引き寄せに失敗しました。");
				}

				return;
			}

			// 現在の所持者を取得
			PlayerController previousHolder =
				FindPlayerControllerByClientId(ball.CurrentHolderId);

			if (previousHolder == null)
				return;

			PlayerNetworkProfile previousProfile =
				previousHolder.GetComponent<PlayerNetworkProfile>();

			// 敵チームのボールだけ奪取可能
			if (previousProfile == null ||
				previousProfile.Team == ETeamType.None ||
				previousProfile.Team == attackerProfile.Team)
			{
				return;
			}

			Debug.Log(
				"[Electric Debug] ボールに電撃が命中。奪取処理を開始します。");

			TryStealAndStun(ball, previousHolder);

			return;
		}

		// 敵プレイヤーに命中した場合
		if (targetType == ELockOnTargetType.EnemyPlayer)
		{
			PlayerController enemy =
				targetNetworkObject.GetComponent<PlayerController>();

			if (enemy == null || enemy == GetComponent<PlayerController>())
				return;

			PlayerNetworkProfile enemyProfile =
				targetNetworkObject.GetComponent<PlayerNetworkProfile>();

			if (enemyProfile == null ||
				enemyProfile.Team == ETeamType.None ||
				attackerProfile.Team == ETeamType.None)
			{
				return;
			}

			// 味方には電撃を当てない
			if (attackerProfile.Team == enemyProfile.Team)
			{
				Debug.Log(
					"[Electric Debug] 味方への電撃を無効化しました。");
				return;
			}

			PlayerBallCarrier enemyCarrier =
				enemy.GetComponent<PlayerBallCarrier>();

			if (enemyCarrier == null || !enemyCarrier.HasBall)
			{
				Debug.Log(
					"[Electric Debug] 敵はボールを所持していません。スタンしません。");
				return;
			}

			// 敵が所持しているボールを検索
			BallController[] balls =
				FindObjectsByType<BallController>(
					FindObjectsSortMode.None);

			foreach (BallController ball in balls)
			{
				if (ball == null || !ball.HasHolder)
					continue;

				if (ball.CurrentHolderId != enemy.OwnerClientId)
					continue;

				Debug.Log(
					"[Electric Debug] 敵に電撃が命中。ボール奪取を試みます。");

				TryStealAndStun(ball, enemy);

				break;
			}
		}
	}

	/// <summary>
	/// 指定されたClientIdのプレイヤーを検索します。
	/// </summary>
	private PlayerController FindPlayerControllerByClientId(
		ulong clientId)
	{
		if (NetworkManager.Singleton == null ||
			NetworkManager.Singleton.SpawnManager == null)
		{
			return null;
		}

		foreach (NetworkObject networkObject in
			NetworkManager.Singleton.SpawnManager.SpawnedObjectsList)
		{
			if (networkObject == null ||
				networkObject.OwnerClientId != clientId)
			{
				continue;
			}

			PlayerController player =
				networkObject.GetComponent<PlayerController>();

			if (player != null)
				return player;
		}

		return null;
	}

	/// <summary>
	/// ボール奪取を開始し、実際に奪取が完了した場合だけ
	/// 元の所持者にスタンを適用します。
	/// </summary>
	private void TryStealAndStun(
		BallController ball,
		PlayerController previousHolder)
	{
		if (!IsServer ||
			ball == null ||
			previousHolder == null ||
			!ball.HasHolder ||
			ball.IsStealing)
		{
			return;
		}

		// 奪取開始
		bool stealStarted = ball.TryStealHolder(
			OwnerClientId,
			NetworkObjectId);

		if (!stealStarted)
		{
			Debug.Log(
				"[Electric Debug] ボール奪取に失敗しました。スタンしません。");
			return;
		}

		// 実際の奪取完了を待ってからスタンさせます。
		StartCoroutine(
			WaitForStealAndApplyStun(ball, previousHolder));
	}

	/// <summary>
	/// ボールの所持者切り替えが完了するまで待機します。
	/// </summary>
	private IEnumerator WaitForStealAndApplyStun(
		BallController ball,
		PlayerController previousHolder)
	{
		// 電撃による引き寄せ・所持者切り替えの完了を待つ
		while (ball != null &&
			   ball.IsSpawned &&
			   ball.IsStealing)
		{
			yield return null;
		}

		// ボールが消滅した場合は終了
		if (ball == null || !ball.IsSpawned)
			yield break;

		// 実際に攻撃者が所持者になったか確認
		if (!ball.HasHolder ||
			ball.CurrentHolderId != OwnerClientId)
		{
			Debug.Log(
				"[Electric Debug] ボール奪取が完了しませんでした。スタンしません。");
			yield break;
		}

		// 元の所持者がまだ有効か確認
		if (previousHolder == null ||
			!previousHolder.IsSpawned)
		{
			yield break;
		}

		// 奪取成功後にだけスタンを適用
		previousHolder.ApplyStunOwnerRpc(m_StunDuration);

		OnServerEnemyStunned?.Invoke(
			OwnerClientId,
			previousHolder.OwnerClientId);

		Debug.Log(
			$"[Electric Debug] ボール奪取成功。敵をスタンさせました。Duration={m_StunDuration}");
	}
}
