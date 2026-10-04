using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

/// <summary>
/// InputSystemからの入力を検知し、PlayerControllerが購読するC#イベントを発火するクラス。
/// キーボードおよびゲームパッドに対応します。
/// </summary>
public class PlayerInputReceiver : MonoBehaviour
{
	[Header("Input Actions(任意設定: 未設定時はデフォルトキーが動作)")]
	[FormerlySerializedAs("aimToggleAction")]
	[SerializeField] private InputActionReference m_AimToggleAction;
	[FormerlySerializedAs("confirmAction")]
	[SerializeField] private InputActionReference m_ConfirmAction;
	[FormerlySerializedAs("brakeAction")]
	[SerializeField] private InputActionReference m_BrakeAction;
	[FormerlySerializedAs("shootAction")]
	[SerializeField] private InputActionReference m_ShootAction;

	/// <summary>エイムモードの開始・解除ボタン押下イベント</summary>
	public event Action OnAimTogglePressed;

	/// <summary>落下方向決定(移動開始)ボタン押下イベント</summary>
	public event Action OnConfirmPressed;

	/// <summary>ブレーキ(停止)ボタン押下イベント</summary>
	public event Action OnBrakePressed;

	/// <summary>シュートボタン押下イベント。</summary>
	public event Action OnShootPressed;

	private void OnEnable()
	{
		BindAction(m_AimToggleAction, HandleAimToggle);
		BindAction(m_ConfirmAction, HandleConfirm);
		BindAction(m_BrakeAction, HandleBrake);
		BindAction(m_ShootAction,HandleShoot);
	}

	private void OnDisable()
	{
		UnbindAction(m_AimToggleAction, HandleAimToggle);
		UnbindAction(m_ConfirmAction, HandleConfirm);
		UnbindAction(m_BrakeAction, HandleBrake);
		UnbindAction(m_ShootAction, HandleShoot);
	}

	private void Update()
	{
		// InputActionReferenceが未設定の場合のキーボード/ゲームパッド標準フォールバック
		HandleKeyboardFallback();
	}

	private void BindAction(InputActionReference actionRef, Action<InputAction.CallbackContext> callback)
	{
		if (actionRef != null && actionRef.action != null)
		{
			actionRef.action.Enable();
			actionRef.action.performed += callback;
		}
	}

	private void UnbindAction(InputActionReference actionRef, Action<InputAction.CallbackContext> callback)
	{
		if (actionRef != null && actionRef.action != null)
		{
			actionRef.action.performed -= callback;
			actionRef.action.Disable();
		}
	}

	private void HandleAimToggle(InputAction.CallbackContext context)
	{
		OnAimTogglePressed?.Invoke();
	}

	private void HandleConfirm(InputAction.CallbackContext context)
	{
		OnConfirmPressed?.Invoke();
	}

	private void HandleBrake(InputAction.CallbackContext context)
	{
		OnBrakePressed?.Invoke();
	}

	private void HandleShoot(InputAction.CallbackContext context)
	{
		OnShootPressed?.Invoke();
	}

	private void HandleKeyboardFallback()
	{
		if (Keyboard.current == null && Gamepad.current == null) return;

		// エイム切り替え: Shiftキー または マウス右クリック または ゲームパッドL2/LB
		if (m_AimToggleAction == null)
		{
			bool aimPressed = (Keyboard.current != null && (Keyboard.current.leftShiftKey.wasPressedThisFrame || Keyboard.current.rightShiftKey.wasPressedThisFrame))
				|| (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
				|| (Gamepad.current != null && Gamepad.current.leftShoulder.wasPressedThisFrame);

			if (aimPressed)
			{
				OnAimTogglePressed?.Invoke();
			}
		}

		// 決定(落下開始): Spaceキー または マウス左クリック または ゲームパッドAボタン
		if (m_ConfirmAction == null)
		{
			bool confirmPressed = (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
				|| (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
				|| (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);

			if (confirmPressed)
			{
				OnConfirmPressed?.Invoke();
			}
		}

		// ブレーキ(停止): Fキー または Sキー または ゲームパッドBボタン
		if (m_BrakeAction == null)
		{
			bool brakePressed = (Keyboard.current != null && (Keyboard.current.fKey.wasPressedThisFrame || Keyboard.current.sKey.wasPressedThisFrame))
				|| (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame);

			if (brakePressed)
			{
				OnBrakePressed?.Invoke();
			}
		}
	}
}
