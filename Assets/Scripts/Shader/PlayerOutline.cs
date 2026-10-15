using UnityEngine;
using UnityEngine.Serialization;

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
	[FormerlySerializedAs("outlineMaterial")]
	[SerializeField] private Material m_OutlineMaterial;

	[Header("Custom/PlayerOutlineStencilMask のマテリアル")]
	[FormerlySerializedAs("stencilMaskMaterial")]
	[SerializeField] private Material m_StencilMaskMaterial;

	[Header("アウトラインを付けたくないRendererをここに登録")]
	[FormerlySerializedAs("excludeRenderers")]
	[SerializeField] private Renderer[] m_ExcludeRenderers;

	[FormerlySerializedAs("playerIndex")]
	[SerializeField] private int m_PlayerIndex;

	[FormerlySerializedAs("playerColors")]
	[SerializeField] private Color[] m_PlayerColors;

	private static readonly int m_OutlineColorId = Shader.PropertyToID("_OutlineColor");
	private static readonly int m_StencilRefId = Shader.PropertyToID("_StencilRef");

	private Renderer[] m_Renderers;

	// このキャラ専用のマテリアルインスタンス
	private Material m_OutlineInstance;
	private Material m_StencilMaskInstance;

	private void Awake()
	{
		m_Renderers = CollectRenderers();
		CreateMaterialInstances();
		AddMaterials();
		ApplyPlayerSettings();
	}

	public void SetPlayerIndex(int index)
	{
		m_PlayerIndex = index;
		ApplyPlayerSettings();
	}

	/// <summary>子の全Rendererから除外リストに入っているものを除いて収集</summary>
	private Renderer[] CollectRenderers()
	{
		var all = GetComponentsInChildren<Renderer>();
		if (m_ExcludeRenderers == null || m_ExcludeRenderers.Length == 0)
		{
			return all;
		}

		var list = new System.Collections.Generic.List<Renderer>(all.Length);
		foreach (var r in all)
		{
			if (System.Array.IndexOf(m_ExcludeRenderers, r) < 0)
			{
				list.Add(r);
			}
		}
		return list.ToArray();
	}

	private void CreateMaterialInstances()
	{
		if (m_OutlineMaterial == null || m_StencilMaskMaterial == null)
		{
			// Debug.LogWarning($"{name}: マテリアルが未設定です", this);
			return;
		}

		m_OutlineInstance = new Material(m_OutlineMaterial);
		m_StencilMaskInstance = new Material(m_StencilMaskMaterial);
	}

	/// <summary>全Rendererのマテリアル配列末尾にマスク→アウトラインの順で追加</summary>
	private void AddMaterials()
	{
		if (m_OutlineInstance == null)
		{
			return;
		}

		foreach (var renderer in m_Renderers)
		{
			var materials = renderer.sharedMaterials;
			var newMaterials = new Material[materials.Length + 2];
			materials.CopyTo(newMaterials, 0);
			newMaterials[materials.Length] = m_StencilMaskInstance;
			newMaterials[materials.Length + 1] = m_OutlineInstance;
			renderer.sharedMaterials = newMaterials;
		}
	}

	private void ApplyPlayerSettings()
	{
		if (m_OutlineInstance == null)
		{
			return;
		}

		Color color = Color.white;
		if (m_PlayerColors != null && m_PlayerColors.Length > 0)
		{
			color = m_PlayerColors[Mathf.Clamp(m_PlayerIndex, 0, m_PlayerColors.Length - 1)];
		}
		int stencilRef = m_PlayerIndex + 1; // 0はステンシルの初期値なので避ける

		m_OutlineInstance.SetColor(m_OutlineColorId, color);
		m_OutlineInstance.SetFloat(m_StencilRefId, stencilRef);
		m_StencilMaskInstance.SetFloat(m_StencilRefId, stencilRef);
	}

	private void OnDestroy()
	{
		// インスタンス化したマテリアルはリークしないよう破棄
		if (m_OutlineInstance != null)
		{
			Destroy(m_OutlineInstance);
		}
		if (m_StencilMaskInstance != null)
		{
			Destroy(m_StencilMaskInstance);
		}
	}

#if UNITY_EDITOR
	private void OnValidate()
	{
		if (Application.isPlaying && m_OutlineInstance != null)
		{
			ApplyPlayerSettings();
		}
	}
#endif
}
