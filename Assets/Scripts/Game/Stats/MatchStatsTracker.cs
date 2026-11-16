using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 試合中の全プレイヤー行動(15項目)を計測・集約し、
/// 終了時に個人表彰(称号)の選出および展示用累計統計の記録を行うコンポーネント。
/// </summary>
public sealed class MatchStatsTracker : MonoBehaviour
{
	private const float m_DefaultSaveGoalDistance = 25.0f;
	private const float m_DefaultClientSyncInterval = 0.5f;
	private const float m_DefaultObservedMaxDistance = 90.0f;
	private const float m_PlayerScanInterval = 0.5f;
	private const float m_MaxSingleFrameFlightStepSqr = 225.0f;

	[Header("計測設定")]
	[Tooltip("相手シュートをセーブとみなす自陣ゴールからの距離(m、0以下で既定値25m)。")]
	[SerializeField] private float m_SaveGoalDistance;
	[Tooltip("クライアントからサーバーへローカル統計を送信する間隔(秒、0以下で既定値0.5秒)。")]
	[SerializeField] private float m_ClientSyncInterval;
	[Tooltip("視界注目時間としてカウントする最大距離(m、0以下で既定値90m)。")]
	[SerializeField] private float m_ObservedMaxDistance;

	[Header("参照(未設定時は自動検索)")]
	[SerializeField] private BallController m_Ball;

	private readonly Dictionary<ulong, PlayerMatchStats> m_ServerStats = new Dictionary<ulong, PlayerMatchStats>(4);
	private readonly Dictionary<ulong, float[]> m_ObservedByObserver = new Dictionary<ulong, float[]>(8);
	private readonly Dictionary<ulong, int> m_TargetSlotByClientId = new Dictionary<ulong, int>(4);
	private readonly ulong[] m_SlotClientIds = new ulong[4];
	private int m_SlotCount;

	private readonly List<PlayerNetworkProfile> m_CachedPlayers = new List<PlayerNetworkProfile>(4);
	private readonly List<PlayerMatchStats> m_EvalStatsBuffer = new List<PlayerMatchStats>(4);
	private readonly Dictionary<ulong, float> m_LocalObservedSeconds = new Dictionary<ulong, float>(4);
	private readonly ulong[] m_LocalObservedKeysBuffer = new ulong[4];

	private BallController m_SubscribedBall;
	private Vector3 m_RedOwnGoalPosition;
	private Vector3 m_BlueOwnGoalPosition;
	private bool m_HasGoalPositions;

	private PlayerNetworkProfile m_LocalOwnerProfile;
	private GravityMover m_LocalGravityMover;
	private PlayerAnimationController m_LocalAnimation;
	private bool m_HasPreviousLocalPos;
	private Vector3 m_PreviousLocalPos;

	private float m_LocalFlightDistance;
	private int m_LocalAirMoveCount;
	private float m_LocalFuelConsumed;
	private float m_LocalGroundWalkSeconds;

	private float m_NextPlayerScanTime;
	private float m_NextClientSyncTime;
	private MatchManager.EMatchState m_PreviousMatchState = MatchManager.EMatchState.Waiting;

	private void OnEnable()
	{
		PlayerController.OnLocalPlayerAirMoveStarted += HandleLocalAirMoveStarted;
		PlayerResourceController.OnLocalResourceConsumed += HandleLocalResourceConsumed;
		EnsureBallSubscription();
	}

	private void OnDisable()
	{
		PlayerController.OnLocalPlayerAirMoveStarted -= HandleLocalAirMoveStarted;
		PlayerResourceController.OnLocalResourceConsumed -= HandleLocalResourceConsumed;
		UnsubscribeFromBall();
	}

	private void Update()
	{
		MatchManager matchManager = MatchManager.Instance;
		if (matchManager == null || !matchManager.IsSpawned)
		{
			return;
		}

		EnsureBallSubscription();
		EnsureGoalPositions();
		RefreshPlayersIfNeeded();

		MatchManager.EMatchState currentState = matchManager.MatchState;
		if (m_PreviousMatchState != currentState)
		{
			if (m_PreviousMatchState == MatchManager.EMatchState.Playing &&
				currentState != MatchManager.EMatchState.Playing)
			{
				SendClientSnapshotToServer(matchManager);
				m_HasPreviousLocalPos = false;
			}

			if (currentState == MatchManager.EMatchState.Waiting)
			{
				ResetMatchStats();
			}

			m_PreviousMatchState = currentState;
		}

		if (currentState != MatchManager.EMatchState.Playing)
		{
			return;
		}

		float dt = Time.deltaTime;
		if (dt <= 0f)
		{
			return;
		}

		if (matchManager.IsServer)
		{
			UpdateServerTimeStats(dt);
		}

		UpdateLocalClientStats(dt, matchManager);
	}

	/// <summary>
	/// 試合開始時・初期化時にサーバー側およびローカル側の計測値をリセットします。
	/// </summary>
	public void ResetMatchStats()
	{
		m_ServerStats.Clear();
		m_ObservedByObserver.Clear();
		m_TargetSlotByClientId.Clear();
		m_SlotCount = 0;

		m_LocalObservedSeconds.Clear();
		m_LocalFlightDistance = 0f;
		m_LocalAirMoveCount = 0;
		m_LocalFuelConsumed = 0f;
		m_LocalGroundWalkSeconds = 0f;
		m_HasPreviousLocalPos = false;
		m_NextClientSyncTime = 0f;
	}

	/// <summary>
	/// サーバー側でゴール成立時の得点者・アシスト者・最長ゴール距離を記録します。
	/// </summary>
	public void RecordGoalScored(ulong scorerClientId, ulong assistClientId, float shotDistance)
	{
		if (scorerClientId != 0)
		{
			PlayerMatchStats scorer = GetOrCreateServerStat(scorerClientId);
			scorer.m_GoalCount++;
			if (shotDistance > scorer.m_LongestGoalDistance)
			{
				scorer.m_LongestGoalDistance = shotDistance;
			}
			m_ServerStats[scorerClientId] = scorer;
		}

		if (assistClientId != 0 && assistClientId != scorerClientId)
		{
			PlayerMatchStats assister = GetOrCreateServerStat(assistClientId);
			assister.m_AssistCount++;
			m_ServerStats[assistClientId] = assister;
		}
	}

	/// <summary>
	/// リプレイのベストゴール評価用に、指定プレイヤーの試合内貢献度スコアを返します。
	/// </summary>
	public float GetPlayerContributorScore(ulong clientId)
	{
		if (clientId == 0 || !m_ServerStats.TryGetValue(clientId, out PlayerMatchStats stat))
		{
			return 0f;
		}

		return (stat.m_GoalCount * 10.0f) +
			(stat.m_AssistCount * 8.0f) +
			(stat.m_SaveCount * 6.0f) +
			(stat.m_PassCutCount * 4.0f) +
			(stat.m_PassCount * 2.0f);
	}

	/// <summary>
	/// クライアント(Owner)から受信したローカル統計スナップショットをサーバー集計へ反映します。
	/// </summary>
	public void ApplyClientStats(ulong senderClientId, in ClientStatsSyncPayload payload)
	{
		if (m_TargetSlotByClientId.ContainsKey(senderClientId) || IsActivePlayerClient(senderClientId))
		{
			PlayerMatchStats stat = GetOrCreateServerStat(senderClientId);
			stat.m_FlightDistance = Mathf.Max(stat.m_FlightDistance, payload.m_FlightDistance);
			stat.m_AirMoveCount = Mathf.Max(stat.m_AirMoveCount, payload.m_AirMoveCount);
			stat.m_FuelConsumed = Mathf.Max(stat.m_FuelConsumed, payload.m_FuelConsumed);
			stat.m_GroundWalkSeconds = Mathf.Max(stat.m_GroundWalkSeconds, payload.m_GroundWalkSeconds);
			m_ServerStats[senderClientId] = stat;
		}

		if (payload.m_ObservedEntryCount <= 0)
		{
			return;
		}

		if (!m_ObservedByObserver.TryGetValue(senderClientId, out float[] observerSlots))
		{
			observerSlots = new float[4];
			m_ObservedByObserver[senderClientId] = observerSlots;
		}

		int count = Mathf.Min(payload.m_ObservedEntryCount, 4);
		if (count >= 1) ApplyObservedSample(observerSlots, payload.m_ObservedTarget0, payload.m_ObservedSeconds0);
		if (count >= 2) ApplyObservedSample(observerSlots, payload.m_ObservedTarget1, payload.m_ObservedSeconds1);
		if (count >= 3) ApplyObservedSample(observerSlots, payload.m_ObservedTarget2, payload.m_ObservedSeconds2);
		if (count >= 4) ApplyObservedSample(observerSlots, payload.m_ObservedTarget3, payload.m_ObservedSeconds3);

		RecalculateAllObservedTotals();
	}

	/// <summary>
	/// 試合終了時に全プレイヤーの統計・個人表彰・展示用累計統計を確定します(サーバー専用)。
	/// </summary>
	public List<PlayerMatchSummary> FinalizeAndBuildSummaries(
		int redScore,
		int blueScore,
		out ExhibitionStatsSnapshot exhibitionSnapshot)
	{
		RefreshPlayersIfNeeded(force: true);
		EnsureAllKnownPlayersRegistered();

		m_EvalStatsBuffer.Clear();
		foreach (var kvp in m_ServerStats)
		{
			PlayerMatchStats stat = kvp.Value;
			if (stat.m_Team != ETeamType.Red && stat.m_Team != ETeamType.Blue)
			{
				continue;
			}
			m_EvalStatsBuffer.Add(stat);
		}

		Dictionary<ulong, PlayerAwardEntry[]> awardsByClient = MatchAwardEvaluator.EvaluateAwards(m_EvalStatsBuffer);
		var summaries = new List<PlayerMatchSummary>(m_EvalStatsBuffer.Count);

		for (int i = 0; i < m_EvalStatsBuffer.Count; i++)
		{
			PlayerMatchStats stat = m_EvalStatsBuffer[i];
			LobbyPlayerData playerData = ResolveLobbyPlayerData(stat.m_ClientId, stat.m_Team);
			awardsByClient.TryGetValue(stat.m_ClientId, out PlayerAwardEntry[] awards);

			summaries.Add(new PlayerMatchSummary
			{
				m_PlayerData = playerData,
				m_Stats = stat,
				m_Awards = awards ?? System.Array.Empty<PlayerAwardEntry>()
			});
		}

		exhibitionSnapshot = ExhibitionStatsStore.RecordMatchResult(redScore, blueScore, summaries);
		return summaries;
	}

	private void HandleLocalAirMoveStarted(ulong clientId)
	{
		if (MatchManager.Instance == null || MatchManager.Instance.MatchState != MatchManager.EMatchState.Playing)
		{
			return;
		}

		if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening &&
			clientId != NetworkManager.Singleton.LocalClientId)
		{
			return;
		}

		m_LocalAirMoveCount++;
	}

	private void HandleLocalResourceConsumed(ulong clientId, float amount)
	{
		if (MatchManager.Instance == null || MatchManager.Instance.MatchState != MatchManager.EMatchState.Playing)
		{
			return;
		}

		if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening &&
			clientId != NetworkManager.Singleton.LocalClientId)
		{
			return;
		}

		if (amount > 0f)
		{
			m_LocalFuelConsumed += amount;
		}
	}

	private void UpdateServerTimeStats(float dt)
	{
		if (m_Ball != null && m_Ball.HasHolder)
		{
			ulong holderId = m_Ball.CurrentHolderId;
			PlayerMatchStats stat = GetOrCreateServerStat(holderId);
			stat.m_BallHoldSeconds += dt;
			m_ServerStats[holderId] = stat;
		}

		if (!m_HasGoalPositions)
		{
			return;
		}

		for (int i = 0; i < m_CachedPlayers.Count; i++)
		{
			PlayerNetworkProfile profile = m_CachedPlayers[i];
			if (profile == null || !profile.gameObject.activeInHierarchy)
			{
				continue;
			}

			if (profile.Team != ETeamType.Red && profile.Team != ETeamType.Blue)
			{
				continue;
			}

			ulong clientId = profile.OwnerClientId;
			PlayerMatchStats stat = GetOrCreateServerStat(clientId);
			stat.m_Team = profile.Team;

			Vector3 pos = profile.transform.position;
			Vector3 ownGoal = profile.Team == ETeamType.Red ? m_RedOwnGoalPosition : m_BlueOwnGoalPosition;
			Vector3 enemyGoal = profile.Team == ETeamType.Red ? m_BlueOwnGoalPosition : m_RedOwnGoalPosition;

			float ownDistSqr = (pos - ownGoal).sqrMagnitude;
			float enemyDistSqr = (pos - enemyGoal).sqrMagnitude;

			if (enemyDistSqr < ownDistSqr)
			{
				stat.m_EnemyTerritorySeconds += dt;
			}
			else
			{
				stat.m_OwnTerritorySeconds += dt;
			}

			m_ServerStats[clientId] = stat;
		}
	}

	private void UpdateLocalClientStats(float dt, MatchManager matchManager)
	{
		ResolveLocalOwnerPlayer();
		if (m_LocalOwnerProfile != null && m_LocalOwnerProfile.gameObject.activeInHierarchy)
		{
			Vector3 currentPos = m_LocalOwnerProfile.transform.position;
			bool isFalling = m_LocalGravityMover != null && m_LocalGravityMover.IsFalling;

			if (m_HasPreviousLocalPos && isFalling)
			{
				Vector3 delta = currentPos - m_PreviousLocalPos;
				if (delta.sqrMagnitude <= m_MaxSingleFrameFlightStepSqr)
				{
					m_LocalFlightDistance += delta.magnitude;
				}
			}

			m_PreviousLocalPos = currentPos;
			m_HasPreviousLocalPos = true;

			if (!isFalling && m_LocalAnimation != null && m_LocalAnimation.IsMoving)
			{
				m_LocalGroundWalkSeconds += dt;
			}
		}

		UpdateLocalObservedTargets(dt);

		float syncInterval = m_ClientSyncInterval > 0.05f ? m_ClientSyncInterval : m_DefaultClientSyncInterval;
		if (Time.time >= m_NextClientSyncTime)
		{
			m_NextClientSyncTime = Time.time + syncInterval;
			SendClientSnapshotToServer(matchManager);
		}
	}

	private void UpdateLocalObservedTargets(float dt)
	{
		Camera activeCam = SpectatorDisplayManager.IsDisplay1Spectator
			? SpectatorDisplayManager.ActiveSpectatorCamera
			: Camera.main;

		if (activeCam == null || !activeCam.isActiveAndEnabled)
		{
			return;
		}

		float maxDist = m_ObservedMaxDistance > 1f ? m_ObservedMaxDistance : m_DefaultObservedMaxDistance;
		ulong localId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : ulong.MaxValue;
		bool isSpectator = SpectatorDisplayManager.IsLocalSpectator;

		for (int i = 0; i < m_CachedPlayers.Count; i++)
		{
			PlayerNetworkProfile target = m_CachedPlayers[i];
			if (target == null || !target.gameObject.activeInHierarchy)
			{
				continue;
			}

			if (target.Team != ETeamType.Red && target.Team != ETeamType.Blue)
			{
				continue;
			}

			ulong targetId = target.OwnerClientId;
			if (!isSpectator && targetId == localId)
			{
				continue;
			}

			Vector3 vp = activeCam.WorldToViewportPoint(target.transform.position + Vector3.up * 0.8f);
			if (vp.z > 0.5f && vp.z <= maxDist &&
				vp.x >= 0.08f && vp.x <= 0.92f &&
				vp.y >= 0.08f && vp.y <= 0.92f)
			{
				m_LocalObservedSeconds.TryGetValue(targetId, out float current);
				m_LocalObservedSeconds[targetId] = current + dt;
			}
		}
	}

	private void SendClientSnapshotToServer(MatchManager matchManager)
	{
		if (matchManager == null || !matchManager.IsSpawned)
		{
			return;
		}

		ulong localId = NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0;
		var payload = new ClientStatsSyncPayload
		{
			m_SenderClientId = localId,
			m_FlightDistance = m_LocalFlightDistance,
			m_AirMoveCount = m_LocalAirMoveCount,
			m_FuelConsumed = m_LocalFuelConsumed,
			m_GroundWalkSeconds = m_LocalGroundWalkSeconds
		};

		int count = 0;
		foreach (var kvp in m_LocalObservedSeconds)
		{
			if (count >= m_LocalObservedKeysBuffer.Length)
			{
				break;
			}
			m_LocalObservedKeysBuffer[count] = kvp.Key;
			count++;
		}

		payload.m_ObservedEntryCount = count;
		if (count >= 1)
		{
			payload.m_ObservedTarget0 = m_LocalObservedKeysBuffer[0];
			payload.m_ObservedSeconds0 = m_LocalObservedSeconds[m_LocalObservedKeysBuffer[0]];
		}
		if (count >= 2)
		{
			payload.m_ObservedTarget1 = m_LocalObservedKeysBuffer[1];
			payload.m_ObservedSeconds1 = m_LocalObservedSeconds[m_LocalObservedKeysBuffer[1]];
		}
		if (count >= 3)
		{
			payload.m_ObservedTarget2 = m_LocalObservedKeysBuffer[2];
			payload.m_ObservedSeconds2 = m_LocalObservedSeconds[m_LocalObservedKeysBuffer[2]];
		}
		if (count >= 4)
		{
			payload.m_ObservedTarget3 = m_LocalObservedKeysBuffer[3];
			payload.m_ObservedSeconds3 = m_LocalObservedSeconds[m_LocalObservedKeysBuffer[3]];
		}

		matchManager.SubmitClientStats(payload);
	}

	private void EnsureBallSubscription()
	{
		if (m_Ball == null)
		{
			m_Ball = FindFirstObjectByType<BallController>();
		}

		if (m_Ball == m_SubscribedBall)
		{
			return;
		}

		UnsubscribeFromBall();
		if (m_Ball != null)
		{
			m_Ball.FriendlyPassCompleted += HandleFriendlyPassCompleted;
			m_Ball.EnemyShotIntercepted += HandleEnemyShotIntercepted;
			m_Ball.BallStolenFromEnemy += HandleBallStolenFromEnemy;
			m_SubscribedBall = m_Ball;
		}
	}

	private void UnsubscribeFromBall()
	{
		if (m_SubscribedBall == null)
		{
			return;
		}

		m_SubscribedBall.FriendlyPassCompleted -= HandleFriendlyPassCompleted;
		m_SubscribedBall.EnemyShotIntercepted -= HandleEnemyShotIntercepted;
		m_SubscribedBall.BallStolenFromEnemy -= HandleBallStolenFromEnemy;
		m_SubscribedBall = null;
	}

	private void HandleFriendlyPassCompleted(ulong passerClientId, ulong receiverClientId, float distance)
	{
		if (MatchManager.Instance == null || !MatchManager.Instance.IsServer)
		{
			return;
		}

		PlayerMatchStats stat = GetOrCreateServerStat(passerClientId);
		stat.m_PassCount++;
		if (distance > stat.m_LongestPassDistance)
		{
			stat.m_LongestPassDistance = distance;
		}
		m_ServerStats[passerClientId] = stat;
	}

	private void HandleEnemyShotIntercepted(
		ulong interceptorClientId,
		ETeamType interceptorTeam,
		Vector3 interceptPos,
		bool wasGoalHomingShot)
	{
		if (MatchManager.Instance == null || !MatchManager.Instance.IsServer)
		{
			return;
		}

		PlayerMatchStats stat = GetOrCreateServerStat(interceptorClientId);
		if (wasGoalHomingShot || IsWithinSaveRange(interceptorTeam, interceptPos))
		{
			stat.m_SaveCount++;
		}
		else
		{
			stat.m_PassCutCount++;
		}
		m_ServerStats[interceptorClientId] = stat;
	}

	private void HandleBallStolenFromEnemy(ulong stealerClientId, ETeamType stealerTeam, Vector3 stealPos)
	{
		if (MatchManager.Instance == null || !MatchManager.Instance.IsServer)
		{
			return;
		}

		PlayerMatchStats stat = GetOrCreateServerStat(stealerClientId);
		if (IsWithinSaveRange(stealerTeam, stealPos))
		{
			stat.m_SaveCount++;
		}
		else
		{
			stat.m_PassCutCount++;
		}
		m_ServerStats[stealerClientId] = stat;
	}

	private bool IsWithinSaveRange(ETeamType defenderTeam, Vector3 position)
	{
		EnsureGoalPositions();
		if (!m_HasGoalPositions)
		{
			return false;
		}

		Vector3 ownGoal = defenderTeam == ETeamType.Red ? m_RedOwnGoalPosition : m_BlueOwnGoalPosition;
		float threshold = m_SaveGoalDistance > 0.1f ? m_SaveGoalDistance : m_DefaultSaveGoalDistance;
		return (position - ownGoal).sqrMagnitude <= threshold * threshold;
	}

	private void EnsureGoalPositions()
	{
		if (m_HasGoalPositions)
		{
			return;
		}

		GoalController[] goals = FindObjectsByType<GoalController>(FindObjectsSortMode.None);
		bool foundRedOwn = false;
		bool foundBlueOwn = false;

		for (int i = 0; i < goals.Length; i++)
		{
			GoalController goal = goals[i];
			if (goal == null)
			{
				continue;
			}

			// Blue得点ゴール＝Red側の防衛対象(自陣ゴール)、Red得点ゴール＝Blue側の防衛対象(自陣ゴール)
			if (goal.ScoringTeam == ETeamType.Blue)
			{
				m_RedOwnGoalPosition = goal.transform.position;
				foundRedOwn = true;
			}
			else if (goal.ScoringTeam == ETeamType.Red)
			{
				m_BlueOwnGoalPosition = goal.transform.position;
				foundBlueOwn = true;
			}
		}

		m_HasGoalPositions = foundRedOwn && foundBlueOwn;
	}

	private void RefreshPlayersIfNeeded(bool force = false)
	{
		if (!force && Time.time < m_NextPlayerScanTime && m_CachedPlayers.Count > 0)
		{
			return;
		}

		m_NextPlayerScanTime = Time.time + m_PlayerScanInterval;
		m_CachedPlayers.Clear();

		PlayerNetworkProfile[] profiles = FindObjectsByType<PlayerNetworkProfile>(FindObjectsSortMode.None);
		for (int i = 0; i < profiles.Length; i++)
		{
			if (profiles[i] != null)
			{
				m_CachedPlayers.Add(profiles[i]);
			}
		}
	}

	private void ResolveLocalOwnerPlayer()
	{
		if (m_LocalOwnerProfile != null)
		{
			return;
		}

		for (int i = 0; i < m_CachedPlayers.Count; i++)
		{
			PlayerNetworkProfile profile = m_CachedPlayers[i];
			if (profile != null && profile.IsOwner)
			{
				m_LocalOwnerProfile = profile;
				m_LocalGravityMover = profile.GetComponent<GravityMover>();
				m_LocalAnimation = profile.GetComponent<PlayerAnimationController>();
				return;
			}
		}
	}

	private void EnsureAllKnownPlayersRegistered()
	{
		for (int i = 0; i < m_CachedPlayers.Count; i++)
		{
			PlayerNetworkProfile profile = m_CachedPlayers[i];
			if (profile == null || (profile.Team != ETeamType.Red && profile.Team != ETeamType.Blue))
			{
				continue;
			}

			PlayerMatchStats stat = GetOrCreateServerStat(profile.OwnerClientId);
			stat.m_Team = profile.Team;
			m_ServerStats[profile.OwnerClientId] = stat;
		}
	}

	private bool IsActivePlayerClient(ulong clientId)
	{
		for (int i = 0; i < m_CachedPlayers.Count; i++)
		{
			if (m_CachedPlayers[i] != null && m_CachedPlayers[i].OwnerClientId == clientId &&
				(m_CachedPlayers[i].Team == ETeamType.Red || m_CachedPlayers[i].Team == ETeamType.Blue))
			{
				return true;
			}
		}
		return false;
	}

	private PlayerMatchStats GetOrCreateServerStat(ulong clientId)
	{
		if (m_ServerStats.TryGetValue(clientId, out PlayerMatchStats existing))
		{
			return existing;
		}

		ETeamType team = ETeamType.None;
		for (int i = 0; i < m_CachedPlayers.Count; i++)
		{
			if (m_CachedPlayers[i] != null && m_CachedPlayers[i].OwnerClientId == clientId)
			{
				team = m_CachedPlayers[i].Team;
				break;
			}
		}

		if (team == ETeamType.None && SessionPlayerDataStore.Instance != null)
		{
			LobbyPlayerData data = SessionPlayerDataStore.Instance.GetOrCreatePlayerData(clientId);
			team = data.Team;
		}

		var created = new PlayerMatchStats
		{
			m_ClientId = clientId,
			m_Team = team
		};
		m_ServerStats[clientId] = created;
		return created;
	}

	private void ApplyObservedSample(float[] observerSlots, ulong targetClientId, float seconds)
	{
		if (!m_TargetSlotByClientId.TryGetValue(targetClientId, out int slotIndex))
		{
			if (m_SlotCount >= m_SlotClientIds.Length)
			{
				return;
			}

			slotIndex = m_SlotCount;
			m_SlotClientIds[slotIndex] = targetClientId;
			m_TargetSlotByClientId[targetClientId] = slotIndex;
			m_SlotCount++;
		}

		if (seconds > observerSlots[slotIndex])
		{
			observerSlots[slotIndex] = seconds;
		}
	}

	private void RecalculateAllObservedTotals()
	{
		for (int slot = 0; slot < m_SlotCount; slot++)
		{
			ulong targetId = m_SlotClientIds[slot];
			float sum = 0f;
			foreach (var kvp in m_ObservedByObserver)
			{
				sum += kvp.Value[slot];
			}

			PlayerMatchStats stat = GetOrCreateServerStat(targetId);
			stat.m_ObservedSeconds = sum;
			m_ServerStats[targetId] = stat;
		}
	}

	private LobbyPlayerData ResolveLobbyPlayerData(ulong clientId, ETeamType fallbackTeam)
	{
		if (SessionPlayerDataStore.Instance != null)
		{
			var all = SessionPlayerDataStore.Instance.GetAllPlayerData();
			if (all.TryGetValue(clientId, out LobbyPlayerData stored))
			{
				return stored;
			}
		}

		for (int i = 0; i < m_CachedPlayers.Count; i++)
		{
			PlayerNetworkProfile profile = m_CachedPlayers[i];
			if (profile != null && profile.OwnerClientId == clientId)
			{
				return new LobbyPlayerData(
					clientId,
					profile.PlayerName,
					profile.Team,
					profile.CharacterType,
					profile.HighlightColor,
					true);
			}
		}

		return new LobbyPlayerData(clientId, $"Player {clientId}", fallbackTeam);
	}
}
