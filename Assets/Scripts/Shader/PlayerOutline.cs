using UnityEngine;

/// <summary>
/// キャラのルートに1つ付けるだけで、
/// ・子の全Rendererにステンシルマスク+アウトラインのマテリアルを自動追加
/// ・プレイヤーごとの色とステンシルID(プレイヤーID)を設定
/// をやってくれるコンポーネント。
///
/// ステンシルにより、自キャラの本体と重なる部分にはアウトラインが描画されず、
/// 外側のシルエット輪郭のみ表示される。他プレイヤーと重なった場合は線が出る。
///
/// 注意: ステンシルRefはMaterialPropertyBlockで変更できないため、
/// このコンポーネントはキャラごとにマテリアルのインスタンスを生成する。
/// </summary>
public class PlayerOutline : MonoBehaviour
{
    [Header("Custom/PlayerOutline のマテリアル")]
    [SerializeField] private Material outlineMaterial;

    [Header("Custom/PlayerOutlineStencilMask のマテリアル")]
    [SerializeField] private Material stencilMaskMaterial;

    [Header("アウトラインを付けたくないRendererをここに登録")]
    [SerializeField] private Renderer[] excludeRenderers;

    [SerializeField] private int playerIndex = 0;

    [SerializeField]
    private Color[] playerColors =
    {
        new Color(0.9f, 0.2f, 0.2f), // 1P: 赤
        new Color(0.2f, 0.4f, 0.9f), // 2P: 青
        new Color(0.9f, 0.8f, 0.2f), // 3P: 黄
        new Color(0.2f, 0.8f, 0.3f), // 4P: 緑
    };

    private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
    private static readonly int StencilRefId = Shader.PropertyToID("_StencilRef");

    private Renderer[] renderers;

    // このキャラ専用のマテリアルインスタンス
    private Material outlineInstance;
    private Material stencilMaskInstance;

    private void Awake()
    {
        renderers = CollectRenderers();
        CreateMaterialInstances();
        AddMaterials();
        ApplyPlayerSettings();
    }

    public void SetPlayerIndex(int index)
    {
        playerIndex = index;
        ApplyPlayerSettings();
    }

    /// <summary>子の全Rendererから除外リストに入っているものを除いて収集</summary>
    private Renderer[] CollectRenderers()
    {
        var all = GetComponentsInChildren<Renderer>();
        if (excludeRenderers == null || excludeRenderers.Length == 0)
            return all;

        var list = new System.Collections.Generic.List<Renderer>(all.Length);
        foreach (var r in all)
        {
            if (System.Array.IndexOf(excludeRenderers, r) < 0)
                list.Add(r);
        }
        return list.ToArray();
    }

    private void CreateMaterialInstances()
    {
        if (outlineMaterial == null || stencilMaskMaterial == null)
        {
            // Debug.LogWarning($"{name}: マテリアルが未設定です", this);
            return;
        }

        outlineInstance = new Material(outlineMaterial);
        stencilMaskInstance = new Material(stencilMaskMaterial);
    }

    /// <summary>全Rendererのマテリアル配列末尾にマスク→アウトラインの順で追加</summary>
    private void AddMaterials()
    {
        if (outlineInstance == null) return;

        foreach (var renderer in renderers)
        {
            var materials = renderer.sharedMaterials;
            var newMaterials = new Material[materials.Length + 2];
            materials.CopyTo(newMaterials, 0);
            newMaterials[materials.Length] = stencilMaskInstance;
            newMaterials[materials.Length + 1] = outlineInstance;
            renderer.sharedMaterials = newMaterials;
        }
    }

    private void ApplyPlayerSettings()
    {
        if (outlineInstance == null) return;

        Color color = playerColors[Mathf.Clamp(playerIndex, 0, playerColors.Length - 1)];
        int stencilRef = playerIndex + 1; // 0はステンシルの初期値なので避ける

        outlineInstance.SetColor(OutlineColorId, color);
        outlineInstance.SetFloat(StencilRefId, stencilRef);
        stencilMaskInstance.SetFloat(StencilRefId, stencilRef);
    }

    private void OnDestroy()
    {
        // インスタンス化したマテリアルはリークしないよう破棄
        if (outlineInstance != null) Destroy(outlineInstance);
        if (stencilMaskInstance != null) Destroy(stencilMaskInstance);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (Application.isPlaying && outlineInstance != null) ApplyPlayerSettings();
    }
#endif
}