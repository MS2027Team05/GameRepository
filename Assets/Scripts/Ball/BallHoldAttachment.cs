using Unity.Netcode;
using UnityEngine;

/// <summary>
/// ボールの描画専用階層を所持ジョイントへ付け替え、解放時に元の階層へ戻します。
/// 通信と物理の階層は変更しません。
/// </summary>
public sealed class BallHoldAttachment
{
	private readonly Transform m_VisualRoot;
	private readonly Transform m_OriginalParent;
	private readonly Vector3 m_OriginalLocalPosition;
	private readonly Quaternion m_OriginalLocalRotation;
	private readonly Vector3 m_OriginalLocalScale;
	private readonly Vector3 m_OriginalWorldScale;
	private Transform m_HoldPoint;

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
	}
}
