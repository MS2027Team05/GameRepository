using System;
using System.Collections.Generic;
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

	[Header("新アクションフロー用 Input Actions(R1移動 / L1干渉アクション)")]
	[SerializeField] private InputActionReference m_MoveTriggerAction;
	[SerializeField] private InputActionReference m_SubAction;

	[Header("視点感度設定(マウス / ゲームパッド基準補正)")]
	[Tooltip("マウス入力時の基準倍率(0以下時は既定値0.5)")]
	[SerializeField] private float m_MouseBaseLookScale;
	[Tooltip("ゲームパッド入力時の基準倍率(60fps換算、0以下時は既定値10.0)")]
	[SerializeField] private float m_PadBaseLookScale;

	private const float m_DefaultMouseBaseLookScale = 0.5f;
	private const float m_DefaultPadBaseLookScale = 10.0f;
	private const float m_DeviceDetectThresholdSq = 0.0001f;

	/// <summary>地上移動・空中着地点シフト入力ベクトル(WASD/左スティック)</summary>
	public Vector2 MoveInput { get; private set; }

	/// <summary>視点操作入力ベクトル(マウスDelta/右スティック)</summary>
	public Vector2 LookInput { get; private set; }

	/// <summary>直近の操作入力がゲームパッドから行われたかどうか</summary>
	public bool IsUsingGamepad { get; private set; }

	/// <summary>エイムモードの開始・解除ボタン押下イベント(旧フロー互換用)</summary>
	public event Action OnAimTogglePressed;

	/// <summary>落下方向決定(移動開始)ボタン押下イベント(旧フロー互換用)</summary>
	public event Action OnConfirmPressed;

	/// <summary>ブレーキ(停止)ボタン押下イベント</summary>
	public event Action OnBrakePressed;

	/// <summary>シュートボタン押下イベント(旧フロー互換用)</summary>
	public event Action OnShootPressed;

	/// <summary>視点リセットボタン押下イベント</summary>
	public event Action OnViewResetPressed;

	/// <summary>ロックオンボタン押下イベント(旧フロー互換用)</summary>
	public event Action OnLockOnPressed;

	/// <summary>新フロー: 移動アクション(マウス右クリック / ゲームパッドR1)押下イベント</summary>
	public event Action OnMoveActionPressed;

	/// <summary>
	/// 新フロー: 移動アクション(マウス右クリック / ゲームパッドR1)離しイベント
	/// </summary>
	public event Action OnMoveActionReleased;

	/// <summary>新フロー: 干渉サブアクション(マウス左クリック / ゲームパッドL1)押下開始イベント</summary>
	public event Action OnSubActionStarted;

	/// <summary>新フロー: 干渉サブアクション(マウス左クリック / ゲームパッドL1)離しイベント</summary>
	public event Action OnSubActionReleased;

	private static readonly HashSet<PlayerInputReceiver> m_ActiveReceivers = new HashSet<PlayerInputReceiver>();
	private static bool m_IsGlobalPaused = false;

	// PlayerControllerから入力を許可されたかどうか
	private bool m_InputAllowed = false;

	// 待機中などに視点回転のみを許可されたかどうか
	private bool m_LookInputAllowed = false;

	// Input Actionの登録が済んでいるかどうか
	private bool m_ActionsBound = false;

	// 視点操作用Actionの有効化が済んでいるかどうか
	private bool m_LookActionsBound = false;

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
	/// ポーズメニュー開閉時などに全プレイヤー入力の一時停止・再開を一括設定します。
	/// </summary>
	/// <param name="paused">trueの場合、プレイヤーの操作入力を一時停止します。</param>
	public static void SetAllPaused(bool paused)
	{
		m_IsGlobalPaused = paused;

		foreach (PlayerInputReceiver receiver in m_ActiveReceivers)
		{
			if (receiver != null)
			{
				receiver.UpdateInputState();
			}
		}
	}

	/// <summary>
	/// コンポーネントの有効・無効に応じて入力を管理
	/// </summary>
	private void OnEnable()
	{
		m_ActiveReceivers.Add(this);

		// OnEnable時点で入力許可済みなら、入力を開始する
		UpdateInputState();
	}

	private void OnDisable()
	{
		m_ActiveReceivers.Remove(this);

		// コンポーネントが無効になったら入力を停止する
		StopInputActions();
	}

	private void OnDestroy()
	{
		m_ActiveReceivers.Remove(this);

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
	/// 待機中など行動制限時でも視点操作(カメラ回転)の入力受付を切り替えます。
	/// </summary>
	/// <param name="allowed">視点入力を許可する場合はtrue</param>
	public void SetLookInputAllowed(bool allowed)
	{
		m_LookInputAllowed = allowed;

		UpdateInputState();
	}

	/// <summary>
	/// 入力許可状態に応じてInput Actionの登録・解除を行います。
	/// </summary>
	private void UpdateInputState()
	{
		bool canAcceptLook = isActiveAndEnabled && !m_IsGlobalPaused && (m_InputAllowed || m_LookInputAllowed);
		UpdateLookActionState(canAcceptLook);

		// 無効なコンポーネントまたはポーズ中は行動入力を開始しない
		if (!isActiveAndEnabled || !m_InputAllowed || m_IsGlobalPaused)
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

	private void UpdateLookActionState(bool shouldEnable)
	{
		if (shouldEnable)
		{
			if (!m_LookActionsBound)
			{
				EnableAction(m_LookAction);
				m_LookActionsBound = true;
			}
			return;
		}

		if (m_LookActionsBound)
		{
			DisableAction(m_LookAction);
			m_LookActionsBound = false;
		}

		LookInput = Vector2.zero;
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

		// 移動用Actionを有効化
		EnableAction(m_MoveAction);

		// 旧フロー用ボタン操作Actionを登録
		BindAction(m_AimToggleAction, HandleAimToggle);
		BindAction(m_ConfirmAction, HandleConfirm);
		BindAction(m_BrakeAction, HandleBrake);
		BindAction(m_ShootAction, HandleShoot);
		BindAction(m_ViewResetAction, HandleViewReset);
		BindAction(m_LockOnAction, HandleLockOn);

		// 新フロー用ボタン操作Actionを登録
		BindPressReleaseAction(
			m_MoveTriggerAction,
			HandleMoveTrigger,
			HandleMoveTriggerCanceled
		);
		BindPressReleaseAction(m_SubAction, HandleSubActionStarted, HandleSubActionCanceled);

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
			UnbindPressReleaseAction(
				m_MoveTriggerAction,
				HandleMoveTrigger,
				HandleMoveTriggerCanceled
			);
			UnbindPressReleaseAction(m_SubAction, HandleSubActionStarted, HandleSubActionCanceled);

			// このクラスが有効化したActionを無効化
			DisableAction(m_MoveAction);

			DisableAction(m_AimToggleAction);
			DisableAction(m_ConfirmAction);
			DisableAction(m_BrakeAction);
			DisableAction(m_ShootAction);
			DisableAction(m_ViewResetAction);
			DisableAction(m_LockOnAction);
			DisableAction(m_MoveTriggerAction);
			DisableAction(m_SubAction);

			m_ActionsBound = false;
		}

		// 入力が残らないようにリセット
		MoveInput = Vector2.zero;
	}

	private void Update()
	{
		if (Gamepad.current == null)
		{
			IsUsingGamepad = false;
		}

		// 無効状態またはポーズ中は入力処理を行わない
		if (!isActiveAndEnabled || m_IsGlobalPaused)
		{
			return;
		}

		if (m_InputAllowed || m_LookInputAllowed)
		{
			UpdateLookInput();
		}

		// 自分が操作するPlayerの行動入力が許可されていない場合は移動・アクション入力を行わない
		if (!m_InputAllowed)
		{
			return;
		}

		UpdateMoveInput();

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
			Vector2 move = m_MoveAction.action.ReadValue<Vector2>();
			if (move.sqrMagnitude > m_DeviceDetectThresholdSq)
			{
				UpdateActiveDeviceFromControl(m_MoveAction.action.activeControl);
			}

			MoveInput = move;
			return;
		}

		// 未設定時のフォールバック(WASD / 矢印キー / 左スティック)
		Vector2 input = Vector2.zero;

		if (Keyboard.current != null)
		{
			if (Keyboard.current.wKey.isPressed ||
				Keyboard.current.upArrowKey.isPressed)
			{
				input.y += 1f;
			}

			if (Keyboard.current.sKey.isPressed ||
				Keyboard.current.downArrowKey.isPressed)
			{
				input.y -= 1f;
			}

			if (Keyboard.current.aKey.isPressed ||
				Keyboard.current.leftArrowKey.isPressed)
			{
				input.x -= 1f;
			}

			if (Keyboard.current.dKey.isPressed ||
				Keyboard.current.rightArrowKey.isPressed)
			{
				input.x += 1f;
			}
		}

		bool hasKeyboardMove = input.sqrMagnitude > m_DeviceDetectThresholdSq;
		bool hasPadMove = false;

		if (Gamepad.current != null)
		{
			Vector2 stick = Gamepad.current.leftStick.ReadValue();

			if (stick.sqrMagnitude > m_DeviceDetectThresholdSq &&
				stick.sqrMagnitude > input.sqrMagnitude)
			{
				input = stick;
				hasPadMove = true;
			}
		}

		if (hasPadMove)
		{
			IsUsingGamepad = true;
		}
		else if (hasKeyboardMove)
		{
			IsUsingGamepad = false;
		}

		MoveInput = Vector2.ClampMagnitude(input, 1f);
	}

	private void UpdateLookInput()
	{
		if (HasAction(m_LookAction))
		{
			Vector2 raw = m_LookAction.action.ReadValue<Vector2>();
			InputControl activeControl = m_LookAction.action.activeControl;
			bool isGamepad = activeControl != null && activeControl.device is Gamepad;
			if (raw.sqrMagnitude > m_DeviceDetectThresholdSq)
			{
				IsUsingGamepad = isGamepad;
			}

			LookInput = ApplyLookSensitivity(raw, isGamepad);
			return;
		}

		// 未設定時のフォールバック(マウス移動デルタ / 右スティック)
		Vector2 rawMouse = Vector2.zero;
		Vector2 mouseInput = Vector2.zero;
		if (Cursor.lockState == CursorLockMode.Locked &&
			Mouse.current != null)
		{
			rawMouse = Mouse.current.delta.ReadValue();
			mouseInput = ApplyLookSensitivity(rawMouse, isGamepad: false);
		}

		Vector2 rawPad = Vector2.zero;
		Vector2 padInput = Vector2.zero;
		if (Gamepad.current != null)
		{
			rawPad = Gamepad.current.rightStick.ReadValue();
			padInput = ApplyLookSensitivity(rawPad, isGamepad: true);
		}

		bool usePad = padInput.sqrMagnitude > mouseInput.sqrMagnitude;
		if (usePad && rawPad.sqrMagnitude > m_DeviceDetectThresholdSq)
		{
			IsUsingGamepad = true;
		}
		else if (!usePad && rawMouse.sqrMagnitude > m_DeviceDetectThresholdSq)
		{
			IsUsingGamepad = false;
		}

		LookInput = usePad ? padInput : mouseInput;
	}

	private Vector2 ApplyLookSensitivity(Vector2 rawInput, bool isGamepad)
	{
		if (rawInput.sqrMagnitude <= Mathf.Epsilon)
		{
			return Vector2.zero;
		}

		float userSensitivity = LocalPlayerProfilePreferences.GetLookSensitivity();
		if (isGamepad)
		{
			float padScale = m_PadBaseLookScale > 0.0f ? m_PadBaseLookScale : m_DefaultPadBaseLookScale;
			float frameScale = Time.deltaTime * 60.0f;
			return rawInput * (padScale * frameScale * userSensitivity);
		}

		float mouseScale = m_MouseBaseLookScale > 0.0f ? m_MouseBaseLookScale : m_DefaultMouseBaseLookScale;
		return rawInput * (mouseScale * userSensitivity);
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

	private void BindPressReleaseAction(
		InputActionReference actionRef,
		Action<InputAction.CallbackContext> startedCallback,
		Action<InputAction.CallbackContext> canceledCallback)
	{
		if (HasAction(actionRef))
		{
			actionRef.action.started -= startedCallback;
			actionRef.action.started += startedCallback;
			actionRef.action.canceled -= canceledCallback;
			actionRef.action.canceled += canceledCallback;

			actionRef.action.Enable();
		}
	}

	private void UnbindPressReleaseAction(
		InputActionReference actionRef,
		Action<InputAction.CallbackContext> startedCallback,
		Action<InputAction.CallbackContext> canceledCallback)
	{
		if (HasAction(actionRef))
		{
			actionRef.action.started -= startedCallback;
			actionRef.action.canceled -= canceledCallback;
		}
	}

	private void UpdateActiveDeviceFromContext(InputAction.CallbackContext context)
	{
		UpdateActiveDeviceFromControl(context.control);
	}

	private void UpdateActiveDeviceFromControl(InputControl control)
	{
		if (control == null)
		{
			return;
		}

		IsUsingGamepad = control.device is Gamepad;
	}

	private void HandleAimToggle(InputAction.CallbackContext context)
	{
		UpdateActiveDeviceFromContext(context);
		OnAimTogglePressed?.Invoke();
	}

	private void HandleConfirm(InputAction.CallbackContext context)
	{
		UpdateActiveDeviceFromContext(context);
		OnConfirmPressed?.Invoke();
	}

	private void HandleBrake(InputAction.CallbackContext context)
	{
		UpdateActiveDeviceFromContext(context);
		OnBrakePressed?.Invoke();
	}

	private void HandleShoot(InputAction.CallbackContext context)
	{
		UpdateActiveDeviceFromContext(context);
		OnShootPressed?.Invoke();
	}

	private void HandleViewReset(InputAction.CallbackContext context)
	{
		UpdateActiveDeviceFromContext(context);
		OnViewResetPressed?.Invoke();
	}

	private void HandleLockOn(InputAction.CallbackContext context)
	{
		UpdateActiveDeviceFromContext(context);
		OnLockOnPressed?.Invoke();
	}

	private void HandleMoveTrigger(InputAction.CallbackContext context)
	{
		UpdateActiveDeviceFromContext(context);
		OnMoveActionPressed?.Invoke();
	}

	/// <summary>
	/// 新フロー: 移動アクションのボタン解放を通知します。
	/// </summary>
	private void HandleMoveTriggerCanceled(InputAction.CallbackContext context)
	{
		UpdateActiveDeviceFromContext(context);
		OnMoveActionReleased?.Invoke();
	}

	private void HandleSubActionStarted(InputAction.CallbackContext context)
	{
		UpdateActiveDeviceFromContext(context);
		OnSubActionStarted?.Invoke();
	}

	private void HandleSubActionCanceled(InputAction.CallbackContext context)
	{
		UpdateActiveDeviceFromContext(context);
		OnSubActionReleased?.Invoke();
	}

	private void HandleKeyboardFallback()
	{
		if (Keyboard.current == null && Mouse.current == null && Gamepad.current == null)
		{
			return;
		}

		// 新フロー移動アクション:
		// マウス右クリック または ゲームパッドR1
		if (!HasAction(m_MoveTriggerAction))
		{
			bool mouseMovePressed =
				Mouse.current != null &&
				Mouse.current.rightButton.wasPressedThisFrame;
			bool padMovePressed =
				Gamepad.current != null &&
				Gamepad.current.rightShoulder.wasPressedThisFrame;

			if (padMovePressed)
			{
				IsUsingGamepad = true;
			}
			else if (mouseMovePressed)
			{
				IsUsingGamepad = false;
			}

			if (mouseMovePressed || padMovePressed)
			{
				OnMoveActionPressed?.Invoke();
			}

			bool mouseMoveReleased =
				Mouse.current != null &&
				Mouse.current.rightButton.wasReleasedThisFrame;
			bool padMoveReleased =
				Gamepad.current != null &&
				Gamepad.current.rightShoulder.wasReleasedThisFrame;

			if (padMoveReleased)
			{
				IsUsingGamepad = true;
			}
			else if (mouseMoveReleased)
			{
				IsUsingGamepad = false;
			}

			if (mouseMoveReleased || padMoveReleased)
			{
				OnMoveActionReleased?.Invoke();
			}
		}

		// 新フロー干渉サブアクション(押下開始・離し): マウス左クリック または ゲームパッドL1
		if (!HasAction(m_SubAction))
		{
			bool mouseSubStarted =
				Mouse.current != null &&
				Mouse.current.leftButton.wasPressedThisFrame;
			bool padSubStarted =
				Gamepad.current != null &&
				Gamepad.current.leftShoulder.wasPressedThisFrame;

			if (padSubStarted)
			{
				IsUsingGamepad = true;
			}
			else if (mouseSubStarted)
			{
				IsUsingGamepad = false;
			}

			if (mouseSubStarted || padSubStarted)
			{
				OnSubActionStarted?.Invoke();
			}

			bool mouseSubReleased =
				Mouse.current != null &&
				Mouse.current.leftButton.wasReleasedThisFrame;
			bool padSubReleased =
				Gamepad.current != null &&
				Gamepad.current.leftShoulder.wasReleasedThisFrame;

			if (padSubReleased)
			{
				IsUsingGamepad = true;
			}
			else if (mouseSubReleased)
			{
				IsUsingGamepad = false;
			}

			if (mouseSubReleased || padSubReleased)
			{
				OnSubActionReleased?.Invoke();
			}
		}

		// 旧フロー エイム切り替え: Shiftキー または マウス右クリック または ゲームパッドL1
		if (!HasAction(m_AimToggleAction))
		{
			bool keyMouseAimPressed =
				(Keyboard.current != null &&
					(Keyboard.current.leftShiftKey.wasPressedThisFrame ||
					Keyboard.current.rightShiftKey.wasPressedThisFrame))
				||
				(Mouse.current != null &&
					Mouse.current.rightButton.wasPressedThisFrame);
			bool padAimPressed =
				Gamepad.current != null &&
				Gamepad.current.leftShoulder.wasPressedThisFrame;

			if (padAimPressed)
			{
				IsUsingGamepad = true;
			}
			else if (keyMouseAimPressed)
			{
				IsUsingGamepad = false;
			}

			if (keyMouseAimPressed || padAimPressed)
			{
				OnAimTogglePressed?.Invoke();
			}
		}

		// 旧フロー 決定(落下開始): Spaceキー または マウス左クリック または ゲームパッドAボタン
		if (!HasAction(m_ConfirmAction))
		{
			bool keyMouseConfirmPressed =
				(Keyboard.current != null &&
				 Keyboard.current.spaceKey.wasPressedThisFrame)
				||
				(Mouse.current != null &&
				 Mouse.current.leftButton.wasPressedThisFrame);
			bool padConfirmPressed =
				Gamepad.current != null &&
				Gamepad.current.buttonSouth.wasPressedThisFrame;

			if (padConfirmPressed)
			{
				IsUsingGamepad = true;
			}
			else if (keyMouseConfirmPressed)
			{
				IsUsingGamepad = false;
			}

			if (keyMouseConfirmPressed || padConfirmPressed)
			{
				OnConfirmPressed?.Invoke();
			}
		}

		// ブレーキ(停止): Fキー または Sキー または ゲームパッドBボタン(旧フロー用フォールバック)
		if (!HasAction(m_BrakeAction))
		{
			bool keyBrakePressed =
				Keyboard.current != null &&
				(Keyboard.current.fKey.wasPressedThisFrame ||
				 Keyboard.current.sKey.wasPressedThisFrame);
			bool padBrakePressed =
				Gamepad.current != null &&
				Gamepad.current.buttonEast.wasPressedThisFrame;

			if (padBrakePressed)
			{
				IsUsingGamepad = true;
			}
			else if (keyBrakePressed)
			{
				IsUsingGamepad = false;
			}

			if (keyBrakePressed || padBrakePressed)
			{
				OnBrakePressed?.Invoke();
			}
		}

		// シュート: Eキー(旧フロー用フォールバック)
		if (!HasAction(m_ShootAction) &&
			Keyboard.current != null &&
			Keyboard.current.eKey.wasPressedThisFrame)
		{
			IsUsingGamepad = false;
			OnShootPressed?.Invoke();
		}

		// 視点リセット: Rキー(旧フロー用フォールバック)
		if (!HasAction(m_ViewResetAction) &&
			Keyboard.current != null &&
			Keyboard.current.rKey.wasPressedThisFrame)
		{
			IsUsingGamepad = false;
			OnViewResetPressed?.Invoke();
		}

		// ロックオン: Lキー または Qキー または ゲームパッドL1(旧フロー用フォールバック)
		if (!HasAction(m_LockOnAction))
		{
			bool keyLockOnPressed =
				Keyboard.current != null &&
				(Keyboard.current.lKey.wasPressedThisFrame ||
				 Keyboard.current.qKey.wasPressedThisFrame);
			bool padLockOnPressed =
				Gamepad.current != null &&
				Gamepad.current.leftShoulder.wasPressedThisFrame;

			if (padLockOnPressed)
			{
				IsUsingGamepad = true;
			}
			else if (keyLockOnPressed)
			{
				IsUsingGamepad = false;
			}

			if (keyLockOnPressed || padLockOnPressed)
			{
				OnLockOnPressed?.Invoke();
			}
		}
	}
}
