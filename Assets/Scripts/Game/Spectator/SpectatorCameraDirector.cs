using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 観戦カメラの演出アングル・自動スイッチングを統括するディレクタークラス。
/// 定点俯瞰カメラ、中継風2点フレーミング追従カメラ(天地Vector3.up絶対維持・3D酔い防止)、
/// ゴール前カメラ(シュート接近時・得点演出時の即時割り込み)を自動制御します。
/// </summary>
public class SpectatorCameraDirector : MonoBehaviour
{
	public enum ECameraShotType
	{
		StadiumOverview, // 定点・全体俯瞰
		PlayerFollow,    // プレイヤー・ボール中継風追従
		GoalArea,        // ゴール前・シュート注目
	}

	[Header("制御対象カメラ(未設定時はSpectatorDisplayManagerから取得)")]
	[SerializeField] private Camera m_ManagedCamera;

	[Header("定点カメラアンカー群(未設定時はステージ全体の既定座標から自動生成)")]
	[SerializeField] private Transform[] m_OverviewAnchors;

	[Header("ゴール前カメラアンカー(Red / Blue)")]
	[SerializeField] private Transform m_RedGoalAnchor;
	[SerializeField] private Transform m_BlueGoalAnchor;

	[Header("スイッチング設定")]
	[Tooltip("通常のカメラ切り替え時の最低維持秒数")]
	[SerializeField] private float m_MinHoldDuration;
	[Tooltip("ゴールカメラへ緊急割り込み切り替えを行うボールとゴールの距離(m)")]
	[SerializeField] private float m_GoalTriggerDistance;
	[Tooltip("追従カメラ時のカメラ移動遅延(SmoothDamp時間)")]
	[SerializeField] private float m_FollowPositionSmoothTime;
	[Tooltip("追従カメラ時の注視点遅延(SmoothDamp時間)")]
	[SerializeField] private float m_LookAtSmoothTime;

	[Header("追従カメラ配置オフセット(ワールド上方向基準)")]
	[SerializeField] private Vector3 m_FollowOffset;
	[SerializeField] private float m_FollowFov;
	[SerializeField] private float m_OverviewFov;
	[SerializeField] private float m_GoalFov;

	[Header("障害物回避設定")]
	[SerializeField] private LayerMask m_ObstacleLayers;
	[SerializeField] private float m_CameraCollisionRadius;

	[Header("俯瞰カメラ動的ズーム設定")]
	[Tooltip("プレイヤー群とボールの広がりがこの半径以下のとき最大接近します(0以下で既定値10m)")]
	[SerializeField] private float m_OverviewCompactSpreadRadius;
	[Tooltip("プレイヤー群とボールの広がりがこの半径以上のとき遠景位置を維持します(0以下で既定値28m)")]
	[SerializeField] private float m_OverviewWideSpreadRadius;
	[Tooltip("俯瞰アンカーからボールへ近づく最大割合(0〜0.85、0以下で既定値0.55)")]
	[SerializeField] private float m_OverviewMaxApproachRatio;
	[Tooltip("俯瞰ズーム時でもボールとの間に維持する最小距離(m、0以下で既定値12m)")]
	[SerializeField] private float m_OverviewMinDistanceToBall;
	[Tooltip("俯瞰ズーム移動のスムージング時間(秒、0以下で既定値0.6秒)")]
	[SerializeField] private float m_OverviewZoomSmoothTime;

	// 現在のショット状態
	public ECameraShotType CurrentShotType { get; private set; } = ECameraShotType.StadiumOverview;
	public string CurrentShotName => CurrentShotType switch
	{
		ECameraShotType.StadiumOverview => "STADIUM CAM",
		ECameraShotType.PlayerFollow => "FOLLOW CAM",
		ECameraShotType.GoalArea => "GOAL CAM",
		_ => "SPECTATOR CAM"
	};

	public Transform CurrentFollowTarget { get; private set; }

	// イベント
	public event Action<ECameraShotType, string> OnCameraShotChanged;

	private float m_CurrentShotTimer;
	private float m_PlayerScanTimer;
	private bool m_HasAppliedInitialBallFocus;
	private Vector3 m_CurrentFollowVelocity;
	private Vector3 m_CurrentLookAtVelocity;
	private Vector3 m_SmoothedLookAtPoint;
	private Transform m_ActiveOverviewAnchor;

	// キャッシュ参照
	private BallController m_CachedBall;
	private GoalController[] m_CachedGoals;
	private readonly List<Transform> m_FallbackOverviewPoints = new List<Transform>();
	private readonly List<PlayerNetworkProfile> m_CachedPlayers = new List<PlayerNetworkProfile>();

	private void Awake()
	{
		if (m_ManagedCamera == null)
		{
			m_ManagedCamera = GetComponent<Camera>();
		}
	}

	private void Start()
	{
		EnsureAnchors();
		FindBallIfNeeded();

		if (m_CachedBall != null)
		{
			CurrentFollowTarget = m_CachedBall.transform;
			m_HasAppliedInitialBallFocus = true;
			SwitchShot(ECameraShotType.PlayerFollow, true);
		}
		else
		{
			SwitchShot(ECameraShotType.StadiumOverview, true);
		}
	}

	private void EnsureAnchors()
	{
		// ゴールアンカーが未指定の場合はシーン内のGoalControllerから自動取得
		if (m_RedGoalAnchor == null || m_BlueGoalAnchor == null)
		{
			m_CachedGoals = FindObjectsByType<GoalController>(FindObjectsSortMode.None);
			for (int i = 0; i < m_CachedGoals.Length; i++)
			{
				var goal = m_CachedGoals[i];
				if (goal == null) continue;

				// ゴール奥側から手前を映すアンカーを自動構築
				Vector3 anchorPos = goal.transform.position - (goal.transform.forward * 4.0f) + (Vector3.up * 4.0f);
				var autoObj = new GameObject($"AutoGoalAnchor_{goal.name}");
				autoObj.transform.position = anchorPos;
				autoObj.transform.LookAt(goal.transform.position);

				if (goal.name.Contains("Red") || (m_RedGoalAnchor == null))
				{
					if (m_RedGoalAnchor == null) m_RedGoalAnchor = autoObj.transform;
					else if (m_BlueGoalAnchor == null) m_BlueGoalAnchor = autoObj.transform;
				}
				else if (m_BlueGoalAnchor == null)
				{
					m_BlueGoalAnchor = autoObj.transform;
				}
			}
		}

		// 定点アンカーが空の場合はスタジアム外周・俯瞰の基準点を自動構築
		if (m_OverviewAnchors == null || m_OverviewAnchors.Length == 0)
		{
			CreateFallbackOverviewAnchor("AutoStadiumOverview_CenterHigh", new Vector3(0f, 22f, -28f));
			CreateFallbackOverviewAnchor("AutoStadiumOverview_SideA", new Vector3(-24f, 15f, 0f));
			CreateFallbackOverviewAnchor("AutoStadiumOverview_SideB", new Vector3(24f, 15f, 0f));
			m_OverviewAnchors = m_FallbackOverviewPoints.ToArray();
		}
	}

	private void CreateFallbackOverviewAnchor(string name, Vector3 pos)
	{
		var anchorObj = new GameObject(name);
		anchorObj.transform.position = pos;
		anchorObj.transform.LookAt(Vector3.zero);
		m_FallbackOverviewPoints.Add(anchorObj.transform);
	}

	private void LateUpdate()
	{
		// 観戦カメラが非アクティブな端末では何もしない
		if (m_ManagedCamera == null || !m_ManagedCamera.gameObject.activeInHierarchy)
		{
			return;
		}

		FindBallIfNeeded();

		// 初期配置のボールが後から見つかった場合、開始前・待機段階であれば即座にボール側を映す
		if (!m_HasAppliedInitialBallFocus && m_CachedBall != null)
		{
			m_HasAppliedInitialBallFocus = true;
			if (!m_CachedBall.HasHolder && IsPreKickoffOrResetState())
			{
				CurrentFollowTarget = m_CachedBall.transform;
				SwitchShot(ECameraShotType.PlayerFollow, true);
			}
		}

		m_CurrentShotTimer += Time.deltaTime;
		m_PlayerScanTimer -= Time.deltaTime;

		// 1. 緊急割り込み判定(ゴール接近またはGoalCelebration)
		if (CheckGoalUrgentCondition(out Transform targetGoalAnchor))
		{
			if (CurrentShotType != ECameraShotType.GoalArea)
			{
				SwitchShot(ECameraShotType.GoalArea, true);
			}
			UpdateGoalShot(targetGoalAnchor);
			return;
		}

		// 開始直後・リスタート待機中で誰にも持たれていないボールは、遠景ではなくボール側カメラを維持
		if (m_CachedBall != null && !m_CachedBall.HasHolder && IsPreKickoffOrResetState())
		{
			if (CurrentShotType != ECameraShotType.PlayerFollow || CurrentFollowTarget != m_CachedBall.transform)
			{
				CurrentFollowTarget = m_CachedBall.transform;
				SwitchShot(ECameraShotType.PlayerFollow, CurrentShotType != ECameraShotType.PlayerFollow);
			}
			UpdateFollowShot();
			return;
		}

		// 2. 通常時の切り替えタイマー評価
		if (m_CurrentShotTimer >= m_MinHoldDuration)
		{
			EvaluateNextShot();
		}

		// 3. 現在のショットに応じたカメラ位置・視線更新
		switch (CurrentShotType)
		{
			case ECameraShotType.StadiumOverview:
				UpdateOverviewShot();
				break;
			case ECameraShotType.PlayerFollow:
				UpdateFollowShot();
				break;
			case ECameraShotType.GoalArea:
				UpdateGoalShot(targetGoalAnchor != null ? targetGoalAnchor : m_RedGoalAnchor);
				break;
		}
	}

	private void FindBallIfNeeded()
	{
		if (m_CachedBall == null)
		{
			m_CachedBall = FindFirstObjectByType<BallController>();
		}
	}

	private bool CheckGoalUrgentCondition(out Transform goalAnchor)
	{
		goalAnchor = null;

		// 得点演出中(GoalCelebration)は最優先
		if (MatchManager.Instance != null && MatchManager.Instance.MatchState == MatchManager.EMatchState.GoalCelebration)
		{
			ETeamType scoringTeam = MatchManager.Instance.GoalScoringTeam;
			goalAnchor = (scoringTeam == ETeamType.Red) ? m_RedGoalAnchor : m_BlueGoalAnchor;
			if (goalAnchor == null) goalAnchor = m_RedGoalAnchor != null ? m_RedGoalAnchor : m_BlueGoalAnchor;
			return goalAnchor != null;
		}

		if (m_CachedBall == null) return false;

		float triggerDistSqr = m_GoalTriggerDistance * m_GoalTriggerDistance;
		Vector3 ballPos = m_CachedBall.transform.position;

		if (m_RedGoalAnchor != null && (m_RedGoalAnchor.position - ballPos).sqrMagnitude <= triggerDistSqr)
		{
			goalAnchor = m_RedGoalAnchor;
			return true;
		}

		if (m_BlueGoalAnchor != null && (m_BlueGoalAnchor.position - ballPos).sqrMagnitude <= triggerDistSqr)
		{
			goalAnchor = m_BlueGoalAnchor;
			return true;
		}

		return false;
	}

	private void EvaluateNextShot()
	{
		m_CurrentShotTimer = 0f;

		// ボールを誰かが保持している場合は中継追従カメラを優先
		if (m_CachedBall != null && m_CachedBall.HasHolder)
		{
			Transform holderTransform = FindBallHolderTransform(m_CachedBall.CurrentHolderId);
			if (holderTransform != null)
			{
				CurrentFollowTarget = holderTransform;
				SwitchShot(ECameraShotType.PlayerFollow, false);
				return;
			}
		}

		// ボールがフリー、または定点が一定時間経過した場合は交互に俯瞰/追従へ
		if (CurrentShotType == ECameraShotType.StadiumOverview && m_CachedBall != null)
		{
			CurrentFollowTarget = m_CachedBall.transform;
			SwitchShot(ECameraShotType.PlayerFollow, false);
		}
		else
		{
			SwitchShot(ECameraShotType.StadiumOverview, false);
		}
	}

	private Transform FindBallHolderTransform(ulong clientId)
	{
		if (NetworkManager.Singleton != null && NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
		{
			if (client.PlayerObject != null)
			{
				return client.PlayerObject.transform;
			}
		}

		var profiles = FindObjectsByType<PlayerNetworkProfile>(FindObjectsSortMode.None);
		for (int i = 0; i < profiles.Length; i++)
		{
			if (profiles[i] != null && profiles[i].OwnerClientId == clientId)
			{
				return profiles[i].transform;
			}
		}

		return null;
	}

	private bool IsPreKickoffOrResetState()
	{
		if (MatchManager.Instance == null)
		{
			return false;
		}

		MatchManager.EMatchState state = MatchManager.Instance.MatchState;
		return state == MatchManager.EMatchState.Waiting ||
			state == MatchManager.EMatchState.Countdown ||
			state == MatchManager.EMatchState.PreparingRestart;
	}

	private void SwitchShot(ECameraShotType nextType, bool instantCut)
	{
		CurrentShotType = nextType;
		m_CurrentShotTimer = 0f;

		float targetFov = nextType switch
		{
			ECameraShotType.StadiumOverview => m_OverviewFov,
			ECameraShotType.PlayerFollow => m_FollowFov,
			ECameraShotType.GoalArea => m_GoalFov,
			_ => 60.0f
		};

		if (m_ManagedCamera != null)
		{
			m_ManagedCamera.fieldOfView = targetFov;
		}

		if (nextType == ECameraShotType.StadiumOverview)
		{
			SelectBestOverviewAnchor();
		}
		else if (nextType == ECameraShotType.PlayerFollow && instantCut && m_ManagedCamera != null)
		{
			SnapFollowCameraToCurrentTarget();
		}

		OnCameraShotChanged?.Invoke(CurrentShotType, CurrentShotName);
	}

	private void SnapFollowCameraToCurrentTarget()
	{
		Transform target = CurrentFollowTarget != null ? CurrentFollowTarget : (m_CachedBall != null ? m_CachedBall.transform : null);
		if (target == null || m_ManagedCamera == null) return;

		Vector3 targetPos = target.position;
		Vector3 ballPos = m_CachedBall != null ? m_CachedBall.transform.position : targetPos;
		Vector3 centerPoint = (targetPos + ballPos) * 0.5f;

		m_SmoothedLookAtPoint = centerPoint;
		m_CurrentFollowVelocity = Vector3.zero;
		m_CurrentLookAtVelocity = Vector3.zero;

		Vector3 desiredCamPos = ResolveSafeCameraPosition(centerPoint, targetPos + m_FollowOffset);
		m_ManagedCamera.transform.position = desiredCamPos;

		ApplyLookAtRotation(m_SmoothedLookAtPoint);
	}

	private void SelectBestOverviewAnchor()
	{
		if (m_OverviewAnchors == null || m_OverviewAnchors.Length == 0) return;

		Vector3 ballPos = m_CachedBall != null ? m_CachedBall.transform.position : Vector3.zero;
		Transform bestAnchor = m_OverviewAnchors[0];
		float bestDist = float.MaxValue;

		for (int i = 0; i < m_OverviewAnchors.Length; i++)
		{
			var anchor = m_OverviewAnchors[i];
			if (anchor == null) continue;

			float dist = (anchor.position - ballPos).sqrMagnitude;
			if (dist < bestDist)
			{
				bestDist = dist;
				bestAnchor = anchor;
			}
		}

		m_ActiveOverviewAnchor = bestAnchor;

		if (m_ManagedCamera != null && bestAnchor != null)
		{
			m_CurrentFollowVelocity = Vector3.zero;
			m_CurrentLookAtVelocity = Vector3.zero;
			m_ManagedCamera.transform.position = ComputeOverviewTargetPosition(bestAnchor.position, ballPos);
			m_SmoothedLookAtPoint = ballPos;
		}
	}

	private void UpdateOverviewShot()
	{
		if (m_ManagedCamera == null) return;

		Vector3 ballPos = m_CachedBall != null ? m_CachedBall.transform.position : Vector3.zero;
		Vector3 anchorPos = m_ActiveOverviewAnchor != null ? m_ActiveOverviewAnchor.position : m_ManagedCamera.transform.position;
		Vector3 desiredCamPos = ComputeOverviewTargetPosition(anchorPos, ballPos);

		float smoothTime = m_OverviewZoomSmoothTime > 0.001f ? m_OverviewZoomSmoothTime : 0.6f;
		m_ManagedCamera.transform.position = Vector3.SmoothDamp(m_ManagedCamera.transform.position, desiredCamPos, ref m_CurrentFollowVelocity, smoothTime);

		m_SmoothedLookAtPoint = Vector3.SmoothDamp(m_SmoothedLookAtPoint, ballPos, ref m_CurrentLookAtVelocity, m_LookAtSmoothTime);

		ApplyLookAtRotation(m_SmoothedLookAtPoint);
	}

	private Vector3 ComputeOverviewTargetPosition(Vector3 anchorPos, Vector3 ballPos)
	{
		float compactRadius = m_OverviewCompactSpreadRadius > 0.01f ? m_OverviewCompactSpreadRadius : 10.0f;
		float wideRadius = m_OverviewWideSpreadRadius > compactRadius + 0.01f ? m_OverviewWideSpreadRadius : Mathf.Max(compactRadius + 10.0f, 28.0f);
		float maxApproachRatio = m_OverviewMaxApproachRatio > 0.01f ? Mathf.Clamp(m_OverviewMaxApproachRatio, 0f, 0.85f) : 0.55f;
		float minDistanceToBall = m_OverviewMinDistanceToBall > 0.01f ? m_OverviewMinDistanceToBall : 12.0f;

		float spreadRadius = CalculatePlayerSpreadRadius(ballPos);
		float compactness = 1f - Mathf.InverseLerp(compactRadius, wideRadius, spreadRadius);

		Vector3 toBall = ballPos - anchorPos;
		float distToBall = toBall.magnitude;
		if (distToBall <= minDistanceToBall || compactness <= 0.001f || maxApproachRatio <= 0.001f)
		{
			return anchorPos;
		}

		float maxAllowedAdvance = Mathf.Max(0f, distToBall - minDistanceToBall);
		float advance = Mathf.Min(distToBall * maxApproachRatio * compactness, maxAllowedAdvance);
		Vector3 desiredPos = anchorPos + (toBall / distToBall) * advance;

		return ResolveSafeCameraPosition(anchorPos, desiredPos);
	}

	private float CalculatePlayerSpreadRadius(Vector3 ballPos)
	{
		RefreshActivePlayersIfNeeded();

		float maxDistSqr = 0f;
		bool hasValidPlayer = false;

		for (int i = 0; i < m_CachedPlayers.Count; i++)
		{
			PlayerNetworkProfile player = m_CachedPlayers[i];
			if (player == null || !player.gameObject.activeInHierarchy || player.Team == ETeamType.Spectator)
			{
				continue;
			}

			hasValidPlayer = true;
			float distSqr = (player.transform.position - ballPos).sqrMagnitude;
			if (distSqr > maxDistSqr)
			{
				maxDistSqr = distSqr;
			}
		}

		return hasValidPlayer ? Mathf.Sqrt(maxDistSqr) : 0f;
	}

	private void RefreshActivePlayersIfNeeded()
	{
		if (m_PlayerScanTimer > 0f && m_CachedPlayers.Count > 0)
		{
			return;
		}

		m_PlayerScanTimer = 0.5f;
		m_CachedPlayers.Clear();

		var profiles = FindObjectsByType<PlayerNetworkProfile>(FindObjectsSortMode.None);
		for (int i = 0; i < profiles.Length; i++)
		{
			if (profiles[i] != null)
			{
				m_CachedPlayers.Add(profiles[i]);
			}
		}
	}

	private Vector3 ResolveSafeCameraPosition(Vector3 origin, Vector3 desiredPos)
	{
		if (m_ObstacleLayers == 0 || m_CameraCollisionRadius <= 0.001f)
		{
			return desiredPos;
		}

		Vector3 castDir = desiredPos - origin;
		float castDist = castDir.magnitude;
		if (castDist > 0.001f)
		{
			castDir /= castDist;
			if (Physics.SphereCast(origin, m_CameraCollisionRadius, castDir, out RaycastHit hit, castDist, m_ObstacleLayers, QueryTriggerInteraction.Ignore))
			{
				return origin + castDir * Mathf.Max(1.0f, hit.distance - 0.2f);
			}
		}

		return desiredPos;
	}

	private void UpdateFollowShot()
	{
		if (m_ManagedCamera == null) return;

		Transform target = CurrentFollowTarget != null ? CurrentFollowTarget : (m_CachedBall != null ? m_CachedBall.transform : null);
		if (target == null)
		{
			UpdateOverviewShot();
			return;
		}

		Vector3 targetPos = target.position;
		Vector3 ballPos = m_CachedBall != null ? m_CachedBall.transform.position : targetPos;

		// 2点フレーミング(対象とボールの中間点)
		Vector3 centerPoint = (targetPos + ballPos) * 0.5f;
		m_SmoothedLookAtPoint = Vector3.SmoothDamp(m_SmoothedLookAtPoint, centerPoint, ref m_CurrentLookAtVelocity, m_LookAtSmoothTime);

		// カメラ理想位置(ワールド上方向・後方への一定オフセット配置)
		Vector3 desiredCamPos = targetPos + m_FollowOffset;

		// 壁めり込み回避(SphereCast)
		desiredCamPos = ResolveSafeCameraPosition(m_SmoothedLookAtPoint, desiredCamPos);

		m_ManagedCamera.transform.position = Vector3.SmoothDamp(m_ManagedCamera.transform.position, desiredCamPos, ref m_CurrentFollowVelocity, m_FollowPositionSmoothTime);

		ApplyLookAtRotation(m_SmoothedLookAtPoint);
	}

	private void UpdateGoalShot(Transform anchor)
	{
		if (m_ManagedCamera == null) return;

		if (anchor != null)
		{
			m_ManagedCamera.transform.position = Vector3.SmoothDamp(m_ManagedCamera.transform.position, anchor.position, ref m_CurrentFollowVelocity, 0.2f);
		}

		Vector3 targetLookAt = m_CachedBall != null ? m_CachedBall.transform.position : Vector3.zero;
		m_SmoothedLookAtPoint = Vector3.SmoothDamp(m_SmoothedLookAtPoint, targetLookAt, ref m_CurrentLookAtVelocity, 0.15f);

		ApplyLookAtRotation(m_SmoothedLookAtPoint);
	}

	private void ApplyLookAtRotation(Vector3 lookAtPoint)
	{
		if (m_ManagedCamera == null) return;

		Vector3 lookDir = lookAtPoint - m_ManagedCamera.transform.position;
		if (lookDir.sqrMagnitude > 0.001f)
		{
			// 天地(Vector3.up)を絶対維持
			m_ManagedCamera.transform.rotation = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
		}
	}

	private void OnDestroy()
	{
		for (int i = 0; i < m_FallbackOverviewPoints.Count; i++)
		{
			if (m_FallbackOverviewPoints[i] != null)
			{
				Destroy(m_FallbackOverviewPoints[i].gameObject);
			}
		}
		m_FallbackOverviewPoints.Clear();
	}
}
