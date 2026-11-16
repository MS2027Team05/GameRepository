using TMPro;
using UnityEngine;

/// <summary>
/// リザルト画面でローカルプレイヤーの個人表彰(実績)1項目分を表示するUIコンポーネント。
/// 金/銀のメダルアイコン、文言テキスト、および個別背景の表示を制御します。
/// </summary>
public class ResultAwardItemUI : MonoBehaviour
{
	[Header("UI要素参照")]
	[Tooltip("1実績ごとの背景Image(任意)")]
	[SerializeField] private UnityEngine.UI.Image m_BackgroundImage;
	[Tooltip("金/銀メダルを表示するImage")]
	[SerializeField] private UnityEngine.UI.Image m_MedalImage;
	[Tooltip("称号名と記録値を表示するTextMeshProUGUI(任意)")]
	[SerializeField] private TextMeshProUGUI m_AwardText;

	[Header("ランク別表示設定(金=1位 / 銀=2位)")]
	[Tooltip("1位(金)用のメダルSprite(任意)")]
	[SerializeField] private Sprite m_GoldMedalSprite;
	[Tooltip("2位(銀)用のメダルSprite(任意)")]
	[SerializeField] private Sprite m_SilverMedalSprite;
	[Tooltip("1位(金)用のメダル乗算カラー")]
	[SerializeField] private Color m_GoldMedalColor;
	[Tooltip("2位(銀)用のメダル乗算カラー")]
	[SerializeField] private Color m_SilverMedalColor;
	[Tooltip("1位(金)用の個別背景カラー(アルファ値が0より大きい場合のみ適用)")]
	[SerializeField] private Color m_GoldBackgroundColor;
	[Tooltip("2位(銀)用の個別背景カラー(アルファ値が0より大きい場合のみ適用)")]
	[SerializeField] private Color m_SilverBackgroundColor;

	/// <summary>
	/// 指定された実績データをもとにUIの表示を更新し、項目を有効化します。
	/// </summary>
	/// <param name="award">表示する実績データ</param>
	public void SetAward(in PlayerAwardEntry award)
	{
		gameObject.SetActive(true);

		bool isGold = award.m_Rank <= 1;
		ApplyMedalVisual(isGold);
		ApplyBackgroundVisual(isGold);

		if (m_AwardText != null)
		{
			m_AwardText.text = MatchAwardEvaluator.FormatAwardUiText(award);
		}
	}

	/// <summary>
	/// この実績項目スロットを非表示にします。
	/// </summary>
	public void Hide()
	{
		gameObject.SetActive(false);
	}

	private void ApplyMedalVisual(bool isGold)
	{
		if (m_MedalImage == null)
		{
			return;
		}

		Sprite targetSprite = isGold ? m_GoldMedalSprite : m_SilverMedalSprite;
		if (targetSprite != null)
		{
			m_MedalImage.sprite = targetSprite;
		}

		Color targetColor = isGold ? m_GoldMedalColor : m_SilverMedalColor;
		if (targetColor.a > 0.0f)
		{
			m_MedalImage.color = targetColor;
		}
	}

	private void ApplyBackgroundVisual(bool isGold)
	{
		if (m_BackgroundImage == null)
		{
			return;
		}

		Color targetColor = isGold ? m_GoldBackgroundColor : m_SilverBackgroundColor;
		if (targetColor.a > 0.0f)
		{
			m_BackgroundImage.color = targetColor;
		}
	}
}
