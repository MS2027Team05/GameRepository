using UnityEngine;

/// <summary>
/// プレイヤーが使用する電流の色の取得元。
/// </summary>
public enum EEffectColorSource
{
	[InspectorName("選択色")]
	SelectedColor = 0,
	[InspectorName("チーム色")]
	TeamColor = 1,
}
