using Stage;
using UnityEditor;
using UnityEngine;

namespace StageEditor
{
	/// <summary>
	/// StageRailPatternSwitcherのカスタムインスペクター拡張。
	/// 2x3の組み合わせマトリクスをワンクリックで切り替えるUIと、ビルダー参照の自動解決ボタンを提供します。
	/// </summary>
	[CustomEditor(typeof(StageRailPatternSwitcher))]
	public sealed class StageRailPatternSwitcherEditor : Editor
	{
		public override void OnInspectorGUI()
		{
			serializedObject.Update();

			EditorGUILayout.LabelField("外周レール 検証パターン切り替え (2x3)", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox(
				"レールの形状(3種+None)と外側足場の有無(On/Off)を切り替えます。\n" +
				"マルチプレイ中はサーバー側で切り替えると全クライアントへ同期されます。\n" +
				"Play中のキー操作: [F2] レール形状変更, [F3] 外側足場On/Off",
				MessageType.Info);

			EditorGUILayout.Space();

			StageRailPatternSwitcher switcher = (StageRailPatternSwitcher)target;

			EditorGUILayout.LabelField("パターン選択 (2x3 マトリクス)", EditorStyles.boldLabel);

			DrawPatternMatrix(switcher);

			EditorGUILayout.Space(10f);

			DrawDefaultInspector();

			EditorGUILayout.Space(10f);

			if (GUILayout.Button("シーン内のStageRailRingBuilderから参照を自動取得", GUILayout.Height(28f)))
			{
				StageRailRingBuilder builder = Object.FindFirstObjectByType<StageRailRingBuilder>();
				if (builder != null)
				{
					Undo.RecordObject(switcher, "Auto Assign Rail Roots");
					switcher.AutoAssignRoots(builder);
					EditorUtility.SetDirty(switcher);
				}
				else
				{
					EditorUtility.DisplayDialog("通知", "シーン内にStageRailRingBuilderが見つかりませんでした。", "OK");
				}
			}

			if (GUI.changed)
			{
				switcher.ApplyCurrentPattern();
			}

			serializedObject.ApplyModifiedProperties();
		}

		private void DrawPatternMatrix(StageRailPatternSwitcher switcher)
		{
			var currentMode = switcher.CurrentRailMode;
			bool currentFootholds = switcher.EnableOuterFootholds;

			EditorGUILayout.BeginVertical(EditorStyles.helpBox);

			// ヘッダー
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField("レール形状", GUILayout.Width(130f));
			EditorGUILayout.LabelField("足場なし (Off)", GUILayout.Width(100f));
			EditorGUILayout.LabelField("足場あり (On)", GUILayout.Width(100f));
			EditorGUILayout.EndHorizontal();

			DrawMatrixRow(switcher, "途切れ目なし (Continuous)", StageRailPatternSwitcher.ERailMode.Continuous, currentMode, currentFootholds);
			DrawMatrixRow(switcher, "5分割 (Split 5)", StageRailPatternSwitcher.ERailMode.Split5, currentMode, currentFootholds);
			DrawMatrixRow(switcher, "4分割+往復 (Split 4 Shuttle)", StageRailPatternSwitcher.ERailMode.Split4WithShuttle, currentMode, currentFootholds);
			DrawMatrixRow(switcher, "レールなし (None)", StageRailPatternSwitcher.ERailMode.None, currentMode, currentFootholds);

			EditorGUILayout.EndVertical();
		}

		private void DrawMatrixRow(
			StageRailPatternSwitcher switcher,
			string label,
			StageRailPatternSwitcher.ERailMode mode,
			StageRailPatternSwitcher.ERailMode currentMode,
			bool currentFootholds)
		{
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField(label, GUILayout.Width(130f));

			// 足場なしボタン
			bool isSelectedOff = currentMode == mode && !currentFootholds;
			if (isSelectedOff) GUI.backgroundColor = new Color(0.4f, 1f, 0.4f);
			if (GUILayout.Button(isSelectedOff ? "● 適用中" : "選択", GUILayout.Width(100f)))
			{
				Undo.RecordObject(switcher, "Switch Rail Pattern");
				switcher.SetPattern(mode, false);
				EditorUtility.SetDirty(switcher);
			}
			GUI.backgroundColor = Color.white;

			// 足場ありボタン
			bool isSelectedOn = currentMode == mode && currentFootholds;
			if (isSelectedOn) GUI.backgroundColor = new Color(0.4f, 1f, 0.4f);
			if (GUILayout.Button(isSelectedOn ? "● 適用中" : "選択", GUILayout.Width(100f)))
			{
				Undo.RecordObject(switcher, "Switch Rail Pattern");
				switcher.SetPattern(mode, true);
				EditorUtility.SetDirty(switcher);
			}
			GUI.backgroundColor = Color.white;

			EditorGUILayout.EndHorizontal();
		}
	}
}

