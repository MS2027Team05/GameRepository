using System;
using System.Collections.Generic;
using Stage;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StageEditor
{
	/// <summary>
	/// StageSymmetricSynchronizerコンポーネントのInspector拡張および点対称同期ロジックを提供します。
	/// </summary>
	[CustomEditor(typeof(StageSymmetricSynchronizer))]
	public sealed class StageSymmetricSynchronizerEditor : Editor
	{
		private SerializedProperty m_SourceRootProperty;
		private SerializedProperty m_TargetRootProperty;
		private SerializedProperty m_CenterPointProperty;
		private SerializedProperty m_RotationAxisProperty;
		private SerializedProperty m_RotationAngleProperty;
		private SerializedProperty m_PrefabMappingsProperty;
		private SerializedProperty m_IncludeInactiveProperty;
		private SerializedProperty m_ClearTargetBeforeSyncProperty;
		private SerializedProperty m_SyncNonMappedObjectsProperty;

		private void OnEnable()
		{
			m_SourceRootProperty = serializedObject.FindProperty("m_SourceRoot");
			m_TargetRootProperty = serializedObject.FindProperty("m_TargetRoot");
			m_CenterPointProperty = serializedObject.FindProperty("m_CenterPoint");
			m_RotationAxisProperty = serializedObject.FindProperty("m_RotationAxis");
			m_RotationAngleProperty = serializedObject.FindProperty("m_RotationAngle");
			m_PrefabMappingsProperty = serializedObject.FindProperty("m_PrefabMappings");
			m_IncludeInactiveProperty = serializedObject.FindProperty("m_IncludeInactive");
			m_ClearTargetBeforeSyncProperty = serializedObject.FindProperty("m_ClearTargetBeforeSync");
			m_SyncNonMappedObjectsProperty = serializedObject.FindProperty("m_SyncNonMappedObjects");
		}

		public override void OnInspectorGUI()
		{
			serializedObject.Update();

			EditorGUILayout.LabelField("ステージ点対称同期ツール", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox(
				"コピー元の配置をもとに、コピー先へ点対称貼り付けを行います。\n" +
				"Prefab置換リストに登録されたオブジェクトは指定チーム用Prefabへ自動置換されます。",
				MessageType.Info);

			EditorGUILayout.Space();

			EditorGUILayout.LabelField("ルート設定", EditorStyles.boldLabel);
			EditorGUILayout.PropertyField(m_SourceRootProperty, new GUIContent("コピー元Root"));
			EditorGUILayout.PropertyField(m_TargetRootProperty, new GUIContent("コピー先Root"));

			EditorGUILayout.Space();

			EditorGUILayout.LabelField("点対称パラメータ", EditorStyles.boldLabel);
			EditorGUILayout.PropertyField(m_CenterPointProperty, new GUIContent("対称中心座標 (Center Point)"));
			EditorGUILayout.PropertyField(m_RotationAxisProperty, new GUIContent("回転軸 (Rotation Axis)"));
			EditorGUILayout.PropertyField(m_RotationAngleProperty, new GUIContent("回転角度 (Rotation Angle)"));

			EditorGUILayout.Space();

			EditorGUILayout.LabelField("チーム固有Prefab置換マッピング", EditorStyles.boldLabel);
			EditorGUILayout.PropertyField(m_PrefabMappingsProperty, new GUIContent("Prefab置換リスト"), true);

			EditorGUILayout.Space();

			EditorGUILayout.LabelField("同期オプション", EditorStyles.boldLabel);
			EditorGUILayout.PropertyField(m_IncludeInactiveProperty, new GUIContent("非アクティブを含める"));
			EditorGUILayout.PropertyField(m_ClearTargetBeforeSyncProperty, new GUIContent("同期前にコピー先を全削除"));
			EditorGUILayout.PropertyField(m_SyncNonMappedObjectsProperty, new GUIContent("共通オブジェクトも複製"));

			serializedObject.ApplyModifiedProperties();

			EditorGUILayout.Space(10f);

			StageSymmetricSynchronizer synchronizer = (StageSymmetricSynchronizer)target;

			if (GUILayout.Button("同期対象の解析・プレビュー", GUILayout.Height(30f)))
			{
				AnalyzeSync(synchronizer);
			}

			EditorGUILayout.Space(5f);

			GUI.backgroundColor = new Color(0.4f, 0.85f, 1f);
			if (GUILayout.Button("同期を実行", GUILayout.Height(42f)))
			{
				ExecuteSync(synchronizer);
			}
			GUI.backgroundColor = Color.white;
		}

		private void OnSceneGUI()
		{
			StageSymmetricSynchronizer synchronizer = (StageSymmetricSynchronizer)target;
			if (synchronizer == null)
			{
				return;
			}

			// 対称中心点のGizmo表示
			Handles.color = Color.yellow;
			Handles.DrawWireDisc(synchronizer.CenterPoint, synchronizer.RotationAxis.normalized, 2f);
			Handles.Label(synchronizer.CenterPoint + Vector3.up * 1f, "Symmetry Center (点対称中心)");

			// コピー元・コピー先の参照線描画
			if (synchronizer.SourceRoot != null)
			{
				Handles.color = Color.cyan;
				Handles.DrawDottedLine(synchronizer.CenterPoint, synchronizer.SourceRoot.position, 4f);
				Handles.Label(synchronizer.SourceRoot.position, "Source: " + synchronizer.SourceRoot.name);
			}

			if (synchronizer.TargetRoot != null)
			{
				Handles.color = Color.red;
				Handles.DrawDottedLine(synchronizer.CenterPoint, synchronizer.TargetRoot.position, 4f);
				Handles.Label(synchronizer.TargetRoot.position, "Target: " + synchronizer.TargetRoot.name);
			}
		}

		/// <summary>
		/// コピー元オブジェクトを解析し、置換対象および複製対象のサマリーをダイアログ表示します。
		/// </summary>
		private void AnalyzeSync(StageSymmetricSynchronizer synchronizer)
		{
			if (synchronizer.SourceRoot == null)
			{
				EditorUtility.DisplayDialog("エラー", "コピー元ルート (Source Root) が指定されていません。", "OK");
				return;
			}

			Dictionary<GameObject, GameObject> mappingDict = BuildMappingDictionary(synchronizer);
			List<Transform> directOrPrefabRoots = CollectSyncTargets(synchronizer.SourceRoot, synchronizer.IncludeInactive);

			int mappedCount = 0;
			int commonPrefabCount = 0;
			int emptyGroupCount = 0;

			foreach (Transform item in directOrPrefabRoots)
			{
				GameObject go = item.gameObject;
				GameObject prefabSource = PrefabUtility.GetCorrespondingObjectFromSource(go);

				if (prefabSource != null)
				{
					if (mappingDict.ContainsKey(prefabSource))
					{
						mappedCount++;
					}
					else
					{
						commonPrefabCount++;
					}
				}
				else
				{
					emptyGroupCount++;
				}
			}

			string message = $"【解析結果】\n" +
				$"走査ルート: {synchronizer.SourceRoot.name}\n" +
				$"検出された同期対象オブジェクト数: {directOrPrefabRoots.Count} 件\n\n" +
				$"- チーム置換Prefab対象: {mappedCount} 件\n" +
				$"- 共通Prefab対象 (そのまま複製): {commonPrefabCount} 件\n" +
				$"- 階層グループ (空GameObject等): {emptyGroupCount} 件\n\n" +
				$"設定されたPrefabマッピング定義数: {mappingDict.Count} 件";

			EditorUtility.DisplayDialog("同期プレビュー", message, "OK");
		}

		/// <summary>
		/// 点対称同期処理を実行します。
		/// </summary>
		private void ExecuteSync(StageSymmetricSynchronizer synchronizer)
		{
			if (synchronizer.SourceRoot == null)
			{
				EditorUtility.DisplayDialog("エラー", "コピー元ルート (Source Root) を設定してください。", "OK");
				return;
			}

			if (synchronizer.TargetRoot == null)
			{
				EditorUtility.DisplayDialog("エラー", "コピー先ルート (Target Root) を設定してください。", "OK");
				return;
			}

			if (synchronizer.SourceRoot == synchronizer.TargetRoot)
			{
				EditorUtility.DisplayDialog("エラー", "コピー元とコピー先に同じオブジェクトは指定できません。", "OK");
				return;
			}

			bool confirmed = EditorUtility.DisplayDialog(
				"同期実行確認",
				$"{synchronizer.SourceRoot.name} の配置を点対称変換し、{synchronizer.TargetRoot.name} へ同期します。\n" +
				(synchronizer.ClearTargetBeforeSync ? "※コピー先の子オブジェクトは一括削除し再生成します。\n" : "") +
				"実行しますか？",
				"実行",
				"キャンセル");

			if (!confirmed)
			{
				return;
			}

			Undo.IncrementCurrentGroup();
			Undo.SetCurrentGroupName("Symmetric Field Sync");
			int undoGroup = Undo.GetCurrentGroup();

			try
			{
				// コピー先配下の既存子オブジェクトの削除
				if (synchronizer.ClearTargetBeforeSync)
				{
					List<GameObject> existingChildren = new List<GameObject>();
					for (int i = synchronizer.TargetRoot.childCount - 1; i >= 0; i--)
					{
						existingChildren.Add(synchronizer.TargetRoot.GetChild(i).gameObject);
					}

					foreach (GameObject child in existingChildren)
					{
						Undo.DestroyObjectImmediate(child);
					}
				}

				// マッピング辞書の作成
				Dictionary<GameObject, GameObject> mappingDict = BuildMappingDictionary(synchronizer);

				// 再帰的に同期オブジェクトを生成
				int createdCount = 0;
				Quaternion rotation = Quaternion.AngleAxis(synchronizer.RotationAngle, synchronizer.RotationAxis.normalized);

				SyncHierarchyRecursive(
					synchronizer.SourceRoot,
					synchronizer.TargetRoot,
					synchronizer.CenterPoint,
					rotation,
					mappingDict,
					synchronizer.IncludeInactive,
					synchronizer.SyncNonMappedObjects,
					ref createdCount);

				EditorSceneManager.MarkSceneDirty(synchronizer.TargetRoot.gameObject.scene);

				EditorUtility.DisplayDialog(
					"同期完了",
					$"{createdCount} 件のオブジェクトを点対称配置しました。",
					"OK");
			}
			catch (Exception ex)
			{
				Debug.LogError($"[StageSymmetricSynchronizer] 同期処理中に例外が発生しました: {ex}");
				EditorUtility.DisplayDialog("エラー", $"処理中に例外が発生しました:\n{ex.Message}", "OK");
			}
			finally
			{
				Undo.CollapseUndoOperations(undoGroup);
			}
		}

		/// <summary>
		/// 階層構造を再帰的に走査し、点対称なオブジェクトツリーをターゲット親配下に生成します。
		/// </summary>
		private void SyncHierarchyRecursive(
			Transform sourceParent,
			Transform targetParent,
			Vector3 centerPoint,
			Quaternion rotation,
			Dictionary<GameObject, GameObject> mappingDict,
			bool includeInactive,
			bool syncNonMapped,
			ref int createdCount)
		{
			for (int i = 0; i < sourceParent.childCount; i++)
			{
				Transform sourceChild = sourceParent.GetChild(i);
				if (!includeInactive && !sourceChild.gameObject.activeSelf)
				{
					continue;
				}

				// ワールド座標・回転・スケールの点対称計算
				Vector3 sourcePos = sourceChild.position;
				Quaternion sourceRot = sourceChild.rotation;

				Vector3 targetPos = centerPoint + rotation * (sourcePos - centerPoint);
				Quaternion targetRot = rotation * sourceRot;

				GameObject sourceGo = sourceChild.gameObject;
				bool isPrefabRoot = PrefabUtility.IsAnyPrefabInstanceRoot(sourceGo);
				GameObject prefabSourceAsset = PrefabUtility.GetCorrespondingObjectFromSource(sourceGo);

				if (isPrefabRoot && prefabSourceAsset != null)
				{
					// 置換マッピングが存在するかチェック
					bool isMapped = mappingDict.TryGetValue(prefabSourceAsset, out GameObject mappedPrefab);
					GameObject targetPrefabToInstantiate = null;
					if (isMapped)
					{
						targetPrefabToInstantiate = mappedPrefab;
					}
					else if (syncNonMapped)
					{
						targetPrefabToInstantiate = prefabSourceAsset;
					}

					if (targetPrefabToInstantiate != null)
					{
						GameObject newInstance = (GameObject)PrefabUtility.InstantiatePrefab(targetPrefabToInstantiate, targetParent);

						if (isMapped)
						{
							// 置換された場合は置換先Prefab名に切り替え(連番サフィックスがある場合は保持)
							string suffix = ExtractNumberSuffix(sourceGo.name);
							newInstance.name = targetPrefabToInstantiate.name + suffix;
						}
						else
						{
							// 共通オブジェクトは元の名前を維持
							newInstance.name = sourceGo.name;
						}

						newInstance.transform.position = targetPos;
						newInstance.transform.rotation = targetRot;
						newInstance.transform.localScale = sourceChild.localScale;

						Undo.RegisterCreatedObjectUndo(newInstance, "Sync Prefab Instance");
						createdCount++;
					}
					// Prefabルートの場合は内部の子オブジェクトはPrefabに含まれるため再帰走査しない
				}
				else
				{
					// Prefabでない通常オブジェクト(フォルダ代わりの空オブジェクト等)
					GameObject newEmptyGo = new GameObject(sourceGo.name);
					newEmptyGo.transform.SetParent(targetParent);
					newEmptyGo.transform.position = targetPos;
					newEmptyGo.transform.rotation = targetRot;
					newEmptyGo.transform.localScale = sourceChild.localScale;

					Undo.RegisterCreatedObjectUndo(newEmptyGo, "Sync Empty GameObject");
					createdCount++;

					// 子オブジェクトを再帰的に同期
					SyncHierarchyRecursive(
						sourceChild,
						newEmptyGo.transform,
						centerPoint,
						rotation,
						mappingDict,
						includeInactive,
						syncNonMapped,
						ref createdCount);
				}
			}
		}

		/// <summary>
		/// インスペクターで設定されたPrefabマッピングリストから辞書を作成します。
		/// </summary>
		private Dictionary<GameObject, GameObject> BuildMappingDictionary(StageSymmetricSynchronizer synchronizer)
		{
			Dictionary<GameObject, GameObject> dict = new Dictionary<GameObject, GameObject>();
			if (synchronizer.PrefabMappings == null)
			{
				return dict;
			}

			foreach (PrefabMappingPair pair in synchronizer.PrefabMappings)
			{
				if (pair.SourcePrefab != null && pair.TargetPrefab != null)
				{
					if (!dict.ContainsKey(pair.SourcePrefab))
					{
						dict.Add(pair.SourcePrefab, pair.TargetPrefab);
					}
				}
			}

			return dict;
		}

		/// <summary>
		/// 同期対象となる最上位オブジェクトおよびPrefabルートを収集します。
		/// </summary>
		private List<Transform> CollectSyncTargets(Transform root, bool includeInactive)
		{
			List<Transform> list = new List<Transform>();
			CollectSyncTargetsRecursive(root, list, includeInactive);
			return list;
		}

		private void CollectSyncTargetsRecursive(Transform current, List<Transform> list, bool includeInactive)
		{
			for (int i = 0; i < current.childCount; i++)
			{
				Transform child = current.GetChild(i);
				if (!includeInactive && !child.gameObject.activeSelf)
				{
					continue;
				}

				list.Add(child);

				// Prefabルートでなければさらに子を収集
				if (!PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject))
				{
					CollectSyncTargetsRecursive(child, list, includeInactive);
				}
			}
		}

		/// <summary>
		/// オブジェクト名末尾の連番サフィックス(例: " (1)")を抽出します。
		/// </summary>
		private static string ExtractNumberSuffix(string sourceName)
		{
			if (string.IsNullOrEmpty(sourceName))
			{
				return string.Empty;
			}

			System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(sourceName, @"\s*\(\d+\)$");
			return match.Success ? match.Value : string.Empty;
		}
	}
}
