using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// UI系統の操作入力(ポーズメニュー開閉・キャンセル等)を検知し、各UIコントローラーが購読するC#イベントを発火するクラス。
/// キーボードおよびゲームパッドのInputActionに対応します(フォールバックは行わずInputActionのみで動作します)。
/// </summary>
/*
 * ====================================================================================================
 * 【設計メモ: uGUI標準の InputSystemUIInputModule との役割分担およびMPPM対策について】
 *
 * 1. InputSystemUIInputModule (EventSystem側) の役割:
 *    - ボタンやスライダー等のUI要素間の上下左右フォーカス移動 (UI/Navigate: 十字キー / スティック / WASD)
 *    - 現在選択されているUI要素の決定押下 (UI/Submit: Enter / Space / ゲームパッドSouth(A)ボタン)
 *    - マウスカーソルによるポイントおよびクリック (UI/Point, UI/Click)
 *    これらはシーン内の EventSystem にアタッチされた InputSystemUIInputModule が標準で自動処理します。
 *    (※ゲームパッドで操作する際は、パネルを開いたタイミングで EventSystem.current.SetSelectedGameObject() を
 *     呼び出して初期選択ボタンを指定する必要があります)
 *
 * 2. 本クラス (UIInputReceiver) の役割:
 *    - EventSystem のフォーカス状態に依存しない「画面全体のステート遷移トリガー」を担当します。
 *      ・インゲーム中にポーズメニュー(Escメニュー)を開く/閉じる操作 (UI/Pause: Escキー / ゲームパッドStartボタン)
 *      ・開いているサブパネル(設定画面など)を1階層閉じて前のメニューへ戻る操作 (UI/Cancel: Escキー / ゲームパッドEast(B)ボタン)
 *      ・将来的にタブ切り替え(L1/R1)やUIショートカット等を追加する場合も本クラスにActionとイベントを追加します。
 *    - プレイヤー操作用の PlayerInputReceiver とは責務を分離しているため、プレイヤーオブジェクトが存在しない
 *      観戦専用ホスト端末やタイトル画面・ロビー画面でも共通して使用できます。
 *
 * 3. Multiplayer Play Mode (MPPM) での入力混同対策:
 *    - InputActionReference はプロジェクト共有の ScriptableObject アセットを参照するため、その内部の action を
 *      直接 Enable() / Disable() すると複数インスタンス間で状態が競合・混同する原因となります。
 *    - そのため、本クラスでは actionRef.action.Clone() を用いて自オブジェクト専用の InputAction インスタンスを
 *      複製して利用しています。
 * ====================================================================================================
 */
public class UIInputReceiver : MonoBehaviour
{
	[Header("UI Input Actions(InputActionReferenceを設定してください)")]
	// 想定設定値: InputSystem_Actions の UI/Pause
	[SerializeField] private InputActionReference m_PauseAction;

	// 想定設定値: InputSystem_Actions の UI/Cancel
	[SerializeField] private InputActionReference m_CancelAction;

	/// <summary>ポーズ(メニュー開閉)ボタン押下イベント</summary>
	public event Action OnPausePressed;

	/// <summary>キャンセル(1階層戻る)ボタン押下イベント</summary>
	public event Action OnCancelPressed;

	// Cloneによって生成した自オブジェクト専用のInputActionインスタンス
	private InputAction m_RuntimePauseAction;
	private InputAction m_RuntimeCancelAction;

	private bool m_InputAllowed = true;
	private bool m_ActionsBound = false;

	private void OnEnable()
	{
		UpdateInputState();
	}

	private void OnDisable()
	{
		StopInputActions();
	}

	private void OnDestroy()
	{
		StopInputActions();
		DisposeRuntimeActions();
	}

	/// <summary>
	/// UI入力の許可・禁止を設定します。
	/// </summary>
	/// <param name="allowed">trueの場合、UI入力検知を有効化します。</param>
	public void SetInputAllowed(bool allowed)
	{
		m_InputAllowed = allowed;
		UpdateInputState();
	}

	private void UpdateInputState()
	{
		if (!isActiveAndEnabled || !m_InputAllowed)
		{
			StopInputActions();
			return;
		}

		if (m_ActionsBound)
		{
			return;
		}

		StartInputActions();
	}

	private void StartInputActions()
	{
		if (m_ActionsBound)
		{
			return;
		}

		if (m_RuntimePauseAction == null && m_PauseAction != null && m_PauseAction.action != null)
		{
			m_RuntimePauseAction = m_PauseAction.action.Clone();
		}

		if (m_RuntimeCancelAction == null && m_CancelAction != null && m_CancelAction.action != null)
		{
			m_RuntimeCancelAction = m_CancelAction.action.Clone();
		}

		BindRuntimeAction(m_RuntimePauseAction, HandlePause);
		BindRuntimeAction(m_RuntimeCancelAction, HandleCancel);

		m_ActionsBound = true;
	}

	private void StopInputActions()
	{
		if (!m_ActionsBound)
		{
			return;
		}

		UnbindRuntimeAction(m_RuntimePauseAction, HandlePause);
		UnbindRuntimeAction(m_RuntimeCancelAction, HandleCancel);

		m_ActionsBound = false;
	}

	private void DisposeRuntimeActions()
	{
		if (m_RuntimePauseAction != null)
		{
			m_RuntimePauseAction.Dispose();
			m_RuntimePauseAction = null;
		}

		if (m_RuntimeCancelAction != null)
		{
			m_RuntimeCancelAction.Dispose();
			m_RuntimeCancelAction = null;
		}
	}

	private void BindRuntimeAction(InputAction action, Action<InputAction.CallbackContext> callback)
	{
		if (action == null)
		{
			return;
		}

		action.performed -= callback;
		action.performed += callback;
		action.Enable();
	}

	private void UnbindRuntimeAction(InputAction action, Action<InputAction.CallbackContext> callback)
	{
		if (action == null)
		{
			return;
		}

		action.performed -= callback;
		action.Disable();
	}

	private void HandlePause(InputAction.CallbackContext context)
	{
		OnPausePressed?.Invoke();
	}

	private void HandleCancel(InputAction.CallbackContext context)
	{
		OnCancelPressed?.Invoke();
	}
}

