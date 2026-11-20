using Unity.Jobs;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

/// <summary>
/// アプリケーション起動中1回に限り、設定された GraphicsStateCollection(DX12等のPSO事前生成) および
/// ShaderVariantCollection(シェーダーバリアント分割ロード) を段階的・非同期でウォームアップする常駐コントローラー。
/// TitleScene 等から開始し、完了前に LobbyScene へ遷移しても途切れずに継続して完了後は停止します。
/// </summary>
public class ShaderPrewarmController : MonoBehaviour
{
	private const int m_FallbackVariantsPerFrame = 5;

	public static ShaderPrewarmController Instance { get; private set; }

	[Header("ウォームアップ対象アセット")]
	[Tooltip("DirectX 12等のPSO事前生成用コレクション(Unity 6推奨)")]
	[SerializeField] private GraphicsStateCollection m_GraphicsStateCollection;

	[Tooltip("シェーダーバリアント分割ロード用コレクション")]
	[SerializeField] private ShaderVariantCollection m_ShaderVariantCollection;

	[Header("負荷分散設定")]
	[Tooltip("ShaderVariantCollection を1フレームあたり何件ずつウォームアップするか(0以下で既定値5)")]
	[SerializeField] private int m_VariantsPerFrame;

	private JobHandle m_GraphicsStateJobHandle;
	private bool m_HasPendingGraphicsStateJob;
	private bool m_IsGraphicsStateDone;
	private bool m_IsShaderVariantDone;

	/// <summary>
	/// すべての対象アセットのウォームアップが完了しているかを取得します。
	/// </summary>
	public bool IsCompleted => m_IsGraphicsStateDone && m_IsShaderVariantDone;

	private void Awake()
	{
		if (Instance != null && Instance != this)
		{
			Destroy(gameObject);
			return;
		}

		Instance = this;
		if (transform.parent == null)
		{
			DontDestroyOnLoad(gameObject);
		}
	}

	private void Start()
	{
		BeginWarmUp();
	}

	private void Update()
	{
		if (m_HasPendingGraphicsStateJob && m_GraphicsStateJobHandle.IsCompleted)
		{
			m_GraphicsStateJobHandle.Complete();
			m_HasPendingGraphicsStateJob = false;
			m_IsGraphicsStateDone = true;
		}

		if (!m_IsShaderVariantDone)
		{
			if (m_ShaderVariantCollection == null || m_ShaderVariantCollection.isWarmedUp)
			{
				m_IsShaderVariantDone = true;
			}
			else
			{
				int batchCount = m_VariantsPerFrame > 0 ? m_VariantsPerFrame : m_FallbackVariantsPerFrame;
				if (m_ShaderVariantCollection.WarmUpProgressively(batchCount))
				{
					m_IsShaderVariantDone = true;
				}
			}
		}

		if (IsCompleted)
		{
			enabled = false;
		}
	}

	private void OnDestroy()
	{
		if (m_HasPendingGraphicsStateJob)
		{
			m_GraphicsStateJobHandle.Complete();
			m_HasPendingGraphicsStateJob = false;
		}

		if (Instance == this)
		{
			Instance = null;
		}
	}

	private void BeginWarmUp()
	{
		if (m_GraphicsStateCollection == null || m_GraphicsStateCollection.isWarmedUp)
		{
			m_IsGraphicsStateDone = true;
		}
		else
		{
			m_GraphicsStateJobHandle = m_GraphicsStateCollection.WarmUp();
			m_HasPendingGraphicsStateJob = true;
			m_IsGraphicsStateDone = false;
		}

		if (m_ShaderVariantCollection == null || m_ShaderVariantCollection.isWarmedUp)
		{
			m_IsShaderVariantDone = true;
		}
		else
		{
			m_IsShaderVariantDone = false;
		}

		if (IsCompleted)
		{
			enabled = false;
		}
	}
}
