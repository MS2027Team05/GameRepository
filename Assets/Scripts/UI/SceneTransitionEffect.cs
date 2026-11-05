using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// シーン開始時および遷移時の画面トランジションを制御するクラス(現状はLobby->Game時に使用)
/// 現状はtransition演出のみなため、3カウントダウン等を行いたい場合は、こっちの演出が終了してから、
/// GameManager側(未作成)にEvent購読を行なって通知連携を行うように設計してね。
/// その場合は、このクラスをLobby->Game専用のクラスにリライトしてね(名前がややこしくなるから)
/// 加えて、3カウント前にクライアントとホストが全て参加していてロードが終わっているかを確認する必要がある点に注意
/// </summary>
public class SceneTransitionEffect : MonoBehaviour
{
	[Header("対象UI参照")]
	[SerializeField] private Image m_OverlayImage;

	[Header("演出パラメータ")]
	[SerializeField] private float m_Duration;
	[SerializeField] private float m_StartDelay;
	[SerializeField] private Ease m_EaseType;

	[Header("動作オプション")]
	[SerializeField] private bool m_PlayOnStart;
	[SerializeField] private bool m_DeactivateOnComplete;

	private Tween m_CurrentTween;

	private void Awake()
	{
		if (m_OverlayImage == null)
		{
			m_OverlayImage = GetComponent<Image>();
		}

		SetupImageProperties();
	}

	private void Start()
	{
		if (m_PlayOnStart)
		{
			PlayTransitionIn();
		}
	}

	private void OnDestroy()
	{
		m_CurrentTween?.Kill();
	}

	/// <summary>
	/// 水平ワイプ用のImageコンポーネントプロパティ(Filled/Horizontal/Right起点)を設定します。
	/// </summary>
	private void SetupImageProperties()
	{
		if (m_OverlayImage == null) return;

		m_OverlayImage.type = Image.Type.Filled;
		m_OverlayImage.fillMethod = Image.FillMethod.Horizontal;
		// 左から右へ
		m_OverlayImage.fillOrigin = (int)Image.OriginHorizontal.Right;
	}

	/// <summary>
	/// 暗転状態から左から右へと画面を開くトランジションイン演出を開始します。
	/// </summary>
	public void PlayTransitionIn(Action onComplete = null)
	{
		if (m_OverlayImage == null) return;

		m_CurrentTween?.Kill();

		// 初期状態として全画面を暗転で覆う
		m_OverlayImage.gameObject.SetActive(true);
		m_OverlayImage.fillAmount = 1f;

		m_CurrentTween = m_OverlayImage.DOFillAmount(0f, m_Duration)
			.SetDelay(m_StartDelay)
			.SetEase(m_EaseType)
			.OnComplete(() =>
			{
				if (m_DeactivateOnComplete)
				{
					m_OverlayImage.gameObject.SetActive(false);
				}
				onComplete?.Invoke();
			});
	}

	/// <summary>
	/// 画面を左から右へと暗転させるトランジションアウト演出を開始します(将来的なシーン終了時用)。
	/// </summary>
	public void PlayTransitionOut(Action onComplete = null)
	{
		if (m_OverlayImage == null) return;

		m_CurrentTween?.Kill();

		m_OverlayImage.gameObject.SetActive(true);
		m_OverlayImage.fillOrigin = (int)Image.OriginHorizontal.Left;
		m_OverlayImage.fillAmount = 0f;

		m_CurrentTween = m_OverlayImage.DOFillAmount(1f, m_Duration)
			.SetDelay(m_StartDelay)
			.SetEase(m_EaseType)
			.OnComplete(() =>
			{
				onComplete?.Invoke();
			});
	}
}
