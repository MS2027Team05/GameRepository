using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(PlayerBallCarrier))]
[RequireComponent(typeof(PlayerInputReceiver))]
public class LockOnManager : MonoBehaviour
{
	[Header("カメラ")]
	[SerializeField] private Camera targetCamera;

	[Header("検索対象のタグ")]
	[SerializeField] private string allyTag = "Ally";
	[SerializeField] private string ballTag = "Ball";

	[Header("味方ロックオンマーカー")]
	[SerializeField]
	private RectTransform allyLockOnMarker;

	private PlayerBallCarrier ballCarrier;
	private PlayerInputReceiver inputReceiver;
	private NetworkObject networkObject;

	// 現在ロックオンしているGameObject
	public GameObject CurrentTarget { get; private set; }

	public bool HasTarget => CurrentTarget != null;

	private void Awake()
	{
		ballCarrier = GetComponent<PlayerBallCarrier>();
		inputReceiver = GetComponent<PlayerInputReceiver>();
		networkObject = GetComponent<NetworkObject>();

		if (targetCamera == null)
		{
			targetCamera = Camera.main;
		}
	}

	private void OnEnable()
	{
		if (inputReceiver != null)
		{
			inputReceiver.OnLockOnPressed += LockOn;
		}
	}

	private void OnDisable()
	{
		if (inputReceiver != null)
		{
			inputReceiver.OnLockOnPressed -= LockOn;
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
		if (networkObject != null &&
			networkObject.IsSpawned &&
			!networkObject.IsOwner)
		{
			return;
		}

		ReleaseTarget();

		if (targetCamera == null || ballCarrier == null)
		{
			return;
		}

		// ボール所持中なら味方、
		// 所持していなければボールを探す
		string searchTag =
			ballCarrier.HasBall ? allyTag : ballTag;

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
				targetCamera.WorldToViewportPoint(
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
			Debug.Log(
				"ロックオン: " + CurrentTarget.name);
		}
	}

	private bool IsOnScreen(GameObject target)
	{
		if (target == null || targetCamera == null)
			return false;

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
				GeometryUtility.CalculateFrustumPlanes(targetCamera);

			return GeometryUtility.TestPlanesAABB(
				planes, bounds);
		}

		// Rendererがない場合は従来の中心位置で判定する
		Vector3 position =
			targetCamera.WorldToViewportPoint(target.transform.position);

		return position.z > 0f &&
			   position.x >= 0f && position.x <= 1f &&
			   position.y >= 0f && position.y <= 1f;
	}

	public void ReleaseTarget()
	{
		CurrentTarget = null;
	}

	private void UpdateLockOnMarker()
	{
		if (allyLockOnMarker == null || targetCamera == null)
			return;

		// ロックオンしていない場合は非表示
		if (CurrentTarget == null)
		{
			allyLockOnMarker.gameObject.SetActive(false);
			return;
		}

		// ボールを所持していない場合は味方マーカーを表示しない
		if (ballCarrier == null || !ballCarrier.HasBall)
		{
			allyLockOnMarker.gameObject.SetActive(false);
			return;
		}

		// ターゲットの画面座標を取得
		Vector3 screenPosition =
			targetCamera.WorldToScreenPoint(
				CurrentTarget.transform.position);

		// カメラの後ろにいる場合は非表示
		if (screenPosition.z <= 0f)
		{
			allyLockOnMarker.gameObject.SetActive(false);
			return;
		}

		// 画面内の味方の位置にマーカーを表示
		allyLockOnMarker.gameObject.SetActive(true);
		allyLockOnMarker.position = screenPosition;
	}
}
