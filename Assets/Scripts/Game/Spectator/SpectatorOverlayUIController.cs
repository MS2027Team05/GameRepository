using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 観戦カメラ画面上にカメラ種別やボール保持チームなどの戦況テロップを表示する中継UIコントローラー。
/// </summary>
public class SpectatorOverlayUIController : MonoBehaviour
{
	[Header("テロップUI参照")]
	[SerializeField] private TMP_Text m_CameraShotText;
	[SerializeField] private TMP_Text m_PossessionText;

	[Header("ディレクター参照(未設定時は自動検索)")]
	[SerializeField] private SpectatorCameraDirector m_CameraDirector;

	private BallController m_CachedBall;

	private void Awake()
	{
		if (m_CameraDirector == null)
		{
			m_CameraDirector = FindFirstObjectByType<SpectatorCameraDirector>();
		}
	}

	private void OnEnable()
	{
		if (m_CameraDirector != null)
		{
			m_CameraDirector.OnCameraShotChanged += HandleCameraShotChanged;
			UpdateCameraShotText(m_CameraDirector.CurrentShotName);
		}
	}

	private void OnDisable()
	{
		if (m_CameraDirector != null)
		{
			m_CameraDirector.OnCameraShotChanged -= HandleCameraShotChanged;
		}
	}

	private void Update()
	{
		UpdatePossessionDisplay();
	}

	private void HandleCameraShotChanged(SpectatorCameraDirector.ECameraShotType shotType, string shotName)
	{
		UpdateCameraShotText(shotName);
	}

	private void UpdateCameraShotText(string shotName)
	{
		if (m_CameraShotText != null)
		{
			m_CameraShotText.text = $"• {shotName}";
		}
	}

	private void UpdatePossessionDisplay()
	{
		if (m_PossessionText == null) return;

		if (m_CachedBall == null)
		{
			m_CachedBall = FindFirstObjectByType<BallController>();
		}

		if (m_CachedBall == null || !m_CachedBall.HasHolder)
		{
			m_PossessionText.text = "<color=#CCCCCC>FREE BALL</color>";
			return;
		}

		ulong holderId = m_CachedBall.CurrentHolderId;
		if (NetworkManager.Singleton != null && NetworkManager.Singleton.ConnectedClients.TryGetValue(holderId, out var client) && client.PlayerObject != null)
		{
			var profile = client.PlayerObject.GetComponent<PlayerNetworkProfile>();
			if (profile != null)
			{
				string colorTag = profile.Team == ETeamType.Red ? "#FF5555" : (profile.Team == ETeamType.Blue ? "#5588FF" : "#FFFFFF");
				string teamName = profile.Team == ETeamType.Red ? "RED" : (profile.Team == ETeamType.Blue ? "BLUE" : "UNKNOWN");
				m_PossessionText.text = $"<color={colorTag}>{teamName} BALL ({profile.PlayerName})</color>";
				return;
			}
		}

		m_PossessionText.text = "<color=#FFFF55>BALL HELD</color>";
	}
}
