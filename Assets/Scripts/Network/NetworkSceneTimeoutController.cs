using System;
using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// ネットワーク経由のシーン同期遷移における <see cref="NetworkConfig.LoadSceneTimeOut"/> を
/// 遷移先シーンに応じて動的に切り替え、完了後に既定値へ復元するコントローラー。
/// GameScene 等のロード負荷が大きいシーンへの遷移時のみ長いタイムアウト時間を適用します。
/// </summary>
public class NetworkSceneTimeoutController : MonoBehaviour
{
	private const int m_FallbackDefaultTimeoutSeconds = 45;
	private const int m_FallbackHeavyTimeoutSeconds = 120;
	private static readonly string[] m_FallbackExtendedScenes = { "GameScene", "LevelDesignScene" };

	public static NetworkSceneTimeoutController Instance { get; private set; }

	[Header("タイムアウト秒数設定")]
	[Tooltip("通常のシーン遷移(Title->Lobby、Game->Result/Lobby等)に適用するタイムアウト秒数")]
	[SerializeField] private int m_DefaultLoadSceneTimeoutSeconds;

	[Tooltip("重量シーン(GameScene等)への遷移時に適用するタイムアウト秒数")]
	[SerializeField] private int m_HeavyLoadSceneTimeoutSeconds;

	[Header("長時間タイムアウト対象シーン名")]
	[Tooltip("長いタイムアウト秒数を適用するシーン名一覧(未設定時はGameScene/LevelDesignScene)")]
	[SerializeField] private string[] m_ExtendedTimeoutSceneNames;

	private NetworkSceneManager m_SubscribedSceneManager;
	private static NetworkSceneManager m_FallbackSubscribedSceneManager;

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
		EnsureSceneEventSubscribed();
		ApplyLoadSceneTimeout(GetDefaultTimeoutSeconds());
	}

	private void OnDestroy()
	{
		UnsubscribeSceneEvent();

		if (Instance == this)
		{
			Instance = null;
		}
	}

	/// <summary>
	/// 遷移先シーンに応じたタイムアウト時間を設定した上でネットワークシーン遷移を実行します。
	/// インスタンスが未配置の場合でもフォールバック値(通常45秒 / 重量シーン120秒)で安全に動作します。
	/// </summary>
	public static SceneEventProgressStatus LoadNetworkScene(string sceneName, LoadSceneMode loadSceneMode = LoadSceneMode.Single)
	{
		if (NetworkManager.Singleton == null || NetworkManager.Singleton.SceneManager == null)
		{
			return SceneEventProgressStatus.SceneManagementNotEnabled;
		}

		if (!NetworkManager.Singleton.IsServer)
		{
			return SceneEventProgressStatus.ServerOnlyAction;
		}

		if (Instance != null)
		{
			return Instance.LoadSceneInternal(sceneName, loadSceneMode);
		}

		return LoadSceneWithFallback(sceneName, loadSceneMode);
	}

	private SceneEventProgressStatus LoadSceneInternal(string sceneName, LoadSceneMode loadSceneMode)
	{
		EnsureSceneEventSubscribed();

		int defaultTimeout = GetDefaultTimeoutSeconds();
		int targetTimeout = IsExtendedTimeoutScene(sceneName)
			? GetHeavyTimeoutSeconds()
			: defaultTimeout;

		ApplyLoadSceneTimeout(targetTimeout);

		SceneEventProgressStatus status = NetworkManager.Singleton.SceneManager.LoadScene(sceneName, loadSceneMode);
		if (status != SceneEventProgressStatus.Started)
		{
			ApplyLoadSceneTimeout(defaultTimeout);
		}

		return status;
	}

	private static SceneEventProgressStatus LoadSceneWithFallback(string sceneName, LoadSceneMode loadSceneMode)
	{
		EnsureFallbackSceneEventSubscribed();

		int targetTimeout = MatchesSceneNameList(sceneName, m_FallbackExtendedScenes)
			? m_FallbackHeavyTimeoutSeconds
			: m_FallbackDefaultTimeoutSeconds;

		ApplyLoadSceneTimeout(targetTimeout);

		SceneEventProgressStatus status = NetworkManager.Singleton.SceneManager.LoadScene(sceneName, loadSceneMode);
		if (status != SceneEventProgressStatus.Started)
		{
			ApplyLoadSceneTimeout(m_FallbackDefaultTimeoutSeconds);
		}

		return status;
	}

	private int GetDefaultTimeoutSeconds()
	{
		return m_DefaultLoadSceneTimeoutSeconds > 0
			? m_DefaultLoadSceneTimeoutSeconds
			: m_FallbackDefaultTimeoutSeconds;
	}

	private int GetHeavyTimeoutSeconds()
	{
		int defaultTimeout = GetDefaultTimeoutSeconds();
		int heavyTimeout = m_HeavyLoadSceneTimeoutSeconds > 0
			? m_HeavyLoadSceneTimeoutSeconds
			: m_FallbackHeavyTimeoutSeconds;

		return Mathf.Max(defaultTimeout, heavyTimeout);
	}

	private bool IsExtendedTimeoutScene(string sceneName)
	{
		string[] targetList = (m_ExtendedTimeoutSceneNames != null && m_ExtendedTimeoutSceneNames.Length > 0)
			? m_ExtendedTimeoutSceneNames
			: m_FallbackExtendedScenes;

		return MatchesSceneNameList(sceneName, targetList);
	}

	private static bool MatchesSceneNameList(string sceneName, string[] candidates)
	{
		if (string.IsNullOrWhiteSpace(sceneName) || candidates == null)
		{
			return false;
		}

		string normalizedName = Path.GetFileNameWithoutExtension(sceneName.Trim());
		for (int i = 0; i < candidates.Length; i++)
		{
			string candidate = candidates[i];
			if (string.IsNullOrWhiteSpace(candidate))
			{
				continue;
			}

			string normalizedCandidate = Path.GetFileNameWithoutExtension(candidate.Trim());
			if (string.Equals(normalizedName, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	private static void ApplyLoadSceneTimeout(int timeoutSeconds)
	{
		if (NetworkManager.Singleton == null || NetworkManager.Singleton.NetworkConfig == null)
		{
			return;
		}

		NetworkManager.Singleton.NetworkConfig.LoadSceneTimeOut = Mathf.Max(1, timeoutSeconds);
	}

	private void EnsureSceneEventSubscribed()
	{
		if (NetworkManager.Singleton == null || NetworkManager.Singleton.SceneManager == null)
		{
			return;
		}

		NetworkSceneManager current = NetworkManager.Singleton.SceneManager;
		if (m_SubscribedSceneManager == current)
		{
			return;
		}

		UnsubscribeSceneEvent();
		m_SubscribedSceneManager = current;
		m_SubscribedSceneManager.OnLoadEventCompleted += HandleLoadEventCompleted;
	}

	private void UnsubscribeSceneEvent()
	{
		if (m_SubscribedSceneManager != null)
		{
			m_SubscribedSceneManager.OnLoadEventCompleted -= HandleLoadEventCompleted;
			m_SubscribedSceneManager = null;
		}
	}

	private void HandleLoadEventCompleted(
		string sceneName,
		LoadSceneMode loadSceneMode,
		List<ulong> clientsCompleted,
		List<ulong> clientsTimedOut)
	{
		ApplyLoadSceneTimeout(GetDefaultTimeoutSeconds());
	}

	private static void EnsureFallbackSceneEventSubscribed()
	{
		if (NetworkManager.Singleton == null || NetworkManager.Singleton.SceneManager == null)
		{
			return;
		}

		NetworkSceneManager current = NetworkManager.Singleton.SceneManager;
		if (m_FallbackSubscribedSceneManager == current)
		{
			return;
		}

		if (m_FallbackSubscribedSceneManager != null)
		{
			m_FallbackSubscribedSceneManager.OnLoadEventCompleted -= HandleFallbackLoadEventCompleted;
		}

		m_FallbackSubscribedSceneManager = current;
		m_FallbackSubscribedSceneManager.OnLoadEventCompleted += HandleFallbackLoadEventCompleted;
	}

	private static void HandleFallbackLoadEventCompleted(
		string sceneName,
		LoadSceneMode loadSceneMode,
		List<ulong> clientsCompleted,
		List<ulong> clientsTimedOut)
	{
		ApplyLoadSceneTimeout(m_FallbackDefaultTimeoutSeconds);
	}
}
