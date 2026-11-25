using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 本体とチームカラーの先頭マテリアルに発光色を上書きします。
/// 元のマテリアルとEmission Mapは変更せず、上書き前のプロパティを保持します。
/// </summary>
public sealed class PlayerEmissionController
{
	private static readonly int m_EmissionColorId = Shader.PropertyToID("_EmissionColor");
	private static readonly int m_EmissionMapId = Shader.PropertyToID("_EmissionMap");
	private readonly Dictionary<Renderer, MaterialPropertyBlock> m_OriginalBlocks = new Dictionary<Renderer, MaterialPropertyBlock>();
	private readonly MaterialPropertyBlock m_PropertyBlock = new MaterialPropertyBlock();

	/// <summary>
	/// Emissionが有効で、マップが設定されたマテリアルだけを対象にします。
	/// 追加されたアウトラインやシルエット用のスロットには上書きしません。
	/// </summary>
	public void Apply(Renderer[] renderers, Color emissionColor)
	{
		if (renderers == null)
		{
			return;
		}

		foreach (Renderer targetRenderer in renderers)
		{
			if (targetRenderer == null)
			{
				continue;
			}

			Material material = targetRenderer.sharedMaterial;
			if (material == null || !material.HasProperty(m_EmissionColorId) ||
				!material.HasProperty(m_EmissionMapId) || material.GetTexture(m_EmissionMapId) == null ||
				!material.IsKeywordEnabled("_EMISSION"))
			{
				Restore(targetRenderer);
				continue;
			}

			if (!m_OriginalBlocks.ContainsKey(targetRenderer))
			{
				MaterialPropertyBlock originalBlock = new MaterialPropertyBlock();
				targetRenderer.GetPropertyBlock(originalBlock, 0);
				m_OriginalBlocks.Add(targetRenderer, originalBlock);
			}

			// 既存のスロット別設定を維持し、発光色だけを変更します。
			targetRenderer.GetPropertyBlock(m_PropertyBlock, 0);
			// スロット別設定がない場合は、Renderer全体の設定を引き継ぎます。
			if (m_PropertyBlock.isEmpty)
			{
				targetRenderer.GetPropertyBlock(m_PropertyBlock);
			}
			m_PropertyBlock.SetColor(m_EmissionColorId, emissionColor);
			targetRenderer.SetPropertyBlock(m_PropertyBlock, 0);
		}
	}

	/// <summary>発光の上書きを解除して、保存したプロパティへ戻します。</summary>
	public void Restore()
	{
		foreach (KeyValuePair<Renderer, MaterialPropertyBlock> entry in m_OriginalBlocks)
		{
			if (entry.Key != null)
			{
				entry.Key.SetPropertyBlock(entry.Value.isEmpty ? null : entry.Value, 0);
			}
		}
		m_OriginalBlocks.Clear();
	}

	private void Restore(Renderer targetRenderer)
	{
		if (m_OriginalBlocks.TryGetValue(targetRenderer, out MaterialPropertyBlock originalBlock))
		{
			targetRenderer.SetPropertyBlock(originalBlock.isEmpty ? null : originalBlock, 0);
			m_OriginalBlocks.Remove(targetRenderer);
		}
	}
}
