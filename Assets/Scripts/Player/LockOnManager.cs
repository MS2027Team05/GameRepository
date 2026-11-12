using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(PlayerBallCarrier))]
[RequireComponent(typeof(PlayerInputReceiver))]
public class LockOnManager : MonoBehaviour, ILockOnProvider
{
	[Header("カメラ")]
	[SerializeField] private Camera m_TargetCamera;

	[Header("検索対象のタグ")]
	[SerializeField] private string m_AllyTag = "Ally";
	[SerializeField] private string m_BallTag = "Ball";

	[Header("ゴール検索")]
	private GoalController[] m_EnemyGoals;

	[Header("味方ロックオンマーカー")]
	[SerializeField]
	private RectTransform m_AllyLockOnMarker;

	[Header("パス対象の捕捉距離")]
	[SerializeField]
	[Min(0f)]
	private float m_MaxPassTargetDistance = 20f;



	private PlayerBallCarrier m_BallCarrier;
	private PlayerInputReceiver m_InputReceiver;
	private NetworkObject m_NetworkObject;
	private ELockOnSearchMode m_CurrentSearchMode = ELockOnSearchMode.None;
	private ELockOnTargetType m_CurrentTargetType = ELockOnTargetType.None;

	// 現在ロックオンしているGameObject
	public GameObject CurrentTarget { get; private set; }

	// プロパティ
	public bool HasTarget => CurrentTarget != null;

	/// <summary>ILockOnProvider: 現在捕捉している対象の種類</summary>
	public ELockOnTargetType CurrentTargetType
	{
		get => m_CurrentTargetType;
	}

	/// <summary>ILockOnProvider: 現在捕捉している対象のGameObject</summary>
	public GameObject CurrentTargetObject
	{
		get => CurrentTarget;
	}

	/// <summary>ILockOnProvider: 有効な対象が存在するか</summary>
	public bool HasValidTarget
	{
		get => CurrentTarget != null;
	}

	private void Awake()
	{
		m_BallCarrier = GetComponent<PlayerBallCarrier>();
		m_InputReceiver = GetComponent<PlayerInputReceiver>();
		m_NetworkObject = GetComponent<NetworkObject>();

		if (m_TargetCamera == null)
		{
			m_TargetCamera = Camera.main;
		}
	}

	/// <summary>
	/// シーン内のゴールを自動取得します。
	/// </summary>
	private void RefreshEnemyGoals()
	{
		m_EnemyGoals = FindObjectsOfType<GoalController>();

		Debug.Log(
			$"[LockOnManager] ゴールを再取得しました。登録数：{m_EnemyGoals.Length}"
		);

		if (m_EnemyGoals.Length == 0)
		{
			Debug.LogWarning(
				"[LockOnManager] シーン内にGoalControllerが見つかりません。"
			);
		}
	}

	private void OnEnable()
	{
		if (m_InputReceiver != null)
		{
			m_InputReceiver.OnLockOnPressed += LockOn;
		}
	}

	private void OnDisable()
	{
		if (m_InputReceiver != null)
		{
			m_InputReceiver.OnLockOnPressed -= LockOn;
		}

		ReleaseTarget();
	}

	private void Update()
	{
		// ターゲットが破棄された場合や
		// カメラの画面外に出た場合は解除
		if (CurrentTarget != null &&
			!IsOnScreen(CurrentTarget))
		{
			ReleaseTarget();
			Debug.Log("ロックオン解除");
		}
	}

	public void LockOn()
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

			// 画面外の対象は除外
			if (!IsOnScreen(candidate))
			{
				continue;
			}

			// 設定した距離より遠い対象は除外
			if (!IsWithinPassTargetDistance(candidate))
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

	/// <summary>
	/// 自分のチームを取得します。
	/// </summary>
	private ETeamType GetMyTeam()
	{
		PlayerAppearance appearance =
			GetComponent<PlayerAppearance>();

		if (appearance == null)
		{
			return ETeamType.None;
		}

		return appearance.CurrentTeam;
	}

	/// <summary>
	/// 指定されたプレイヤーが味方かどうかを判定します。
	/// </summary>
	private bool IsAllyPlayer(GameObject target)
	{
		if (target == null)
		{
			return false;
		}

		PlayerAppearance appearance =
			target.GetComponentInParent<PlayerAppearance>();

		if (appearance == null)
		{
			return false;
		}

		ETeamType myTeam = GetMyTeam();

		Debug.Log(
	$"[LockOn Debug] パス対象検索開始 / Team={myTeam} / Camera={m_TargetCamera.name}"
);

		return myTeam != ETeamType.None &&
			   appearance.CurrentTeam == myTeam;
	}


	private bool IsOnScreen(GameObject target)
	{
		if (target == null || m_TargetCamera == null)
		{
			return false;
		}

		// カメラの視界範囲を取得
		Plane[] planes =
			GeometryUtility.CalculateFrustumPlanes(m_TargetCamera);

		// -----------------------------------------
		// 1. Colliderの範囲で判定
		// -----------------------------------------

		Collider[] colliders =
			target.GetComponentsInChildren<Collider>(true);

		foreach (Collider col in colliders)
		{
			if (col == null || !col.enabled)
			{
				continue;
			}

			// Colliderの範囲がカメラ内にあれば有効
			if (GeometryUtility.TestPlanesAABB(
				planes, col.bounds))
			{
				return true;
			}
		}

		// -----------------------------------------
		// 2. Rendererの範囲で判定
		// -----------------------------------------

		Renderer[] renderers =
			target.GetComponentsInChildren<Renderer>(true);

		foreach (Renderer renderer in renderers)
		{
			if (renderer == null || !renderer.enabled)
			{
				continue;
			}

			if (GeometryUtility.TestPlanesAABB(
				planes, renderer.bounds))
			{
				return true;
			}
		}

		// -----------------------------------------
		// 3. 最後にオブジェクトの中心座標で判定
		// -----------------------------------------

		Vector3 position =
			m_TargetCamera.WorldToViewportPoint(
				target.transform.position);

		return position.z > 0f &&
			   position.x >= 0f && position.x <= 1f &&
			   position.y >= 0f && position.y <= 1f;
	}


	public void ReleaseTarget()
	{
		CurrentTarget = null;
		m_CurrentTargetType = ELockOnTargetType.None;
	}

	// =========================================================================
	// ILockOnProvider インターフェース窓口 (PG3担当: 本実装・改修対象)
	// =========================================================================

	/// <summary>
	/// 検索モードを設定します(PG3本実装対象)。
	/// </summary>
	public void SetSearchMode(ELockOnSearchMode mode)
	{
		m_CurrentSearchMode = mode;
		if (mode == ELockOnSearchMode.None)
		{
			ClearTarget();
		}
	}

	/// <summary>
	/// 現在の検索モードに基づいて対象を検索・更新します(PG3本実装対象)。
	/// </summary>
	public void UpdateTargetSearch()
	{
		if (m_CurrentSearchMode == ELockOnSearchMode.None)
		{
			ClearTarget();
			return;
		}

		if (m_TargetCamera == null)
		{
			m_TargetCamera = Camera.main;

			if (m_TargetCamera == null)
			{
				Debug.LogWarning("[LockOnManager] カメラが見つかりません。");
				return;
			}
		}

		if (m_CurrentSearchMode == ELockOnSearchMode.BallHolder)
		{
			// ゴールが未取得、または破棄されていたら再取得
			if (m_EnemyGoals == null ||
				m_EnemyGoals.Length == 0 ||
				System.Array.TrueForAll(m_EnemyGoals, goal => goal == null))
			{
				RefreshEnemyGoals();
			}

			SearchPassTarget();
		}
	}

	/// <summary>
	/// 自分と対象との距離が捕捉範囲内か判定します。
	/// </summary>
	private bool IsWithinPassTargetDistance(GameObject target)
	{
		if (target == null)
		{
			return false;
		}

		float distance = Vector3.Distance(
			transform.position,
			target.transform.position
		);

		bool canCapture = distance <= m_MaxPassTargetDistance;

		Debug.Log(
			$"[LockOn Debug] 距離判定: {target.name} / " +
			$"距離={distance:F2}m / " +
			$"上限={m_MaxPassTargetDistance:F2}m / " +
			$"判定={canCapture}"
		);

		return canCapture;
	}

	/// <summary>
	/// 味方プレイヤーと相手ゴールから、レティクルに最も近い対象を検索します。
	/// </summary>
	private void SearchPassTarget()
	{
		GameObject bestTarget = null;
		ELockOnTargetType bestTargetType = ELockOnTargetType.None;

		float nearestDistance = float.MaxValue;

		// 自分のチームを取得
		ETeamType myTeam = GetMyTeam();

		if (myTeam == ETeamType.None)
		{
			ClearTarget();
			return;
		}

		// -------------------------------------------------
		// 味方プレイヤーを検索
		// -------------------------------------------------

		PlayerAppearance[] players =
			FindObjectsOfType<PlayerAppearance>();

		foreach (PlayerAppearance player in players)
		{
			if (player == null)
			{
				continue;
			}

			GameObject candidate = player.gameObject;

			// 自分自身は除外
			if (candidate == gameObject)
			{
				continue;
			}

			Debug.Log(
	$"[LockOn Debug] プレイヤー発見: {candidate.name} / Team={player.CurrentTeam}"
);

			// 味方以外は除外
			if (player.CurrentTeam != myTeam)
			{
				continue;
			}

			// ボールを持っている味方は除外
			PlayerBallCarrier carrier =
				candidate.GetComponent<PlayerBallCarrier>();

			if (carrier != null && carrier.HasBall)
			{
				continue;
			}

			//if (!IsOnScreen(candidate))
			//{
			//	Debug.Log($"[LockOn Debug] 画面外のため除外: {candidate.name}");
			//	continue;
			//}

			// 自分との距離で捕捉範囲を判定
			if (!IsWithinPassTargetDistance(candidate))
			{
				continue;
			}

			Debug.Log($"[LockOn Debug] 味方候補として有効: {candidate.name}");

			//Vector3 screenPosition =
			//	m_TargetCamera.WorldToViewportPoint(
			//		candidate.transform.position);

			//float distance = Vector2.Distance(
			//	new Vector2(screenPosition.x, screenPosition.y),
			//	new Vector2(0.5f, 0.5f));

			// 自分と味方とのワールド距離を計算
			float distance = Vector3.Distance(
				transform.position,
				candidate.transform.position
			);

			if (distance < nearestDistance)
			{
				nearestDistance = distance;
				bestTarget = candidate;
				bestTargetType = ELockOnTargetType.AllyPlayer;
			}
		}


		// -------------------------------------------------
		// 相手ゴールを検索
		// -------------------------------------------------

		if (m_EnemyGoals != null)
		{
			foreach (GoalController goal in m_EnemyGoals)
			{
				if (goal == null)
					continue;

				Debug.Log(
					$"[LockOn Debug] ゴール発見: {goal.name} / " +
					$"ScoringTeam={goal.ScoringTeam} / MyTeam={myTeam}"
				);


				// 自分のチームが得点できるゴールだけを対象にする
				if (goal.ScoringTeam != myTeam)
				{
					Debug.Log(
						$"[LockOn Debug] 自チームが得点できないゴールなので除外: " +
						$"{goal.name} / ScoringTeam={goal.ScoringTeam}"
					);

					continue;
				}


				GameObject candidate = goal.gameObject;

				// 画面内判定
				bool isVisible = IsOnScreen(candidate);

				Debug.Log(
					$"[LockOn Debug] ゴール画面内判定: {isVisible}"
				);

				//if (!isVisible)
				//{
				//	Debug.LogWarning(
				//		$"[LockOn Debug] ゴールが画面外: {candidate.name}"
				//	);
				//	continue;
				//}

				// 画面外のゴールは除外
				if (!IsOnScreen(goal.gameObject))
				{
					continue;
				}

				// 設定した距離より遠いゴールは除外
				if (!IsWithinPassTargetDistance(goal.gameObject))
				{
					continue;
				}

				// ビューポート座標を取得
				//Vector3 screenPosition =
				//	m_TargetCamera.WorldToViewportPoint(
				//		candidate.transform.position
				//	);

				//Debug.Log(
				//	$"[LockOn Debug] ゴール座標: {screenPosition}"
				//);

				// レティクルとの距離を計算
				//float distance = Vector2.Distance(
				//	new Vector2(screenPosition.x, screenPosition.y),
				//	new Vector2(0.5f, 0.5f)
				//);

				// 自分とゴールとのワールド距離を計算
				float distance = Vector3.Distance(
					transform.position,
					goal.transform.position
				);
				
				Debug.Log(
					$"[LockOn Debug] ゴール距離: {distance} / " +
					$"現在の最短距離: {nearestDistance}"
				);

				// 異常な座標を除外
				if (float.IsNaN(distance) ||
					float.IsInfinity(distance))
				{
					Debug.LogWarning(
						"[LockOn Debug] ゴール座標が不正です"
					);
					continue;
				}

				// 最もレティクルに近い対象を選択
				if (distance < nearestDistance)
				{
					nearestDistance = distance;
					bestTarget = candidate;
					bestTargetType = ELockOnTargetType.EnemyGoal;

					Debug.Log(
						$"[LockOn Debug] ゴールを選択しました: {candidate.name}"
					);
				}
			}
		}


		// -------------------------------------------------
		// 検索結果を反映
		// -------------------------------------------------


		CurrentTarget = bestTarget;
		m_CurrentTargetType = bestTargetType;

		Debug.Log(
			$"[LockOn Debug] 最終検索結果: " +
			$"Target={(CurrentTarget != null ? CurrentTarget.name : "None")} / " +
			$"Type={m_CurrentTargetType} / " +
			$"Distance={nearestDistance}"
		);

		UpdateLockOnMarker();

		if (CurrentTarget != null)
		{
			Debug.Log(
				$"[LockOn Debug] ロックオン成功: " +
				$"{CurrentTarget.name} / Type={m_CurrentTargetType}"
			);
		}
		else
		{
			Debug.LogWarning(
				"[LockOn Debug] ロックオン対象が見つかりませんでした。"
			);
		}

	}

	/// <summary>
	/// 現在のロックオン対象およびマーカー表示をクリアします。
	/// </summary>
	public void ClearTarget()
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
