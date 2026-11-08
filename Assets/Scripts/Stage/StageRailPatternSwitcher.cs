using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Stage
{
	/// <summary>
	/// 外周レールパターン(3種+None)および外側足場ブロック(On/Off)の組み合わせ(2x3パターン)を切り替えるコンポーネント。
	/// Inspector上でのワンクリック切り替え、F2/F3キーボードショートカット、およびマルチプレイ時のNetworkVariable同期に対応します。
	/// </summary>
	[ExecuteInEditMode]
	[AddComponentMenu("Stage/Stage Rail Pattern Switcher")]
	public sealed class StageRailPatternSwitcher : NetworkBehaviour
	{
		public enum ERailMode : byte
		{
			None = 0,
			Continuous = 1,
			Split5 = 2,
			Split4WithShuttle = 3,
		}

		[Header("対象オブジェクト参照")]
		[Tooltip("途切れ目なしレールの親ルート")]
		[SerializeField] private GameObject m_ContinuousRoot;

		[Tooltip("5分割レールの親ルート")]
		[SerializeField] private GameObject m_Split5Root;

		[Tooltip("4分割+往復キューブレールの親ルート")]
		[SerializeField] private GameObject m_Split4ShuttleRoot;

		[Tooltip("外側足場ブロック群の親ルート")]
		[SerializeField] private GameObject m_OuterFootholdsRoot;

		[Header("現在のパターン設定")]
		[Tooltip("レールの形状モード")]
		[SerializeField] private ERailMode m_CurrentRailMode = ERailMode.Continuous;

		[Tooltip("外側の足場ブロックを配置するか")]
		[SerializeField] private bool m_EnableOuterFootholds = true;

		[Header("デバッグショートカット設定")]
		[Tooltip("Play中にF2/F3キーでの切り替えを許可するか")]
		[SerializeField] private bool m_EnableKeyboardShortcut = true;

		// マルチプレイ同期用のNetworkVariable
		private readonly NetworkVariable<byte> m_NetworkRailMode = new NetworkVariable<byte>(
			(byte)ERailMode.Continuous,
			NetworkVariableReadPermission.Everyone,
			NetworkVariableWritePermission.Server);

		private readonly NetworkVariable<bool> m_NetworkOuterFootholds = new NetworkVariable<bool>(
			true,
			NetworkVariableReadPermission.Everyone,
			NetworkVariableWritePermission.Server);

		public ERailMode CurrentRailMode => m_CurrentRailMode;
		public bool EnableOuterFootholds => m_EnableOuterFootholds;

		public void SetPattern(ERailMode mode, bool outerFootholds)
		{
			m_CurrentRailMode = mode;
			m_EnableOuterFootholds = outerFootholds;

			if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer && IsSpawned)
			{
				m_NetworkRailMode.Value = (byte)mode;
				m_NetworkOuterFootholds.Value = outerFootholds;
			}

			ApplyCurrentPattern();
		}

		public void CycleRailMode()
		{
			int next = ((int)m_CurrentRailMode + 1) % 4;
			SetPattern((ERailMode)next, m_EnableOuterFootholds);
		}

		public void ToggleOuterFootholds()
		{
			SetPattern(m_CurrentRailMode, !m_EnableOuterFootholds);
		}

		private void OnEnable()
		{
			ApplyCurrentPattern();
		}

		public override void OnNetworkSpawn()
		{
			base.OnNetworkSpawn();

			if (IsServer)
			{
				m_NetworkRailMode.Value = (byte)m_CurrentRailMode;
				m_NetworkOuterFootholds.Value = m_EnableOuterFootholds;
			}
			else
			{
				m_CurrentRailMode = (ERailMode)m_NetworkRailMode.Value;
				m_EnableOuterFootholds = m_NetworkOuterFootholds.Value;
			}

			m_NetworkRailMode.OnValueChanged += HandleNetworkRailModeChanged;
			m_NetworkOuterFootholds.OnValueChanged += HandleNetworkOuterFootholdsChanged;

			ApplyCurrentPattern();
		}

		public override void OnNetworkDespawn()
		{
			m_NetworkRailMode.OnValueChanged -= HandleNetworkRailModeChanged;
			m_NetworkOuterFootholds.OnValueChanged -= HandleNetworkOuterFootholdsChanged;

			base.OnNetworkDespawn();
		}

		private void HandleNetworkRailModeChanged(byte previousValue, byte newValue)
		{
			m_CurrentRailMode = (ERailMode)newValue;
			ApplyCurrentPattern();
		}

		private void HandleNetworkOuterFootholdsChanged(bool previousValue, bool newValue)
		{
			m_EnableOuterFootholds = newValue;
			ApplyCurrentPattern();
		}

		private void Update()
		{
			if (!Application.isPlaying || !m_EnableKeyboardShortcut)
			{
				return;
			}

			if (Keyboard.current == null)
			{
				return;
			}

			// F2キー: レール形状を None -> Continuous -> Split5 -> Split4 と順繰りに切り替え
			if (Keyboard.current.f2Key.wasPressedThisFrame)
			{
				CycleRailMode();
			}

			// F3キー: 外側足場の On / Off 切り替え
			if (Keyboard.current.f3Key.wasPressedThisFrame)
			{
				ToggleOuterFootholds();
			}
		}

		/// <summary>
		/// 現在のモード設定に合わせて各ルートオブジェクトのActive状態を適用します。
		/// </summary>
		public void ApplyCurrentPattern()
		{
			if (m_ContinuousRoot != null)
			{
				m_ContinuousRoot.SetActive(m_CurrentRailMode == ERailMode.Continuous);
			}

			if (m_Split5Root != null)
			{
				m_Split5Root.SetActive(m_CurrentRailMode == ERailMode.Split5);
			}

			if (m_Split4ShuttleRoot != null)
			{
				m_Split4ShuttleRoot.SetActive(m_CurrentRailMode == ERailMode.Split4WithShuttle);
			}

			if (m_OuterFootholdsRoot != null)
			{
				m_OuterFootholdsRoot.SetActive(m_EnableOuterFootholds);
			}
		}

		public void AutoAssignRoots(StageRailRingBuilder builder)
		{
			if (builder == null)
			{
				return;
			}

			m_ContinuousRoot = builder.ContinuousRoot != null ? builder.ContinuousRoot.gameObject : null;
			m_Split5Root = builder.Split5Root != null ? builder.Split5Root.gameObject : null;
			m_Split4ShuttleRoot = builder.Split4ShuttleRoot != null ? builder.Split4ShuttleRoot.gameObject : null;
			m_OuterFootholdsRoot = builder.OuterFootholdsRoot != null ? builder.OuterFootholdsRoot.gameObject : null;

			ApplyCurrentPattern();
		}
	}
}

