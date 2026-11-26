using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 電撃チャージの進捗UIを管理します。
/// </summary>
public class PlayerChargeProgress : MonoBehaviour
{
	[Header("UI設定")]
	[SerializeField] private RectTransform m_SegmentsRoot;

	[Header("円形配置設定")]
	[SerializeField] private int m_SegmentCount = 20;
	[SerializeField] private float m_Radius = 75f;
	[SerializeField] private float m_SegmentWidth = 5f;
	[SerializeField] private float m_SegmentHeight = 18f;

	[Header("色設定")]
	[SerializeField] private Color m_ActiveColor = Color.cyan;
	[SerializeField] private Color m_InactiveColor = Color.gray;

	private ElectricAction m_ElectricAction;
	private NetworkObject m_PlayerNetworkObject;

	private Image[] m_Segments;

	// 追尾パスチャージ表示モード
	private bool m_IsPassChargeMode;

	// 全ての棒で共通使用する白いSprite
	private static Sprite m_WhiteSprite;

	// Owner判定用
	private bool m_OwnerChecked;

	private void Start()
	{
		// 棒を生成
		CreateSegments();

		// 親のPlayerからElectricActionを取得
		m_ElectricAction =
			GetComponentInParent<ElectricAction>();

		// 親のPlayerからNetworkObjectを取得
		m_PlayerNetworkObject =
			GetComponentInParent<NetworkObject>();

		if (m_ElectricAction == null)
		{
			Debug.LogWarning(
				"[PlayerChargeProgress] ElectricActionが見つかりません。");
		}

		if (m_PlayerNetworkObject == null)
		{
			Debug.LogWarning(
				"[PlayerChargeProgress] NetworkObjectが見つかりません。");
		}
	}

	private void Update()
	{
		// Owner判定がまだの場合
		if (!m_OwnerChecked)
		{
			if (m_PlayerNetworkObject == null)
				return;

			// NetworkObjectのSpawn完了を待つ
			if (!m_PlayerNetworkObject.IsSpawned)
				return;

			m_OwnerChecked = true;

			// 自分が操作していないPlayerのUIは非表示
			if (!m_PlayerNetworkObject.IsOwner)
			{
				gameObject.SetActive(false);
				return;
			}
		}

		if (m_ElectricAction == null)
			return;

		// 追尾パスチャージ中はPlayerController側から進捗を受け取る
		if (m_IsPassChargeMode)
		{
			return;
		}

		// 通常は電撃チャージの進捗を表示
		SetProgress(m_ElectricAction.ChargeRatio);
	}

	/// <summary>
	/// 円形に棒を生成します。
	/// </summary>
	private void CreateSegments()
	{
		if (m_SegmentsRoot == null)
			return;

		m_Segments = new Image[m_SegmentCount];

		for (int i = 0; i < m_SegmentCount; i++)
		{
			GameObject segment = new GameObject(
				$"Segment_{i}",
				typeof(RectTransform),
				typeof(Image));

			segment.transform.SetParent(
				m_SegmentsRoot,
				false);

			RectTransform rect =
				segment.GetComponent<RectTransform>();

			rect.sizeDelta = new Vector2(
				m_SegmentWidth,
				m_SegmentHeight);

			float angle = i * (360f / m_SegmentCount);
			float radian = angle * Mathf.Deg2Rad;

			Vector2 direction = new Vector2(
				Mathf.Sin(radian),
				Mathf.Cos(radian));

			rect.anchoredPosition =
				direction * m_Radius;

			rect.localRotation = Quaternion.Euler(
				0f,
				0f,
				-angle);

			m_Segments[i] =
				segment.GetComponent<Image>();

			// 白いSpriteを設定
			m_Segments[i].sprite = GetWhiteSprite();

			// 色を初期化
			m_Segments[i].color = m_InactiveColor;
		}
	}

	/// <summary>
	/// UI用の白いSpriteを生成して取得します。
	/// </summary>
	private static Sprite GetWhiteSprite()
	{
		if (m_WhiteSprite != null)
			return m_WhiteSprite;

		Texture2D texture = new Texture2D(
			1,
			1,
			TextureFormat.RGBA32,
			false);

		texture.SetPixel(0, 0, Color.white);
		texture.Apply();

		m_WhiteSprite = Sprite.Create(
			texture,
			new Rect(0, 0, 1, 1),
			new Vector2(0.5f, 0.5f));

		return m_WhiteSprite;
	}

	/// <summary>
	/// チャージ進捗に応じて棒の色を変更します。
	/// progressは0～1で指定します。
	/// </summary>
	public void SetProgress(float progress)
	{
		if (m_Segments == null)
			return;

		progress = Mathf.Clamp01(progress);

		int activeCount = Mathf.RoundToInt(
			progress * m_SegmentCount);

		for (int i = 0; i < m_Segments.Length; i++)
		{
			Image segment = m_Segments[i];

			if (segment == null)
				continue;

			segment.enabled = true;

			segment.color =
				i < activeCount
				? m_ActiveColor
				: m_InactiveColor;
		}
	}

	/// <summary>
	/// 追尾パスチャージ表示モードを切り替えます。
	/// </summary>
	public void SetPassChargeMode(bool isActive)
	{
		m_IsPassChargeMode = isActive;

		if (isActive)
		{
			// 追尾パスチャージ開始時は0から表示
			SetProgress(0.0f);
		}
	}

	/// <summary>
	/// 追尾パスチャージの進捗をUIに反映します。
	/// progressは0～1。
	/// </summary>
	public void SetPassChargeProgress(float progress)
	{
		if (!m_IsPassChargeMode)
		{
			return;
		}

		SetProgress(progress);
	}
}
