using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// InputSystemからの入力を検知し、PlayerControllerが購読するC#イベントを発火するクラス。
/// キーボードおよびゲームパッドに対応します。
/// </summary>
public class PlayerInputReceiver : MonoBehaviour
{
    [Header("Input Actions(任意設定: 未設定時はデフォルトキーが動作)")]
    [SerializeField] private InputActionReference aimToggleAction;
    [SerializeField] private InputActionReference confirmAction;
    [SerializeField] private InputActionReference brakeAction;

    /// <summary>エイムモードの開始・解除ボタン押下イベント</summary>
    public event Action OnAimTogglePressed;

    /// <summary>落下方向決定(移動開始)ボタン押下イベント</summary>
    public event Action OnConfirmPressed;

    /// <summary>ブレーキ(停止)ボタン押下イベント</summary>
    public event Action OnBrakePressed;

    private void OnEnable()
    {
        BindAction(aimToggleAction, HandleAimToggle);
        BindAction(confirmAction, HandleConfirm);
        BindAction(brakeAction, HandleBrake);
    }

    private void OnDisable()
    {
        UnbindAction(aimToggleAction, HandleAimToggle);
        UnbindAction(confirmAction, HandleConfirm);
        UnbindAction(brakeAction, HandleBrake);
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

    private void HandleKeyboardFallback()
    {
        if (Keyboard.current == null && Gamepad.current == null) return;

        // エイム切り替え: Shiftキー または マウス右クリック または ゲームパッドL2/LB
        if (aimToggleAction == null)
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
        if (confirmAction == null)
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
        if (brakeAction == null)
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
