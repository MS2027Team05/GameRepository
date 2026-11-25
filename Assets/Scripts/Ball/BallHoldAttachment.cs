using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 所持中のボール描画をプレイヤーの所持ジョイントへ追従させます。
/// </summary>
/// <remarks>
/// 主な処理の流れ:
/// 1. 生成時にVisualRootの元Parent・位置・回転・Scaleを保存します。
/// 2. 所持開始時にVisualRootだけをHoldPointへ付け替えます。
/// 3. 所持終了時に保存していた元の階層とTransformへ戻します。
///
/// NetworkObjectやRigidbodyを含むボール本体の階層は変更しません。
/// 通信・物理と見た目の追従を分離するための補助クラスです。
/// </remarks>
public sealed class BallHoldAttachment
{
	private readonly Transform m_VisualRoot;
	private readonly Transform m_OriginalParent;
	private readonly Vector3 m_OriginalLocalPosition;
	private readonly Quaternion m_OriginalLocalRotation;
	private readonly Vector3 m_OriginalLocalScale;
	private readonly Vector3 m_OriginalWorldScale;
	private readonly ALightningBolt[] m_LightningEffects;
	private Transform m_HoldPoint;

	/// <summary>
	/// 描画専用のVisualRootが有効に設定されているかを取得します。
	/// </summary>
	public bool HasVisualRoot => m_VisualRoot != null;

	/// <summary>
	/// 解放復元時の初期ローカル座標を取得します。
	/// </summary>
	public Vector3 OriginalLocalPosition => m_OriginalLocalPosition;

	/// <summary>
	/// 解放復元時の初期ローカル回転を取得します。
	/// </summary>
	public Quaternion OriginalLocalRotation => m_OriginalLocalRotation;

	/// <summary>
	/// 解放復元時の初期ローカルスケールを取得します。
	/// </summary>
	public Vector3 OriginalLocalScale => m_OriginalLocalScale;

	public BallHoldAttachment(Transform ballRoot, Transform visualRoot)
	{
		if (visualRoot == null)
		{
			Debug.LogWarning("[BallHoldAttachment] 描画専用のVisual RootをInspectorで設定してください。", ballRoot);
			return;
		}

		// 物理・通信コンポーネントをボーンの子へ移さないため、設定時に検証します。
		if (visualRoot.parent != ballRoot ||
			visualRoot.GetComponentInChildren<NetworkObject>(true) != null ||
			visualRoot.GetComponentInChildren<Collider>(true) != null ||
			visualRoot.GetComponentInChildren<Rigidbody>(true) != null)
		{
			Debug.LogError("[BallHoldAttachment] Visual Rootには、ボール直下の描画専用階層を指定してください。Collider・Rigidbody・NetworkObjectは含められません。", ballRoot);
			return;
		}

		m_VisualRoot = visualRoot;
		m_OriginalParent = visualRoot.parent;
		m_OriginalLocalPosition = visualRoot.localPosition;
		m_OriginalLocalRotation = visualRoot.localRotation;
		m_OriginalLocalScale = visualRoot.localScale;
		m_OriginalWorldScale = visualRoot.lossyScale;
		m_LightningEffects = visualRoot.GetComponentsInChildren<ALightningBolt>(true);
	}

	/// <summary>
	/// 描画をジョイントの子にし、ローカル位置と回転をゼロにします。
	/// モデル側のスケールが異なる場合も、装着時のボールの大きさを維持します。
	/// </summary>
	public void Attach(Transform holdPoint)
	{
		if (m_VisualRoot == null || holdPoint == null ||
			(m_HoldPoint == holdPoint && m_VisualRoot.parent == holdPoint))
		{
			return;
		}

		Vector3 parentScale = holdPoint.lossyScale;
		if (Mathf.Approximately(parentScale.x, 0.0f) ||
			Mathf.Approximately(parentScale.y, 0.0f) ||
			Mathf.Approximately(parentScale.z, 0.0f))
		{
			return;
		}

		m_VisualRoot.SetParent(holdPoint, false);
		m_VisualRoot.localPosition = Vector3.zero;
		m_VisualRoot.localRotation = Quaternion.identity;
		m_VisualRoot.localScale = new Vector3(
			m_OriginalWorldScale.x / parentScale.x,
			m_OriginalWorldScale.y / parentScale.y,
			m_OriginalWorldScale.z / parentScale.z);
		m_HoldPoint = holdPoint;

		// 祖先の付け替えだけでは子の親変更通知が届かないため、所有者を明示します。
		PlayerAppearance owner = holdPoint.GetComponentInParent<PlayerAppearance>();
		foreach (ALightningBolt effect in m_LightningEffects)
		{
			effect.SetOwner(owner);
		}
	}

	/// <summary>
	/// 描画を元のボール階層へ戻し、初期のローカルTransformを復元します。
	/// </summary>
	public void Restore()
	{
		if (m_VisualRoot == null || m_HoldPoint == null)
		{
			return;
		}

		m_VisualRoot.SetParent(m_OriginalParent, false);
		m_VisualRoot.localPosition = m_OriginalLocalPosition;
		m_VisualRoot.localRotation = m_OriginalLocalRotation;
		m_VisualRoot.localScale = m_OriginalLocalScale;
		m_HoldPoint = null;
		foreach (ALightningBolt effect in m_LightningEffects)
		{
			effect.SetOwner(null);
		}
	}
}
