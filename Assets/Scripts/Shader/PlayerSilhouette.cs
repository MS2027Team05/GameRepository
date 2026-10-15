using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// プレイヤー(PlayerPrefab)専用の輪郭線・遮蔽シルエット制御コンポーネント。
/// 描画キュー(Render Queue)を制御し、手持ち道具によるプレイヤーの意図しない遮蔽(透け)を防止する。
/// </summary>
public class PlayerSilhouette : MonoBehaviour
{
	[Header("機能の有効化設定")]
	[FormerlySerializedAs("useOutline")]
	[SerializeField] private bool m_UseOutline;
	[FormerlySerializedAs("useSilhouette")]
	[SerializeField] private bool m_UseSilhouette;

	[Header("マテリアル設定")]
	[FormerlySerializedAs("outlineMaterial")]
	[SerializeField] private Material m_OutlineMaterial;
	[FormerlySerializedAs("stencilMaskMaterial")]
	[SerializeField] private Material m_StencilMaskMaterial;
	[FormerlySerializedAs("silhouetteMaterial")]
	[SerializeField] private Material m_SilhouetteMaterial;

	[Header("除外設定")]
	[FormerlySerializedAs("excludeRenderers")]
	[SerializeField] private Renderer[] m_ExcludeRenderers;

	[Header("プレイヤー設定")]
	[FormerlySerializedAs("playerIndex")]
	[SerializeField] private int m_PlayerIndex;
	[FormerlySerializedAs("playerColors")]
	[SerializeField] private Color[] m_PlayerColors;
	[Range(0f, 1f)]
	[FormerlySerializedAs("playerSilhouetteAlpha")]
	[SerializeField] private float m_PlayerSilhouetteAlpha;

	private static readonly int m_OutlineColorId = Shader.PropertyToID("_OutlineColor");
	private static readonly int m_SilhouetteColorId = Shader.PropertyToID("_SilhouetteColor");
	private static readonly int m_StencilRefId = Shader.PropertyToID("_StencilRef");
	private static readonly int m_StencilCompId = Shader.PropertyToID("_StencilComp");
	private static readonly int m_StencilPassId = Shader.PropertyToID("_StencilPass");

	// レンダリングキュー定数(道具本体の2050より前に描画して誤遮蔽を防止)
	private const int m_StencilMaskQueue = 2010;
	private const int m_SilhouetteQueue = 2015;
	private const int m_OutlineQueue = 2020;

	private Renderer[] m_Renderers;

	private Material m_OutlineInstance;
	private Material m_StencilMaskInstance;
	private Material m_SilhouetteInstance;

	private void Awake()
	{
		m_Renderers = CollectRenderers();
		CreateMaterialInstances();
		AddMaterials();
		ApplySettings();
	}

	public void SetPlayerIndex(int index)
	{
		m_PlayerIndex = index;
		ApplySettings();
	}

	private Renderer[] CollectRenderers()
	{
		var all = GetComponentsInChildren<Renderer>();
		if (m_ExcludeRenderers == null || m_ExcludeRenderers.Length == 0)
		{
			return all;
		}

		var list = new List<Renderer>(all.Length);
		foreach (var r in all)
		{
			if (Array.IndexOf(m_ExcludeRenderers, r) < 0)
			{
				list.Add(r);
			}
		}
		return list.ToArray();
	}

	private void CreateMaterialInstances()
	{
		if (m_UseOutline)
		{
			if (m_OutlineMaterial != null)
			{
				m_OutlineInstance = new Material(m_OutlineMaterial);
				m_OutlineInstance.renderQueue = m_OutlineQueue;
			}
			if (m_StencilMaskMaterial != null)
			{
				m_StencilMaskInstance = new Material(m_StencilMaskMaterial);
				m_StencilMaskInstance.renderQueue = m_StencilMaskQueue;
			}
		}

		if (m_UseSilhouette)
		{
			if (m_SilhouetteMaterial != null)
			{
				m_SilhouetteInstance = new Material(m_SilhouetteMaterial);
				// 道具本体(Queue 2050)より前に描画することで道具による誤遮蔽を防止
				m_SilhouetteInstance.renderQueue = m_SilhouetteQueue;
			}
		}
	}

	private void AddMaterials()
	{
		int addCount = 0;
		if (m_StencilMaskInstance != null) addCount++;
		if (m_SilhouetteInstance != null) addCount++;
		if (m_OutlineInstance != null) addCount++;

		if (addCount == 0 || m_Renderers == null)
		{
			return;
		}

		foreach (var renderer in m_Renderers)
		{
			if (renderer == null) continue;

			var materials = renderer.sharedMaterials;
			var newMaterials = new Material[materials.Length + addCount];
			materials.CopyTo(newMaterials, 0);

			int index = materials.Length;
			if (m_StencilMaskInstance != null)
			{
				newMaterials[index++] = m_StencilMaskInstance;
			}
			if (m_SilhouetteInstance != null)
			{
				newMaterials[index++] = m_SilhouetteInstance;
			}
			if (m_OutlineInstance != null)
			{
				newMaterials[index++] = m_OutlineInstance;
			}

			renderer.sharedMaterials = newMaterials;
		}
	}

	private void ApplySettings()
	{
		int stencilRef = m_PlayerIndex + 1; // 0はステンシル初期値のため回避
		Color outColor = Color.white;
		Color silColor = Color.white;

		if (m_PlayerColors != null && m_PlayerColors.Length > 0)
		{
			int colorIndex = Mathf.Clamp(m_PlayerIndex, 0, m_PlayerColors.Length - 1);
			outColor = m_PlayerColors[colorIndex];
			silColor = new Color(outColor.r, outColor.g, outColor.b, m_PlayerSilhouetteAlpha);
		}

		if (m_StencilMaskInstance != null)
		{
			m_StencilMaskInstance.SetFloat(m_StencilRefId, stencilRef);
		}

		if (m_SilhouetteInstance != null)
		{
			m_SilhouetteInstance.SetColor(m_SilhouetteColorId, silColor);
			// ステンシル値が 0 (道具がない領域) のみ描画し、描画済みを 1 にインクリメント
			m_SilhouetteInstance.SetFloat(m_StencilRefId, 0f);
			m_SilhouetteInstance.SetFloat(m_StencilCompId, (float)UnityEngine.Rendering.CompareFunction.Equal);
			m_SilhouetteInstance.SetFloat(m_StencilPassId, (float)UnityEngine.Rendering.StencilOp.IncrementSaturate);
		}

		if (m_OutlineInstance != null)
		{
			m_OutlineInstance.SetColor(m_OutlineColorId, outColor);
			m_OutlineInstance.SetFloat(m_StencilRefId, stencilRef);
		}
	}

	private void OnDestroy()
	{
		if (m_OutlineInstance != null) Destroy(m_OutlineInstance);
		if (m_StencilMaskInstance != null) Destroy(m_StencilMaskInstance);
		if (m_SilhouetteInstance != null) Destroy(m_SilhouetteInstance);
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
