using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

/// <summary>
/// InputSystemからの入力を検知し、PlayerControllerが購読するC#イベントを発火するクラス。
/// キーボードおよびゲームパッドに対応します。
/// 入力の許可・禁止もこのクラス内で管理します。
/// </summary>
public class PlayerInputReceiver : MonoBehaviour
{
	[Header("Input Actions(任意設定: 未設定時はデフォルトキーが動作)")]
	[FormerlySerializedAs("moveAction")]
	[SerializeField] private InputActionReference m_MoveAction;

	[SerializeField] private InputActionReference m_LookAction;

	[FormerlySerializedAs("aimToggleAction")]
	[SerializeField] private InputActionReference m_AimToggleAction;

	[FormerlySerializedAs("confirmAction")]
	[SerializeField] private InputActionReference m_ConfirmAction;

	[FormerlySerializedAs("brakeAction")]
	[SerializeField] private InputActionReference m_BrakeAction;

	[FormerlySerializedAs("shootAction")]
	[SerializeField] private InputActionReference m_ShootAction;

	[FormerlySerializedAs("viewResetAction")]
	[SerializeField] private InputActionReference m_ViewResetAction;

	[SerializeField] private InputActionReference m_LockOnAction;

	/// <summary>地上移動入力ベクトル(WASD/左スティック)</summary>
	public Vector2 MoveInput { get; private set; }

	/// <summary>視点操作入力ベクトル(マウスDelta/右スティック)</summary>
	public Vector2 LookInput { get; private set; }

	/// <summary>エイムモードの開始・解除ボタン押下イベント</summary>
	public event Action OnAimTogglePressed;

	/// <summary>落下方向決定(移動開始)ボタン押下イベント</summary>
	public event Action OnConfirmPressed;

	/// <summary>ブレーキ(停止)ボタン押下イベント</summary>
	public event Action OnBrakePressed;

	/// <summary>シュートボタン押下イベント。</summary>
	public event Action OnShootPressed;

	/// <summary>視点リセットボタン押下イベント</summary>
	public event Action OnViewResetPressed;

	// PlayerControllerから入力を許可されたかどうか
	private bool m_InputAllowed = false;

	// Input Actionの登録が済んでいるかどうか
	private bool m_ActionsBound = false;

	/// <summary>ロックオンボタン押下イベント。</summary>
	public event Action OnLockOnPressed;

	/// <summary>
	/// マウスカーソルのロック状態・表示状態を設定します。
	/// </summary>
	/// <param name="locked">trueの場合、カーソルを画面中央にロックして非表示にします。</param>
	public static void SetCursorLocked(bool locked)
	{
		Cursor.lockState = locked
			? CursorLockMode.Locked
			: CursorLockMode.None;

		Cursor.visible = !locked;
	}

	/// <summary>
	/// コンポーネントの有効・無効に応じて入力を管理
	/// </summary>

	private void OnEnable()
	{
		// OnEnable時点で入力許可済みなら、入力を開始する
		UpdateInputState();
	}

	private void OnDisable()
	{
		// コンポーネントが無効になったら入力を停止する
		StopInputActions();
	}

	private void OnDestroy()
	{
		// 破棄時にもイベント登録が残らないようにする
		StopInputActions();
	}

	/// <summary>
	/// PlayerControllerから入力の許可・禁止を設定します。
	/// 自分が操作するPlayerに対してtrueを指定してください。
	/// </summary>
	public void SetInputAllowed(bool allowed)
	{
		m_InputAllowed = allowed;

		UpdateInputState();
	}

	/// <summary>
	/// 入力許可状態に応じてInput Actionの登録・解除を行います。
	/// </summary>
	private void UpdateInputState()
	{
		// 無効なコンポーネントでは入力を開始しない
		if (!isActiveAndEnabled || !m_InputAllowed)
		{
			StopInputActions();
			return;
		}

		// 二重登録を防止する
		if (m_ActionsBound)
		{
			return;
		}

		StartInputActions();
	}

	/// <summary>
	/// Input Actionを有効化し、ボタンイベントを登録します。
	/// </summary>
	private void StartInputActions()
	{
		if (m_ActionsBound)
		{
			return;
		}

		// 移動・視点操作用Actionを有効化
		EnableAction(m_MoveAction);
		EnableAction(m_LookAction);

		// ボタン操作用Actionを登録
		BindAction(m_AimToggleAction, HandleAimToggle);
		BindAction(m_ConfirmAction, HandleConfirm);
		BindAction(m_BrakeAction, HandleBrake);
		BindAction(m_ShootAction, HandleShoot);
		BindAction(m_ViewResetAction, HandleViewReset);
		BindAction(m_LockOnAction, HandleLockOn);

		m_ActionsBound = true;
	}

	/// <summary>
	/// Input Actionのイベント登録を解除し、入力値をリセットします。
	/// </summary>
	private void StopInputActions()
	{
		if (m_ActionsBound)
		{
			// 登録したイベントを解除
			UnbindAction(m_AimToggleAction, HandleAimToggle);
			UnbindAction(m_ConfirmAction, HandleConfirm);
			UnbindAction(m_BrakeAction, HandleBrake);
			UnbindAction(m_ShootAction, HandleShoot);
			UnbindAction(m_ViewResetAction, HandleViewReset);
			UnbindAction(m_LockOnAction, HandleLockOn);

			// このクラスが有効化したActionを無効化
			DisableAction(m_MoveAction);
			DisableAction(m_LookAction);

			DisableAction(m_AimToggleAction);
			DisableAction(m_ConfirmAction);
			DisableAction(m_BrakeAction);
			DisableAction(m_ShootAction);
			DisableAction(m_ViewResetAction);

			m_ActionsBound = false;
		}

		// 入力が残らないようにリセット
		MoveInput = Vector2.zero;
		LookInput = Vector2.zero;
	}

	private void Update()
	{
		// 自分が操作するPlayer以外は入力処理を行わない
		if (!m_InputAllowed || !isActiveAndEnabled)
		{
			return;
		}

		UpdateMoveInput();
		UpdateLookInput();
		HandleCursorToggle();

		// InputActionReferenceが未設定の場合のキーボード/ゲームパッド標準フォールバック
		HandleKeyboardFallback();
	}

	private bool HasAction(InputActionReference actionRef)
	{
		return actionRef != null && actionRef.action != null;
	}

	private void EnableAction(InputActionReference actionRef)
	{
		if (HasAction(actionRef))
		{
			actionRef.action.Enable();
		}
	}

	private void DisableAction(InputActionReference actionRef)
	{
		if (HasAction(actionRef))
		{
			actionRef.action.Disable();
		}
	}

	private void UpdateMoveInput()
	{
		if (HasAction(m_MoveAction))
		{
			MoveInput = m_MoveAction.action.ReadValue<Vector2>();
			return;
		}

		// 未設定時のフォールバック(WASD / 矢印キー / 左スティック)
		Vector2 input = Vector2.zero;

		if (Keyboard.current != null)
		{
			if (Keyboard.current.wKey.isPressed ||
				Keyboard.current.upArrowKey.isPressed)
				input.y += 1f;

			if (Keyboard.current.sKey.isPressed ||
				Keyboard.current.downArrowKey.isPressed)
				input.y -= 1f;

			if (Keyboard.current.aKey.isPressed ||
				Keyboard.current.leftArrowKey.isPressed)
				input.x -= 1f;

			if (Keyboard.current.dKey.isPressed ||
				Keyboard.current.rightArrowKey.isPressed)
				input.x += 1f;
		}

		if (Gamepad.current != null)
		{
			Vector2 stick = Gamepad.current.leftStick.ReadValue();

			if (stick.sqrMagnitude > input.sqrMagnitude)
			{
				input = stick;
			}
		}

		MoveInput = Vector2.ClampMagnitude(input, 1f);
	}

	private void UpdateLookInput()
	{
		if (HasAction(m_LookAction))
		{
			LookInput = m_LookAction.action.ReadValue<Vector2>();
			return;
		}

		// 未設定時のフォールバック(マウス移動デルタ / 右スティック)
		Vector2 input = Vector2.zero;

		if (Cursor.lockState == CursorLockMode.Locked &&
			Mouse.current != null)
		{
			input = Mouse.current.delta.ReadValue();
		}

		if (Gamepad.current != null)
		{
			Vector2 stick = Gamepad.current.rightStick.ReadValue();

			if (stick.sqrMagnitude > input.sqrMagnitude)
			{
				input = stick;
			}
		}

		LookInput = input;
	}

	/// <summary>
	/// 開発・デバッグ用: Escapeキーでカーソルロックをトグル、
	/// 画面クリックで再ロックします。
	/// </summary>
	private void HandleCursorToggle()
	{
		if (Keyboard.current != null &&
			Keyboard.current.escapeKey.wasPressedThisFrame)
		{
			bool isLocked = Cursor.lockState == CursorLockMode.Locked;
			SetCursorLocked(!isLocked);
		}
		else if (Cursor.lockState != CursorLockMode.Locked &&
				 Mouse.current != null &&
				 Mouse.current.leftButton.wasPressedThisFrame)
		{
			SetCursorLocked(true);
		}
	}

	private void BindAction(
		InputActionReference actionRef,
		Action<InputAction.CallbackContext> callback)
	{
		if (HasAction(actionRef))
		{
			// 二重登録を防止してから登録
			actionRef.action.performed -= callback;
			actionRef.action.performed += callback;

			actionRef.action.Enable();
		}
	}

	private void UnbindAction(
		InputActionReference actionRef,
		Action<InputAction.CallbackContext> callback)
	{
		if (HasAction(actionRef))
		{
			actionRef.action.performed -= callback;
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

	private void HandleViewReset(InputAction.CallbackContext context)
	{
		OnViewResetPressed?.Invoke();
	}

	//
	private void HandleLockOn(InputAction.CallbackContext context)
	{
		OnLockOnPressed?.Invoke();
	}

	private void HandleKeyboardFallback()
	{
		if (Keyboard.current == null && Gamepad.current == null)
		{
			return;
		}

		// エイム切り替え: Shiftキー または マウス右クリック または ゲームパッドL2/LB
		if (!HasAction(m_AimToggleAction))
		{
			bool aimPressed =
				(Keyboard.current != null &&
				 (Keyboard.current.leftShiftKey.wasPressedThisFrame ||
				  Keyboard.current.rightShiftKey.wasPressedThisFrame))
				||
				(Mouse.current != null &&
				 Mouse.current.rightButton.wasPressedThisFrame)
				||
				(Gamepad.current != null &&
				 Gamepad.current.leftShoulder.wasPressedThisFrame);

			if (aimPressed)
			{
				OnAimTogglePressed?.Invoke();
			}
		}

		// 決定(落下開始): Spaceキー または マウス左クリック または ゲームパッドAボタン
		if (!HasAction(m_ConfirmAction))
		{
			bool confirmPressed =
				(Keyboard.current != null &&
				 Keyboard.current.spaceKey.wasPressedThisFrame)
				||
				(Mouse.current != null &&
				 Mouse.current.leftButton.wasPressedThisFrame)
				||
				(Gamepad.current != null &&
				 Gamepad.current.buttonSouth.wasPressedThisFrame);

			if (confirmPressed)
			{
				OnConfirmPressed?.Invoke();
			}
		}

		// ブレーキ(停止): Fキー または Sキー または ゲームパッドBボタン
		if (!HasAction(m_BrakeAction))
		{
			bool brakePressed =
				(Keyboard.current != null &&
				 (Keyboard.current.fKey.wasPressedThisFrame ||
				  Keyboard.current.sKey.wasPressedThisFrame))
				||
				(Gamepad.current != null &&
				 Gamepad.current.buttonEast.wasPressedThisFrame);

			if (brakePressed)
			{
				OnBrakePressed?.Invoke();
			}
		}

		// シュート: Eキー
		if (!HasAction(m_ShootAction) &&
			Keyboard.current != null &&
			Keyboard.current.eKey.wasPressedThisFrame)
		{
			OnShootPressed?.Invoke();
		}

		// 視点リセット: Rキー
		if (!HasAction(m_ViewResetAction) &&
			Keyboard.current != null &&
			Keyboard.current.rKey.wasPressedThisFrame)
		{
			OnViewResetPressed?.Invoke();
		}

		// ロックオン：Lキー または Qキー または ゲームパッドL1
		if (m_LockOnAction == null)
		{
			bool lockOnPressed =
				(Keyboard.current != null &&
				 (Keyboard.current.lKey.wasPressedThisFrame ||
				  Keyboard.current.qKey.wasPressedThisFrame))
				||
				(Gamepad.current != null &&
				 Gamepad.current.leftShoulder.wasPressedThisFrame);

			if (lockOnPressed)
			{
				OnLockOnPressed?.Invoke();
			}
		}
	}
}
