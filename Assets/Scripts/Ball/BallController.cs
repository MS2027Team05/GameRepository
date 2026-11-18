// BallController.cs
using System;
using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// ボール本体の所持状態と物理状態を管理します。
/// </summary>
/// <remarks>
/// 主な処理の流れ:
/// 1. Hostがプレイヤーからの取得要求を検証します。
/// 2. 取得成功時はプレイヤーへNetworkObjectを親子付けします。
/// 3. 所持状態をNetworkVariableで全Clientへ同期します。
/// 4. シュート時は親子関係を解除して物理挙動へ戻します。
/// 5. 強制解放時も同じく親子関係を解除します。
/// 6. シュート・解放直後は元所持者のみ一定時間再キャッチを禁止します。
/// </remarks>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(NetworkTransform))]
public class BallController : NetworkBehaviour
{
	[Header("ボールの当たり判定")]
	[Tooltip("ボールの物理衝突とキャッチ判定に使用するColliderです。")]
	[SerializeField] private Collider m_PhysicsCollider;
	[Tooltip("ボール周辺の検知に使用するSensor用Colliderです。")]
	[SerializeField] private Collider m_SensorCollider;

	[Header("所持中の描画")]
	[Tooltip("ボール直下にある描画専用の親Transformです。ColliderやNetworkObjectは含めず、所持中はプレイヤーの所持ジョイントへ移動します。")]
	[SerializeField] private Transform m_VisualRoot;

	[Header("シュート設定")]
	[Tooltip("ボールを射出するときの初速度に使用する値です(おおむねm/s)。")]
	[SerializeField] private float m_ShootPower;

	[Header("再キャッチ防止")]
	[Tooltip("射出または強制解放した元の所持者が、再びキャッチできるまでの時間です(秒)。")]
	[SerializeField] private float m_RepickupDelay;

	[Header("味方パス受取ボーナス判定")]
	[Tooltip("味方パス回復の資格を失効させるボール速度のしきい値です(m/s)。この速度以下になると資格が失われ、再加速しても復活しません。")]
	[SerializeField] private float m_PassBonusMinSpeed;

	[Header("電撃奪取設定")]
	[Tooltip("電撃でボールを奪ったとき、攻撃側プレイヤーまで移動する時間です(秒)。")]
	[SerializeField] private float m_ElectricStealPullDuration = 0.5f;

	private readonly NetworkVariable<ulong> m_CurrentHolderId =
		new NetworkVariable<ulong>();

	private readonly NetworkVariable<ulong>
		m_CurrentHolderNetworkObjectId =
			new NetworkVariable<ulong>();

	private readonly NetworkVariable<bool> m_HasHolder =
		new NetworkVariable<bool>();

	private readonly NetworkVariable<bool> m_IsShotInFlight =
		new NetworkVariable<bool>();

	private Rigidbody m_Rigidbody;
	private NetworkTransform m_NetworkTransform;
	private BallPhysicsController m_BallPhysics;
	private BallHoldAttachment m_HoldAttachment;
	private PlayerBallCarrier m_AppliedHolder;
	private Vector3 m_InitialPosition;
	private Quaternion m_InitialRotation;
	public bool IsMatchPaused { get; private set; }

	private bool m_HasRepickupLock;
	private ulong m_LastHolderClientId;
	private float m_RepickupUnlockTime;

	private bool m_IsStealing;
	private Coroutine m_ElectricStealCoroutine;

	// Host側で、最後の正常なシュートが味方パスとして受取可能か記録します。
	private bool m_IsPassInFlight;
	private ETeamType m_LastPasserTeam;
	private ulong m_LastPasserClientId;
	private ulong m_LastAssistClientId;
	private int m_CurrentPassChainCount;
	private bool m_IsGoalHomingInFlight;
	private Vector3 m_LastReleaseOrigin;
	private bool m_WasFriendlyPassAssisted;

	/// <summary>
	/// ボールがシュートされたときにHost側で通知します。
	/// </summary>
	public event Action OnShot;

	/// <summary>
	/// タックルなどによってボールが強制解放されたときに
	/// Host側で通知します。
	/// </summary>
	public event Action OnReleased;

	/// <summary>
	/// Host側で味方へのパスが成立したときに通知します(送り手ClientId, 受け手ClientId, パス距離)。
	/// </summary>
	public event Action<ulong, ulong, float> FriendlyPassCompleted;

	/// <summary>
	/// Host側で相手チームの飛翔中ボールをキャッチしてカット/セーブしたときに通知します(取得者ClientId, 取得者チーム, 取得座標, ゴール追尾シュートだったか)。
	/// </summary>
	public event Action<ulong, ETeamType, Vector3, bool> EnemyShotIntercepted;

	/// <summary>
	/// Host側で相手保持者からボールを直接奪取したときに通知します(奪取者ClientId, 奪取者チーム, 奪取座標)。
	/// </summary>
	public event Action<ulong, ETeamType, Vector3> BallStolenFromEnemy;

	/// <summary>
	/// Host側でボールを投げたときに通知します(投球者ClientId, 味方へのロックオンパスの場合はtrue)。
	/// </summary>
	public event Action<ulong, bool> BallThrownServer;

	/// <summary>
	/// 現在ボールを所持しているプレイヤーのClientIdを取得します。
	/// </summary>
	public ulong CurrentHolderId
	{
		get
		{
			return m_CurrentHolderId.Value;
		}
	}

	/// <summary>
	/// 現在ボールに所持者が存在するか取得します。
	/// </summary>
	public bool HasHolder
	{
		get
		{
			return m_HasHolder.Value;
		}
	}

	/// <summary>
	/// 投球後、停止または所持されるまでの軌跡表示対象か取得します。
	/// </summary>
	public bool IsShotInFlight => IsSpawned && !HasHolder && m_IsShotInFlight.Value;

	/// <summary>
	/// サーバー側で最後にボールを保持または放したプレイヤーのClientIdを取得します。
	/// </summary>
	public ulong LastInteractingClientId => m_HasHolder.Value ? m_CurrentHolderId.Value : m_LastHolderClientId;

	/// <summary>
	/// サーバー側で最後にシュート・パス・解放が行われた座標を取得します。
	/// </summary>
	public Vector3 LastReleaseOrigin => m_HasHolder.Value ? VisualTransform.position : m_LastReleaseOrigin;

	/// <summary>
	/// 描画基準となるTransformを取得します(未設定時は本体Transform)。
	/// </summary>
	public Transform VisualTransform => m_VisualRoot != null ? m_VisualRoot : transform;

	/// <summary>
	/// VisualRootの初期ローカル座標を取得します。
	/// </summary>
	public Vector3 VisualDefaultLocalPosition => (m_HoldAttachment != null && m_HoldAttachment.HasVisualRoot)
		? m_HoldAttachment.OriginalLocalPosition
		: Vector3.zero;

	/// <summary>
	/// VisualRootの初期ローカル回転を取得します。
	/// </summary>
	public Quaternion VisualDefaultLocalRotation => (m_HoldAttachment != null && m_HoldAttachment.HasVisualRoot)
		? m_HoldAttachment.OriginalLocalRotation
		: Quaternion.identity;

	/// <summary>
	/// VisualRootの初期ローカルスケールを取得します。
	/// </summary>
	public Vector3 VisualDefaultLocalScale => (m_HoldAttachment != null && m_HoldAttachment.HasVisualRoot)
		? m_HoldAttachment.OriginalLocalScale
		: Vector3.one;

	/// <summary>
	/// サーバー側で直近のプレーが味方パスを経由していたか取得します。
	/// </summary>
	public bool WasFriendlyPassAssisted => m_WasFriendlyPassAssisted || m_IsPassInFlight;

	/// <summary>
	/// サーバー側で直前に味方パスを出したプレイヤー(アシスト候補)のClientIdを取得します。
	/// </summary>
	public ulong LastAssistClientId => m_LastAssistClientId;

	/// <summary>
	/// サーバー側で現在の攻撃ポゼッションにおける連続パス成功回数を取得します。
	/// </summary>
	public int CurrentPassChainCount => m_CurrentPassChainCount;

	/// <summary>
	/// Rigidbodyの参照を取得します。
	/// </summary>
	private void Awake()
	{
		m_Rigidbody =
			GetComponent<Rigidbody>();
		m_NetworkTransform = GetComponent<NetworkTransform>();
		m_BallPhysics = GetComponent<BallPhysicsController>();
		m_HoldAttachment = new BallHoldAttachment(transform, m_VisualRoot);
	}

	/// <summary>
	/// NetworkObjectのSpawn時に所持状態の変更通知を登録し、
	/// 現在の所持状態を反映します。
	/// </summary>
	public override void OnNetworkSpawn()
	{
		if (IsServer)
		{
			m_IsShotInFlight.Value = false;
			m_InitialPosition = transform.position;
			m_InitialRotation = transform.rotation;
			m_LastReleaseOrigin = m_InitialPosition;
			m_WasFriendlyPassAssisted = false;
			SetMatchPaused(MatchManager.Instance != null && MatchManager.Instance.MatchState != MatchManager.EMatchState.Playing);
		}
		m_HasHolder.OnValueChanged +=
			HandleHolderStateChanged;
		m_CurrentHolderNetworkObjectId.OnValueChanged += HandleHolderObjectChanged;

		if (m_HasHolder.Value)
		{
			ApplyHolderState(true);
		}
	}

	/// <summary>
	/// アニメーション反映後に、所持ジョイントへ描画とサーバー側のボール本体を合わせます。
	/// </summary>
	private void LateUpdate()
	{
		if (!IsSpawned || !HasHolder)
		{
			return;
		}

		// 途中参加などで所持者が後からSpawnした場合は、参照解決を再試行します。
		if (m_AppliedHolder == null)
		{
			RefreshHolderAttachment();
		}

		if (m_AppliedHolder == null || m_AppliedHolder.BallHoldPoint == null)
		{
			return;
		}

		m_HoldAttachment.Attach(m_AppliedHolder.BallHoldPoint);
		if (IsServer)
		{
			UpdateHeldPose();
		}
	}

	private void FixedUpdate()
	{
		if (!IsSpawned || !IsServer)
		{
			return;
		}

		UpdatePassBonusEligibility();
		UpdateShotInFlight();
	}

	/// <summary>
	/// ホストで停止を判定し、停止後は次の投球まで軌跡表示を再開しません。
	/// </summary>
	private void UpdateShotInFlight()
	{
		if (!m_IsShotInFlight.Value)
		{
			return;
		}

		bool isMoving = m_BallPhysics != null
			? m_BallPhysics.IsMoving
			: m_Rigidbody.linearVelocity.sqrMagnitude > Mathf.Epsilon;
		if (HasHolder || IsMatchPaused || !isMoving)
		{
			m_IsShotInFlight.Value = false;
		}
	}

	/// <summary>
	/// 射出後のボール速度が設定値以下になったら、味方パスの回復資格を失効させます。
	/// 一度失効した資格は、ボールが再加速しても復活しません。
	/// </summary>
	private void UpdatePassBonusEligibility()
	{
		if (!m_IsPassInFlight || m_Rigidbody == null)
		{
			return;
		}

		float minimumSpeed = Mathf.Max(0.0f, m_PassBonusMinSpeed);
		if (m_Rigidbody.linearVelocity.sqrMagnitude > minimumSpeed * minimumSpeed)
		{
			return;
		}

		m_IsPassInFlight = false;
		m_LastPasserTeam = ETeamType.None;
	}

	/// <summary>
	/// NetworkObjectのDespawn時に所持状態の変更通知を解除します。
	/// </summary>
	public override void OnNetworkDespawn()
	{
		m_HasHolder.OnValueChanged -=
			HandleHolderStateChanged;
		m_CurrentHolderNetworkObjectId.OnValueChanged -= HandleHolderObjectChanged;
		ClearHolderAttachment();
	}

	/// <summary>
	/// 指定されたColliderがキャッチ判定用Colliderか確認します。
	/// </summary>
	/// <param name="targetCollider">
	/// 判定対象のColliderです。
	/// </param>
	/// <returns>
	/// キャッチ判定用Colliderの場合はtrueです。
	/// </returns>
	public bool IsCatchCollider(
		Collider targetCollider)
	{
		return targetCollider ==
			m_PhysicsCollider;
	}

	/// <summary>
	/// 指定されたColliderがSensor用Colliderか確認します。
	/// </summary>
	/// <param name="targetCollider">
	/// 判定対象のColliderです。
	/// </param>
	/// <returns>
	/// Sensor用Colliderの場合はtrueです。
	/// </returns>
	public bool IsSensorCollider(
		Collider targetCollider)
	{
		return targetCollider ==
			m_SensorCollider;
	}

	/// <summary>
	/// Host上で指定プレイヤーをボールの所持者として設定します。
	/// </summary>
	public bool TrySetHolder(
		ulong holderClientId,
		ulong holderNetworkObjectId)
	{
		if (!IsServer || !CanPlayMatch() || m_IsStealing)
		{
			return false;
		}

		if (m_HasHolder.Value)
		{
			return false;
		}

		if (m_HasRepickupLock &&
			Time.time >= m_RepickupUnlockTime)
		{
			m_HasRepickupLock = false;
		}

		if (m_HasRepickupLock &&
			holderClientId == m_LastHolderClientId)
		{
			return false;
		}

		if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(
			holderNetworkObjectId,
			out NetworkObject holderNetworkObject))
		{
			return false;
		}

		if (holderNetworkObject.OwnerClientId !=
			holderClientId)
		{
			return false;
		}

		PlayerBallCarrier holder =
			holderNetworkObject.GetComponent<PlayerBallCarrier>();

		if (holder == null ||
			holder.BallHoldPoint == null)
		{
			return false;
		}

		// キャッチ直前の速度を確認します。AttachToHolder後は速度が0になるため先に判定。
		UpdatePassBonusEligibility();

		if (!AttachToHolder(
			holderNetworkObject,
			holder))
		{
			return false;
		}

		m_CurrentHolderId.Value =
			holderClientId;

		m_CurrentHolderNetworkObjectId.Value =
			holderNetworkObjectId;

		m_HasHolder.Value =
			true;
		m_IsShotInFlight.Value = false;

		// 所持確定時に一度だけ判定し、強制ドロップや敵からの取得では付与しません。
		bool wasPassInFlight = m_IsPassInFlight;
		ETeamType previousPasserTeam = m_LastPasserTeam;
		ulong previousPasserClientId = m_LastPasserClientId;
		bool wasGoalHoming = m_IsGoalHomingInFlight;

		PlayerNetworkProfile receiverProfile = holderNetworkObject.GetComponent<PlayerNetworkProfile>();
		bool isFriendlyPass = wasPassInFlight &&
			holderClientId != previousPasserClientId &&
			previousPasserTeam != ETeamType.None &&
			receiverProfile != null &&
			receiverProfile.Team == previousPasserTeam;

		m_IsPassInFlight = false;
		m_LastPasserTeam = ETeamType.None;
		m_IsGoalHomingInFlight = false;
		m_WasFriendlyPassAssisted = isFriendlyPass;

		if (isFriendlyPass)
		{
			m_LastAssistClientId = previousPasserClientId;
			m_CurrentPassChainCount++;
			float passDistance = Vector3.Distance(m_LastReleaseOrigin, holderNetworkObject.transform.position);
			FriendlyPassCompleted?.Invoke(previousPasserClientId, holderClientId, passDistance);
			holder.NotifyFriendlyPassReceivedServer();
		}
		else
		{
			m_LastAssistClientId = 0;
			m_CurrentPassChainCount = 0;

			if (wasPassInFlight &&
				previousPasserTeam != ETeamType.None &&
				receiverProfile != null &&
				receiverProfile.Team != ETeamType.None &&
				receiverProfile.Team != previousPasserTeam)
			{
				EnemyShotIntercepted?.Invoke(
					holderClientId,
					receiverProfile.Team,
					holderNetworkObject.transform.position,
					wasGoalHoming);
			}
		}

		return true;
	}

	/// <summary>
	/// Host上で現在の所持者からボールを奪い、
	/// 指定プレイヤーへ引き寄せて所持者を切り替えます。
	/// </summary>
	public bool TryStealHolder(
		ulong newHolderClientId,
		ulong newHolderNetworkObjectId)
	{
		if (!IsServer || m_IsStealing || !CanPlayMatch())
			return false;

		// 攻撃側プレイヤーを取得
		if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(
			newHolderNetworkObjectId,
			out NetworkObject newHolderNetworkObject))
		{
			return false;
		}

		if (newHolderNetworkObject.OwnerClientId != newHolderClientId)
			return false;

		PlayerBallCarrier newHolder =
			newHolderNetworkObject.GetComponent<PlayerBallCarrier>();

		if (newHolder == null ||
			newHolder.BallHoldPoint == null ||
			newHolder.HasBall)
		{
			return false;
		}

		bool stolenFromEnemy = false;
		ETeamType newHolderTeam = ETeamType.None;

		// 現在の所持者がいる場合だけ、所持状態を解除
		if (m_HasHolder.Value)
		{
			if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(
				m_CurrentHolderNetworkObjectId.Value,
				out NetworkObject oldHolderNetworkObject))
			{
				return false;
			}

			PlayerBallCarrier oldHolder =
				oldHolderNetworkObject.GetComponent<PlayerBallCarrier>();

			if (oldHolder == null)
				return false;

			PlayerNetworkProfile oldProfile = oldHolderNetworkObject.GetComponent<PlayerNetworkProfile>();
			PlayerNetworkProfile newProfile = newHolderNetworkObject.GetComponent<PlayerNetworkProfile>();
			if (oldProfile != null && newProfile != null &&
				oldProfile.Team != ETeamType.None &&
				newProfile.Team != ETeamType.None &&
				oldProfile.Team != newProfile.Team)
			{
				stolenFromEnemy = true;
				newHolderTeam = newProfile.Team;
			}

			if (!DetachFromHolder())
				return false;

			// 元の所持者の所持状態を解除
			m_HasHolder.Value = false;
			m_CurrentHolderId.Value = 0;
			m_CurrentHolderNetworkObjectId.Value = 0;
			oldHolder.SetBallState(false);
		}

		m_IsPassInFlight = false;
		m_IsShotInFlight.Value = false;
		m_LastPasserTeam = ETeamType.None;
		m_LastPasserClientId = 0;
		m_LastAssistClientId = 0;
		m_CurrentPassChainCount = 0;
		m_IsGoalHomingInFlight = false;
		m_WasFriendlyPassAssisted = false;
		m_BallPhysics?.ResetMotionState();

		// 移動中は物理挙動と当たり判定を停止
		m_Rigidbody.linearVelocity = Vector3.zero;
		m_Rigidbody.angularVelocity = Vector3.zero;
		m_Rigidbody.isKinematic = true;

		if (m_PhysicsCollider != null)
		{
			m_PhysicsCollider.enabled = false;
		}

		// 引き寄せ開始
		m_IsStealing = true;

		m_ElectricStealCoroutine =
			StartCoroutine(ElectricStealPullCoroutine(
				newHolderNetworkObject,
				newHolder,
				newHolderClientId,
				newHolderNetworkObjectId,
				stolenFromEnemy,
				newHolderTeam));

		return true;
	}

	/// <summary>
	/// 電撃で奪ったボールを攻撃側プレイヤーまで引き寄せます。
	/// </summary>
	private IEnumerator ElectricStealPullCoroutine(
		NetworkObject newHolderNetworkObject,
		PlayerBallCarrier newHolder,
		ulong newHolderClientId,
		ulong newHolderNetworkObjectId,
		bool stolenFromEnemy,
		ETeamType newHolderTeam)
	{
		Debug.Log("[BallController] 電撃による引き寄せ開始");
		Vector3 startPosition = transform.position;
		float elapsedTime = 0f;

		float duration = Mathf.Max(
			0.01f,
			m_ElectricStealPullDuration);

		while (elapsedTime < duration)
		{
			// 試合終了やプレイヤー消失時は引き寄せを中止
			if (!CanPlayMatch() ||
				newHolderNetworkObject == null ||
				!newHolderNetworkObject.IsSpawned ||
				newHolder == null ||
				newHolder.BallHoldPoint == null)
			{
				break;
			}

			elapsedTime += Time.deltaTime;

			float t = Mathf.Clamp01(elapsedTime / duration);

			// 最初と最後がゆっくりになる補間
			float smoothT = Mathf.SmoothStep(0f, 1f, t);

			// プレイヤーの移動にも追従するよう、目的地を毎フレーム取得
			Vector3 targetPosition =
				newHolder.BallHoldPoint.position;

			transform.position = Vector3.Lerp(
				startPosition,
				targetPosition,
				smoothT);

			Debug.Log(
	$"[BallController] 引き寄せ中 Position: {transform.position}");

			yield return null;
		}

		// 移動先が有効なら、最後に所持位置へ装着
		if (CanPlayMatch() &&
			newHolderNetworkObject != null &&
			newHolderNetworkObject.IsSpawned &&
			newHolder != null &&
			newHolder.BallHoldPoint != null)
		{
			if (AttachToHolder(
				newHolderNetworkObject,
				newHolder))
			{
				// 所持者情報を更新
				m_CurrentHolderId.Value =
					newHolderClientId;

				m_CurrentHolderNetworkObjectId.Value =
					newHolderNetworkObjectId;

				// 新しい所持者の状態を設定
				m_HasHolder.Value = true;
				m_IsShotInFlight.Value = false;

				// 所持者の描画・PlayerBallCarrier状態はRefreshHolderAttachmentで更新します。
				m_WasFriendlyPassAssisted = false;
				m_LastAssistClientId = 0;
				m_CurrentPassChainCount = 0;
				m_IsGoalHomingInFlight = false;

				if (stolenFromEnemy)
				{
					BallStolenFromEnemy?.Invoke(newHolderClientId, newHolderTeam, newHolderNetworkObject.transform.position);
				}

				Debug.Log("[BallController] 引き寄せ完了");

				m_IsStealing = false;
				m_ElectricStealCoroutine = null;
				yield break;
			}
		}

		// 装着できなかった場合は、ボールを自由状態に戻す
		m_CurrentHolderId.Value = 0;
		m_CurrentHolderNetworkObjectId.Value = 0;

		m_Rigidbody.isKinematic = IsMatchPaused;

		if (m_PhysicsCollider != null)
		{
			m_PhysicsCollider.enabled = true;
		}

		m_IsStealing = false;
		m_ElectricStealCoroutine = null;
	}

	/// <summary>
	/// Transformを持つ対象への追尾パス。
	/// 主に味方プレイヤー用。
	/// </summary>
	public bool TryStartHomingPass(Transform target)
	{
		if (target == null)
		{
			return false;
		}

		return StartHomingPassInternal(
			target,
			Vector3.zero,
			true);
	}

	/// <summary>
	/// 固定座標への追尾パス。
	/// 主に相手ゴール用。
	/// </summary>
	public bool TryStartHomingPass(Vector3 targetPosition)
	{
		return StartHomingPassInternal(
			null,
			targetPosition,
			false);
	}

	/// <summary>
	/// 追尾パスの共通処理。
	/// </summary>
	private bool StartHomingPassInternal(
		Transform target,
		Vector3 targetPosition,
		bool useTransform)
	{
		if (!IsServer || !CanPlayMatch() || m_IsStealing)
		{
			return false;
		}

		if (!m_HasHolder.Value)
		{
			return false;
		}

		if (m_BallPhysics == null)
		{
			Debug.LogError(
				"[BallController] BallPhysicsControllerが見つかりません。",
				this);

			return false;
		}

		ulong previousHolderClientId =
			m_CurrentHolderId.Value;

		ETeamType passerTeam = ETeamType.None;

		if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(
			m_CurrentHolderNetworkObjectId.Value,
			out NetworkObject previousHolderObject))
		{
			PlayerNetworkProfile profile =
				previousHolderObject.GetComponent<PlayerNetworkProfile>();

			if (profile != null)
			{
				passerTeam = profile.Team;
			}
		}

		StartRepickupLock(previousHolderClientId);

		if (!DetachFromHolder())
		{
			m_HasRepickupLock = false;
			return false;
		}

		m_HasHolder.Value = false;
		m_CurrentHolderId.Value = 0;

		m_LastPasserTeam = passerTeam;
		m_LastPasserClientId = previousHolderClientId;
		m_IsPassInFlight = true;
		m_IsShotInFlight.Value = true;
		m_IsGoalHomingInFlight = !useTransform;
		m_LastReleaseOrigin = transform.position;

		// 対象の種類によって追尾方法を切り替える
		if (useTransform)
		{
			m_BallPhysics.BeginHoming(target);
		}
		else
		{
			m_BallPhysics.BeginHoming(targetPosition);
		}

		// 味方へのロックオンパス(useTransform=true)とそれ以外のシュート/投げを区別して通知します。
		BallThrownServer?.Invoke(previousHolderClientId, useTransform);

		Debug.Log(
			"[BallController] 追尾パス開始: " +
			(useTransform ? target.name : "EnemyGoal"));

		return true;
	}
	/// <summary>
	/// Host上で所持中のボールを指定方向へ射出します。
	/// </summary>
	public bool TryShoot(
		Vector3 shootDirection)
	{
		if (!IsServer || !CanPlayMatch() || m_IsStealing)
		{
			return false;
		}

		if (!m_HasHolder.Value)
		{
			return false;
		}

		if (shootDirection.sqrMagnitude <=
			Mathf.Epsilon)
		{
			return false;
		}

		shootDirection.Normalize();

		ulong previousHolderClientId =
			m_CurrentHolderId.Value;

		// TryShoot成功時のみ使用するパス元チームを先に取得しておきます。
		ETeamType passerTeam = ETeamType.None;
		if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(
			m_CurrentHolderNetworkObjectId.Value,
			out NetworkObject previousHolderObject))
		{
			PlayerNetworkProfile profile = previousHolderObject.GetComponent<PlayerNetworkProfile>();
			if (profile != null)
			{
				passerTeam = profile.Team;
			}
		}

		StartRepickupLock(
			previousHolderClientId);

		if (!DetachFromHolder())
		{
			m_HasRepickupLock =
				false;

			return false;
		}

		m_HasHolder.Value =
			false;

		m_CurrentHolderId.Value =
			0;

		m_Rigidbody.linearVelocity =
			shootDirection *
			m_ShootPower;

		m_LastPasserTeam = passerTeam;
		m_LastPasserClientId = previousHolderClientId;
		m_IsPassInFlight = true;
		m_IsShotInFlight.Value = true;
		m_IsGoalHomingInFlight = false;
		m_LastReleaseOrigin = transform.position;

		// BallPhysicsControllerへ、
		// シュート開始を通知します。
		OnShot?.Invoke();
		BallThrownServer?.Invoke(previousHolderClientId, false);

		return true;
	}

	/// <summary>
	/// Host上で現在の所持者からボールを強制的に解放します。
	/// </summary>
	public void ReleaseBall()
	{
		if (!IsServer || m_IsStealing)
		{
			return;
		}

		if (!m_HasHolder.Value)
		{
			return;
		}

		ulong previousHolderClientId =
			m_CurrentHolderId.Value;

		StartRepickupLock(
			previousHolderClientId);

		if (!DetachFromHolder())
		{
			m_HasRepickupLock =
				false;

			return;
		}

		m_HasHolder.Value =
			false;

		m_CurrentHolderId.Value =
			0;

		// 強制解放されたボールは、味方パスボーナスの対象外です。
		m_IsPassInFlight = false;
		m_IsShotInFlight.Value = false;
		m_LastPasserTeam = ETeamType.None;
		m_LastPasserClientId = 0;
		m_LastAssistClientId = 0;
		m_CurrentPassChainCount = 0;
		m_IsGoalHomingInFlight = false;
		m_LastReleaseOrigin = transform.position;
		m_WasFriendlyPassAssisted = false;

		// シュートではない解放として、
		// BallPhysicsControllerへ通知します。
		OnReleased?.Invoke();
	}

	/// <summary>
	/// 指定プレイヤーに対する再キャッチ禁止時間を開始します。
	/// </summary>
	private void StartRepickupLock(
		ulong holderClientId)
	{
		m_LastHolderClientId =
			holderClientId;

		m_RepickupUnlockTime =
			Time.time +
			Mathf.Max(
				0.0f,
				m_RepickupDelay);

		m_HasRepickupLock =
			m_RepickupDelay > 0.0f;
	}

	private bool CanPlayMatch()
	{
		return !IsMatchPaused && (MatchManager.Instance == null || MatchManager.Instance.MatchState == MatchManager.EMatchState.Playing);
	}

	/// <summary>
	/// サーバー側でボールの物理挙動と取得・シュートを停止します。
	/// </summary>
	public void SetMatchPaused(bool paused)
	{
		if (!IsServer || !IsSpawned) return;
		IsMatchPaused = paused;
		if (paused)
		{
			m_IsShotInFlight.Value = false;
		}
		if (!m_Rigidbody.isKinematic)
		{
			m_Rigidbody.linearVelocity = Vector3.zero;
			m_Rigidbody.angularVelocity = Vector3.zero;
		}
		m_Rigidbody.isKinematic = paused || HasHolder;
	}

	/// <summary>
	/// 所持・再取得制限・物理状態を初期化し、生成時の位置へ戻して静止させます。
	/// </summary>
	public bool ResetForRestart()
	{
		if (!IsServer || !IsSpawned)
		{
			return false;
		}

		bool resetSucceeded = RespawnAtInitialPosition();
		SetMatchPaused(true);
		return resetSucceeded;
	}

	/// <summary>
	/// 試合の停止状態を維持し、所持・投球・物理状態を初期化して生成時の位置へ戻します。
	/// </summary>
	public bool RespawnAtInitialPosition()
	{
		if (!IsServer || !IsSpawned)
		{
			return false;
		}

		if (m_ElectricStealCoroutine != null)
		{
			StopCoroutine(m_ElectricStealCoroutine);
			m_ElectricStealCoroutine = null;
		}
		m_IsStealing = false;
		if (m_PhysicsCollider != null)
		{
			m_PhysicsCollider.enabled = true;
		}

		m_IsShotInFlight.Value = false;
		ReleaseBall();
		if (HasHolder || (transform.parent != null && !NetworkObject.TryRemoveParent(true)))
		{
			return false;
		}

		m_IsPassInFlight = false;
		m_LastPasserTeam = ETeamType.None;
		m_HasRepickupLock = false;
		m_LastHolderClientId = 0;
		m_LastPasserClientId = 0;
		m_LastAssistClientId = 0;
		m_CurrentPassChainCount = 0;
		m_IsGoalHomingInFlight = false;
		m_RepickupUnlockTime = 0f;
		m_CurrentHolderId.Value = 0;
		ClearHolderAttachment();
		m_LastReleaseOrigin = m_InitialPosition;
		m_WasFriendlyPassAssisted = false;
		// Clientの所持解除通知が旧所持者を参照できるよう、所持者NetworkObjectIdは保持します。
		m_BallPhysics?.ResetMotionState();
		m_Rigidbody.isKinematic = false;
		m_Rigidbody.useGravity = false;
		m_Rigidbody.linearVelocity = Vector3.zero;
		m_Rigidbody.angularVelocity = Vector3.zero;
		m_NetworkTransform.Teleport(m_InitialPosition, m_InitialRotation, transform.localScale);
		SetMatchPaused(IsMatchPaused);
		return true;
	}

	/// <summary>
	/// NetworkVariableの所持状態変更を
	/// ローカル状態へ反映します。
	/// </summary>
	private void HandleHolderStateChanged(
		bool previousValue,
		bool newValue)
	{
		ApplyHolderState(
			newValue);

	}

	/// <summary>
	/// 所持中の直接奪取も含め、所持者の変更を各端末へ反映します。
	/// </summary>
	private void HandleHolderObjectChanged(ulong previousValue, ulong newValue)
	{
		RefreshHolderAttachment();
	}

	/// <summary>
	/// 所持状態に合わせてColliderと
	/// プレイヤー側の状態を更新します。
	/// </summary>
	private void ApplyHolderState(
		bool hasHolder)
	{
		if (m_PhysicsCollider != null)
		{
			m_PhysicsCollider.enabled =
				!hasHolder;
		}

		if (m_SensorCollider != null)
		{
			m_SensorCollider.enabled =
				true;
		}

		RefreshHolderAttachment();
	}

	/// <summary>
	/// Host上でボールを指定プレイヤーへ親子付けし、
	/// 所持位置へ移動します。
	/// </summary>
	private bool AttachToHolder(
		NetworkObject holderNetworkObject,
		PlayerBallCarrier holder)
	{
		if (!IsServer)
		{
			return false;
		}

		// Surface Physics中は既にKinematicのため、
		// Dynamic状態の場合だけ速度を停止します。
		if (!m_Rigidbody.isKinematic)
		{
			m_Rigidbody.linearVelocity =
				Vector3.zero;

			m_Rigidbody.angularVelocity =
				Vector3.zero;
		}

		m_Rigidbody.isKinematic =
			true;

		if (m_PhysicsCollider != null)
		{
			m_PhysicsCollider.enabled =
				false;
		}

		bool parentSucceeded =
			NetworkObject.TrySetParent(
				holderNetworkObject,
				false);

		if (!parentSucceeded)
		{
			if (m_PhysicsCollider != null)
			{
				m_PhysicsCollider.enabled =
					true;
			}

			m_Rigidbody.isKinematic =
				false;

			return false;
		}

		transform.SetPositionAndRotation(
			holder.BallHoldPoint.position,
			holder.BallHoldPoint.rotation);

		if (m_NetworkTransform != null && m_NetworkTransform.CanCommitToTransform)
		{
			m_NetworkTransform.Teleport(
				transform.position,
				transform.rotation,
				transform.localScale);
		}

		return true;
	}

	/// <summary>
	/// Host上でボールと所持者の親子関係を解除し、
	/// Rigidbodyによる物理挙動へ戻します。
	/// </summary>
	private bool DetachFromHolder()
	{
		if (!IsServer)
		{
			return false;
		}

		// 射出要求を処理した時点のジョイント位置から解放します。
		UpdateHeldPose();

		if (transform.parent != null)
		{
			bool removeSucceeded =
				NetworkObject.TryRemoveParent(
					true);

			if (!removeSucceeded)
			{
				return false;
			}
		}

		m_HoldAttachment.Restore();

		m_Rigidbody.isKinematic =
			false;

		if (m_PhysicsCollider != null)
		{
			m_PhysicsCollider.enabled =
				true;
		}

		return true;
	}

	/// <summary>
	/// 同期された所持者を解決し、プレイヤーの所持状態と描画の親を更新します。
	/// </summary>
	private void RefreshHolderAttachment()
	{
		PlayerBallCarrier holder = null;
		if (HasHolder && NetworkManager != null && NetworkManager.SpawnManager != null && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(
			m_CurrentHolderNetworkObjectId.Value, out NetworkObject holderNetworkObject))
		{
			holder = holderNetworkObject.GetComponent<PlayerBallCarrier>();
		}

		if (m_AppliedHolder != holder)
		{
			ClearHolderAttachment();
			m_AppliedHolder = holder;
		}

		if (holder == null)
		{
			return;
		}

		holder.SetBallState(true);
		m_HoldAttachment.Attach(holder.BallHoldPoint);
	}

	/// <summary>
	/// 旧所持者の状態を解除し、描画をボール本体へ戻します。
	/// </summary>
	private void ClearHolderAttachment()
	{
		if (m_AppliedHolder != null)
		{
			m_AppliedHolder.SetBallState(false);
		}

		m_AppliedHolder = null;
		m_HoldAttachment?.Restore();
	}

	/// <summary>
	/// 通信・物理用の本体を、描画と同じ所持ジョイント位置へ合わせます。
	/// </summary>
	private void UpdateHeldPose()
	{
		if (m_AppliedHolder == null || m_AppliedHolder.BallHoldPoint == null)
		{
			return;
		}

		Transform holdPoint = m_AppliedHolder.BallHoldPoint;
		transform.SetPositionAndRotation(holdPoint.position, holdPoint.rotation);
	}

}
