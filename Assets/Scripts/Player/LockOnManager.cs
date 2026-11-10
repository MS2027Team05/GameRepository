using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 電撃・追尾パス中に対象を自動検索するロックオン管理クラス。
/// </summary>
[RequireComponent(typeof(PlayerBallCarrier))]
[RequireComponent(typeof(PlayerInputReceiver))]
public class LockOnManager : MonoBehaviour, ILockOnProvider
{
	[Header("カメラ")]
	[SerializeField] private Camera m_TargetCamera;

	[Header("検索設定")]
	[Tooltip("ロックオン対象を検索する最大距離")]
	[SerializeField, Min(0.1f)]
	private float m_MaxSearchDistance = 30f;

	[Tooltip("レティクル周辺の検索範囲")]
	[SerializeField, Range(0.01f, 0.5f)]
	private float m_ReticleSearchRadius = 0.18f;

	[Tooltip("ボールを優先する度合い")]
	[SerializeField, Range(0f, 1f)]
	private float m_BallPriority = 0.35f;

	[Header("ゴール設定")]
	[SerializeField]
	private string m_EnemyGoalTag = "Goal";

	[Header("ロックオンマーカー")]
	[SerializeField]
	private RectTransform m_AllyLockOnMarker;

	private PlayerBallCarrier m_BallCarrier;
	private NetworkObject m_NetworkObject;
	private PlayerNetworkProfile m_PlayerProfile;

	private ELockOnSearchMode m_CurrentSearchMode =
		ELockOnSearchMode.None;

	private ELockOnTargetType m_CurrentTargetType =
		ELockOnTargetType.None;

	[Header("デバッグログ")]
	[SerializeField]
	private bool m_EnableDebugLog = true;

	private GameObject m_PreviousTarget;
	private float m_NextDebugSearchLogTime;

	/// <summary>
	/// ロックオンのデバッグログを出力
	/// </summary>
	private void LogDebug(string message)
	{
		if (!m_EnableDebugLog)
		{
			return;
		}

		Debug.Log(
			$"[LockOn] {message}",
			this);
	}

	/// <summary>
	/// 毎フレームの過度なログ出力を抑えつつ、定期的に詳細ログを出力可能か判定
	/// </summary>
	private bool CanLogSearchDetail()
	{
		if (!m_EnableDebugLog)
		{
			return false;
		}

		if (Time.time < m_NextDebugSearchLogTime)
		{
			return false;
		}

		m_NextDebugSearchLogTime = Time.time + 0.3f;
		return true;
	}

	// 現在ロックオンしているGameObject
	public GameObject CurrentTarget { get; private set; }

	// プロパティ
	public bool HasTarget => CurrentTarget != null;

	/// <summary>ILockOnProvider: 現在捕捉している対象の種類</summary>
	public ELockOnTargetType CurrentTargetType =>
		m_CurrentTargetType;

	/// <summary>ILockOnProvider: 現在捕捉している対象のGameObject</summary>
	public GameObject CurrentTargetObject =>
		CurrentTarget;

	/// <summary>ILockOnProvider: 有効な対象が存在するか</summary>
	public bool HasValidTarget =>
		CurrentTarget != null;

	private void Awake()
	{
		m_BallCarrier = GetComponent<PlayerBallCarrier>();
		m_NetworkObject = GetComponent<NetworkObject>();
		m_PlayerProfile = GetComponent<PlayerNetworkProfile>();

		EnsureTargetCamera();

		if (m_AllyLockOnMarker != null)
		{
			m_AllyLockOnMarker.gameObject.SetActive(false);
		}
	}

	/// <summary>
	/// ターゲット検索に使用するカメラを外部から設定します。
	/// </summary>
	public void SetTargetCamera(Camera camera)
	{
		m_TargetCamera = camera;
	}

	/// <summary>
	/// ターゲット検索に使用するカメラを確保します。
	/// 未設定または無効な場合はCamera.mainおよび有効なカメラから自動再取得します。
	/// </summary>
	private bool EnsureTargetCamera()
	{
		if (m_TargetCamera != null && m_TargetCamera.isActiveAndEnabled)
		{
			return true;
		}

		m_TargetCamera = Camera.main;

		if (m_TargetCamera != null && m_TargetCamera.isActiveAndEnabled)
		{
			return true;
		}

		Camera[] allCameras = Camera.allCameras;
		for (int i = 0; i < allCameras.Length; i++)
		{
			Camera cam = allCameras[i];
			if (cam != null && cam.isActiveAndEnabled)
			{
				m_TargetCamera = cam;
				return true;
			}
		}

		return false;
	}

	private void OnDisable()
	{
		SetSearchMode(ELockOnSearchMode.None);
	}

	private void Update()
	{
		UpdateLockOnMarker();
	}

	// 旧キー操作との互換用
	public void LockOn()
	{
		if (m_BallCarrier == null)
		{
			return;
		}

		SetSearchMode(
			m_BallCarrier.HasBall
			? ELockOnSearchMode.BallHolder
			: ELockOnSearchMode.NonBallHolder);

		UpdateTargetSearch();
	}

	// =========================================================================
	// ILockOnProvider インターフェース窓口
	// =========================================================================

	/// <summary>
	/// 検索モードを設定
	/// </summary>
	public void SetSearchMode(ELockOnSearchMode mode)
	{
		if (m_CurrentSearchMode == mode)
		{
			return;
		}

		m_CurrentSearchMode = mode;

		if (mode == ELockOnSearchMode.None)
		{
			LogDebug("検索終了");

			ClearTarget();
		}
		else
		{
			LogDebug($"ロックオン検索開始: Mode = {mode}");

			UpdateTargetSearch();
		}
	}

	/// <summary>
	/// 現在のモードに応じて対象を検索
	/// </summary>
	public void UpdateTargetSearch()
	{
		if (m_CurrentSearchMode == ELockOnSearchMode.None ||
			m_BallCarrier == null)
		{
			return;
		}

		if (!EnsureTargetCamera())
		{
			if (CanLogSearchDetail())
			{
				LogDebug("検索中断: 有効なカメラが取得できていません。");
			}
			return;
		}

		if (m_NetworkObject != null &&
			m_NetworkObject.IsSpawned &&
			!m_NetworkObject.IsOwner)
		{
			return;
		}

		bool hasBall = m_BallCarrier.HasBall;

		if (hasBall &&
			m_CurrentSearchMode != ELockOnSearchMode.BallHolder)
		{
			m_CurrentSearchMode = ELockOnSearchMode.BallHolder;
		}
		else if (!hasBall &&
				 m_CurrentSearchMode != ELockOnSearchMode.NonBallHolder)
		{
			m_CurrentSearchMode = ELockOnSearchMode.NonBallHolder;
		}

		GameObject bestTarget = null;
		ELockOnTargetType bestType = ELockOnTargetType.None;
		float bestScore = float.MaxValue;

		if (m_CurrentSearchMode == ELockOnSearchMode.NonBallHolder)
		{
			// ボールを検索
			FindBallCandidate(
				ref bestTarget,
				ref bestType,
				ref bestScore);

			// 敵プレイヤーも検索
			FindPlayerCandidates(
				false,
				ref bestTarget,
				ref bestType,
				ref bestScore);
		}
		else
		{
			// 味方を検索
			FindPlayerCandidates(
				true,
				ref bestTarget,
				ref bestType,
				ref bestScore);

			// 相手ゴールを検索
			FindEnemyGoalCandidates(
				ref bestTarget,
				ref bestType,
				ref bestScore);
		}

		// 検索結果を反映
		CurrentTarget = bestTarget;
		m_CurrentTargetType = bestType;

		// 対象が変更された場合のみログを出す
		if (m_PreviousTarget != CurrentTarget)
		{
			if (CurrentTarget != null)
			{
				LogDebug(
					$"ターゲット発見: {CurrentTarget.name}, " +
					$"Type = {m_CurrentTargetType}");
			}
			else if (m_PreviousTarget != null)
			{
				LogDebug("対象を見失った");
			}
			else
			{
				LogDebug("対象が見つからない");
			}

			m_PreviousTarget = CurrentTarget;
		}
	}

	/// <summary>
	/// ボールを検索
	/// </summary>
	private void FindBallCandidate(
		ref GameObject bestTarget,
		ref ELockOnTargetType bestType,
		ref float bestScore)
	{
		BallController[] balls =
			FindObjectsByType<BallController>(FindObjectsSortMode.None);

		if (balls.Length == 0)
		{
			if (CanLogSearchDetail())
			{
				LogDebug("ボール候補探索: シーン上にBallControllerが見つかりません。");
			}

			return;
		}

		foreach (BallController ball in balls)
		{
			if (ball == null ||
				!ball.gameObject.activeInHierarchy)
			{
				continue;
			}

			ConsiderCandidate(
				ball.gameObject,
				ELockOnTargetType.Ball,
				ref bestTarget,
				ref bestType,
				ref bestScore);
		}
	}

	/// <summary>
	/// 味方または敵プレイヤーを検索
	/// </summary>
	private void FindPlayerCandidates(
		bool allies,
		ref GameObject bestTarget,
		ref ELockOnTargetType bestType,
		ref float bestScore)
	{
		PlayerNetworkProfile[] players =
			FindObjectsByType<PlayerNetworkProfile>(FindObjectsSortMode.None);

		ETeamType myTeam =
			m_PlayerProfile != null
			? m_PlayerProfile.Team
			: ETeamType.None;

		foreach (PlayerNetworkProfile profile in players)
		{
			if (profile == null ||
				profile.gameObject == gameObject ||
				!profile.gameObject.activeInHierarchy)
			{
				continue;
			}

			bool sameTeam =
				myTeam != ETeamType.None &&
				profile.Team == myTeam;

			if (allies != sameTeam)
			{
				continue;
			}

			if (myTeam == ETeamType.None ||
				profile.Team == ETeamType.None)
			{
				continue;
			}

			ConsiderCandidate(
				profile.gameObject,
				allies
					? ELockOnTargetType.AllyPlayer
					: ELockOnTargetType.EnemyPlayer,
				ref bestTarget,
				ref bestType,
				ref bestScore);
		}
	}

	/// <summary>
	/// 相手ゴールを検索
	/// </summary>
	private void FindEnemyGoalCandidates(
		ref GameObject bestTarget,
		ref ELockOnTargetType bestType,
		ref float bestScore)
	{
		if (string.IsNullOrWhiteSpace(m_EnemyGoalTag))
		{
			return;
		}

		GameObject[] goals;

		try
		{
			goals = GameObject.FindGameObjectsWithTag(
				m_EnemyGoalTag);
		}
		catch (UnityException)
		{
			return;
		}

		foreach (GameObject goal in goals)
		{
			if (goal == null)
			{
				continue;
			}

			ConsiderCandidate(
				goal,
				ELockOnTargetType.EnemyGoal,
				ref bestTarget,
				ref bestType,
				ref bestScore);
		}
	}

	/// <summary>
	/// 検索対象が条件を満たすか判定
	/// </summary>
	private void ConsiderCandidate(
		GameObject candidate,
		ELockOnTargetType type,
		ref GameObject bestTarget,
		ref ELockOnTargetType bestType,
		ref float bestScore)
	{
		if (candidate == null ||
			candidate == gameObject)
		{
			return;
		}

		Vector3 worldPosition =
			GetTargetPosition(candidate);

		float worldDistance =
			Vector3.Distance(
				transform.position,
				worldPosition);

		bool canLog = CanLogSearchDetail();

		if (worldDistance > m_MaxSearchDistance)
		{
			if (canLog && type == ELockOnTargetType.Ball)
			{
				LogDebug(
					$"ボール除外: 距離超過 (現在={worldDistance:F1}m > 最大={m_MaxSearchDistance:F1}m)");
			}

			return;
		}

		Vector3 viewport =
			m_TargetCamera.WorldToViewportPoint(
				worldPosition);

		if (viewport.z <= 0f)
		{
			if (canLog && type == ELockOnTargetType.Ball)
			{
				LogDebug(
					$"ボール除外: カメラ背面 (Viewport.z={viewport.z:F2})");
			}

			return;
		}

		Vector2 reticle =
			new Vector2(0.5f, 0.5f);

		float screenDistance =
			Vector2.Distance(
				new Vector2(viewport.x, viewport.y),
				reticle);

		if (screenDistance > m_ReticleSearchRadius)
		{
			if (canLog && type == ELockOnTargetType.Ball)
			{
				LogDebug(
					$"ボール除外: レティクル外 (距離={screenDistance:F3} > 半径={m_ReticleSearchRadius:F3}, Viewport=({viewport.x:F2}, {viewport.y:F2}))");
			}

			return;
		}

		float score =
			screenDistance +
			(worldDistance /
			Mathf.Max(0.1f, m_MaxSearchDistance)) * 0.05f;

		// ボールを優先
		if (type == ELockOnTargetType.Ball)
		{
			score -= m_BallPriority;

			if (canLog)
			{
				LogDebug(
					$"ボール適合: 画面距離={screenDistance:F3}, ワールド距離={worldDistance:F1}m, Score={score:F3}");
			}
		}

		if (score < bestScore)
		{
			bestScore = score;
			bestTarget = candidate;
			bestType = type;
		}
	}

	/// <summary>
	/// 対象の中心座標を取得
	/// </summary>
	private Vector3 GetTargetPosition(GameObject target)
	{
		Collider collider =
			target.GetComponentInChildren<Collider>();

		if (collider != null)
		{
			return collider.bounds.center;
		}

		Renderer renderer =
			target.GetComponentInChildren<Renderer>();

		if (renderer != null)
		{
			return renderer.bounds.center;
		}

		return target.transform.position;
	}

	/// <summary>
	/// ロックオン解除
	/// </summary>
	public void ReleaseTarget()
	{
		CurrentTarget = null;
		m_CurrentTargetType = ELockOnTargetType.None;

		if (m_AllyLockOnMarker != null)
		{
			m_AllyLockOnMarker.gameObject.SetActive(false);
		}
	}

	public void ClearTarget()
	{
		if (CurrentTarget != null)
		{
			LogDebug(
				$"ターゲット解放: {CurrentTarget.name}");
		}

		CurrentTarget = null;
		m_CurrentTargetType = ELockOnTargetType.None;
		m_PreviousTarget = null;

		if (m_AllyLockOnMarker != null)
		{
			m_AllyLockOnMarker.gameObject.SetActive(false);
		}
	}

	/// <summary>
	/// ロックオンマーカーを更新
	/// </summary>
	private void UpdateLockOnMarker()
	{
		if (m_AllyLockOnMarker == null ||
			m_TargetCamera == null)
		{
			return;
		}

		if (CurrentTarget == null)
		{
			m_AllyLockOnMarker.gameObject.SetActive(false);
			return;
		}

		Vector3 screenPosition =
			m_TargetCamera.WorldToScreenPoint(
				GetTargetPosition(CurrentTarget));

		if (screenPosition.z <= 0f)
		{
			m_AllyLockOnMarker.gameObject.SetActive(false);
			return;
		}

		m_AllyLockOnMarker.gameObject.SetActive(true);
		m_AllyLockOnMarker.position = screenPosition;
	}
}
