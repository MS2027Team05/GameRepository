using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// プレイヤー（PlayerPrefab）専用の輪郭線・遮蔽シルエット制御コンポーネント。
/// 描画キュー（Render Queue）を制御し、手持ち道具によるプレイヤーの意図しない遮蔽（透け）を防止する。
/// </summary>
public class PlayerSilhouette : MonoBehaviour
{
    [Header("機能の有効化設定")]
    [SerializeField] private bool useOutline;
    [SerializeField] private bool useSilhouette;

    [Header("マテリアル設定")]
    [SerializeField] private Material outlineMaterial;
    [SerializeField] private Material stencilMaskMaterial;
    [SerializeField] private Material silhouetteMaterial;

    [Header("除外設定")]
    [SerializeField] private Renderer[] excludeRenderers;

    [Header("プレイヤー設定")]
    [SerializeField] private int playerIndex;
    [SerializeField] private Color[] playerColors;
    [Range(0f, 1f)]
    [SerializeField] private float playerSilhouetteAlpha;

    private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
    private static readonly int SilhouetteColorId = Shader.PropertyToID("_SilhouetteColor");
    private static readonly int StencilRefId = Shader.PropertyToID("_StencilRef");
    private static readonly int StencilCompId = Shader.PropertyToID("_StencilComp");
    private static readonly int StencilPassId = Shader.PropertyToID("_StencilPass");

    // レンダリングキュー定数（道具本体の2050より前に描画して誤遮蔽を防止）
    private const int StencilMaskQueue = 2010;
    private const int SilhouetteQueue = 2015;
    private const int OutlineQueue = 2020;

    private Renderer[] renderers;

    private Material outlineInstance;
    private Material stencilMaskInstance;
    private Material silhouetteInstance;

    private void Awake()
    {
        renderers = CollectRenderers();
        CreateMaterialInstances();
        AddMaterials();
        ApplySettings();
    }

    public void SetPlayerIndex(int index)
    {
        playerIndex = index;
        ApplySettings();
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

    private void CreateMaterialInstances()
    {
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
                // 道具本体(Queue 2050)より前に描画することで道具による誤遮蔽を防止
                silhouetteInstance.renderQueue = SilhouetteQueue;
            }
        }
    }

    private void AddMaterials()
    {
        int addCount = 0;
        if (stencilMaskInstance != null) addCount++;
        if (silhouetteInstance != null) addCount++;
        if (outlineInstance != null) addCount++;

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
            if (stencilMaskInstance != null)
            {
                newMaterials[index++] = stencilMaskInstance;
            }
            if (silhouetteInstance != null)
            {
                newMaterials[index++] = silhouetteInstance;
            }
            if (outlineInstance != null)
            {
                newMaterials[index++] = outlineInstance;
            }

            renderer.sharedMaterials = newMaterials;
        }
    }

    private void ApplySettings()
    {
        int stencilRef = playerIndex + 1; // 0はステンシル初期値のため回避
        Color outColor = Color.white;
        Color silColor = Color.white;

        if (playerColors != null && playerColors.Length > 0)
        {
            int colorIndex = Mathf.Clamp(playerIndex, 0, playerColors.Length - 1);
            outColor = playerColors[colorIndex];
            silColor = new Color(outColor.r, outColor.g, outColor.b, playerSilhouetteAlpha);
        }

        if (stencilMaskInstance != null)
        {
            stencilMaskInstance.SetFloat(StencilRefId, stencilRef);
        }

        if (silhouetteInstance != null)
        {
            silhouetteInstance.SetColor(SilhouetteColorId, silColor);
            // ステンシル値が 0 (道具がない領域) のみ描画し、描画済みを 1 にインクリメント
            silhouetteInstance.SetFloat(StencilRefId, 0f);
            silhouetteInstance.SetFloat(StencilCompId, (float)UnityEngine.Rendering.CompareFunction.Equal);
            silhouetteInstance.SetFloat(StencilPassId, (float)UnityEngine.Rendering.StencilOp.IncrementSaturate);
        }

        if (outlineInstance != null)
        {
            outlineInstance.SetColor(OutlineColorId, outColor);
            outlineInstance.SetFloat(StencilRefId, stencilRef);
        }
    }

    private void OnDestroy()
    {
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
