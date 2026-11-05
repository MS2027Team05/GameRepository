using Stage;
using UnityEditor;
using UnityEngine;

namespace StageEditor
{
	/// <summary>
	/// StageRailRingBuilderのカスタムインスペクター拡張。
	/// ワンクリックでの全パターン一括生成、クリア、および設定調整を提供します。
	/// </summary>
	[CustomEditor(typeof(StageRailRingBuilder))]
	public sealed class StageRailRingBuilderEditor : Editor
	{
		public override void OnInspectorGUI()
		{
			serializedObject.Update();

			EditorGUILayout.LabelField("外周レール & 足場ジェネレーター", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox(
				"フィールド外周に両ゴールを囲む楕円レール(3パターン)と外側足場を一括生成します。\n" +
				"生成される各直方体は親Scale (1, 1, 1) を維持し、BoxColliderで安定した接地を実現します。",
				MessageType.Info);

			EditorGUILayout.Space();

			DrawDefaultInspector();

			EditorGUILayout.Space(10f);

			StageRailRingBuilder builder = (StageRailRingBuilder)target;

			GUI.backgroundColor = new Color(0.4f, 0.85f, 1f);
			if (GUILayout.Button("外周レール & 足場を一括生成 (Rebuild All)", GUILayout.Height(36f)))
			{
				Undo.RegisterFullObjectHierarchyUndo(builder.gameObject, "Rebuild Rail Ring");
				builder.BuildAllPatterns();
			}
			GUI.backgroundColor = Color.white;

			EditorGUILayout.Space(4f);

			GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
			if (GUILayout.Button("生成されたオブジェクトを全削除 (Clear All)", GUILayout.Height(26f)))
			{
				if (EditorUtility.DisplayDialog("確認", "生成されたレール・足場オブジェクトをすべて削除しますか？", "削除", "キャンセル"))
				{
					Undo.RegisterFullObjectHierarchyUndo(builder.gameObject, "Clear Rail Ring");
					builder.ClearAll();
				}
			}
			GUI.backgroundColor = Color.white;

			serializedObject.ApplyModifiedProperties();
		}
	}
}

