using TMPro;
using UnityEngine;

/// <summary>
/// 操作デバイス(キーボード+マウス / ゲームパッド)と行動状態に応じた操作補助UIを更新します。
/// </summary>
public class PlayerActionHelpUI : MonoBehaviour
{
	[Header("表示対象")]
	[Tooltip("操作案内を表示するTextMeshProテキストです。")]
	[SerializeField] private TMP_Text m_HelpText;

	[Header("表示タイミング")]
	[Tooltip("ONの場合はサブアクション長押し中のみ表示し、OFFの場合は行動可能時に常時表示します。")]
	[SerializeField] private bool m_HideWhenNotHoldingSubAction;

	private enum EHelpState
	{
		None,
		IdleNoBall,
		IdleWithBall,
		HoldElectricCharging,
		HoldElectricReady,
		HoldBallThrowOrCharge,
		HoldBallHomingReady
	}

	private const string m_IdleNoBallPadText =
		"L2長押し: 奪取・スタン\nR2: 移動";
	private const string m_IdleNoBallMouseText =
		"左クリック長押し: 奪取・スタン\n右クリック: 移動";

	private const string m_IdleWithBallPadText =
		"L2: 投げる / 長押し: 追尾パス\nR2: 移動";
	private const string m_IdleWithBallMouseText =
		"左クリック: 投げる / 長押し: 追尾パス\n右クリック: 移動";

	private const string m_HoldElectricChargingPadText =
		"たまるとボールを奪取 / 敵をスタン\nR2でキャンセル";
	private const string m_HoldElectricChargingMouseText =
		"たまるとボールを奪取 / 敵をスタン\n右クリックでキャンセル";

	private const string m_HoldElectricReadyPadText =
		"L2を離すとボールを奪取 / 敵をスタン\nR2でキャンセル";
	private const string m_HoldElectricReadyMouseText =
		"離すとボールを奪取 / 敵をスタン\n右クリックでキャンセル";

	private const string m_HoldBallThrowPadText =
		"離すとボールを投げる\nたまると追尾パス\nR2でキャンセル";
	private const string m_HoldBallThrowMouseText =
		"離すとボールを投げる\nたまると追尾パス\n右クリックでキャンセル";

	private const string m_HoldBallHomingReadyPadText =
		"離すと追尾パス\nR2でキャンセル";
	private const string m_HoldBallHomingReadyMouseText =
		"離すと追尾パス\n右クリックでキャンセル";

	private bool m_IsInitialized;
	private bool m_LastVisible;
	private EHelpState m_LastState;
	private bool m_LastIsUsingGamepad;

	/// <summary>
	/// 現在の操作状態と入力デバイスに応じて補助UIの表示有無・文言を更新します。
	/// </summary>
	public void UpdateHelpState(
		bool canShow,
		bool isHoldingSubAction,
		bool hasBall,
		bool isPassChargeFull,
		bool isAllyPassTarget,
		bool isChargingElectric,
		bool isElectricReady,
		bool isUsingGamepad)
	{
		if (m_HelpText == null)
		{
			return;
		}

		bool shouldShow = canShow && (!m_HideWhenNotHoldingSubAction || isHoldingSubAction);
		if (!shouldShow)
		{
			Hide();
			return;
		}

		EHelpState nextState = ResolveHelpState(
			isHoldingSubAction,
			hasBall,
			isPassChargeFull,
			isAllyPassTarget,
			isChargingElectric,
			isElectricReady);

		if (m_IsInitialized &&
			m_LastVisible &&
			m_LastState == nextState &&
			m_LastIsUsingGamepad == isUsingGamepad)
		{
			return;
		}

		m_IsInitialized = true;
		m_LastVisible = true;
		m_LastState = nextState;
		m_LastIsUsingGamepad = isUsingGamepad;

		if (!m_HelpText.gameObject.activeSelf)
		{
			m_HelpText.gameObject.SetActive(true);
		}

		m_HelpText.text = ResolveText(nextState, isUsingGamepad);
	}

	/// <summary>
	/// 操作補助UIを非表示にします。
	/// </summary>
	public void Hide()
	{
		if (m_HelpText == null)
		{
			return;
		}

		if (!m_IsInitialized || m_LastVisible || m_HelpText.gameObject.activeSelf)
		{
			m_HelpText.gameObject.SetActive(false);
		}

		m_IsInitialized = true;
		m_LastVisible = false;
		m_LastState = EHelpState.None;
	}

	private static EHelpState ResolveHelpState(
		bool isHoldingSubAction,
		bool hasBall,
		bool isPassChargeFull,
		bool isAllyPassTarget,
		bool isChargingElectric,
		bool isElectricReady)
	{
		if (!isHoldingSubAction)
		{
			return hasBall ? EHelpState.IdleWithBall : EHelpState.IdleNoBall;
		}

		if (hasBall)
		{
			return (isPassChargeFull && isAllyPassTarget)
				? EHelpState.HoldBallHomingReady
				: EHelpState.HoldBallThrowOrCharge;
		}

		return (isChargingElectric && isElectricReady)
			? EHelpState.HoldElectricReady
			: EHelpState.HoldElectricCharging;
	}

	private static string ResolveText(EHelpState state, bool isUsingGamepad)
	{
		switch (state)
		{
			case EHelpState.IdleNoBall:
				return isUsingGamepad ? m_IdleNoBallPadText : m_IdleNoBallMouseText;
			case EHelpState.IdleWithBall:
				return isUsingGamepad ? m_IdleWithBallPadText : m_IdleWithBallMouseText;
			case EHelpState.HoldElectricCharging:
				return isUsingGamepad ? m_HoldElectricChargingPadText : m_HoldElectricChargingMouseText;
			case EHelpState.HoldElectricReady:
				return isUsingGamepad ? m_HoldElectricReadyPadText : m_HoldElectricReadyMouseText;
			case EHelpState.HoldBallThrowOrCharge:
				return isUsingGamepad ? m_HoldBallThrowPadText : m_HoldBallThrowMouseText;
			case EHelpState.HoldBallHomingReady:
				return isUsingGamepad ? m_HoldBallHomingReadyPadText : m_HoldBallHomingReadyMouseText;
			default:
				return string.Empty;
		}
	}
}
