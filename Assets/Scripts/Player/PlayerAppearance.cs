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

	[Header("電流カラー設定")]
	[Tooltip("このプレイヤーが使用する電流の色を、選択色または所属チームの色から決定します。")]
	[SerializeField] private EEffectColorSource m_EffectColorSource;

	private PlayerNetworkProfile m_NetworkProfile;
	private PlayerSilhouette m_PlayerSilhouette;
	private PlayerEmissionController m_EmissionController;
	private PlayerAppearanceCatalog m_SubscribedEmissionCatalog;

	private bool m_HasAppliedCharacterType;
	private bool m_HasAppliedColor;
	private bool m_HasAppliedTeamColor;
	private ECharacterType m_CurrentCharacterType;
	private EHighlightColor m_CurrentColor;
	private ETeamType m_CurrentTeam;
	private Material m_CurrentMaterial;
	private Material m_CurrentTeamMaterial;
	private bool m_HasSelectedEffectColor;

	/// <summary>
	/// 電流に使用する色の変更を演出側へ通知します。
	/// </summary>
	public event System.Action EffectColorChanged;

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
	}

	private void OnEnable()
	{
		SubscribeEmissionSettings();
		RefreshEmission();

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
		UnsubscribeEmissionSettings();
		m_EmissionController?.Restore();

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

		// 差し替え後の本体マテリアルをシルエット制御へ再キャッシュさせる
		if (Application.isPlaying && m_PlayerSilhouette != null)
		{
			m_PlayerSilhouette.RefreshBaseMaterials();
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
		RefreshEmission();
		m_HasSelectedEffectColor = true;
		EffectColorChanged?.Invoke();
	}

	/// <summary>
	/// Inspectorで指定した取得元に対応する電流用カラーを取得します。
	/// </summary>
	public bool TryGetEffectColor(out Color color)
	{
		return TryGetEffectColor(out color, out _);
	}

	/// <summary>
	/// 電流の色とVFX用パレット種別を同じ取得元から返します。
	/// チーム未確定時にはカタログの既定表示色を使います。
	/// </summary>
	public bool TryGetEffectColor(out Color color, out EHighlightColor? paletteColor)
	{
		color = default;
		paletteColor = null;
		if (m_AppearanceCatalog == null)
		{
			return false;
		}

		if (m_EffectColorSource == EEffectColorSource.TeamColor)
		{
			color = m_AppearanceCatalog.GetTeamDisplayColor(m_CurrentTeam);
			paletteColor = m_CurrentTeam switch
			{
				ETeamType.Red => EHighlightColor.Red,
				ETeamType.Blue => EHighlightColor.Blue,
				_ => null,
			};
			return true;
		}

		if (!m_HasSelectedEffectColor || !m_AppearanceCatalog.TryGetPreviewColor(m_CurrentColor, out color))
		{
			return false;
		}
		paletteColor = m_CurrentColor;
		return true;
	}

	/// <summary>
	/// カタログからチームカラーマテリアルを取得して対象Rendererへ反映します。
	/// </summary>
	public void ApplyTeamColor(ETeamType team)
	{
		m_CurrentTeam = team;
		RefreshEmission();
		if (m_EffectColorSource == EEffectColorSource.TeamColor)
		{
			EffectColorChanged?.Invoke();
		}

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
		RefreshEmission();
	}

	private void SubscribeEmissionSettings()
	{
		if (m_SubscribedEmissionCatalog == m_AppearanceCatalog)
		{
			return;
		}

		UnsubscribeEmissionSettings();
		m_SubscribedEmissionCatalog = m_AppearanceCatalog;
		if (m_SubscribedEmissionCatalog != null)
		{
			m_SubscribedEmissionCatalog.EmissionSettingsChanged += RefreshEmission;
		}
	}

	private void UnsubscribeEmissionSettings()
	{
		if (m_SubscribedEmissionCatalog != null)
		{
			m_SubscribedEmissionCatalog.EmissionSettingsChanged -= RefreshEmission;
		}
		m_SubscribedEmissionCatalog = null;
	}

	private void RefreshEmission()
	{
		if (!isActiveAndEnabled || m_AppearanceCatalog == null || !m_AppearanceCatalog.UseEmissionSettings)
		{
			m_EmissionController?.Restore();
			return;
		}

		// Unityの描画APIを呼ぶため、MonoBehaviourのフィールド初期化時には生成しません。
		m_EmissionController ??= new PlayerEmissionController();
		Color emissionColor = m_AppearanceCatalog.GetEmissionColor(m_CurrentTeam);
		m_EmissionController.Apply(m_ManRenderers, emissionColor);
		m_EmissionController.Apply(m_WomanRenderers, emissionColor);
		m_EmissionController.Apply(m_TeamColorRenderers, emissionColor);
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
		UnityEditor.EditorApplication.delayCall -= NotifyEffectColorChangedDelayed;
		UnityEditor.EditorApplication.delayCall += NotifyEffectColorChangedDelayed;

		if (Application.isPlaying)
		{
			return;
		}

		UnityEditor.EditorApplication.delayCall -= ApplyAppearanceDelayed;
		UnityEditor.EditorApplication.delayCall += ApplyAppearanceDelayed;
	}

	private void NotifyEffectColorChangedDelayed()
	{
		if (this != null)
		{
			if (Application.isPlaying && isActiveAndEnabled)
			{
				SubscribeEmissionSettings();
			}
			RefreshEmission();
			EffectColorChanged?.Invoke();
		}
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

#endif

	private void OnDestroy()
	{
		UnsubscribeEmissionSettings();
		m_EmissionController?.Restore();
#if UNITY_EDITOR
		UnityEditor.EditorApplication.delayCall -= ApplyAppearanceDelayed;
		UnityEditor.EditorApplication.delayCall -= NotifyEffectColorChangedDelayed;
#endif
	}
}
