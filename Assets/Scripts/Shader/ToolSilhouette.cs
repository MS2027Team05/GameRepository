using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 道具(Tool)専用の輪郭線・遮蔽シルエット制御コンポーネント。
/// 道具本体の描画キューを遅らせることで、手持ち時にプレイヤーを誤遮蔽するのを防止する。
/// </summary>
public class ToolSilhouette : MonoBehaviour
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
	[FormerlySerializedAs("occludedMaskMaterial")]
	[SerializeField] private Material m_OccludedMaskMaterial;

	[Header("除外設定")]
	[FormerlySerializedAs("excludeRenderers")]
	[SerializeField] private Renderer[] m_ExcludeRenderers;

	[Header("カラー・ステンシル設定")]
	[FormerlySerializedAs("outlineColor")]
	[SerializeField] private Color m_OutlineColor;
	[FormerlySerializedAs("silhouetteColor")]
	[SerializeField] private Color m_SilhouetteColor;
	[FormerlySerializedAs("stencilRef")]
	[SerializeField] private int m_StencilRef;

	private static readonly int m_OutlineColorId = Shader.PropertyToID("_OutlineColor");
	private static readonly int m_SilhouetteColorId = Shader.PropertyToID("_SilhouetteColor");
	private static readonly int m_StencilRefId = Shader.PropertyToID("_StencilRef");
	private static readonly int m_StencilCompId = Shader.PropertyToID("_StencilComp");
	private static readonly int m_StencilPassId = Shader.PropertyToID("_StencilPass");

	// 道具遮蔽マスクはプレイヤーシルエット(2015)の直前(2012)に実行
	private const int m_ToolOccludedMaskQueue = 2012;
	// 道具本体はプレイヤーシルエット(2015)より後に描画
	private const int m_ToolBaseQueue = 2050;
	private const int m_StencilMaskQueue = 2055;
	private const int m_OutlineQueue = 2060;
	private const int m_SilhouetteQueue = 3010; // 半透明キュー

	private Renderer[] m_Renderers;

	private Material m_OutlineInstance;
	private Material m_StencilMaskInstance;
	private Material m_SilhouetteInstance;
	private Material m_OccludedMaskInstance;

	private void Awake()
	{
		m_Renderers = CollectRenderers();
		AdjustBaseRenderQueue();
		CreateMaterialInstances();
		AddMaterials();
		ApplySettings();
	}

	public void SetSilhouetteColor(Color color)
	{
		m_SilhouetteColor = color;
		if (m_SilhouetteInstance != null)
		{
			m_SilhouetteInstance.SetColor(m_SilhouetteColorId, color);
		}
	}

	public void SetOutlineColor(Color color)
	{
		m_OutlineColor = color;
		if (m_OutlineInstance != null)
		{
			m_OutlineInstance.SetColor(m_OutlineColorId, color);
		}
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

	/// <summary>道具本体の描画順をプレイヤーシルエットより後に遅らせる</summary>
	private void AdjustBaseRenderQueue()
	{
		if (m_Renderers == null)
		{
			return;
		}

		foreach (var renderer in m_Renderers)
		{
			if (renderer == null)
			{
				continue;
			}

			// 各Rendererの既存マテリアルのQueueを2050に調整
			var materials = renderer.materials; // インスタンス化して変更
			foreach (var mat in materials)
			{
				if (mat != null && mat.renderQueue < m_ToolBaseQueue)
				{
					mat.renderQueue = m_ToolBaseQueue;
				}
			}
		}
	}

	private void CreateMaterialInstances()
	{
		if (m_OccludedMaskMaterial != null)
		{
			m_OccludedMaskInstance = new Material(m_OccludedMaskMaterial);
			m_OccludedMaskInstance.renderQueue = m_ToolOccludedMaskQueue;
		}

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
				m_SilhouetteInstance.renderQueue = m_SilhouetteQueue;
			}
		}
	}

	private void AddMaterials()
	{
		int addCount = 0;
		if (m_OccludedMaskInstance != null) addCount++;
		if (m_StencilMaskInstance != null) addCount++;
		if (m_OutlineInstance != null) addCount++;
		if (m_SilhouetteInstance != null) addCount++;

		if (addCount == 0 || m_Renderers == null)
		{
			return;
		}

		foreach (var renderer in m_Renderers)
		{
			if (renderer == null)
			{
				continue;
			}

			var materials = renderer.sharedMaterials;
			var newMaterials = new Material[materials.Length + addCount];
			materials.CopyTo(newMaterials, 0);

			int index = materials.Length;
			if (m_OccludedMaskInstance != null)
			{
				newMaterials[index++] = m_OccludedMaskInstance;
			}
			if (m_StencilMaskInstance != null)
			{
				newMaterials[index++] = m_StencilMaskInstance;
			}
			if (m_OutlineInstance != null)
			{
				newMaterials[index++] = m_OutlineInstance;
			}
			if (m_SilhouetteInstance != null)
			{
				newMaterials[index++] = m_SilhouetteInstance;
			}

			renderer.sharedMaterials = newMaterials;
		}
	}

	private void ApplySettings()
	{
		int baseRef = m_StencilRef > 0 ? m_StencilRef : 10;
		int outlineStencilRef = baseRef;
		// 壁裏遮蔽専用のステンシル値(通常時アウトライン用と分離して通常時の誤描画を100%防止)
		int occlusionStencilRef = baseRef + 100;

		// 1. 通常時用アウトラインマスク(見えているピクセルにマーク)
		if (m_StencilMaskInstance != null)
		{
			m_StencilMaskInstance.SetFloat(m_StencilRefId, outlineStencilRef);
		}

		// 2. 通常時用アウトライン
		if (m_OutlineInstance != null)
		{
			m_OutlineInstance.SetColor(m_OutlineColorId, m_OutlineColor);
			m_OutlineInstance.SetFloat(m_StencilRefId, outlineStencilRef);
		}

		// 3. 壁裏用遮蔽マスク(壁裏のピクセルにのみマーク)
		if (m_OccludedMaskInstance != null)
		{
			m_OccludedMaskInstance.SetFloat(m_StencilRefId, occlusionStencilRef);
		}

		// 4. 壁裏用遮蔽シルエット
		if (m_SilhouetteInstance != null)
		{
			m_SilhouetteInstance.SetColor(m_SilhouetteColorId, m_SilhouetteColor);

			if (m_OccludedMaskInstance != null)
			{
				// 壁裏遮蔽マスクでマークされたピクセル(occlusionStencilRef)にのみ半透明グレーを描画
				m_SilhouetteInstance.SetFloat(m_StencilRefId, occlusionStencilRef);
				m_SilhouetteInstance.SetFloat(m_StencilCompId, (float)UnityEngine.Rendering.CompareFunction.Equal);
				m_SilhouetteInstance.SetFloat(m_StencilPassId, (float)UnityEngine.Rendering.StencilOp.IncrementSaturate);
			}
			else
			{
				// マスク未使用時は通常通り
				m_SilhouetteInstance.SetFloat(m_StencilRefId, occlusionStencilRef);
				m_SilhouetteInstance.SetFloat(m_StencilCompId, (float)UnityEngine.Rendering.CompareFunction.NotEqual);
				m_SilhouetteInstance.SetFloat(m_StencilPassId, (float)UnityEngine.Rendering.StencilOp.Replace);
			}
		}
	}

	private void OnDestroy()
	{
		if (m_OccludedMaskInstance != null)
		{
			Destroy(m_OccludedMaskInstance);
		}
		if (m_OutlineInstance != null)
		{
			Destroy(m_OutlineInstance);
		}
		if (m_StencilMaskInstance != null)
		{
			Destroy(m_StencilMaskInstance);
		}
		if (m_SilhouetteInstance != null)
		{
			Destroy(m_SilhouetteInstance);
		}
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
