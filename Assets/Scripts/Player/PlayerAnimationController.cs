using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 地上移動状態を他端末へ同期し、移動状態とボール所持状態をAnimatorへ反映するコンポーネント。
/// </summary>
public sealed class PlayerAnimationController : NetworkBehaviour
{
	private static readonly int m_IsMovingHash = Animator.StringToHash("IsMoving");
	private static readonly int m_HasBallHash = Animator.StringToHash("HasBall");

	private IBallCarrier m_BallCarrier;

	[Header("参照")]
	[SerializeField] private Animator[] m_Animators;
	[SerializeField] private PlayerSurfaceMover m_SurfaceMover;
	[SerializeField] private PlayerController m_PlayerController;

	[Header("移動判定")]
	[SerializeField] private float m_MoveSpeedThreshold;

	private readonly NetworkVariable<bool> m_IsMoving = new NetworkVariable<bool>(
		false,
		NetworkVariableReadPermission.Everyone,
		NetworkVariableWritePermission.Owner);
	private readonly NetworkVariable<uint> m_RespawnVersion = new NetworkVariable<uint>(
		0,
		NetworkVariableReadPermission.Everyone,
		NetworkVariableWritePermission.Owner);
	private bool m_LocalIsMoving;

	/// <summary>
	/// 現在の歩行アニメーション状態を取得します。
	/// </summary>
	public bool IsMoving => IsSpawned ? m_IsMoving.Value : m_LocalIsMoving;

	private void Awake()
	{
		m_BallCarrier = GetComponent<IBallCarrier>();

		if (m_SurfaceMover == null)
		{
			m_SurfaceMover = GetComponent<PlayerSurfaceMover>();
		}

		if (m_PlayerController == null)
		{
			m_PlayerController = GetComponent<PlayerController>();
		}
	}

	public override void OnNetworkSpawn()
	{
		m_IsMoving.OnValueChanged += HandleMovingChanged;
		m_RespawnVersion.OnValueChanged += HandleRespawnChanged;
		ApplyMovingState(m_IsMoving.Value);
		ApplyBallState();
	}

	private void Update()
	{
		// 所持情報は既存のボール同期から各端末へ反映されるため、所有者以外も更新する。
		ApplyBallState();

		if (m_SurfaceMover == null || m_PlayerController == null)
		{
			return;
		}

		bool shouldMove =
			m_PlayerController.IsMatchActionEnabled &&
			m_PlayerController.CurrentState == PlayerController.EPlayerState.Idle &&
			m_SurfaceMover.CurrentPlaneSpeed > m_MoveSpeedThreshold;

		// NetworkObjectとしてスポーンしていない単体テスト時は、Animatorのみ直接更新する。
		if (!IsSpawned)
		{
			m_LocalIsMoving = shouldMove;
			ApplyMovingState(shouldMove);
			return;
		}

		// 移動判定とNetworkVariableへの書き込みは所有者だけが行う。
		if (!IsOwner || m_IsMoving.Value == shouldMove)
		{
			return;
		}

		m_IsMoving.Value = shouldMove;
	}

	public override void OnNetworkDespawn()
	{
		m_IsMoving.OnValueChanged -= HandleMovingChanged;
		m_RespawnVersion.OnValueChanged -= HandleRespawnChanged;
	}

	/// <summary>
	/// Ownerで歩行判定を解除し、全端末のAnimatorを初期状態へ戻します。
	/// </summary>
	public void ResetForRespawn()
	{
		if (!IsSpawned)
		{
			ResetAnimators();
			return;
		}
		if (!IsOwner) return;
		m_IsMoving.Value = false;
		m_RespawnVersion.Value++;
	}

	private void HandleRespawnChanged(uint previousValue, uint newValue)
	{
		ResetAnimators();
	}

	private void ResetAnimators()
	{
		if (m_Animators == null) return;
		foreach (Animator animator in m_Animators)
		{
			if (animator == null || animator.runtimeAnimatorController == null) continue;
			animator.Rebind();
			animator.SetBool(m_IsMovingHash, false);
			animator.SetBool(m_HasBallHash, m_BallCarrier != null && m_BallCarrier.HasBall);
			if (animator.isActiveAndEnabled)
			{
				animator.Update(0f);
			}
		}
	}

	private void HandleMovingChanged(bool previousValue, bool newValue)
	{
		ApplyMovingState(newValue);
	}

	private void ApplyBallState()
	{
		if (m_Animators == null)
		{
			return;
		}

		bool hasBall = m_BallCarrier != null && m_BallCarrier.HasBall;
		foreach (Animator animator in m_Animators)
		{
			if (animator != null)
			{
				animator.SetBool(m_HasBallHash, hasBall);
			}
		}
	}

	private void ApplyMovingState(bool isMoving)
	{
		if (m_Animators == null)
		{
			return;
		}

		foreach (Animator animator in m_Animators)
		{
			if (animator != null)
			{
				animator.SetBool(m_IsMovingHash, isMoving);
			}
		}
	}
}
