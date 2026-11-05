using Unity.Netcode;
using UnityEngine;

/// <summary>
/// プレイヤーの地上移動速度からAnimatorの移動状態を更新し、他端末へ同期するコンポーネント。
/// </summary>
public sealed class PlayerAnimationController : NetworkBehaviour
{
	private static readonly int m_IsMovingHash = Animator.StringToHash("IsMoving");

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

	private void Awake()
	{
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
		ApplyMovingState(m_IsMoving.Value);
	}

	private void Update()
	{
		if (m_SurfaceMover == null || m_PlayerController == null)
		{
			return;
		}

		bool shouldMove =
			m_PlayerController.CurrentState == PlayerController.EPlayerState.Idle &&
			m_SurfaceMover.CurrentPlaneSpeed > m_MoveSpeedThreshold;

		// NetworkObjectとしてスポーンしていない単体テスト時は、Animatorのみ直接更新する。
		if (!IsSpawned)
		{
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
	}

	private void HandleMovingChanged(bool previousValue, bool newValue)
	{
		ApplyMovingState(newValue);
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
