using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// チーム別チュートリアルシーンでの進行・同期待ち合わせ・ゲームシーン遷移を統括するクラス。
/// RedチームエリアおよびBlueチームエリアでの練習進捗を管理し、完了または制限時間経過によりGameSceneへ一括同期遷移します。
/// </summary>
public class TutorialManager : NetworkBehaviour
{
	public static TutorialManager Instance { get; private set; }

	[Header("シーン遷移先")]
	[SerializeField] private string m_GameSceneName;
	[SerializeField] private string m_LevelDesignSceneName;

	[Header("チュートリアル設定")]
	[SerializeField] private float m_TutorialTimeLimitSeconds;

	[Header("チーム別完了状態")]
	private readonly NetworkVariable<bool> m_IsRedTeamCompleted = new NetworkVariable<bool>(
		false,
		NetworkVariableReadPermission.Everyone,
		NetworkVariableWritePermission.Server);

	private readonly NetworkVariable<bool> m_IsBlueTeamCompleted = new NetworkVariable<bool>(
		false,
		NetworkVariableReadPermission.Everyone,
		NetworkVariableWritePermission.Server);

	private bool m_IsTransitioning;
	private Coroutine m_TimerCoroutine;

	public bool IsRedTeamCompleted { get => m_IsRedTeamCompleted.Value; }
	public bool IsBlueTeamCompleted { get => m_IsBlueTeamCompleted.Value; }

	private void Awake()
	{
		if (Instance != null && Instance != this)
		{
			Destroy(gameObject);
			return;
		}

		Instance = this;
	}

	public override void OnNetworkSpawn()
	{
		if (IsServer)
		{
			m_IsRedTeamCompleted.Value = false;
			m_IsBlueTeamCompleted.Value = false;

			float limit = m_TutorialTimeLimitSeconds > 0f ? m_TutorialTimeLimitSeconds : 90f;
			m_TimerCoroutine = StartCoroutine(TutorialTimerRoutine(limit));
		}
	}

	public override void OnNetworkDespawn()
	{
		if (m_TimerCoroutine != null)
		{
			StopCoroutine(m_TimerCoroutine);
			m_TimerCoroutine = null;
		}

		base.OnNetworkDespawn();
	}

	public override void OnDestroy()
	{
		if (Instance == this)
		{
			Instance = null;
		}

		base.OnDestroy();
	}

	/// <summary>
	/// 指定チームのチュートリアル完了を通知します。
	/// クライアント側から呼び出された場合はServerRpcを経由してサーバーに報告します。
	/// </summary>
	public void MarkTeamCompleted(ETeamType team)
	{
		if (team == ETeamType.None) return;

		if (IsServer)
		{
			SetTeamCompletedServer(team);
		}
		else
		{
			NotifyTeamCompletedServerRpc(team);
		}
	}

	[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
	private void NotifyTeamCompletedServerRpc(ETeamType team)
	{
		SetTeamCompletedServer(team);
	}

	private void SetTeamCompletedServer(ETeamType team)
	{
		if (!IsServer || m_IsTransitioning) return;

		if (team == ETeamType.Red)
		{
			m_IsRedTeamCompleted.Value = true;
		}
		else if (team == ETeamType.Blue)
		{
			m_IsBlueTeamCompleted.Value = true;
		}

		// 両チーム完了した場合はゲームシーンへ遷移
		if (m_IsRedTeamCompleted.Value && m_IsBlueTeamCompleted.Value)
		{
			TransitionToGameScene();
		}
	}

	private IEnumerator TutorialTimerRoutine(float limitSeconds)
	{
		yield return new WaitForSeconds(limitSeconds);

		if (!m_IsTransitioning && IsServer)
		{
			TransitionToGameScene();
		}
	}

	/// <summary>
	/// スタッフまたはホストによる手動スキップを行い、即座にゲームシーンへ遷移させます。
	/// </summary>
	public void ForceSkipToGameScene()
	{
		if (!IsServer || m_IsTransitioning) return;
		TransitionToGameScene();
	}

	private void TransitionToGameScene()
	{
		if (!IsServer || m_IsTransitioning) return;
		m_IsTransitioning = true;

		if (m_TimerCoroutine != null)
		{
			StopCoroutine(m_TimerCoroutine);
			m_TimerCoroutine = null;
		}

		string targetScene = !string.IsNullOrEmpty(m_GameSceneName) ? m_GameSceneName : "GameScene";

		if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
		{
			NetworkManager.Singleton.SceneManager.LoadScene(targetScene, LoadSceneMode.Single);
		}
	}
}

