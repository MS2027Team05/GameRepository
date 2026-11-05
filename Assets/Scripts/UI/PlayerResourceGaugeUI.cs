using TMPro;
using UnityEngine;

/// <summary>
/// PlayerResourceControllerから通知された残量を、uGUIゲージと数値表示に反映します。
/// プレイヤーPrefab内の子Canvasに配置し、Owner本人のHUDだけを表示します。
/// </summary>
public class PlayerResourceGaugeUI : MonoBehaviour
{
	[Header("ゲージ表示用Image (Image Type: Filled)")]
	[SerializeField] private UnityEngine.UI.Image m_FillImage;

	[Header("現在値 / 最大値の表示用TextMeshPro")]
	[SerializeField] private TextMeshProUGUI m_ResourceText;

	/// <summary>
	/// リソース残量をゲージと数値表示に反映します。
	/// </summary>
	/// <param name="current">現在のリソース量</param>
	/// <param name="maximum">最大リソース量</param>
	public void SetResource(float current, float maximum)
	{
		float safeMaximum = Mathf.Max(0.0f, maximum);
		float safeCurrent = Mathf.Clamp(current, 0.0f, safeMaximum);

		if (m_FillImage != null)
		{
			m_FillImage.fillAmount = safeMaximum > 0.0f
				? safeCurrent / safeMaximum
				: 0.0f;
		}

		if (m_ResourceText != null)
		{
			// TMPの数値書式を利用し、文字列連結による毎回の割り当てを避けます。
			m_ResourceText.SetText("{0:0.0} / {1:0.0}", safeCurrent, safeMaximum);
		}
	}
}
