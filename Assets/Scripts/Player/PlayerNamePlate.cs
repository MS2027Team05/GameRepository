using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// プレイヤー頭上にプレイヤーネームおよびチームカラーを表示するネームプレートコンポーネント。
/// カメラ追従(Billboard)および自機視点での非表示制御を担当します。
/// </summary>
public class PlayerNamePlate : MonoBehaviour
{
	[Header("UIテキスト参照")]
	[FormerlySerializedAs("nameText")]
	[SerializeField] private TMP_Text m_NameText;

	[Header("表示設定")]
	[FormerlySerializedAs("hideOnLocalPlayer")]
	[SerializeField] private bool m_HideOnLocalPlayer;
	[SerializeField] private bool m_KeepConstantSize;
	[SerializeField] private float m_ReferenceDistance;
	[SerializeField] private float m_MinScaleMultiplier;
	[SerializeField] private float m_MaxScaleMultiplier;

	[Header("チームカラー設定")]
	[FormerlySerializedAs("redTeamColor")]
	[SerializeField] private Color m_RedTeamColor;
	[FormerlySerializedAs("blueTeamColor")]
	[SerializeField] private Color m_BlueTeamColor;
	[FormerlySerializedAs("defaultColor")]
	[SerializeField] private Color m_DefaultColor;

	private static readonly List<PlayerNamePlate> s_Instances = new List<PlayerNamePlate>();

	private PlayerNetworkProfile m_Profile;
	private NetworkObject m_NetworkObject;
	private Camera m_CachedCamera;
	private Vector3 m_InitialScale;

	private void Awake()
	{
		m_Profile = GetComponentInParent<PlayerNetworkProfile>();
		m_NetworkObject = GetComponentInParent<NetworkObject>();
		m_InitialScale = transform.localScale;

		if (m_NameText == null)
		{
			m_NameText = GetComponentInChildren<TMP_Text>();
		}
	}

	private void OnEnable()
	{
		if (!s_Instances.Contains(this))
		{
			s_Instances.Add(this);
		}
	}

	private void OnDisable()
	{
		s_Instances.Remove(this);
	}

	private void Start()
	{
		if (m_Profile != null)
		{
			m_Profile.OnProfileChanged += HandleProfileChanged;
			UpdateNamePlate();
		}

		UpdateVisibility();
	}

	private void OnDestroy()
	{
		if (m_Profile != null)
		{
			m_Profile.OnProfileChanged -= HandleProfileChanged;
		}
	}

	private void HandleProfileChanged()
	{
		UpdateNamePlate();

		// 自端末のプレイヤープロファイルが確定・更新された場合はシーン内の全ネームプレートを再評価
		if (m_NetworkObject != null && m_NetworkObject.IsOwner)
		{
			RefreshAll();
		}
	}

	/// <summary>
	/// シーン内に存在するすべてのPlayerNamePlateの表示設定を再評価します。
	/// ローカルプレイヤーの所属チームが確定した際に呼び出します。
	/// </summary>
	public static void RefreshAll()
	{
		for (int i = 0; i < s_Instances.Count; i++)
		{
			if (s_Instances[i] != null)
			{
				s_Instances[i].UpdateVisibility();
			}
		}
	}

	private void LateUpdate()
	{
		// カメラが存在しない、または非アクティブ時はスキップ
		if (m_NameText == null || !m_NameText.gameObject.activeInHierarchy) return;

		UpdateBillboardAndScale();
	}

	private void UpdateBillboardAndScale()
	{
		if (m_CachedCamera == null || !m_CachedCamera.isActiveAndEnabled)
		{
			m_CachedCamera = Camera.main;
		}

		if (m_CachedCamera == null) return;

		// カメラの向きと同じ回転を適用して正面を向かせる
		transform.rotation = m_CachedCamera.transform.rotation;

		// 距離に応じたサイズ補正(カメラから離れても極端に縮小しないように維持)
		if (m_KeepConstantSize)
		{
			float distance = Vector3.Distance(transform.position, m_CachedCamera.transform.position);
			float refDist = m_ReferenceDistance > 0.001f ? m_ReferenceDistance : 10f;
			float scaleMultiplier = distance / refDist;

			float minMult = m_MinScaleMultiplier > 0.001f ? m_MinScaleMultiplier : 0.5f;
			float maxMult = m_MaxScaleMultiplier > 0.001f ? m_MaxScaleMultiplier : 3.0f;
			scaleMultiplier = Mathf.Clamp(scaleMultiplier, minMult, maxMult);

			transform.localScale = m_InitialScale * scaleMultiplier;
		}
	}

	private void UpdateNamePlate()
	{
		if (m_Profile == null || m_NameText == null) return;

		m_NameText.text = m_Profile.PlayerName;

		// チームカラーの適用
		switch (m_Profile.Team)
		{
			case ETeamType.Red:
				m_NameText.color = m_RedTeamColor;
				break;
			case ETeamType.Blue:
				m_NameText.color = m_BlueTeamColor;
				break;
			default:
				m_NameText.color = m_DefaultColor;
				break;
		}

		UpdateVisibility();
	}

	private void UpdateVisibility()
	{
		if (m_NameText == null) return;

		// 自端末プレイヤーかつ非表示設定が有効な場合は非表示
		bool isLocalOwner = (m_NetworkObject != null && m_NetworkObject.IsOwner);
		if (isLocalOwner && m_HideOnLocalPlayer)
		{
			m_NameText.gameObject.SetActive(false);
			return;
		}

		// 観戦者判定: ホスト専用端末(観戦カメラ)の場合は全プレイヤー名を表示
		bool isSpectator = !NetworkConnectManager.IsPlayerHost && (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer);
		if (isSpectator)
		{
			m_NameText.gameObject.SetActive(true);
			return;
		}

		// ローカルクライアントの所属チームを取得
		ETeamType localPlayerTeam = ETeamType.None;
		if (NetworkManager.Singleton != null &&
			NetworkManager.Singleton.LocalClient != null &&
			NetworkManager.Singleton.LocalClient.PlayerObject != null)
		{
			var localProfile = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerNetworkProfile>();
			if (localProfile != null)
			{
				localPlayerTeam = localProfile.Team;
			}
		}

		ETeamType targetTeam = m_Profile != null ? m_Profile.Team : ETeamType.None;

		// 敵プレイヤーの名前は見えないように制御(味方のみ表示)
		if (localPlayerTeam != ETeamType.None && targetTeam != ETeamType.None)
		{
			bool isAlly = (localPlayerTeam == targetTeam);
			m_NameText.gameObject.SetActive(isAlly);
		}
		else
		{
			// ローカルプレイヤーの所属が未定の段階では一旦表示(確定時にRefreshAllで再評価)
			m_NameText.gameObject.SetActive(true);
		}
	}
}
