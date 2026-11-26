using System;
using UnityEngine;

/// <summary>
/// プレイヤー外見(男女別12色マテリアル・チームカラーマテリアル・UIプレビュー色)を一元管理するカタログ。
/// ロビー表示・ロビー色選択UI・インゲームプレイヤーの3箇所から共通参照します。
/// </summary>
[CreateAssetMenu(fileName = "PlayerAppearanceCatalog", menuName = "ScriptableObjects/Player/PlayerAppearanceCatalog")]
public sealed class PlayerAppearanceCatalog : ScriptableObject
{
	[Serializable]
	public struct ColorMaterialEntry
	{
		[Tooltip("対象のカラー種別")]
		[SerializeField] private EHighlightColor m_ColorType;

		[Tooltip("UIピッカーや台座演出に使用するプレビュー色")]
		[SerializeField] private Color m_PreviewColor;

		[Tooltip("男性モデル(Man)に適用するマテリアル")]
		[SerializeField] private Material m_ManMaterial;

		[Tooltip("女性モデル(Woman)に適用するマテリアル")]
		[SerializeField] private Material m_WomanMaterial;

		public EHighlightColor ColorType { get => m_ColorType; }
		public Color PreviewColor { get => m_PreviewColor; }
		public Material ManMaterial { get => m_ManMaterial; }
		public Material WomanMaterial { get => m_WomanMaterial; }
	}

	[Header("12色カラーマテリアル設定")]
	[SerializeField] private ColorMaterialEntry[] m_ColorEntries;

	[Header("チームカラーマテリアル設定")]
	[SerializeField] private Material m_RedTeamMaterial;
	[SerializeField] private Material m_BlueTeamMaterial;

	[Header("チーム表示色設定(ロビー頭上ネーム・台座演出用)")]
	[SerializeField] private Color m_RedTeamDisplayColor;
	[SerializeField] private Color m_BlueTeamDisplayColor;
	[SerializeField] private Color m_DefaultTeamDisplayColor;

	[Header("プレイヤー共通の発光設定")]
	[Tooltip("有効にすると、Emission Mapを持つ本体とチームカラーの発光を一括調整します。")]
	[SerializeField] private bool m_UseEmissionSettings;
	[Tooltip("全プレイヤー共通の発光倍率です。0で発光を停止し、1を超える値で明るくします。")]
	[Min(0.0f)]
	[SerializeField] private float m_EmissionIntensity;
	[Tooltip("赤チームの発光色です。明るさは共通の発光倍率で調整します。")]
	[ColorUsage(false, false)]
	[SerializeField] private Color m_RedTeamEmissionColor;
	[Tooltip("青チームの発光色です。明るさは共通の発光倍率で調整します。")]
	[ColorUsage(false, false)]
	[SerializeField] private Color m_BlueTeamEmissionColor;
	[Tooltip("ロビーなど、チーム未確定時の発光色です。")]
	[ColorUsage(false, false)]
	[SerializeField] private Color m_DefaultEmissionColor;

	/// <summary>発光設定の変更を表示側へ通知します。</summary>
	public event Action EmissionSettingsChanged;

	public bool UseEmissionSettings { get => m_UseEmissionSettings; }

	public ColorMaterialEntry[] ColorEntries { get => m_ColorEntries; }

	/// <summary>
	/// チーム別の発光色に全プレイヤー共通の倍率を掛けたHDRカラーを返します。
	/// </summary>
	public Color GetEmissionColor(ETeamType team)
	{
		Color color = team switch
		{
			ETeamType.Red => m_RedTeamEmissionColor,
			ETeamType.Blue => m_BlueTeamEmissionColor,
			_ => m_DefaultEmissionColor,
		};
		color *= Mathf.Max(0.0f, m_EmissionIntensity);
		color.a = 1.0f;
		return color;
	}

#if UNITY_EDITOR
	private void OnValidate()
	{
		// Inspectorの検証中にRendererを操作しないよう、通知をメインスレッドへ遅延します。
		UnityEditor.EditorApplication.delayCall -= NotifyEmissionSettingsChanged;
		UnityEditor.EditorApplication.delayCall += NotifyEmissionSettingsChanged;
	}

	private void NotifyEmissionSettingsChanged()
	{
		if (this != null)
		{
			EmissionSettingsChanged?.Invoke();
		}
	}

	private void OnDisable()
	{
		UnityEditor.EditorApplication.delayCall -= NotifyEmissionSettingsChanged;
	}
#endif

	/// <summary>
	/// キャラクター種別(男女)とカラー種別に対応する本体マテリアルを取得します。
	/// </summary>
	public Material GetBodyMaterial(ECharacterType characterType, EHighlightColor colorType)
	{
		if (m_ColorEntries == null)
		{
			return null;
		}

		for (int i = 0; i < m_ColorEntries.Length; i++)
		{
			if (m_ColorEntries[i].ColorType == colorType)
			{
				return characterType == ECharacterType.Woman
					? m_ColorEntries[i].WomanMaterial
					: m_ColorEntries[i].ManMaterial;
			}
		}

		return null;
	}

	/// <summary>
	/// 所属チームに対応するチームカラーマテリアルを取得します。
	/// </summary>
	public Material GetTeamMaterial(ETeamType team)
	{
		switch (team)
		{
			case ETeamType.Red:
				return m_RedTeamMaterial;
			case ETeamType.Blue:
				return m_BlueTeamMaterial;
			default:
				return null;
		}
	}

	/// <summary>
	/// 指定したカラー種別に対応するUI・演出用プレビューカラーを取得します。
	/// </summary>
	public bool TryGetPreviewColor(EHighlightColor colorType, out Color previewColor)
	{
		if (m_ColorEntries != null)
		{
			for (int i = 0; i < m_ColorEntries.Length; i++)
			{
				if (m_ColorEntries[i].ColorType == colorType)
				{
					previewColor = m_ColorEntries[i].PreviewColor;
					return true;
				}
			}
		}

		previewColor = Color.white;
		return false;
	}

	/// <summary>
	/// 所属チームに対応するUI・ネームプレート用の表示色を取得します。
	/// </summary>
	public Color GetTeamDisplayColor(ETeamType team)
	{
		switch (team)
		{
			case ETeamType.Red:
				return m_RedTeamDisplayColor;
			case ETeamType.Blue:
				return m_BlueTeamDisplayColor;
			default:
				return m_DefaultTeamDisplayColor;
		}
	}
}

