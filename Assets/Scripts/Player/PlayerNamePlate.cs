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

	[Header("チームカラー設定")]
	[FormerlySerializedAs("redTeamColor")]
	[SerializeField] private Color m_RedTeamColor;
	[FormerlySerializedAs("blueTeamColor")]
	[SerializeField] private Color m_BlueTeamColor;
	[FormerlySerializedAs("defaultColor")]
	[SerializeField] private Color m_DefaultColor;

	private PlayerNetworkProfile m_Profile;
	private NetworkObject m_NetworkObject;
	private Camera m_CachedCamera;

	private void Awake()
	{
		m_Profile = GetComponentInParent<PlayerNetworkProfile>();
		m_NetworkObject = GetComponentInParent<NetworkObject>();

		if (m_NameText == null)
		{
			m_NameText = GetComponentInChildren<TMP_Text>();
		}
	}

	private void Start()
	{
		if (m_Profile != null)
		{
			m_Profile.OnProfileChanged += UpdateNamePlate;
			UpdateNamePlate();
		}

		UpdateVisibility();
	}

	private void OnDestroy()
	{
		if (m_Profile != null)
		{
			m_Profile.OnProfileChanged -= UpdateNamePlate;
		}
	}

	private void LateUpdate()
	{
		// カメラが存在しない、または非アクティブ時はスキップ
		if (m_NameText == null || !m_NameText.gameObject.activeInHierarchy) return;

		UpdateBillboardRotation();
	}

	private void UpdateBillboardRotation()
	{
		if (m_CachedCamera == null || !m_CachedCamera.isActiveAndEnabled)
		{
			m_CachedCamera = Camera.main;
		}

		if (m_CachedCamera != null)
		{
			// カメラの向きと同じ回転を適用して正面を向かせる
			transform.rotation = m_CachedCamera.transform.rotation;
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
		}
		else
		{
			m_NameText.gameObject.SetActive(true);
		}
	}
}
