using System;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Stage
{
	/// <summary>
	/// 配置方向の軸。
	/// </summary>
	public enum EAlignmentAxis
	{
		X,
		Y,
		Z
	}

	/// <summary>
	/// 配置する方向モード。
	/// </summary>
	public enum EPlacementDirection
	{
		Both,
		Positive,
		Negative
	}

	/// <summary>
	/// 基準となるオブジェクトのBoxColliderサイズを基に、指定軸へ等間隔に複製配置するコンポーネント。
	/// [ExecuteAlways]により、Prefab編集モードおよび通常Scene編集の両方でパラメータ変更時に即時反映されます。
	/// 再帰複製を防止するため、本コンポーネントは雛形オブジェクト自身ではなく管理用親オブジェクト(Root)にアタッチして使用します。
	/// </summary>
	[ExecuteAlways]
	[AddComponentMenu("Stage/Stage Object Bilateral Placer")]
	public sealed class StageObjectBilateralPlacer : MonoBehaviour
	{
#if UNITY_EDITOR
		private static bool m_IsSpawning;
#endif

		[Header("対象オブジェクト設定")]
		[Tooltip("複製基準となる雛形オブジェクト(BoxColliderを保持している必要があります)")]
		[SerializeField] private GameObject m_TemplateObject;

		[Tooltip("対象のBoxCollider(未指定時はTemplateObjectから自動取得します)")]
		[SerializeField] private BoxCollider m_TargetCollider;

		[Header("配置設定")]
		[Tooltip("等間隔配置を行うローカル軸")]
		[SerializeField] private EAlignmentAxis m_AlignmentAxis;

		[Tooltip("配置する方向(Both: 両端, Positive: 正方向のみ, Negative: 負方向のみ)")]
		[SerializeField] private EPlacementDirection m_PlacementDirection;

		[Tooltip("配置する個数(片側あたり)")]
		[SerializeField] private int m_CountPerSide;

		[Tooltip("基準オブジェクトの位置を中心に含めて両端へ伸ばすか")]
		[SerializeField] private bool m_IncludeCenter;

		[Tooltip("オブジェクト間の追加余白(隙間)。負の値を指定すると端同士が重なります")]
		[SerializeField] private float m_Gap;

		[Header("確認用(自動算出された実効サイズ)")]
		[Tooltip("BoxColliderとスケールから算出された1個あたりの長さ")]
		[SerializeField] private float m_CalculatedBaseSize;

		[Header("生成管理設定")]
		[Tooltip("複製オブジェクトを格納するコンテナTransform(未指定時は自動生成されます)")]
		[SerializeField] private Transform m_GeneratedContainer;

#if UNITY_EDITOR
		private bool m_IsRebuildScheduled;
		private GameObject m_PrevTemplateObject;
		private BoxCollider m_PrevTargetCollider;
		private EAlignmentAxis m_PrevAlignmentAxis;
		private EPlacementDirection m_PrevPlacementDirection;
		private int m_PrevCountPerSide;
		private bool m_PrevIncludeCenter;
		private float m_PrevGap;
#endif

		private void OnEnable()
		{
#if UNITY_EDITOR
			if (!Application.isPlaying)
			{
				UpdatePrevParameters();
			}
#endif
		}

#if UNITY_EDITOR
		private void OnValidate()
		{
			if (Application.isPlaying || m_IsSpawning)
			{
				return;
			}

			// パラメータが実際に変更された場合のみ再構築を要求し、AutoSaveの無限保存ループを防止
			if (CheckParametersChanged())
			{
				ScheduleRebuild();
			}
		}

		private bool CheckParametersChanged()
		{
			if (m_TemplateObject != m_PrevTemplateObject ||
				m_TargetCollider != m_PrevTargetCollider ||
				m_AlignmentAxis != m_PrevAlignmentAxis ||
				m_PlacementDirection != m_PrevPlacementDirection ||
				m_CountPerSide != m_PrevCountPerSide ||
				m_IncludeCenter != m_PrevIncludeCenter ||
				!Mathf.Approximately(m_Gap, m_PrevGap))
			{
				UpdatePrevParameters();
				return true;
			}

			return false;
		}

		private void UpdatePrevParameters()
		{
			m_PrevTemplateObject = m_TemplateObject;
			m_PrevTargetCollider = m_TargetCollider;
			m_PrevAlignmentAxis = m_AlignmentAxis;
			m_PrevPlacementDirection = m_PlacementDirection;
			m_PrevCountPerSide = m_CountPerSide;
			m_PrevIncludeCenter = m_IncludeCenter;
			m_PrevGap = m_Gap;
		}

		/// <summary>
		/// OnValidate内の直接操作を避け、安全なタイミングで再構築を遅延実行します。
		/// </summary>
		private void ScheduleRebuild()
		{
			if (m_IsRebuildScheduled || m_IsSpawning)
			{
				return;
			}

			m_IsRebuildScheduled = true;
			EditorApplication.delayCall += DelayedRebuild;
		}

		private void DelayedRebuild()
		{
			EditorApplication.delayCall -= DelayedRebuild;
			m_IsRebuildScheduled = false;

			if (this == null)
			{
				return;
			}

			Rebuild();
		}
#endif

		/// <summary>
		/// 配置オブジェクトを再構築します。
		/// </summary>
		[ContextMenu("配置を再構築 (Rebuild)")]
		public void Rebuild()
		{
#if UNITY_EDITOR
			// Projectウィンドウ上のPrefabアセット自体の場合は、シーン漏れ出しを防ぐため生成処理を行わない
			if (PrefabUtility.IsPartOfPrefabAsset(gameObject) || !gameObject.scene.IsValid())
			{
				return;
			}
#endif

			if (m_TemplateObject == null)
			{
				return;
			}

			// 自己指定による再帰を防止
			if (m_TemplateObject == gameObject)
			{
				Debug.LogError($"[StageObjectBilateralPlacer] '{gameObject.name}' 自身が TemplateObject に指定されています。再帰防止のため、親オブジェクト(Root)に本コンポーネントをアタッチし、TemplateObject には子オブジェクトを指定してください。", this);
				return;
			}

			BoxCollider collider = m_TargetCollider;
			if (collider == null)
			{
				collider = m_TemplateObject.GetComponent<BoxCollider>();
			}

			if (collider == null)
			{
				Debug.LogWarning($"[StageObjectBilateralPlacer] '{m_TemplateObject.name}' に BoxCollider が見つからないため配置を中断しました。", this);
				return;
			}

			ClearGeneratedObjects();

			if (m_CountPerSide <= 0 && !m_IncludeCenter)
			{
				return;
			}

			EnsureContainerExists();

			Vector3 localScale = m_TemplateObject.transform.localScale;
			Vector3 colliderSize = collider.size;

			Vector3 localAxis;
			float colliderDimension;
			float localScaleDimension;

			switch (m_AlignmentAxis)
			{
				case EAlignmentAxis.X:
					localAxis = Vector3.right;
					colliderDimension = colliderSize.x;
					localScaleDimension = Mathf.Abs(localScale.x);
					break;
				case EAlignmentAxis.Y:
					localAxis = Vector3.up;
					colliderDimension = colliderSize.y;
					localScaleDimension = Mathf.Abs(localScale.y);
					break;
				case EAlignmentAxis.Z:
					localAxis = Vector3.forward;
					colliderDimension = colliderSize.z;
					localScaleDimension = Mathf.Abs(localScale.z);
					break;
				default:
					localAxis = Vector3.right;
					colliderDimension = colliderSize.x;
					localScaleDimension = Mathf.Abs(localScale.x);
					break;
			}

			float baseSize = colliderDimension * localScaleDimension;
			m_CalculatedBaseSize = baseSize;

			float stepDistance = baseSize + m_Gap;
			Vector3 centerLocalPosition = m_TemplateObject.transform.localPosition;
			Quaternion templateRotation = m_TemplateObject.transform.localRotation;
			Transform containerTransform = m_GeneratedContainer;

			// 雛形オブジェクトのローカル回転を反映した移動方向ベクトル
			Vector3 moveDirection = templateRotation * localAxis;

#if UNITY_EDITOR
			m_IsSpawning = true;
#endif
			try
			{
				bool spawnPositive = m_PlacementDirection == EPlacementDirection.Both || m_PlacementDirection == EPlacementDirection.Positive;
				bool spawnNegative = m_PlacementDirection == EPlacementDirection.Both || m_PlacementDirection == EPlacementDirection.Negative;

				if (m_IncludeCenter)
				{
					// 基準オブジェクト自身を中心として残し、指定方向へ複製を並べる
					m_TemplateObject.SetActive(true);

					for (int i = 1; i <= m_CountPerSide; i++)
					{
						if (spawnPositive)
						{
							Vector3 positiveOffset = moveDirection * (stepDistance * i);
							SpawnInstance(centerLocalPosition + positiveOffset, templateRotation, localScale, containerTransform, $"+{i}");
						}

						if (spawnNegative)
						{
							Vector3 negativeOffset = -moveDirection * (stepDistance * i);
							SpawnInstance(centerLocalPosition + negativeOffset, templateRotation, localScale, containerTransform, $"-{i}");
						}
					}
				}
				else
				{
					// 基準オブジェクト自身は非アクティブ化し、指定方向へ複製を並べる
					m_TemplateObject.SetActive(false);

					if (m_PlacementDirection == EPlacementDirection.Both)
					{
						// 両端配置の場合は中心を挟んで左右均等に対称配置
						for (int i = 0; i < m_CountPerSide; i++)
						{
							float distance = stepDistance * (i + 0.5f);

							Vector3 positiveOffset = moveDirection * distance;
							SpawnInstance(centerLocalPosition + positiveOffset, templateRotation, localScale, containerTransform, $"+{i + 1}");

							Vector3 negativeOffset = -moveDirection * distance;
							SpawnInstance(centerLocalPosition + negativeOffset, templateRotation, localScale, containerTransform, $"-{i + 1}");
						}
					}
					else
					{
						// 片側配置の場合は基準位置から順に指定方向へ配置
						for (int i = 0; i < m_CountPerSide; i++)
						{
							float distance = stepDistance * i;
							Vector3 offset = (m_PlacementDirection == EPlacementDirection.Positive) ? (moveDirection * distance) : (-moveDirection * distance);
							string suffix = (m_PlacementDirection == EPlacementDirection.Positive) ? $"+{i + 1}" : $"-{i + 1}";
							SpawnInstance(centerLocalPosition + offset, templateRotation, localScale, containerTransform, suffix);
						}
					}
				}
			}
			finally
			{
#if UNITY_EDITOR
				m_IsSpawning = false;
#endif
			}

#if UNITY_EDITOR
			if (!Application.isPlaying)
			{
				EditorUtility.SetDirty(this);
				if (m_GeneratedContainer != null)
				{
					EditorUtility.SetDirty(m_GeneratedContainer.gameObject);
				}
			}
#endif
		}

		/// <summary>
		/// 生成済みオブジェクトを安全にクリアします。
		/// </summary>
		[ContextMenu("配置をクリア (Clear)")]
		public void ClearGeneratedObjects()
		{
			if (m_GeneratedContainer == null)
			{
				return;
			}

			int childCount = m_GeneratedContainer.childCount;
			for (int i = childCount - 1; i >= 0; i--)
			{
				GameObject child = m_GeneratedContainer.GetChild(i).gameObject;
#if UNITY_EDITOR
				if (!Application.isPlaying)
				{
					DestroyImmediate(child);
				}
				else
				{
					Destroy(child);
				}
#else
				Destroy(child);
#endif
			}
		}

		private void EnsureContainerExists()
		{
			if (m_GeneratedContainer != null)
			{
				return;
			}

			Transform existingChild = transform.Find($"Generated_{m_TemplateObject.name}");
			if (existingChild != null)
			{
				m_GeneratedContainer = existingChild;
				return;
			}

			GameObject containerObject = new GameObject($"Generated_{m_TemplateObject.name}");

			if (gameObject.scene.IsValid() && containerObject.scene != gameObject.scene)
			{
				SceneManager.MoveGameObjectToScene(containerObject, gameObject.scene);
			}

			containerObject.transform.SetParent(transform, false);
			containerObject.transform.localPosition = Vector3.zero;
			containerObject.transform.localRotation = Quaternion.identity;
			containerObject.transform.localScale = Vector3.one;

			m_GeneratedContainer = containerObject.transform;
		}

		private void SpawnInstance(Vector3 localPosition, Quaternion localRotation, Vector3 localScale, Transform parent, string suffix)
		{
			GameObject instance = Instantiate(m_TemplateObject, parent);
			instance.name = $"{m_TemplateObject.name}_{suffix}";

			// 複製物に本スクリプトが付着していた場合の連鎖を防ぐ
			StageObjectBilateralPlacer duplicatedPlacer = instance.GetComponent<StageObjectBilateralPlacer>();
			if (duplicatedPlacer != null)
			{
#if UNITY_EDITOR
				if (!Application.isPlaying)
				{
					DestroyImmediate(duplicatedPlacer);
				}
				else
				{
					Destroy(duplicatedPlacer);
				}
#else
				Destroy(duplicatedPlacer);
#endif
			}

			Transform instanceTransform = instance.transform;
			instanceTransform.localPosition = localPosition;
			instanceTransform.localRotation = localRotation;
			instanceTransform.localScale = localScale;

			instance.SetActive(true);
		}
	}
}
