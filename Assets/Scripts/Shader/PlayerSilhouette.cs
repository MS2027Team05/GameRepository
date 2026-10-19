using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// プレイヤーキャラクターのチーム別アウトライン・遮蔽シルエット制御コンポーネント。
/// 「視界が通っている時は敵味方ともにアウトライン表示」「遮蔽時は味方のみシルエット透過」「観戦者は両陣営透過」を制御します。
/// </summary>
public class PlayerSilhouette : MonoBehaviour
{
	[Header("機能の有効化設定")]
	[FormerlySerializedAs("useOutline")]
	[SerializeField] private bool m_UseOutline;

	[Header("マテリアル設定")]
	[FormerlySerializedAs("outlineMaterial")]
	[SerializeField] private Material m_OutlineMaterial;
	[FormerlySerializedAs("stencilMaskMaterial")]
	[SerializeField] private Material m_StencilMaskMaterial;
	[FormerlySerializedAs("silhouetteMaterial")]
	[SerializeField] private Material m_SilhouetteMaterial;

	[Header("除外設定")]
	[FormerlySerializedAs("excludeRenderers")]
	[SerializeField] private Renderer[] m_ExcludeRenderers;

	[Header("チームカラー設定")]
	[SerializeField] private Color m_RedTeamColor;
	[SerializeField] private Color m_BlueTeamColor;
	[SerializeField] private Color m_DefaultOutlineColor;
	[Range(0f, 1f)]
	[FormerlySerializedAs("playerSilhouetteAlpha")]
	[SerializeField] private float m_PlayerSilhouetteAlpha;

	private static readonly int m_OutlineColorId = Shader.PropertyToID("_OutlineColor");
	private static readonly int m_SilhouetteColorId = Shader.PropertyToID("_SilhouetteColor");
	private static readonly int m_StencilRefId = Shader.PropertyToID("_StencilRef");
	private static readonly int m_StencilCompId = Shader.PropertyToID("_StencilComp");
	private static readonly int m_StencilPassId = Shader.PropertyToID("_StencilPass");

	// レンダリングキュー定数(道具本体の2050より前に描画して誤遮蔽を防止)
	private const int m_StencilMaskQueue = 2010;
	private const int m_SilhouetteQueue = 2015;
	private const int m_OutlineQueue = 2020;

	private Renderer[] m_Renderers;
	private Dictionary<Renderer, Material[]> m_OriginalMaterials;

	private Material m_OutlineInstance;
	private Material m_StencilMaskInstance;
	private Material m_SilhouetteInstance;

	private PlayerNetworkProfile m_Profile;
	private NetworkObject m_NetworkObject;

	private void Awake()
	{
		m_Profile = GetComponentInParent<PlayerNetworkProfile>();
		m_NetworkObject = GetComponentInParent<NetworkObject>();

		m_Renderers = CollectRenderers();
		CacheOriginalMaterials();
		CreateMaterialInstances();
	}

	private void Start()
	{
		if (m_Profile != null)
		{
			m_Profile.OnProfileChanged += HandleProfileChanged;
		}

		ApplyTeamSettings();
	}

	private void OnDestroy()
	{
		if (m_Profile != null)
		{
			m_Profile.OnProfileChanged -= HandleProfileChanged;
		}

		RestoreOriginalMaterials();

		if (m_OutlineInstance != null) Destroy(m_OutlineInstance);
		if (m_StencilMaskInstance != null) Destroy(m_StencilMaskInstance);
		if (m_SilhouetteInstance != null) Destroy(m_SilhouetteInstance);
	}

	private void HandleProfileChanged()
	{
		ApplyTeamSettings();
	}

	private Renderer[] CollectRenderers()
	{
		var all = GetComponentsInChildren<Renderer>();
		if (m_ExcludeRenderers == null || m_ExcludeRenderers.Length == 0)
		{
			return all;
		}

		var list = new List<Renderer>(all.Length);
		foreach (var r in all)
		{
			if (Array.IndexOf(m_ExcludeRenderers, r) < 0)
			{
				list.Add(r);
			}
		}
		return list.ToArray();
	}

	private void CacheOriginalMaterials()
	{
		m_OriginalMaterials = new Dictionary<Renderer, Material[]>(m_Renderers.Length);
		foreach (var r in m_Renderers)
		{
			if (r != null)
			{
				m_OriginalMaterials[r] = r.sharedMaterials;
			}
		}
	}

	private void RestoreOriginalMaterials()
	{
		if (m_OriginalMaterials == null) return;

		foreach (var kvp in m_OriginalMaterials)
		{
			if (kvp.Key != null && kvp.Value != null)
			{
				kvp.Key.sharedMaterials = kvp.Value;
			}
		}
	}

	private void CreateMaterialInstances()
	{
		if (m_OutlineMaterial != null)
		{
			m_OutlineInstance = new Material(m_OutlineMaterial);
			m_OutlineInstance.renderQueue = m_OutlineQueue;
		}

		if (m_StencilMaskMaterial != null)
		{
			m_StencilMaskInstance = new Material(m_StencilMaskMaterial);
			m_StencilMaskInstance.renderQueue = m_StencilMaskQueue;
		}

		if (m_SilhouetteMaterial != null)
		{
			m_SilhouetteInstance = new Material(m_SilhouetteMaterial);
			m_SilhouetteInstance.renderQueue = m_SilhouetteQueue;
		}
	}

	/// <summary>
	/// 自端末の視点(味方・敵・観戦者)および所属チームに基づいてアウトライン・シルエット設定を適用します。
	/// </summary>
	public void ApplyTeamSettings()
	{
		ETeamType myTeam = m_Profile != null ? m_Profile.Team : ETeamType.None;

		// ローカルクライアント情報の判定
		bool isSpectator = !NetworkConnectManager.IsPlayerHost && (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer);
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

		// 遮蔽シルエットの表示可否: 観戦者または自チーム(味方)の場合のみ有効
		bool isAlly = (localPlayerTeam != ETeamType.None && localPlayerTeam == myTeam);
		bool shouldShowSilhouette = isSpectator || isAlly;

		// チームカラーの決定
		Color teamColor;
		switch (myTeam)
		{
			case ETeamType.Red:
				teamColor = m_RedTeamColor;
				break;
			case ETeamType.Blue:
				teamColor = m_BlueTeamColor;
				break;
			default:
				teamColor = m_DefaultOutlineColor;
				break;
		}

		// ステンシル参照値(0を回避するため +1)
		int stencilRef = m_NetworkObject != null ? ((int)(m_NetworkObject.NetworkObjectId % 250) + 1) : 1;

		// マテリアルパラメータの設定
		if (m_OutlineInstance != null)
		{
			m_OutlineInstance.SetColor(m_OutlineColorId, teamColor);
			m_OutlineInstance.SetFloat(m_StencilRefId, stencilRef);
		}

		if (m_StencilMaskInstance != null)
		{
			m_StencilMaskInstance.SetFloat(m_StencilRefId, stencilRef);
		}

		if (m_SilhouetteInstance != null)
		{
			Color silColor = new Color(teamColor.r, teamColor.g, teamColor.b, m_PlayerSilhouetteAlpha);
			m_SilhouetteInstance.SetColor(m_SilhouetteColorId, silColor);
			m_SilhouetteInstance.SetFloat(m_StencilRefId, 0f);
			m_SilhouetteInstance.SetFloat(m_StencilCompId, (float)UnityEngine.Rendering.CompareFunction.Equal);
			m_SilhouetteInstance.SetFloat(m_StencilPassId, (float)UnityEngine.Rendering.StencilOp.IncrementSaturate);
		}

		// マテリアル配列の再構築
		RebuildMaterials(m_UseOutline, shouldShowSilhouette);
	}

	private void RebuildMaterials(bool useOutline, bool useSilhouette)
	{
		if (m_Renderers == null || m_OriginalMaterials == null) return;

		int extraCount = 0;
		if (useOutline && m_StencilMaskInstance != null) extraCount++;
		if (useSilhouette && m_SilhouetteInstance != null) extraCount++;
		if (useOutline && m_OutlineInstance != null) extraCount++;

		foreach (var r in m_Renderers)
		{
			if (r == null || !m_OriginalMaterials.TryGetValue(r, out var originals) || originals == null)
			{
				continue;
			}

			var newMaterials = new Material[originals.Length + extraCount];
			originals.CopyTo(newMaterials, 0);

			int index = originals.Length;
			if (useOutline && m_StencilMaskInstance != null)
			{
				newMaterials[index++] = m_StencilMaskInstance;
			}
			if (useSilhouette && m_SilhouetteInstance != null)
			{
				newMaterials[index++] = m_SilhouetteInstance;
			}
			if (useOutline && m_OutlineInstance != null)
			{
				newMaterials[index++] = m_OutlineInstance;
			}

			r.sharedMaterials = newMaterials;
		}
	}

#if UNITY_EDITOR
	private void OnValidate()
	{
		if (Application.isPlaying)
		{
			ApplyTeamSettings();
		}
	}
#endif
}

