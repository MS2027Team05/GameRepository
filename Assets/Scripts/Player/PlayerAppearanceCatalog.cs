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

	public ColorMaterialEntry[] ColorEntries { get => m_ColorEntries; }

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

