//LockOnManager.cs
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

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

	/// <summary>
	/// ロックオンのデバッグログを出力
	/// </summary>
	private void LogDebug(string message)
	{
		if (!m_EnableDebugLog)
			return;

		Debug.Log(
			$"[LockOn] {message}",
			this);
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

		if (m_TargetCamera == null)
			m_TargetCamera = Camera.main;

		if (m_AllyLockOnMarker != null)
			m_AllyLockOnMarker.gameObject.SetActive(false);
	}

	//   private void OnEnable()
	//{
	//	if (m_InputReceiver != null)
	//	{
	//		m_InputReceiver.OnLockOnPressed += LockOn;
	//	}
	//}

	private void OnDisable()
	{
		SetSearchMode(ELockOnSearchMode.None);
	}

	private void Update()
	{
		/*if (Keyboard.current != null &&
			Keyboard.current.lKey.wasPressedThisFrame)
		{
			Debug.Log("[LockOn Debug] Lを押した");

			if (m_CurrentSearchMode == ELockOnSearchMode.None)
			{
				SetSearchMode(
					m_BallCarrier != null && m_BallCarrier.HasBall
					? ELockOnSearchMode.BallHolder
					: ELockOnSearchMode.NonBallHolder);
			}
			else
			{
				SetSearchMode(ELockOnSearchMode.None);
			}
		}*/

		UpdateLockOnMarker();
	}

	// 旧キー操作との互換用
	public void LockOn()
	{
		if (m_BallCarrier == null)
			return;

		SetSearchMode(
			m_BallCarrier.HasBall
			? ELockOnSearchMode.BallHolder
			: ELockOnSearchMode.NonBallHolder);

		UpdateTargetSearch();
	}

	/* public void LockOn()
     {
         // すでにロックオン中なら、もう一度押すと解除
         if (CurrentTarget != null)
         {
             ReleaseTarget();
             Debug.Log("ロックオン解除");
             return;
         }


         // マルチプレイでは操作中のプレイヤーだけが実行する
         if (m_NetworkObject != null &&
             m_NetworkObject.IsSpawned &&
             !m_NetworkObject.IsOwner)
         {
             return;
         }

         ReleaseTarget();

         if (m_TargetCamera == null || m_BallCarrier == null)
         {
             return;
         }

         // ボール所持中なら味方、
         // 所持していなければボールを探す
         string searchTag =
             m_BallCarrier.HasBall ? m_AllyTag : m_BallTag;

         GameObject[] candidates;

         try
         {
             candidates =
                 GameObject.FindGameObjectsWithTag(searchTag);
         }
         catch (UnityException)
         {
             Debug.LogError(
                 "タグが登録されていません: " + searchTag);
             return;
         }

         float nearestDistance = float.MaxValue;

         foreach (GameObject candidate in candidates)
         {
             if (candidate == null)
             {
                 continue;
             }

             // 自分自身はターゲットにしない
             if (candidate == gameObject)
             {
                 continue;
             }

             if (!IsOnScreen(candidate))
             {
                 continue;
             }

             Vector3 screenPosition =
                 m_TargetCamera.WorldToViewportPoint(
                     candidate.transform.position);

             // 画面中央に近い対象を優先
             float distance = Vector2.Distance(
                 new Vector2(screenPosition.x, screenPosition.y),
                 new Vector2(0.5f, 0.5f));

             if (distance < nearestDistance)
             {
                 nearestDistance = distance;
                 CurrentTarget = candidate;
             }
         }

         if (CurrentTarget != null)
         {
             m_CurrentTargetType = m_BallCarrier.HasBall
                 ? ELockOnTargetType.AllyPlayer
                 : ELockOnTargetType.Ball;

             Debug.Log(
                 "ロックオン: " + CurrentTarget.name);
         }
     }

     private bool IsOnScreen(GameObject target)
     {
         if (target == null || m_TargetCamera == null)
         {
             return false;
         }

         // ボールの見た目や当たり判定の大きさを考慮する
         Renderer[] renderers =
             target.GetComponentsInChildren<Renderer>();

         if (renderers.Length > 0)
         {
             Bounds bounds = renderers[0].bounds;

             for (int i = 1; i < renderers.Length; i++)
             {
                 bounds.Encapsulate(renderers[i].bounds);
             }

             // ボールの中心だけでなく、表示範囲で判定する
             Plane[] planes =
                 GeometryUtility.CalculateFrustumPlanes(m_TargetCamera);

             return GeometryUtility.TestPlanesAABB(
                 planes, bounds);
         }

         // Rendererがない場合は従来の中心位置で判定する
         Vector3 position =
             m_TargetCamera.WorldToViewportPoint(target.transform.position);

         return position.z > 0f &&
                position.x >= 0f && position.x <= 1f &&
                position.y >= 0f && position.y <= 1f;
     }

     public void ReleaseTarget()
     {
         CurrentTarget = null;
         m_CurrentTargetType = ELockOnTargetType.None;
     }
    */
	// =========================================================================
	// ILockOnProvider インターフェース窓口 (PG3担当: 本実装・改修対象)
	// =========================================================================

	/// <summary>
	/// 検索モードを設定
	/// </summary>
	public void SetSearchMode(ELockOnSearchMode mode)
	{
		if (m_CurrentSearchMode == mode)
			return;

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
	/// 現在の検索モードに基づいて対象を検索・更新します(PG3本実装対象)。
	/// </summary>
	/// <summary>
	/// 現在のモードに応じて対象を検索
	/// </summary>
	public void UpdateTargetSearch()
	{
		if (m_CurrentSearchMode == ELockOnSearchMode.None ||
			m_TargetCamera == null ||
			m_BallCarrier == null)
			return;

		if (m_NetworkObject != null &&
			m_NetworkObject.IsSpawned &&
			!m_NetworkObject.IsOwner)
			return;

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
			FindObjectsOfType<BallController>();

		foreach (BallController ball in balls)
		{
			if (ball == null ||
				!ball.gameObject.activeInHierarchy)
				continue;

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
			FindObjectsOfType<PlayerNetworkProfile>();

		ETeamType myTeam =
			m_PlayerProfile != null
			? m_PlayerProfile.Team
			: ETeamType.None;

		foreach (PlayerNetworkProfile profile in players)
		{
			if (profile == null ||
				profile.gameObject == gameObject ||
				!profile.gameObject.activeInHierarchy)
				continue;

			bool sameTeam =
				myTeam != ETeamType.None &&
				profile.Team == myTeam;

			if (allies != sameTeam)
				continue;

			if (myTeam == ETeamType.None ||
				profile.Team == ETeamType.None)
				continue;

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
			return;

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
				continue;

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
			return;

		Vector3 worldPosition =
			GetTargetPosition(candidate);

		float worldDistance =
			Vector3.Distance(
				transform.position,
				worldPosition);

		if (worldDistance > m_MaxSearchDistance)
			return;

		Vector3 viewport =
			m_TargetCamera.WorldToViewportPoint(
				worldPosition);

		if (viewport.z <= 0f)
			return;

		Vector2 reticle =
			new Vector2(0.5f, 0.5f);

		float screenDistance =
			Vector2.Distance(
				new Vector2(viewport.x, viewport.y),
				reticle);

		if (screenDistance > m_ReticleSearchRadius)
			return;

		float score =
			screenDistance +
			(worldDistance /
			Mathf.Max(0.1f, m_MaxSearchDistance)) * 0.05f;

		// ボールを優先
		if (type == ELockOnTargetType.Ball)
			score -= m_BallPriority;

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
			return collider.bounds.center;

		Renderer renderer =
			target.GetComponentInChildren<Renderer>();

		if (renderer != null)
			return renderer.bounds.center;

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
			m_AllyLockOnMarker.gameObject.SetActive(false);
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
			return;

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


/// <summary>
/// 現在のロックオン対象およびマーカー表示をクリアします。
/// </summary>
/*public void ClearTarget()
{
	ReleaseTarget();
	if (m_AllyLockOnMarker != null)
	{
		m_AllyLockOnMarker.gameObject.SetActive(false);
	}
}

private void UpdateLockOnMarker()
{
	if (m_AllyLockOnMarker == null || m_TargetCamera == null)
	{
		return;
	}

	// ロックオンしていない場合は非表示
	if (CurrentTarget == null)
	{
		m_AllyLockOnMarker.gameObject.SetActive(false);
		return;
	}

	// ボールを所持していない場合は味方マーカーを表示しない
	if (m_BallCarrier == null || !m_BallCarrier.HasBall)
	{
		m_AllyLockOnMarker.gameObject.SetActive(false);
		return;
	}

	// ターゲットの画面座標を取得
	Vector3 screenPosition =
		m_TargetCamera.WorldToScreenPoint(
			CurrentTarget.transform.position);

	// カメラの後ろにいる場合は非表示
	if (screenPosition.z <= 0f)
	{
		m_AllyLockOnMarker.gameObject.SetActive(false);
		return;
	}

	// 画面内の味方の位置にマーカーを表示
	m_AllyLockOnMarker.gameObject.SetActive(true);
	m_AllyLockOnMarker.position = screenPosition;
}
}
*/
