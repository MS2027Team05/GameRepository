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

	// レンダリングキュー定数(不透明壁描画後の3010でZTest Greaterを評価して確実に壁裏描画)
	private const int m_StencilMaskQueue = 2010;
	private const int m_SilhouetteQueue = 3010;
	private const int m_OutlineQueue = 2020;

	private static readonly List<PlayerSilhouette> s_Instances = new List<PlayerSilhouette>();

	private Renderer[] m_Renderers;
	private Dictionary<Renderer, Material[]> m_OriginalMaterials;

	private Material m_OutlineInstance;
	private Material m_StencilMaskInstance;
	private Material m_SilhouetteInstance;

	private PlayerNetworkProfile m_Profile;
	private NetworkObject m_NetworkObject;
	private bool m_IsAiming;

	private void Awake()
	{
		m_Profile = GetComponentInParent<PlayerNetworkProfile>();
		m_NetworkObject = GetComponentInParent<NetworkObject>();

		m_Renderers = CollectRenderers();
		CacheOriginalMaterials();
		CreateMaterialInstances();
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

		// 自端末のプレイヤープロファイルが確定・更新された場合はシーン内の全シルエットを再評価
		if (m_NetworkObject != null && m_NetworkObject.IsOwner)
		{
			RefreshAll();
		}
	}

	/// <summary>
	/// シーン内に存在するすべてのPlayerSilhouetteのチーム設定を再評価します。
	/// ローカルプレイヤーの所属チームが確定した際に呼び出します。
	/// </summary>
	public static void RefreshAll()
	{
		for (int i = 0; i < s_Instances.Count; i++)
		{
			if (s_Instances[i] != null)
			{
				s_Instances[i].ApplyTeamSettings();
			}
		}
	}

	private Renderer[] CollectRenderers()
	{
		var all = GetComponentsInChildren<Renderer>(true);
		var list = new List<Renderer>(all.Length);

		// キャラクターモデル(SkinnedMeshRenderer)が存在するか判定
		bool hasSkinnedMesh = false;
		for (int i = 0; i < all.Length; i++)
		{
			if (all[i] is SkinnedMeshRenderer)
			{
				hasSkinnedMesh = true;
				break;
			}
		}

		for (int i = 0; i < all.Length; i++)
		{
			var r = all[i];
			if (r == null) continue;

			// LineRendererやTrailRenderer等の特殊ライン演出は除外
			if (r is LineRenderer || r is TrailRenderer || r is ParticleSystemRenderer)
			{
				continue;
			}

			// TextMeshPro等のUI・テキスト用レンダラーは除外
			if (r.GetComponent<TMPro.TMP_Text>() != null || r.GetComponent<TMPro.TMP_SubMeshUI>() != null)
			{
				continue;
			}

			// 手動除外リストに含まれている場合は除外
			if (m_ExcludeRenderers != null && Array.IndexOf(m_ExcludeRenderers, r) >= 0)
			{
				continue;
			}

			// Tポーズ等のSkinnedMeshRendererが存在する場合、ルートのカプセル用MeshRenderer等の干渉を除外
			if (hasSkinnedMesh && r is MeshRenderer && r.gameObject == gameObject)
			{
				continue;
			}

			list.Add(r);
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
	/// 自端末のローカルプレイヤーの所属チームを取得します。
	/// </summary>
	private static ETeamType GetLocalPlayerTeam()
	{
		// 1. NGO標準のLocalClient.PlayerObjectから取得
		if (NetworkManager.Singleton != null &&
			NetworkManager.Singleton.LocalClient != null &&
			NetworkManager.Singleton.LocalClient.PlayerObject != null)
		{
			var localProfile = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerNetworkProfile>();
			if (localProfile != null && localProfile.Team != ETeamType.None)
			{
				return localProfile.Team;
			}
		}

		Debug.Log("所属チームfallback実行");

		// 2. シーン内の全インスタンスからIsOwnerであるものを探索(フォールバック)
		for (int i = 0; i < s_Instances.Count; i++)
		{
			var instance = s_Instances[i];
			if (instance != null && instance.m_NetworkObject != null && instance.m_NetworkObject.IsOwner)
			{
				if (instance.m_Profile != null && instance.m_Profile.Team != ETeamType.None)
				{
					return instance.m_Profile.Team;
				}
			}
		}

		return ETeamType.None;
	}

	/// <summary>
	/// 自端末の視点(味方・敵・観戦者)および所属チームに基づいてアウトライン・シルエット設定を適用します。
	/// </summary>
	public void ApplyTeamSettings()
	{
		if (m_IsAiming) return;

		ETeamType myTeam = m_Profile != null ? m_Profile.Team : ETeamType.None;

		// ローカルクライアント情報の判定
		bool isSpectator = !NetworkConnectManager.IsPlayerHost && (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer);
		ETeamType localPlayerTeam = GetLocalPlayerTeam();

		// 遮蔽シルエットの表示可否: 観戦者または自チーム(味方)の場合のみ有効
		// 自端末のチームが確定している場合、敵チーム(myTeam != localPlayerTeam)は完全に除外
		bool isAlly;
		if (localPlayerTeam != ETeamType.None && myTeam != ETeamType.None)
		{
			isAlly = (localPlayerTeam == myTeam);
		}
		else
		{
			// ローカルプレイヤーまたは自身の所属チームが未定の段階(エディタ単体テスト等)は表示を許可
			isAlly = true;
		}

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
			// Inspectorで0設定の場合の保護(未設定時でも最低限の視認性を確保)
			float alpha = m_PlayerSilhouetteAlpha > 0.001f ? m_PlayerSilhouetteAlpha : 0.6f;
			Color silColor = new Color(teamColor.r, teamColor.g, teamColor.b, alpha);
			m_SilhouetteInstance.SetColor(m_SilhouetteColorId, silColor);
			// 遮蔽時は壁の奥のメッシュを無条件描画するためCompareFunction.Alwaysに設定
			m_SilhouetteInstance.SetFloat(m_StencilCompId, (float)UnityEngine.Rendering.CompareFunction.Always);
			m_SilhouetteInstance.SetFloat(m_StencilPassId, (float)UnityEngine.Rendering.StencilOp.Keep);
		}

		// マテリアル配列の再構築
		RebuildMaterials(m_UseOutline, shouldShowSilhouette);
	}

	/// <summary>
	/// エイム時の半透明マテリアル適用状態をアウトラインおよびシルエットと合成して反映します。
	/// </summary>
	public void ApplyAimTransparentState(bool isAiming, Renderer[] renderers, Material[][] transparentMaterials)
	{
		m_IsAiming = isAiming;

		if (!isAiming)
		{
			ApplyTeamSettings();
			return;
		}

		if (renderers == null || transparentMaterials == null) return;

		// 遮蔽シルエットの表示可否を判定
		bool isSpectator = !NetworkConnectManager.IsPlayerHost && (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer);
		ETeamType localPlayerTeam = GetLocalPlayerTeam();
		ETeamType myTeam = m_Profile != null ? m_Profile.Team : ETeamType.None;

		bool isAlly;
		if (localPlayerTeam != ETeamType.None && myTeam != ETeamType.None)
		{
			isAlly = (localPlayerTeam == myTeam);
		}
		else
		{
			isAlly = true;
		}

		bool shouldShowSilhouette = isSpectator || isAlly;

		for (int i = 0; i < renderers.Length; i++)
		{
			var r = renderers[i];
			if (r == null || i >= transparentMaterials.Length) continue;

			var baseMats = transparentMaterials[i];
			if (baseMats == null) continue;

			// 半透明マテリアルの末尾にアウトライン・シルエット用マテリアルを付与
			int extraCount = 0;
			if (m_UseOutline && m_StencilMaskInstance != null) extraCount++;
			if (shouldShowSilhouette && m_SilhouetteInstance != null) extraCount++;
			if (m_UseOutline && m_OutlineInstance != null) extraCount++;

			var newMats = new Material[baseMats.Length + extraCount];
			baseMats.CopyTo(newMats, 0);

			int idx = baseMats.Length;
			if (m_UseOutline && m_StencilMaskInstance != null)
			{
				newMats[idx++] = m_StencilMaskInstance;
			}
			if (shouldShowSilhouette && m_SilhouetteInstance != null)
			{
				newMats[idx++] = m_SilhouetteInstance;
			}
			if (m_UseOutline && m_OutlineInstance != null)
			{
				newMats[idx++] = m_OutlineInstance;
			}

			r.sharedMaterials = newMats;
		}
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

