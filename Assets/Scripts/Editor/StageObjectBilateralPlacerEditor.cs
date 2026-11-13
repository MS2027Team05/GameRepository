using Stage;
using UnityEditor;
using UnityEngine;

namespace StageEditor
{
	/// <summary>
	/// StageObjectBilateralPlacerのカスタムインスペクター拡張。
	/// シーンロード時の意図しない再生成・Dirty化を防ぐため、ユーザーによるInspector操作時および手動ボタン実行時のみ配置を再構築します。
	/// </summary>
	[CustomEditor(typeof(StageObjectBilateralPlacer))]
	public sealed class StageObjectBilateralPlacerEditor : Editor
	{
		public override void OnInspectorGUI()
		{
			serializedObject.Update();

			StageObjectBilateralPlacer placer = (StageObjectBilateralPlacer)target;
			bool isPrefabAsset = PrefabUtility.IsPartOfPrefabAsset(placer.gameObject);
			bool isPrefabInstance = !isPrefabAsset && PrefabUtility.IsPartOfPrefabInstance(placer.gameObject);

			DrawStatusHelpBox(isPrefabAsset, isPrefabInstance);

			EditorGUI.BeginChangeCheck();
			DrawDefaultInspector();
			bool inspectorChanged = EditorGUI.EndChangeCheck();

			serializedObject.ApplyModifiedProperties();

			if (inspectorChanged && !Application.isPlaying && !isPrefabAsset && !isPrefabInstance)
			{
				placer.Rebuild();
			}

			EditorGUILayout.Space(8f);

			using (new EditorGUI.DisabledScope(Application.isPlaying || isPrefabAsset))
			{
				if (GUILayout.Button("配置を再構築 (Rebuild)", GUILayout.Height(28f)))
				{
					if (!isPrefabInstance || ConfirmPrefabInstanceModification("再構築"))
					{
						placer.Rebuild();
					}
				}

				if (GUILayout.Button("生成済み配置をクリア (Clear)"))
				{
					if (!isPrefabInstance || ConfirmPrefabInstanceModification("クリア"))
					{
						placer.ClearGeneratedObjects();
						EditorUtility.SetDirty(placer);
					}
				}
			}
		}

		private static void DrawStatusHelpBox(bool isPrefabAsset, bool isPrefabInstance)
		{
			if (isPrefabAsset)
			{
				EditorGUILayout.HelpBox(
					"Project上のPrefabアセットを直接選択中です。配置を変更する場合はPrefabを開いて編集モード内で再構築してください。",
					MessageType.Info);
			}
			else if (isPrefabInstance)
			{
				EditorGUILayout.HelpBox(
					"シーン上のPrefabインスタンスです。シーン開閉時の自動再構築は無効化されています。\n" +
					"ここで再構築するとシーン側に大量の追加・削除オーバーライドが記録されるため、元のPrefabを開いて編集することを推奨します。",
					MessageType.Warning);
			}
			else
			{
				EditorGUILayout.HelpBox(
					"Inspectorで値を変更するか「配置を再構築 (Rebuild)」ボタンを押すと配置が更新されます。\n" +
					"シーンを開いただけでは再生成やDirty化は行われません。",
					MessageType.Info);
			}
		}

		private static bool ConfirmPrefabInstanceModification(string actionName)
		{
			return EditorUtility.DisplayDialog(
				"Prefabインスタンスでの操作確認",
				$"Prefabインスタンス上で{actionName}を行うと、シーン側に追加・削除オーバーライドが記録され、FileID変化やコンフリクトの原因になる場合があります。\n元のPrefabを開いて編集することを推奨しますが、このまま{actionName}を実行しますか？",
				"実行する",
				"キャンセル");
		}
	}
}
