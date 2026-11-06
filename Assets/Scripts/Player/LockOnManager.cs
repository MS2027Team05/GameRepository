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

	[Header("味方ロックオンマーカー")]
	[SerializeField]
	private RectTransform m_AllyLockOnMarker;

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
			return;
		}

		// TODO(PG3): NonBallHolder時はボール優先+敵プレイヤー、BallHolder時は味方+相手ゴールを壁越し許容で検索する
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
