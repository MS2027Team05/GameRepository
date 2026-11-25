using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ボール・ゴール・プレイヤーの位置を画面上に表示するUIを管理します。
/// </summary>
public class PlayerWorldTargetUI : MonoBehaviour
{
	[Header("カメラ設定")]
	[Tooltip("対象を画面上に表示するためのカメラです。未設定の場合はMainCameraを使用します。")]
	[SerializeField] private Camera m_TargetCamera;

	[Header("ボールUI")]
	[SerializeField] private GameObject m_BallRoot;
	[SerializeField] private GameObject m_BallBlue;
	[SerializeField] private GameObject m_BallRed;
	[SerializeField] private GameObject m_BallNormal;

	[Header("ゴールUIテンプレート")]
	[Tooltip("Goal以下のUIをテンプレートとして使用します。")]
	[SerializeField] private GameObject m_GoalTemplate;
	[SerializeField] private GameObject m_GoalBlue;
	[SerializeField] private GameObject m_GoalRed;

	[Header("プレイヤーUIテンプレート")]
	[Tooltip("Player以下のUIをテンプレートとして使用します。")]
	[SerializeField] private GameObject m_PlayerTemplate;
	[SerializeField] private GameObject m_PlayerBlue;
	[SerializeField] private GameObject m_PlayerRed;

	[Header("対象位置の調整")]
	[Tooltip("ボールUIの表示位置を上方向へずらす量です。")]
	[SerializeField] private float m_BallHeightOffset = 0.5f;

	[Tooltip("ゴールUIの表示位置を上方向へずらす量です。")]
	[SerializeField] private float m_GoalHeightOffset = 3.0f;

	[Tooltip("プレイヤーUIの表示位置を上方向へずらす量です。")]
	[SerializeField] private float m_PlayerHeightOffset = 2.0f;

	[Header("UIサイズ設定")]
	[Tooltip("ボールUIの基本サイズ倍率です。")]
	[SerializeField, Range(0.01f, 1f)]
	private float m_BallMarkerScale = 0.1f;

	[Tooltip("ゴールUIの基本サイズ倍率です。")]
	[SerializeField, Range(0.01f, 1f)]
	private float m_GoalMarkerScale = 0.1f;

	[Tooltip("プレイヤーUIの基本サイズ倍率です。")]
	[SerializeField, Range(0.01f, 1f)]
	private float m_PlayerMarkerScale = 0.1f;

	[Header("画面外のUI表示設定")]
	[Tooltip("ONの場合、画面外の対象UIを非表示にします。OFFの場合は画面端に表示します。")]
	[SerializeField] private bool m_HideWhenOffScreen = true;

	[Tooltip("画面端に表示するときの余白です（ピクセル）。")]
	[SerializeField] private float m_EdgeMargin = 40f;

	[Tooltip("画面端に表示するときのUIの縮小率です。")]
	[SerializeField, Range(0.1f, 1f)]
	private float m_EdgeMarkerScale = 0.65f;

	[Header("更新設定")]
	[Tooltip("対象の検索を行う間隔です（秒）。")]
	[SerializeField] private float m_RefreshInterval = 0.5f;

	private RectTransform m_RootRect;
	private NetworkObject m_LocalNetworkObject;
	private PlayerNetworkProfile m_LocalProfile;

	private BallController m_BallController;

	private float m_NextRefreshTime;

	private Vector3 m_BallOriginalScale;

	private readonly Dictionary<int, GoalMarker> m_GoalMarkers =
		new Dictionary<int, GoalMarker>();

	private readonly Dictionary<ulong, PlayerMarker> m_PlayerMarkers =
		new Dictionary<ulong, PlayerMarker>();

	/// <summary>
	/// ゴールUIと対象ゴールの組み合わせです。
	/// </summary>
	private class GoalMarker
	{
		public GoalController m_Target;
		public MarkerView m_View;
	}

	/// <summary>
	/// プレイヤーUIと対象プレイヤーの組み合わせです。
	/// </summary>
	private class PlayerMarker
	{
		public PlayerNetworkProfile m_Target;
		public MarkerView m_View;
	}

	/// <summary>
	/// 赤・青のUIをまとめて管理します。
	/// </summary>
	private class MarkerView
	{
		public GameObject m_Root;
		public GameObject m_Blue;
		public GameObject m_Red;
		public Vector3 m_OriginalScale;
	}

	private void Awake()
	{
		m_RootRect = GetComponent<RectTransform>();

		m_LocalNetworkObject =
			GetComponentInParent<NetworkObject>();

		m_LocalProfile =
			GetComponentInParent<PlayerNetworkProfile>();

		if (m_TargetCamera == null)
		{
			m_TargetCamera = Camera.main;
		}

		if (m_BallRoot != null)
		{
			m_BallOriginalScale =
				m_BallRoot.transform.localScale;

			m_BallRoot.SetActive(false);
		}

		// ゴールとプレイヤーのUIはテンプレートとして保持します。
		if (m_GoalTemplate != null)
		{
			m_GoalTemplate.SetActive(false);
		}

		if (m_PlayerTemplate != null)
		{
			m_PlayerTemplate.SetActive(false);
		}
	}

	private void Update()
	{
		// ローカルプレイヤー以外のUIは表示しません。
		if (m_LocalNetworkObject == null ||
			!m_LocalNetworkObject.IsOwner)
		{
			HideAllMarkers();
			return;
		}

		if (m_TargetCamera == null)
		{
			m_TargetCamera = Camera.main;
		}

		if (m_TargetCamera == null ||
			m_RootRect == null ||
			m_LocalProfile == null)
		{
			HideAllMarkers();
			return;
		}

		// 一定間隔で対象を検索します。
		if (Time.time >= m_NextRefreshTime)
		{
			RefreshTargets();

			m_NextRefreshTime =
				Time.time + Mathf.Max(0.1f, m_RefreshInterval);
		}

		UpdateBallMarker();
		UpdateGoalMarkers();
		UpdatePlayerMarkers();
	}

	/// <summary>
	/// ボール・ゴール・プレイヤーの対象を検索します。
	/// </summary>
	private void RefreshTargets()
	{
		// ボールを検索します。
		BallController[] balls =
			FindObjectsByType<BallController>(
				FindObjectsSortMode.None);

		m_BallController =
			balls.Length > 0 ? balls[0] : null;

		// ゴールを検索します。
		HashSet<int> foundGoals = new HashSet<int>();

		GoalController[] goals =
			FindObjectsByType<GoalController>(
				FindObjectsSortMode.None);

		foreach (GoalController goal in goals)
		{
			if (goal == null)
			{
				continue;
			}

			int instanceId = goal.GetInstanceID();
			foundGoals.Add(instanceId);

			if (!m_GoalMarkers.ContainsKey(instanceId))
			{
				MarkerView view = CreateMarkerView(
					m_GoalTemplate,
					m_GoalBlue,
					m_GoalRed);

				if (view != null)
				{
					m_GoalMarkers.Add(
						instanceId,
						new GoalMarker
						{
							m_Target = goal,
							m_View = view
						});
				}
			}
		}

		// 既に消滅したゴールのUIを削除します。
		List<int> removedGoals = new List<int>();

		foreach (var pair in m_GoalMarkers)
		{
			if (!foundGoals.Contains(pair.Key))
			{
				Destroy(pair.Value.m_View.m_Root);
				removedGoals.Add(pair.Key);
			}
		}

		foreach (int id in removedGoals)
		{
			m_GoalMarkers.Remove(id);
		}

		// NetworkObjectからプレイヤーを検索します。
		if (NetworkManager.Singleton == null ||
			NetworkManager.Singleton.SpawnManager == null)
		{
			return;
		}

		HashSet<ulong> foundPlayers = new HashSet<ulong>();

		foreach (NetworkObject networkObject in
			NetworkManager.Singleton.SpawnManager.SpawnedObjectsList)
		{
			if (networkObject == null)
			{
				continue;
			}

			PlayerNetworkProfile profile =
				networkObject.GetComponent<PlayerNetworkProfile>();

			if (profile == null || profile == m_LocalProfile)
			{
				continue;
			}

			ulong networkObjectId =
				networkObject.NetworkObjectId;

			foundPlayers.Add(networkObjectId);

			if (!m_PlayerMarkers.ContainsKey(networkObjectId))
			{
				MarkerView view = CreateMarkerView(
					m_PlayerTemplate,
					m_PlayerBlue,
					m_PlayerRed);

				if (view != null)
				{
					m_PlayerMarkers.Add(
						networkObjectId,
						new PlayerMarker
						{
							m_Target = profile,
							m_View = view
						});
				}
			}
		}

		// DespawnされたプレイヤーのUIを削除します。
		List<ulong> removedPlayers = new List<ulong>();

		foreach (var pair in m_PlayerMarkers)
		{
			if (!foundPlayers.Contains(pair.Key))
			{
				Destroy(pair.Value.m_View.m_Root);
				removedPlayers.Add(pair.Key);
			}
		}

		foreach (ulong id in removedPlayers)
		{
			m_PlayerMarkers.Remove(id);
		}
	}

	/// <summary>
	/// テンプレートを複製してUIを作成します。
	/// </summary>
	private MarkerView CreateMarkerView(
		GameObject template,
		GameObject blue,
		GameObject red)
	{
		if (template == null ||
			blue == null ||
			red == null)
		{
			Debug.LogError(
				"[PlayerWorldTargetUI] UIテンプレートまたは色別UIが設定されていません。",
				this);

			return null;
		}

		GameObject instance =
			Instantiate(template, transform);

		instance.name = template.name + "_Instance";
		instance.SetActive(true);

		// テンプレートと同じ名前の子オブジェクトを取得します。
		Transform blueTransform =
			instance.transform.Find(blue.name);

		Transform redTransform =
			instance.transform.Find(red.name);

		if (blueTransform == null || redTransform == null)
		{
			Debug.LogError(
				"[PlayerWorldTargetUI] テンプレート内に色別UIが見つかりません。",
				instance);

			Destroy(instance);
			return null;
		}

		MarkerView view = new MarkerView
		{
			m_Root = instance,
			m_Blue = blueTransform.gameObject,
			m_Red = redTransform.gameObject,
			m_OriginalScale = instance.transform.localScale
		};

		view.m_Blue.SetActive(false);
		view.m_Red.SetActive(false);

		return view;
	}

	/// <summary>
	/// ボールUIを更新します。
	/// </summary>
	private void UpdateBallMarker()
	{
		if (m_BallRoot == null)
		{
			return;
		}

		if (m_BallController == null ||
			!m_BallController.IsSpawned)
		{
			m_BallRoot.SetActive(false);
			return;
		}

		GameObject selectedImage = m_BallNormal;

		if (m_BallController.HasHolder)
		{
			PlayerNetworkProfile holderProfile =
				FindHolderProfile(m_BallController.CurrentHolderId);

			if (holderProfile != null)
			{
				if (holderProfile.Team == ETeamType.Red)
				{
					selectedImage = m_BallRed;
				}
				else if (holderProfile.Team == ETeamType.Blue)
				{
					selectedImage = m_BallBlue;
				}
			}
		}

		SetBallImage(selectedImage);

		Vector3 position =
			m_BallController.VisualTransform.position +
			Vector3.up * m_BallHeightOffset;

		UpdateMarkerPosition(
			m_BallRoot.transform,
			position,
			m_BallOriginalScale * m_BallMarkerScale);
	}

	/// <summary>
	/// ClientIdからボールの所持者を検索します。
	/// </summary>
	private PlayerNetworkProfile FindHolderProfile(
		ulong holderClientId)
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
				networkObject.OwnerClientId != holderClientId)
			{
				continue;
			}

			PlayerNetworkProfile profile =
				networkObject.GetComponent<PlayerNetworkProfile>();

			if (profile != null)
			{
				return profile;
			}
		}

		return null;
	}

	/// <summary>
	/// ボールの色別画像を切り替えます。
	/// </summary>
	private void SetBallImage(GameObject selectedImage)
	{
		if (m_BallBlue != null)
		{
			m_BallBlue.SetActive(selectedImage == m_BallBlue);
		}

		if (m_BallRed != null)
		{
			m_BallRed.SetActive(selectedImage == m_BallRed);
		}

		if (m_BallNormal != null)
		{
			m_BallNormal.SetActive(selectedImage == m_BallNormal);
		}
	}

	/// <summary>
	/// ゴールUIを更新します。
	/// </summary>
	private void UpdateGoalMarkers()
	{
		foreach (var pair in m_GoalMarkers)
		{
			GoalMarker marker = pair.Value;

			if (marker.m_Target == null ||
				marker.m_View == null)
			{
				continue;
			}

			ETeamType team =
				marker.m_Target.ScoringTeam;

			// ScoringTeamは得点を獲得するチームなので、
			// ゴールUIは反対チームの色にします。
			if (team == ETeamType.Red)
			{
				// 赤チームが得点するゴール → 青チーム側のゴール
				SetMarkerImage(marker.m_View, false);
			}
			else if (team == ETeamType.Blue)
			{
				// 青チームが得点するゴール → 赤チーム側のゴール
				SetMarkerImage(marker.m_View, true);
			}
			else
			{
				marker.m_View.m_Root.SetActive(false);
				continue;
			}

			Vector3 position =
				marker.m_Target.transform.position +
				Vector3.up * m_GoalHeightOffset;

			UpdateMarkerPosition(
				marker.m_View.m_Root.transform,
				position,
				marker.m_View.m_OriginalScale * m_GoalMarkerScale);
		}
	}

	/// <summary>
	/// プレイヤーUIを更新します。
	/// </summary>
	private void UpdatePlayerMarkers()
	{
		foreach (var pair in m_PlayerMarkers)
		{
			PlayerMarker marker = pair.Value;

			if (marker.m_Target == null ||
				marker.m_View == null)
			{
				continue;
			}

			ETeamType team =
				marker.m_Target.Team;

			// 自分と違うチームのプレイヤーUIは表示しない
			if (team != m_LocalProfile.Team)
			{
				marker.m_View.m_Root.SetActive(false);
				continue;
			}

			if (team == ETeamType.Red)
			{
				SetMarkerImage(marker.m_View, true);
			}
			else if (team == ETeamType.Blue)
			{
				SetMarkerImage(marker.m_View, false);
			}
			else
			{
				marker.m_View.m_Root.SetActive(false);
				continue;
			}

			Vector3 position =
				marker.m_Target.transform.position +
				Vector3.up * m_PlayerHeightOffset;

			UpdateMarkerPosition(
				marker.m_View.m_Root.transform,
				position,
				marker.m_View.m_OriginalScale * m_PlayerMarkerScale);
		}
	}

	/// <summary>
	/// 赤・青の画像を切り替えます。
	/// </summary>
	private void SetMarkerImage(
		MarkerView view,
		bool isRed)
	{
		view.m_Blue.SetActive(!isRed);
		view.m_Red.SetActive(isRed);
	}

	/// <summary>
	/// ワールド座標を画面座標に変換してUIを配置します。
	/// </summary>
	private void UpdateMarkerPosition(
		Transform markerTransform,
		Vector3 worldPosition,
		Vector3 originalScale)
	{
		if (markerTransform == null)
		{
			return;
		}

		Vector3 screenPosition =
			m_TargetCamera.WorldToScreenPoint(worldPosition);

		Rect cameraRect =
			m_TargetCamera.pixelRect;

		bool isBehindCamera =
			screenPosition.z <= 0f;

		bool isOutsideScreen =
			screenPosition.x < cameraRect.xMin ||
			screenPosition.x > cameraRect.xMax ||
			screenPosition.y < cameraRect.yMin ||
			screenPosition.y > cameraRect.yMax;

		bool isOffScreen =
			isBehindCamera || isOutsideScreen;

		if (isOffScreen && m_HideWhenOffScreen)
		{
			markerTransform.gameObject.SetActive(false);
			return;
		}

		markerTransform.gameObject.SetActive(true);

		float scaleMultiplier = 1f;

		if (isOffScreen)
		{
			// カメラの後ろにいる対象は、画面中央に対して反対側へ向けます。
			if (isBehindCamera)
			{
				screenPosition.x =
					cameraRect.center.x * 2f - screenPosition.x;

				screenPosition.y =
					cameraRect.center.y * 2f - screenPosition.y;
			}

			float margin = Mathf.Max(0f, m_EdgeMargin);

			// 画面の左右どちらに表示するか判定
			bool showOnRight =
				screenPosition.x >= cameraRect.center.x;

			// 左右の表示位置を決定
			screenPosition.x = showOnRight
				? cameraRect.xMax - margin
				: cameraRect.xMin + margin;

			// 上下方向は画面内に収める
			screenPosition.y = Mathf.Clamp(
				screenPosition.y,
				cameraRect.yMin + margin,
				cameraRect.yMax - margin);

			// 画面外ではUIを縮小
			scaleMultiplier = m_EdgeMarkerScale;
		}

		if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
			m_RootRect,
			screenPosition,
			null,
			out Vector2 localPoint))
		{
			RectTransform markerRect =
				markerTransform as RectTransform;

			if (markerRect != null)
			{
				markerRect.anchoredPosition = localPoint;
			}
		}

		markerTransform.localScale =
			originalScale * scaleMultiplier;
	}

	/// <summary>
	/// UIをすべて非表示にします。
	/// </summary>
	private void HideAllMarkers()
	{
		if (m_BallRoot != null)
		{
			m_BallRoot.SetActive(false);
		}

		foreach (var pair in m_GoalMarkers)
		{
			if (pair.Value.m_View != null)
			{
				pair.Value.m_View.m_Root.SetActive(false);
			}
		}

		foreach (var pair in m_PlayerMarkers)
		{
			if (pair.Value.m_View != null)
			{
				pair.Value.m_View.m_Root.SetActive(false);
			}
		}
	}
}
