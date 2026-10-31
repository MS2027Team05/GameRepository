
using System;
using UnityEngine;

public enum EPlayerGender
{
	Man,
	Woman
}

public sealed class PlayerAppearance : MonoBehaviour
{
	[Header("モデル")]
	[SerializeField] private GameObject m_ManModel;
	[SerializeField] private GameObject m_WomanModel;
	[SerializeField] private Renderer[] m_ManRenderers;
	[SerializeField] private Renderer[] m_WomanRenderers;

	[Header("チームカラー設定")]
	[SerializeField] private Renderer[] m_TeamColorRenderers;

	private bool m_HasGender;
	private bool m_HasColor;
	private bool m_HasTeamColor;
	private EPlayerGender m_CurrentGender;
	private Material m_CurrentMaterial;
	private Material m_CurrentTeamMaterial;

	public bool HasAppliedGender => m_HasGender;
	public bool HasAppliedColor => m_HasColor;
	public bool HasAppliedTeamColor => m_HasTeamColor;
	public EPlayerGender CurrentGender => m_CurrentGender;
	public Material CurrentMaterial => m_CurrentMaterial;
	public Material CurrentTeamMaterial => m_CurrentTeamMaterial;

	// 外部管理クラスから性別を取得する処理は、参照先が決まってから実装する。
	public EPlayerGender GetGender()
	{
		throw new NotImplementedException("性別の外部参照処理は未実装です。");
	}

	// 外部管理クラスからマテリアルを取得する処理は、参照先が決まってから実装する。
	public Material GetColor()
	{
		throw new NotImplementedException("マテリアルの外部参照処理は未実装です。");
	}

	// 外部管理クラスからチームカラーマテリアルを取得する処理は、参照先が決まってから実装する。
	public Material GetTeamColor()
	{
		throw new NotImplementedException("チームカラーマテリアルの外部参照処理は未実装です。");
	}

	// 取得元を選択し、性別に応じて表示モデルを切り替える。
	public void SetGender()
	{
		if (m_ManModel == null || m_WomanModel == null)
		{
			Debug.LogWarning("男性・女性モデルの参照が設定されていません。", this);
			return;
		}

		EPlayerGender gender = GetSelectedGender();

		m_ManModel.SetActive(gender == EPlayerGender.Man);
		m_WomanModel.SetActive(gender == EPlayerGender.Woman);

		m_CurrentGender = gender;
		m_HasGender = true;
	}

	// 取得元を選択し、受け取ったマテリアルを両モデルに反映する。
	public void SetColor()
	{
		Material selectedMaterial = GetSelectedColor();

		if (selectedMaterial == null)
		{
			Debug.LogWarning("反映するマテリアルが設定されていません。", this);
			return;
		}

		ApplyMaterial(m_ManRenderers, selectedMaterial);
		ApplyMaterial(m_WomanRenderers, selectedMaterial);

		m_CurrentMaterial = selectedMaterial;
		m_HasColor = true;
	}

	// 取得元を選択し、受け取ったチームカラーマテリアルを指定されたRendererに反映する。
	public void SetTeamColor()
	{
		if (m_TeamColorRenderers == null || m_TeamColorRenderers.Length == 0)
		{
			Debug.LogWarning("チームカラーを反映するRendererが設定されていません。", this);
			return;
		}

		Material selectedMaterial = GetSelectedTeamColor();

		if (selectedMaterial == null)
		{
			Debug.LogWarning("反映するチームカラーマテリアルが設定されていません。", this);
			return;
		}

		ApplyMaterial(m_TeamColorRenderers, selectedMaterial);

		m_CurrentTeamMaterial = selectedMaterial;
		m_HasTeamColor = true;
	}

	// 指定されたRendererにマテリアルを反映する。
	private static void ApplyMaterial(Renderer[] renderers, Material material)
	{
		if (renderers == null)
		{
			return;
		}

		foreach (Renderer targetRenderer in renderers)
		{
			if (targetRenderer != null)
			{
				targetRenderer.sharedMaterial = material;
			}
		}
	}

#if UNITY_EDITOR
	[Header("デバッグ設定")]
	[SerializeField] private bool m_UseDebugAppearance;
	[SerializeField] private EPlayerGender m_DebugGender;
	[SerializeField] private Material m_DebugMaterial;
	[SerializeField] private Material m_DebugTeamMaterial;

	// 使用する性別をデバッグ設定または外部管理クラスから取得する。
	private EPlayerGender GetSelectedGender()
	{
		return m_UseDebugAppearance ? SetGenderForDebug() : GetGender();
	}

	// 使用するカラーマテリアルをデバッグ設定または外部管理クラスから取得する。
	private Material GetSelectedColor()
	{
		return m_UseDebugAppearance ? SetColorForDebug() : GetColor();
	}

	// 使用するチームカラーマテリアルをデバッグ設定または外部管理クラスから取得する。
	private Material GetSelectedTeamColor()
	{
		return m_UseDebugAppearance ? SetTeamColorForDebug() : GetTeamColor();
	}

	// Inspectorで選択したデバッグ用の性別を返す。
	public EPlayerGender SetGenderForDebug()
	{
		return m_DebugGender;
	}

	// Inspectorで指定したデバッグ用のカラーマテリアルを返す。
	public Material SetColorForDebug()
	{
		return m_DebugMaterial;
	}

	// Inspectorで指定したデバッグ用のチームカラーマテリアルを返す。
	public Material SetTeamColorForDebug()
	{
		return m_DebugTeamMaterial;
	}

	// Inspectorの設定が変更された際、外見更新を遅延登録する。
	private void OnValidate()
	{
		UnityEditor.EditorApplication.delayCall -= ApplyAppearanceDelayed;
		UnityEditor.EditorApplication.delayCall += ApplyAppearanceDelayed;
	}

	// OnValidateの処理終了後にデバッグ用の外見を反映する。
	private void ApplyAppearanceDelayed()
	{
		UnityEditor.EditorApplication.delayCall -= ApplyAppearanceDelayed;

		if (this == null || !m_UseDebugAppearance)
		{
			return;
		}

		SetGender();
		SetColor();
		SetTeamColor();
	}

	// コンポーネントが破棄される際、遅延登録を解除する。
	private void OnDestroy()
	{
		UnityEditor.EditorApplication.delayCall -= ApplyAppearanceDelayed;
	}
#else
	// Playerビルドでは外部管理クラスから性別を取得する。
	private EPlayerGender GetSelectedGender()
	{
		return GetGender();
	}

	// Playerビルドでは外部管理クラスからカラーマテリアルを取得する。
	private Material GetSelectedColor()
	{
		return GetColor();
	}

	// Playerビルドでは外部管理クラスからチームカラーマテリアルを取得する。
	private Material GetSelectedTeamColor()
	{
		return GetTeamColor();
	}
#endif
}
