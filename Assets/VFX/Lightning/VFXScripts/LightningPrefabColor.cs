using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// 生成された電流PrefabのVFX色入力を制御し、元のアセットとパレットを維持します。
/// </summary>
public sealed class LightningPrefabColor
{
    private static readonly int m_EffectColorId = Shader.PropertyToID("EffectColor");
    private static readonly int m_ColorIndexId = Shader.PropertyToID("ColorIndex");
    private const uint m_DirectColorIndex = 12;
    private readonly VisualEffect[] m_Effects;
    private readonly Vector4[] m_OriginalColors;
    private readonly bool[] m_HasColorInput;
    private readonly uint[] m_OriginalColorIndices;
    private readonly bool[] m_HasIndexInput;

    public LightningPrefabColor(GameObject instance)
    {
        m_Effects = instance.GetComponentsInChildren<VisualEffect>(true);
        m_OriginalColors = new Vector4[m_Effects.Length];
        m_HasColorInput = new bool[m_Effects.Length];
        m_OriginalColorIndices = new uint[m_Effects.Length];
        m_HasIndexInput = new bool[m_Effects.Length];

        for (int i = 0; i < m_Effects.Length; i++)
        {
            VisualEffect effect = m_Effects[i];
            m_HasColorInput[i] = effect.HasVector4(m_EffectColorId);
            m_HasIndexInput[i] = effect.HasUInt(m_ColorIndexId);
            if (m_HasIndexInput[i])
            {
                m_OriginalColorIndices[i] = effect.GetUInt(m_ColorIndexId);
            }
            if (m_HasColorInput[i])
            {
                m_OriginalColors[i] = effect.GetVector4(m_EffectColorId);
            }
            else if (!m_HasIndexInput[i])
            {
                Debug.LogWarning("電流のVFX Graphに公開入力ColorIndexまたはEffectColorがありません。", effect);
            }
        }
    }

    /// <summary>
    /// 色番号に対応したGraphはSwitchで色を選びます。
    /// 直接色指定はSwitchのDefault、従来のGraphはEffectColorへ反映します。
    /// </summary>
    public void SetColor(Color color, EHighlightColor? paletteColor)
    {
        int paletteId = paletteColor.HasValue
            ? Shader.PropertyToID(paletteColor.Value.ToString())
            : 0;

        for (int i = 0; i < m_Effects.Length; i++)
        {
            VisualEffect effect = m_Effects[i];
            if (effect == null)
            {
                continue;
            }

            if (m_HasIndexInput[i])
            {
                if (paletteColor.HasValue)
                {
                    effect.SetUInt(m_ColorIndexId, (uint)paletteColor.Value);
                }
                else if (m_HasColorInput[i])
                {
                    effect.SetVector4(m_EffectColorId, (Vector4)color);
                    effect.SetUInt(m_ColorIndexId, m_DirectColorIndex);
                }
                else
                {
                    effect.SetUInt(m_ColorIndexId, m_OriginalColorIndices[i]);
                }
                continue;
            }

            if (!m_HasColorInput[i])
            {
                continue;
            }

            Vector4 value = paletteColor.HasValue && effect.HasVector4(paletteId)
                ? effect.GetVector4(paletteId)
                : (Vector4)color;
            effect.SetVector4(m_EffectColorId, value);
        }
    }

    /// <summary>
    /// 所有者を解除した場合に生成直後の色へ戻します。
    /// </summary>
    public void RestoreColor()
    {
        for (int i = 0; i < m_Effects.Length; i++)
        {
            if (m_Effects[i] != null && m_HasColorInput[i])
            {
                m_Effects[i].SetVector4(m_EffectColorId, m_OriginalColors[i]);
            }
            if (m_Effects[i] != null && m_HasIndexInput[i])
            {
                m_Effects[i].SetUInt(m_ColorIndexId, m_OriginalColorIndices[i]);
            }
        }
    }
}
