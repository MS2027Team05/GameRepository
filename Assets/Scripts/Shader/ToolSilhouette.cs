using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 道具（Tool）専用の輪郭線・遮蔽シルエット制御コンポーネント。
/// 道具本体の描画キューを遅らせることで、手持ち時にプレイヤーを誤遮蔽するのを防止する。
/// </summary>
public class ToolSilhouette : MonoBehaviour
{
    [Header("機能の有効化設定")]
    [SerializeField] private bool useOutline;
    [SerializeField] private bool useSilhouette;

    [Header("マテリアル設定")]
    [SerializeField] private Material outlineMaterial;
    [SerializeField] private Material stencilMaskMaterial;
    [SerializeField] private Material silhouetteMaterial;
    [SerializeField] private Material occludedMaskMaterial;

    [Header("除外設定")]
    [SerializeField] private Renderer[] excludeRenderers;

    [Header("カラー・ステンシル設定")]
    [SerializeField] private Color outlineColor;
    [SerializeField] private Color silhouetteColor;
    [SerializeField] private int stencilRef;

    private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
    private static readonly int SilhouetteColorId = Shader.PropertyToID("_SilhouetteColor");
    private static readonly int StencilRefId = Shader.PropertyToID("_StencilRef");
    private static readonly int StencilCompId = Shader.PropertyToID("_StencilComp");
    private static readonly int StencilPassId = Shader.PropertyToID("_StencilPass");

    // 道具遮蔽マスクはプレイヤーシルエット(2015)の直前(2012)に実行
    private const int ToolOccludedMaskQueue = 2012;
    // 道具本体はプレイヤーシルエット(2015)より後に描画
    private const int ToolBaseQueue = 2050;
    private const int StencilMaskQueue = 2055;
    private const int OutlineQueue = 2060;
    private const int SilhouetteQueue = 3010; // 半透明キュー

    private Renderer[] renderers;

    private Material outlineInstance;
    private Material stencilMaskInstance;
    private Material silhouetteInstance;
    private Material occludedMaskInstance;

    private void Awake()
    {
        renderers = CollectRenderers();
        AdjustBaseRenderQueue();
        CreateMaterialInstances();
        AddMaterials();
        ApplySettings();
    }

    public void SetSilhouetteColor(Color color)
    {
        silhouetteColor = color;
        if (silhouetteInstance != null)
        {
            silhouetteInstance.SetColor(SilhouetteColorId, color);
        }
    }

    public void SetOutlineColor(Color color)
    {
        outlineColor = color;
        if (outlineInstance != null)
        {
            outlineInstance.SetColor(OutlineColorId, color);
        }
    }

    private Renderer[] CollectRenderers()
    {
        var all = GetComponentsInChildren<Renderer>();
        if (excludeRenderers == null || excludeRenderers.Length == 0)
        {
            return all;
        }

        var list = new List<Renderer>(all.Length);
        foreach (var r in all)
        {
            if (Array.IndexOf(excludeRenderers, r) < 0)
            {
                list.Add(r);
            }
        }
        return list.ToArray();
    }

    /// <summary>道具本体の描画順をプレイヤーシルエットより後に遅らせる</summary>
    private void AdjustBaseRenderQueue()
    {
        if (renderers == null) return;

        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;

            // 各Rendererの既存マテリアルのQueueを2050に調整
            var materials = renderer.materials; // インスタンス化して変更
            foreach (var mat in materials)
            {
                if (mat != null && mat.renderQueue < ToolBaseQueue)
                {
                    mat.renderQueue = ToolBaseQueue;
                }
            }
        }
    }

    private void CreateMaterialInstances()
    {
        if (occludedMaskMaterial != null)
        {
            occludedMaskInstance = new Material(occludedMaskMaterial);
            occludedMaskInstance.renderQueue = ToolOccludedMaskQueue;
        }

        if (useOutline)
        {
            if (outlineMaterial != null)
            {
                outlineInstance = new Material(outlineMaterial);
                outlineInstance.renderQueue = OutlineQueue;
            }
            if (stencilMaskMaterial != null)
            {
                stencilMaskInstance = new Material(stencilMaskMaterial);
                stencilMaskInstance.renderQueue = StencilMaskQueue;
            }
        }

        if (useSilhouette)
        {
            if (silhouetteMaterial != null)
            {
                silhouetteInstance = new Material(silhouetteMaterial);
                silhouetteInstance.renderQueue = SilhouetteQueue;
            }
        }
    }

    private void AddMaterials()
    {
        int addCount = 0;
        if (occludedMaskInstance != null) addCount++;
        if (stencilMaskInstance != null) addCount++;
        if (outlineInstance != null) addCount++;
        if (silhouetteInstance != null) addCount++;

        if (addCount == 0 || renderers == null)
        {
            return;
        }

        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;

            var materials = renderer.sharedMaterials;
            var newMaterials = new Material[materials.Length + addCount];
            materials.CopyTo(newMaterials, 0);

            int index = materials.Length;
            if (occludedMaskInstance != null)
            {
                newMaterials[index++] = occludedMaskInstance;
            }
            if (stencilMaskInstance != null)
            {
                newMaterials[index++] = stencilMaskInstance;
            }
            if (outlineInstance != null)
            {
                newMaterials[index++] = outlineInstance;
            }
            if (silhouetteInstance != null)
            {
                newMaterials[index++] = silhouetteInstance;
            }

            renderer.sharedMaterials = newMaterials;
        }
    }

    private void ApplySettings()
    {
        int baseRef = stencilRef > 0 ? stencilRef : 10;
        int outlineStencilRef = baseRef;
        // 壁裏遮蔽専用のステンシル値（通常時アウトライン用と分離して通常時の誤描画を100%防止）
        int occlusionStencilRef = baseRef + 100;

        // 1. 通常時用アウトラインマスク(見えているピクセルにマーク)
        if (stencilMaskInstance != null)
        {
            stencilMaskInstance.SetFloat(StencilRefId, outlineStencilRef);
        }

        // 2. 通常時用アウトライン
        if (outlineInstance != null)
        {
            outlineInstance.SetColor(OutlineColorId, outlineColor);
            outlineInstance.SetFloat(StencilRefId, outlineStencilRef);
        }

        // 3. 壁裏用遮蔽マスク(壁裏のピクセルにのみマーク)
        if (occludedMaskInstance != null)
        {
            occludedMaskInstance.SetFloat(StencilRefId, occlusionStencilRef);
        }

        // 4. 壁裏用遮蔽シルエット
        if (silhouetteInstance != null)
        {
            silhouetteInstance.SetColor(SilhouetteColorId, silhouetteColor);

            if (occludedMaskInstance != null)
            {
                // 壁裏遮蔽マスクでマークされたピクセル(occlusionStencilRef)にのみ半透明グレーを描画
                silhouetteInstance.SetFloat(StencilRefId, occlusionStencilRef);
                silhouetteInstance.SetFloat(StencilCompId, (float)UnityEngine.Rendering.CompareFunction.Equal);
                silhouetteInstance.SetFloat(StencilPassId, (float)UnityEngine.Rendering.StencilOp.IncrementSaturate);
            }
            else
            {
                // マスク未使用時は通常通り
                silhouetteInstance.SetFloat(StencilRefId, occlusionStencilRef);
                silhouetteInstance.SetFloat(StencilCompId, (float)UnityEngine.Rendering.CompareFunction.NotEqual);
                silhouetteInstance.SetFloat(StencilPassId, (float)UnityEngine.Rendering.StencilOp.Replace);
            }
        }
    }

    private void OnDestroy()
    {
        if (occludedMaskInstance != null) Destroy(occludedMaskInstance);
        if (outlineInstance != null) Destroy(outlineInstance);
        if (stencilMaskInstance != null) Destroy(stencilMaskInstance);
        if (silhouetteInstance != null) Destroy(silhouetteInstance);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (Application.isPlaying)
        {
            ApplySettings();
        }
    }
#endif
}
