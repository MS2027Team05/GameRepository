using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StageEditor
{
	/// <summary>
	/// シーン上のModel Prefabおよび散らばった既存Prefabインスタンスを検出し、
	/// Transform情報を維持したまま共通ディレクトリの通常Prefab(Regular Prefab)へ一括集約・置換するエディタ拡張ウィンドウです。
	/// </summary>
	public sealed class StagePrefabReplacerWindow : EditorWindow
	{
		[SerializeField]
		private GameObject m_TargetRoot;

		[SerializeField]
		private string m_SaveDirectoryPath = "Assets/Prefabs/Stage";

		[SerializeField]
		private bool m_CreateRegularPrefab = true;

		[SerializeField]
		private bool m_OverwriteExisting = true;

		[SerializeField]
		private bool m_IncludeExistingPrefabs = true;

		[SerializeField]
		private bool m_AutoResolveColorVariations = true;

		[SerializeField]
		private bool m_IncludeInactive = true;

		private Vector2 m_ScrollPosition;

		[MenuItem("Tools/Stage/Stage Prefab Replacer")]
		public static void Open()
		{
			StagePrefabReplacerWindow window = GetWindow<StagePrefabReplacerWindow>("Stage Prefab Replacer");
			window.minSize = new Vector2(380f, 420f);
			window.Show();
		}

		private void OnGUI()
		{
			EditorGUILayout.LabelField("ステージPrefab一括集約・置換ツール", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox(
				"シーン上のModel Prefab(3Dモデル)や各フォルダに散らばった既存Prefabを、Transform(座標・回転・Scale)を維持したまま指定フォルダに集約・置換します。",
				MessageType.Info);

			EditorGUILayout.Space();

			m_ScrollPosition = EditorGUILayout.BeginScrollView(m_ScrollPosition);

			EditorGUI.BeginChangeCheck();
			m_TargetRoot = (GameObject)EditorGUILayout.ObjectField(
				new GUIContent("置換対象ルート", "指定した親オブジェクト配下を走査します。未指定の場合は選択中のオブジェクトを対象にします。"),
				m_TargetRoot,
				typeof(GameObject),
				true);

			m_SaveDirectoryPath = EditorGUILayout.TextField(
				new GUIContent("Prefab集約先フォルダ", "生成したPrefabを保存するパスを指定します。"),
				m_SaveDirectoryPath);

			m_CreateRegularPrefab = EditorGUILayout.Toggle(
				new GUIContent("通常Prefabにする", "Variantではなく完全な青アイコンの通常Prefab(Regular Prefab)として生成します。インスペクターの青いオーバーライド表示を解消できます。"),
				m_CreateRegularPrefab);

			m_OverwriteExisting = EditorGUILayout.Toggle(
				new GUIContent("集約先Prefabを上書き更新", "集約先フォルダに同名のPrefabが既に存在する場合、通常Prefabとして再生成・上書きします。"),
				m_OverwriteExisting);

			m_IncludeExistingPrefabs = EditorGUILayout.Toggle(
				new GUIContent("既存Prefabも含める", "別フォルダに存在する既存Prefab(メインタワー・看板・箱等)も集約対象に含めるかどうか。"),
				m_IncludeExistingPrefabs);

			m_AutoResolveColorVariations = EditorGUILayout.Toggle(
				new GUIContent("赤/青バリエーション自動判別", "親階層名やマテリアルから赤/青のPrefabを自動的に判別して割り当てます。"),
				m_AutoResolveColorVariations);

			m_IncludeInactive = EditorGUILayout.Toggle(
				new GUIContent("非アクティブを含める", "非アクティブなGameObjectも置換対象に含めるかどうか。"),
				m_IncludeInactive);

			EditorGUILayout.Space();

			if (GUILayout.Button("置換対象の解析・プレビュー", GUILayout.Height(30f)))
			{
				AnalyzeTargets();
			}

			EditorGUILayout.Space();

			GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
			if (GUILayout.Button("一括置換を実行", GUILayout.Height(40f)))
			{
				ExecuteReplacement();
			}
			GUI.backgroundColor = Color.white;

			EditorGUILayout.EndScrollView();
		}

		/// <summary>
		/// 対象オブジェクトを走査して対象件数をログに表示します。
		/// </summary>
		private void AnalyzeTargets()
		{
			List<GameObject> targets = CollectTargetGameObjects();
			if (targets.Count == 0)
			{
				EditorUtility.DisplayDialog(
					"解析結果",
					"置換対象となるオブジェクトは見つかりませんでした。\n(置換対象ルートまたはヒエラルキーの選択を確認してください)",
					"OK");
				return;
			}

			Dictionary<GameObject, int> sourceCountDict = new Dictionary<GameObject, int>();
			foreach (GameObject target in targets)
			{
				GameObject sourceAsset = ResolveSourceAsset(target);
				if (sourceAsset != null)
				{
					if (sourceCountDict.ContainsKey(sourceAsset))
					{
						sourceCountDict[sourceAsset]++;
					}
					else
					{
						sourceCountDict.Add(sourceAsset, 1);
					}
				}
			}

			string summary = $"置換対象のオブジェクト数: {targets.Count} 件\nアセット種類数: {sourceCountDict.Count} 種\n\n【内訳】\n";
			foreach (KeyValuePair<GameObject, int> pair in sourceCountDict)
			{
				string assetPath = AssetDatabase.GetAssetPath(pair.Key);
				PrefabAssetType type = PrefabUtility.GetPrefabAssetType(pair.Key);
				summary += $"- {pair.Key.name} ({type}) : {pair.Value} 件\n  パス: {assetPath}\n";
			}

			EditorUtility.DisplayDialog("解析結果プレビュー", summary, "OK");
		}

		/// <summary>
		/// 置換処理を一括実行します。
		/// </summary>
		private void ExecuteReplacement()
		{
			List<GameObject> targets = CollectTargetGameObjects();
			if (targets.Count == 0)
			{
				EditorUtility.DisplayDialog("通知", "置換対象のオブジェクトがありません。", "OK");
				return;
			}

			bool confirmed = EditorUtility.DisplayDialog(
				"実行確認",
				$"{targets.Count} 件のオブジェクトを指定フォルダの通常Prefabに置換します。\nよろしいですか？",
				"実行",
				"キャンセル");

			if (!confirmed)
			{
				return;
			}

			EnsureDirectoryExists(m_SaveDirectoryPath);

			Dictionary<GameObject, GameObject> sourceToTargetPrefabMap = new Dictionary<GameObject, GameObject>();
			HashSet<GameObject> oldSelectedObjects = new HashSet<GameObject>(Selection.gameObjects);
			List<GameObject> newSelectedObjects = new List<GameObject>();
			HashSet<UnityEngine.SceneManagement.Scene> affectedScenes = new HashSet<UnityEngine.SceneManagement.Scene>();
			int replacedCount = 0;

			Undo.IncrementCurrentGroup();
			Undo.SetCurrentGroupName("Replace Stage Prefabs");
			int undoGroup = Undo.GetCurrentGroup();

			try
			{
				for (int i = 0; i < targets.Count; i++)
				{
					GameObject target = targets[i];
					if (target == null)
					{
						continue;
					}

					EditorUtility.DisplayProgressBar(
						"置換処理実行中",
						$"オブジェクトを置換中 ({i + 1}/{targets.Count}): {target.name}",
						(float)i / targets.Count);

					GameObject sourceAsset = ResolveSourceAsset(target);
					if (sourceAsset == null)
					{
						continue;
					}

					// 対応するPrefabを取得または新規生成/複製
					if (!sourceToTargetPrefabMap.TryGetValue(sourceAsset, out GameObject unifiedPrefab))
					{
						unifiedPrefab = GetOrCreateUnifiedPrefab(sourceAsset, m_SaveDirectoryPath, m_CreateRegularPrefab, m_OverwriteExisting);
						if (unifiedPrefab != null)
						{
							sourceToTargetPrefabMap.Add(sourceAsset, unifiedPrefab);
						}
					}

					if (unifiedPrefab == null)
					{
						Debug.LogError($"[StagePrefabReplacer] Prefabの集約作成に失敗したためスキップしました: {sourceAsset.name}");
						continue;
					}

					bool isTargetSelected = oldSelectedObjects.Contains(target);
					bool isTargetRoot = (m_TargetRoot == target);

					// Transform・階層情報を保持して置換
					GameObject newInstance = ReplaceInstance(target, unifiedPrefab);
					if (newInstance != null)
					{
						replacedCount++;
						affectedScenes.Add(newInstance.scene);

						if (isTargetSelected)
						{
							newSelectedObjects.Add(newInstance);
						}

						if (isTargetRoot)
						{
							m_TargetRoot = newInstance;
						}
					}
				}
			}
			finally
			{
				EditorUtility.ClearProgressBar();
				Undo.CollapseUndoOperations(undoGroup);

				// 破棄されたオブジェクトがSelectionに残りInspectorで例外が出るのを防止
				if (newSelectedObjects.Count > 0)
				{
					Selection.objects = newSelectedObjects.ToArray();
				}
				else
				{
					Selection.objects = Array.Empty<UnityEngine.Object>();
				}

				if (m_TargetRoot == null)
				{
					m_TargetRoot = null;
				}

				foreach (UnityEngine.SceneManagement.Scene scene in affectedScenes)
				{
					if (scene.IsValid() && scene.isLoaded)
					{
						EditorSceneManager.MarkSceneDirty(scene);
					}
				}

				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
			}

			EditorUtility.DisplayDialog(
				"置換完了",
				$"{replacedCount} 件のオブジェクトを集約通常Prefabに置換しました。\n保存先: {m_SaveDirectoryPath}",
				"OK");
		}

		/// <summary>
		/// 置換対象となるGameObjectのリストを収集します。
		/// </summary>
		private List<GameObject> CollectTargetGameObjects()
		{
			HashSet<GameObject> resultSet = new HashSet<GameObject>();

			if (m_TargetRoot != null)
			{
				Transform[] children = m_TargetRoot.GetComponentsInChildren<Transform>(m_IncludeInactive);
				foreach (Transform child in children)
				{
					CheckAndAddTarget(child.gameObject, resultSet);
				}
			}
			else if (Selection.gameObjects != null && Selection.gameObjects.Length > 0)
			{
				foreach (GameObject selected in Selection.gameObjects)
				{
					Transform[] children = selected.GetComponentsInChildren<Transform>(m_IncludeInactive);
					foreach (Transform child in children)
					{
						CheckAndAddTarget(child.gameObject, resultSet);
					}
				}
			}

			return new List<GameObject>(resultSet);
		}

		/// <summary>
		/// 対象GameObjectが置換対象のルートであるか判定し、リストに追加します。
		/// </summary>
		private void CheckAndAddTarget(GameObject go, HashSet<GameObject> resultSet)
		{
			if (go == null)
			{
				return;
			}

			// 最も外側のPrefabインスタンスのルートであるかチェック
			if (!PrefabUtility.IsAnyPrefabInstanceRoot(go))
			{
				return;
			}

			GameObject source = ResolveSourceAsset(go);
			if (source == null)
			{
				return;
			}

			string sourcePath = AssetDatabase.GetAssetPath(source);

			// すでに集約先フォルダのPrefabを参照している場合は除外(上書き無効時)
			string normalizedSaveDir = m_SaveDirectoryPath.TrimEnd('/').ToLowerInvariant();
			string normalizedSourceDir = Path.GetDirectoryName(sourcePath)?.Replace("\\", "/").ToLowerInvariant();
			if (!m_OverwriteExisting && normalizedSourceDir == normalizedSaveDir)
			{
				return;
			}

			PrefabAssetType assetType = PrefabUtility.GetPrefabAssetType(source);
			if (assetType == PrefabAssetType.Model)
			{
				resultSet.Add(go);
			}
			else if (m_IncludeExistingPrefabs && (assetType == PrefabAssetType.Regular || assetType == PrefabAssetType.Variant))
			{
				resultSet.Add(go);
			}
		}

		/// <summary>
		/// 指定したGameObjectの参照元アセットを返します。
		/// 赤/青などのカラーバリエーション自動判別が有効な場合は、適切なPrefabへ解決します。
		/// </summary>
		private GameObject ResolveSourceAsset(GameObject go)
		{
			GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(go);
			if (source == null)
			{
				return null;
			}

			string path = AssetDatabase.GetAssetPath(source);
			if (string.IsNullOrEmpty(path))
			{
				return null;
			}

			GameObject sourceAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
			if (sourceAsset == null)
			{
				return null;
			}

			// カラーバリエーション(赤/青)の自動判別
			if (m_AutoResolveColorVariations)
			{
				sourceAsset = ResolveColorVariation(go, sourceAsset);
			}

			return sourceAsset;
		}

		/// <summary>
		/// 親階層名またはマテリアルから赤/青のバリエーションを判別し、適切なPrefabアセットを返します。
		/// </summary>
		private static GameObject ResolveColorVariation(GameObject go, GameObject currentSource)
		{
			string currentName = currentSource.name;
			bool isRedAsset = currentName.IndexOf("red", StringComparison.OrdinalIgnoreCase) >= 0 || currentName.Contains("赤");
			bool isBlueAsset = currentName.IndexOf("blue", StringComparison.OrdinalIgnoreCase) >= 0 || currentName.Contains("青");

			if (!isRedAsset && !isBlueAsset)
			{
				return currentSource;
			}

			bool isTargetBlue = DetectIsBlue(go);

			// redアセットだがシーン上は青側である場合: blueアセットに解決
			if (isRedAsset && isTargetBlue)
			{
				GameObject bluePrefab = FindVariantPrefab(currentSource, "red", "blue", "赤", "青");
				if (bluePrefab != null)
				{
					return bluePrefab;
				}
			}
			// blueアセットだがシーン上は赤側である場合: redアセットに解決
			else if (isBlueAsset && !isTargetBlue)
			{
				GameObject redPrefab = FindVariantPrefab(currentSource, "blue", "red", "青", "赤");
				if (redPrefab != null)
				{
					return redPrefab;
				}
			}

			return currentSource;
		}

		/// <summary>
		/// オブジェクトが青側(ステージ青側または青マテリアル)であるかを検出します。
		/// </summary>
		private static bool DetectIsBlue(GameObject go)
		{
			// 1. 親階層のパスによるチェック
			Transform current = go.transform.parent;
			while (current != null)
			{
				string parentName = current.name;
				if (parentName.Contains("青") || parentName.IndexOf("blue", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return true;
				}
				if (parentName.Contains("赤") || parentName.IndexOf("red", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return false;
				}
				current = current.parent;
			}

			// 2. マテリアル設定によるチェック
			Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
			foreach (Renderer r in renderers)
			{
				if (r.sharedMaterials == null)
				{
					continue;
				}

				foreach (Material mat in r.sharedMaterials)
				{
					if (mat == null)
					{
						continue;
					}

					string matName = mat.name;
					if (matName.Contains("青") || matName.IndexOf("blue", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						return true;
					}
					if (matName.Contains("赤") || matName.IndexOf("red", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						return false;
					}
				}
			}

			return false;
		}

		/// <summary>
		/// 名前パターン(red <-> blue)を置換して同階層等にあるPrefabアセットを探索します。
		/// </summary>
		private static GameObject FindVariantPrefab(GameObject baseAsset, string from1, string to1, string from2, string to2)
		{
			string basePath = AssetDatabase.GetAssetPath(baseAsset);
			string directory = Path.GetDirectoryName(basePath);
			string fileName = Path.GetFileNameWithoutExtension(basePath);

			string replacedName = fileName;
			if (replacedName.IndexOf(from1, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				// 大文字小文字を考慮して置換
				int index = replacedName.IndexOf(from1, StringComparison.OrdinalIgnoreCase);
				replacedName = replacedName.Substring(0, index) + to1 + replacedName.Substring(index + from1.Length);
			}
			else if (replacedName.Contains(from2))
			{
				replacedName = replacedName.Replace(from2, to2);
			}

			string candidatePath = Path.Combine(directory, $"{replacedName}.prefab").Replace("\\", "/");
			GameObject candidate = AssetDatabase.LoadAssetAtPath<GameObject>(candidatePath);
			if (candidate != null)
			{
				return candidate;
			}

			// 全アセット検索
			string[] guids = AssetDatabase.FindAssets($"t:Prefab {replacedName}");
			foreach (string guid in guids)
			{
				string p = AssetDatabase.GUIDToAssetPath(guid);
				if (Path.GetFileNameWithoutExtension(p).Equals(replacedName, StringComparison.OrdinalIgnoreCase))
				{
					return AssetDatabase.LoadAssetAtPath<GameObject>(p);
				}
			}

			return null;
		}

		/// <summary>
		/// 元アセットに対応する集約Prefabを取得、無ければ新規生成または複製して返します。
		/// </summary>
		private static GameObject GetOrCreateUnifiedPrefab(
			GameObject sourceAsset,
			string directoryPath,
			bool createRegularPrefab,
			bool overwriteExisting)
		{
			string assetName = sourceAsset.name;
			string targetPath = Path.Combine(directoryPath, $"{assetName}.prefab").Replace("\\", "/");

			// 既に集約先に存在し、上書きしない場合はそのままロード
			if (!overwriteExisting)
			{
				GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
				if (existing != null)
				{
					return existing;
				}
			}

			// 一時インスタンスを生成
			GameObject tempInstance = (GameObject)PrefabUtility.InstantiatePrefab(sourceAsset);
			if (tempInstance == null)
			{
				return null;
			}

			try
			{
				if (createRegularPrefab)
				{
					// Model Prefab / Prefab Variantの親子リンクを完全に解除(全面青アイコンの通常Prefab化)
					PrefabUtility.UnpackPrefabInstance(tempInstance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
				}

				GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(tempInstance, targetPath);
				return savedPrefab;
			}
			finally
			{
				DestroyImmediate(tempInstance);
			}
		}

		/// <summary>
		/// Transformと階層位置を保持してPrefabインスタンスに置き換えます。
		/// </summary>
		private static GameObject ReplaceInstance(GameObject oldInstance, GameObject prefabAsset)
		{
			Transform oldTransform = oldInstance.transform;
			Transform parent = oldTransform.parent;
			int siblingIndex = oldTransform.GetSiblingIndex();

			// Transform情報の退避
			Vector3 localPos = oldTransform.localPosition;
			Quaternion localRot = oldTransform.localRotation;
			Vector3 localScale = oldTransform.localScale;
			string originalName = oldInstance.name;
			int layer = oldInstance.layer;
			string tag = oldInstance.tag;

			// 新しいPrefabインスタンスを生成
			GameObject newInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, parent);
			if (newInstance == null)
			{
				return null;
			}

			Undo.RegisterCreatedObjectUndo(newInstance, "Instantiate Regular Prefab");

			newInstance.name = originalName;
			newInstance.transform.localPosition = localPos;
			newInstance.transform.localRotation = localRot;
			newInstance.transform.localScale = localScale;
			newInstance.transform.SetSiblingIndex(siblingIndex);
			newInstance.layer = layer;

			if (!string.IsNullOrEmpty(tag) && tag != "Untagged")
			{
				newInstance.tag = tag;
			}

			// 古いオブジェクトの破棄をUndoに登録
			Undo.DestroyObjectImmediate(oldInstance);
			return newInstance;
		}

		/// <summary>
		/// ディレクトリが存在しない場合に再帰的に作成します。
		/// </summary>
		private static void EnsureDirectoryExists(string directoryPath)
		{
			if (AssetDatabase.IsValidFolder(directoryPath))
			{
				return;
			}

			string[] folders = directoryPath.Split('/');
			string currentPath = folders[0];

			for (int i = 1; i < folders.Length; i++)
			{
				string nextPath = $"{currentPath}/{folders[i]}";
				if (!AssetDatabase.IsValidFolder(nextPath))
				{
					AssetDatabase.CreateFolder(currentPath, folders[i]);
				}
				currentPath = nextPath;
			}
		}
	}
}
