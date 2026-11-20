using TMPro;
using UnityEngine;

/// <summary>
/// ロビーの台座上に配置される表示特化キャラクターPrefab用の制御コンポーネント。
/// PlayerAppearanceへの外見反映と、頭上3Dネームテキストの表示・カメラ追従を担当します。
/// </summary>
public sealed class LobbyPlayerDisplay : MonoBehaviour
{
	[Header("コンポーネント参照")]
	[SerializeField] private PlayerAppearance m_PlayerAppearance;
	[SerializeField] private TMP_Text m_NameText;
	[Tooltip("カメラ方向へ向ける回転基準Transform(未設定時はm_NameTextのTransformを使用)")]
	[SerializeField] private Transform m_NameBillboardRoot;

	[Header("頭上ネーム表示設定")]
	[Tooltip("有効時、頭上ネームが常にMainCameraの正面を向くように回転補正します")]
	[SerializeField] private bool m_FaceMainCamera;
	[Tooltip("有効時、中央と端の台座でのカメラ距離差による文字サイズ差を補正します")]
	[SerializeField] private bool m_KeepConstantNameScale;
	[Tooltip("スケール補正の基準カメラ距離(0以下の場合は10mとして扱います)")]
	[SerializeField] private float m_ReferenceDistance;

	private static readonly int m_IsMovingHash = Animator.StringToHash("IsMoving");

	private Camera m_CachedCamera;
	private Camera m_OverrideCamera;
	private Animator[] m_Animators;
	private Transform m_TargetBillboardTransform;
	private Vector3 m_InitialNameScale;
	private bool m_HasCachedScale;

	private void Awake()
	{
		ResolveReferences();
		CacheInitialNameScale();
	}

	private void LateUpdate()
	{
		if (m_NameText == null || !m_NameText.gameObject.activeInHierarchy)
		{
			return;
		}

		if (!m_FaceMainCamera && !m_KeepConstantNameScale)
		{
			return;
		}

		Camera targetCamera = m_OverrideCamera != null && m_OverrideCamera.isActiveAndEnabled
			? m_OverrideCamera
			: null;

		if (targetCamera == null)
		{
			if (m_CachedCamera == null || !m_CachedCamera.isActiveAndEnabled)
			{
				m_CachedCamera = Camera.main;
			}
			targetCamera = m_CachedCamera;
		}

		if (targetCamera == null || m_TargetBillboardTransform == null)
		{
			return;
		}

		if (m_FaceMainCamera)
		{
			m_TargetBillboardTransform.rotation = targetCamera.transform.rotation;
		}

		if (m_KeepConstantNameScale && m_HasCachedScale)
		{
			float distance = Vector3.Distance(m_TargetBillboardTransform.position, targetCamera.transform.position);
			float refDist = m_ReferenceDistance > 0.001f ? m_ReferenceDistance : 10f;
			float multiplier = distance / refDist;
			m_TargetBillboardTransform.localScale = m_InitialNameScale * multiplier;
		}
	}

	/// <summary>
	/// 頭上ネームのビルボード向き・距離補正に使うカメラを上書き設定します(nullで既定のMainCameraへ戻します)。
	/// </summary>
	public void SetBillboardCameraOverride(Camera overrideCamera)
	{
		m_OverrideCamera = overrideCamera;
	}

	/// <summary>
	/// リプレイ再生時などに表示用モデルの歩行アニメーション状態を切り替えます。
	/// </summary>
	public void SetMovingAnimation(bool isMoving)
	{
		ResolveReferences();
		if (m_Animators == null)
		{
			return;
		}

		for (int i = 0; i < m_Animators.Length; i++)
		{
			Animator animator = m_Animators[i];
			if (animator != null && animator.runtimeAnimatorController != null)
			{
				animator.SetBool(m_IsMovingHash, isMoving);
			}
		}
	}

	/// <summary>
	/// 表示用モデルのAnimator状態を初期状態へ戻します。
	/// </summary>
	public void ResetAnimationState()
	{
		ResolveReferences();
		if (m_Animators == null)
		{
			return;
		}

		for (int i = 0; i < m_Animators.Length; i++)
		{
			Animator animator = m_Animators[i];
			if (animator == null || animator.runtimeAnimatorController == null)
			{
				continue;
			}

			animator.Rebind();
			animator.SetBool(m_IsMovingHash, false);
			if (animator.isActiveAndEnabled)
			{
				animator.Update(0f);
			}
		}
	}

	/// <summary>
	/// ロビー同期データに基づいて外見(モデル・12色マテリアル・チーム色)と頭上ネームを更新します。
	/// </summary>
	public void ApplyPlayerData(LobbyPlayerData playerData, Color nameColor)
	{
		ResolveReferences();
		CacheInitialNameScale();

		if (m_PlayerAppearance != null)
		{
			m_PlayerAppearance.ApplyAppearance(
				playerData.CharacterType,
				playerData.HighlightColor,
				playerData.Team);
		}

		if (m_NameText != null)
		{
			m_NameText.text = playerData.PlayerName.ToString();
			m_NameText.color = nameColor.a > 0.001f ? nameColor : Color.white;
		}
	}

	private void ResolveReferences()
	{
		if (m_PlayerAppearance == null)
		{
			m_PlayerAppearance = GetComponent<PlayerAppearance>();
		}

		if (m_NameText == null)
		{
			m_NameText = GetComponentInChildren<TMP_Text>(true);
		}

		if (m_TargetBillboardTransform == null)
		{
			m_TargetBillboardTransform = m_NameBillboardRoot != null
				? m_NameBillboardRoot
				: (m_NameText != null ? m_NameText.transform : null);
		}

		if (m_Animators == null || m_Animators.Length == 0)
		{
			m_Animators = GetComponentsInChildren<Animator>(true);
		}
	}

	private void CacheInitialNameScale()
	{
		if (!m_HasCachedScale && m_TargetBillboardTransform != null)
		{
			m_InitialNameScale = m_TargetBillboardTransform.localScale;
			m_HasCachedScale = true;
		}
	}
}

