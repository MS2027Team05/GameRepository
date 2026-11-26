
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 画面サイズに応じてUI全体の大きさを調整します。
/// </summary>
public class PlayerUIResolutionManager : MonoBehaviour
{
	[Header("基準画面サイズ")]
	[SerializeField]
	private Vector2 m_ReferenceResolution =
		new Vector2(1920f, 1080f);

	[Header("サイズ調整するUI")]
	[SerializeField]
	private List<RectTransform> m_TargetUIList =
		new List<RectTransform>();

	// 各UIの初期スケールを保存
	private List<Vector3> m_InitialScales = new List<Vector3>();

	// 前回確認した画面サイズ
	private int m_PreviousWidth;
	private int m_PreviousHeight;

	private void Awake()
	{
		// 初期スケールを保存
		m_InitialScales.Clear();

		foreach (RectTransform target in m_TargetUIList)
		{
			if (target != null)
			{
				m_InitialScales.Add(target.localScale);
			}
			else
			{
				m_InitialScales.Add(Vector3.one);
			}
		}

		UpdateResolution();
	}

	private void Update()
	{
		// 画面サイズが変わった場合だけ更新
		if (Screen.width != m_PreviousWidth ||
			Screen.height != m_PreviousHeight)
		{
			UpdateResolution();
		}
	}

	/// <summary>
	/// 画面サイズに応じてUIのスケールを更新します。
	/// </summary>
	private void UpdateResolution()
	{
		m_PreviousWidth = Screen.width;
		m_PreviousHeight = Screen.height;

		if (m_ReferenceResolution.x <= 0f ||
			m_ReferenceResolution.y <= 0f)
		{
			return;
		}

		// 基準画面に対する幅・高さの倍率
		float widthRatio =
			Screen.width / m_ReferenceResolution.x;

		float heightRatio =
			Screen.height / m_ReferenceResolution.y;

		// 小さい方の倍率を採用して縦横比を維持
		float scaleRatio = Mathf.Min(widthRatio, heightRatio);

		// 登録されたUIを一括調整
		for (int i = 0; i < m_TargetUIList.Count; i++)
		{
			RectTransform target = m_TargetUIList[i];

			if (target == null)
				continue;

			target.localScale = m_InitialScales[i] * scaleRatio;
		}
	}
}
