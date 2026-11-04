using UnityEngine;

/// <summary>
/// プレイヤーキャラクターの外見(男女モデル切り替え・12色マテリアル・チームカラーマテリアル)を管理するコンポーネント。
/// ロビー表示用PrefabおよびインゲームのPlayerPrefabの双方で共通利用します。
/// </summary>
public sealed class PlayerAppearance : MonoBehaviour
{
	[Header("外見カタログ設定")]
	[SerializeField] private PlayerAppearanceCatalog m_AppearanceCatalog;

	[Header("モデル")]
	[SerializeField] private GameObject m_ManModel;
	[SerializeField] private GameObject m_WomanModel;
	[SerializeField] private Renderer[] m_ManRenderers;
	[SerializeField] private Renderer[] m_WomanRenderers;

	[Header("チームカラー設定")]
	[SerializeField] private Renderer[] m_TeamColorRenderers;

	private PlayerNetworkProfile m_NetworkProfile;
	private PlayerSilhouette m_PlayerSilhouette;
	private PlayerAimGuide m_PlayerAimGuide;

	private bool m_HasAppliedCharacterType;
	private bool m_HasAppliedColor;
	private bool m_HasAppliedTeamColor;
	private ECharacterType m_CurrentCharacterType;
	private EHighlightColor m_CurrentColor;
	private ETeamType m_CurrentTeam;
	private Material m_CurrentMaterial;
	private Material m_CurrentTeamMaterial;

	public PlayerAppearanceCatalog AppearanceCatalog { get => m_AppearanceCatalog; }
	public bool HasAppliedCharacterType { get => m_HasAppliedCharacterType; }
	public bool HasAppliedColor { get => m_HasAppliedColor; }
	public bool HasAppliedTeamColor { get => m_HasAppliedTeamColor; }
	public ECharacterType CurrentCharacterType { get => m_CurrentCharacterType; }
	public EHighlightColor CurrentColor { get => m_CurrentColor; }
	public ETeamType CurrentTeam { get => m_CurrentTeam; }
	public Material CurrentMaterial { get => m_CurrentMaterial; }
	public Material CurrentTeamMaterial { get => m_CurrentTeamMaterial; }

	private void Awake()
	{
		m_NetworkProfile = GetComponent<PlayerNetworkProfile>();
		m_PlayerSilhouette = GetComponent<PlayerSilhouette>();
		m_PlayerAimGuide = GetComponent<PlayerAimGuide>();
	}

	private void OnEnable()
	{
		if (m_NetworkProfile != null)
		{
			m_NetworkProfile.OnProfileChanged += HandleProfileChanged;
		}
	}

	private void Start()
	{
		// インゲームでPlayerNetworkProfileが存在する場合は初期同期値を反映
		if (m_NetworkProfile != null)
		{
			ApplyFromNetworkProfile();
		}
	}

	private void OnDisable()
	{
		if (m_NetworkProfile != null)
		{
			m_NetworkProfile.OnProfileChanged -= HandleProfileChanged;
		}
	}

	private void HandleProfileChanged()
	{
		ApplyFromNetworkProfile();
	}

	/// <summary>
	/// 同一オブジェクト上のPlayerNetworkProfileからプロファイル情報を読み取って外見を反映します。
	/// </summary>
	public void ApplyFromNetworkProfile()
	{
		if (m_NetworkProfile == null)
		{
			return;
		}

		ApplyAppearance(
			m_NetworkProfile.CharacterType,
			m_NetworkProfile.HighlightColor,
			m_NetworkProfile.Team);
	}

	/// <summary>
	/// 指定されたモデル種別・12色カラー・所属チームに基づいて外見を一括反映します。
	/// </summary>
	public void ApplyAppearance(ECharacterType characterType, EHighlightColor colorType, ETeamType team)
	{
		// インゲームでアウトラインやシルエットが付与されている場合は一旦元のマテリアル配列に戻す
		if (m_PlayerSilhouette != null && Application.isPlaying)
		{
			m_PlayerSilhouette.RestoreBaseMaterials();
		}

		ApplyCharacterModel(characterType);
		ApplyBodyColor(characterType, colorType);
		ApplyTeamColor(team);

		// 差し替え後の本体マテリアルをシルエットおよびエイム半透明制御へ再キャッシュさせる
		if (Application.isPlaying)
		{
			if (m_PlayerSilhouette != null)
			{
				m_PlayerSilhouette.RefreshBaseMaterials();
			}

			if (m_PlayerAimGuide != null)
			{
				m_PlayerAimGuide.RefreshPlayerMaterials();
			}
		}
	}

	/// <summary>
	/// キャラクター種別(男/女)に応じて表示モデルの有効化を切り替えます。
	/// </summary>
	public void ApplyCharacterModel(ECharacterType characterType)
	{
		if (m_ManModel == null || m_WomanModel == null)
		{
			Debug.LogWarning("男性・女性モデルの参照が設定されていません。", this);
			return;
		}

		m_ManModel.SetActive(characterType == ECharacterType.Man);
		m_WomanModel.SetActive(characterType == ECharacterType.Woman);

		m_CurrentCharacterType = characterType;
		m_HasAppliedCharacterType = true;
	}

	/// <summary>
	/// カタログから男女別のカラーマテリアルを取得して対象Rendererへ反映します。
	/// </summary>
	public void ApplyBodyColor(ECharacterType activeCharacterType, EHighlightColor colorType)
	{
		if (m_AppearanceCatalog == null)
		{
			Debug.LogWarning("PlayerAppearanceCatalogの参照が設定されていません。", this);
			return;
		}

		Material manMaterial = m_AppearanceCatalog.GetBodyMaterial(ECharacterType.Man, colorType);
		Material womanMaterial = m_AppearanceCatalog.GetBodyMaterial(ECharacterType.Woman, colorType);

		if (manMaterial != null)
		{
			ApplyMaterial(m_ManRenderers, manMaterial);
		}

		if (womanMaterial != null)
		{
			ApplyMaterial(m_WomanRenderers, womanMaterial);
		}

		m_CurrentColor = colorType;
		m_CurrentMaterial = activeCharacterType == ECharacterType.Woman ? womanMaterial : manMaterial;
		m_HasAppliedColor = m_CurrentMaterial != null;
	}

	/// <summary>
	/// カタログからチームカラーマテリアルを取得して対象Rendererへ反映します。
	/// </summary>
	public void ApplyTeamColor(ETeamType team)
	{
		m_CurrentTeam = team;

		if (team == ETeamType.None)
		{
			return;
		}

		if (m_TeamColorRenderers == null || m_TeamColorRenderers.Length == 0)
		{
			return;
		}

		if (m_AppearanceCatalog == null)
		{
			Debug.LogWarning("PlayerAppearanceCatalogの参照が設定されていません。", this);
			return;
		}

		Material teamMaterial = m_AppearanceCatalog.GetTeamMaterial(team);
		if (teamMaterial == null)
		{
			return;
		}

		ApplyMaterial(m_TeamColorRenderers, teamMaterial);

		m_CurrentTeamMaterial = teamMaterial;
		m_HasAppliedTeamColor = true;
	}

	private static void ApplyMaterial(Renderer[] renderers, Material material)
	{
		if (renderers == null || material == null)
		{
			return;
		}

		foreach (Renderer targetRenderer in renderers)
		{
			if (targetRenderer != null)
			{
				targetRenderer.sharedMaterial = material;
			}
		}
	}

#if UNITY_EDITOR
	[Header("エディタ確認用デバッグ設定(EditMode専用)")]
	[SerializeField] private bool m_UseDebugAppearance;
	[SerializeField] private ECharacterType m_DebugCharacterType;
	[SerializeField] private EHighlightColor m_DebugColor;
	[SerializeField] private ETeamType m_DebugTeam;

	private void OnValidate()
	{
		if (Application.isPlaying)
		{
			return;
		}

		UnityEditor.EditorApplication.delayCall -= ApplyAppearanceDelayed;
		UnityEditor.EditorApplication.delayCall += ApplyAppearanceDelayed;
	}

	private void ApplyAppearanceDelayed()
	{
		UnityEditor.EditorApplication.delayCall -= ApplyAppearanceDelayed;

		if (this == null || Application.isPlaying || !m_UseDebugAppearance)
		{
			return;
		}

		ApplyCharacterModel(m_DebugCharacterType);
		if (m_AppearanceCatalog != null)
		{
			ApplyBodyColor(m_DebugCharacterType, m_DebugColor);
			ApplyTeamColor(m_DebugTeam);
		}
	}

	private void OnDestroy()
	{
		UnityEditor.EditorApplication.delayCall -= ApplyAppearanceDelayed;
	}
#endif
}
