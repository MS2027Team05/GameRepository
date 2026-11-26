using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 特殊移動の開始・着地・中断を同期し、プレイヤーとテンタクルへ同時に反映します。
/// 各クリップの再生終了による遷移はAnimator側で管理します。
/// </summary>
public sealed partial class PlayerAnimationController
{
	private enum ESpecialMoveAnimationPhase : byte
	{
		None,
		Aim,
		Jump,
		Landing,
		Fall
	}

	private struct SpecialMoveAnimationState : INetworkSerializable, IEquatable<SpecialMoveAnimationState>
	{
		public ESpecialMoveAnimationPhase m_Phase;
		public double m_StartedAt;
		public uint m_Version;

		public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
		{
			serializer.SerializeValue(ref m_Phase);
			serializer.SerializeValue(ref m_StartedAt);
			serializer.SerializeValue(ref m_Version);
		}

		public bool Equals(SpecialMoveAnimationState other)
		{
			return m_Phase == other.m_Phase && m_StartedAt.Equals(other.m_StartedAt) && m_Version == other.m_Version;
		}
	}

	private static readonly int m_SpecialMoveAimHash = Animator.StringToHash("Base Layer.SpecialMoveAim");
	private static readonly int m_SpecialMoveStandbyHash = Animator.StringToHash("Base Layer.SpecialMoveStandby");
	private static readonly int m_SpecialMoveJumpHash = Animator.StringToHash("Base Layer.SpecialMoveJump");
	private static readonly int m_SpecialMoveLandingHash = Animator.StringToHash("Base Layer.SpecialMoveLanding");
	private static readonly int m_SpecialMoveFallHash = Animator.StringToHash("Base Layer.SpecialMoveFall");
	private static readonly int m_IdleHash = Animator.StringToHash("Base Layer.Idle");
	private static readonly int m_IdleBallHash = Animator.StringToHash("Base Layer.Idle_Ball");
	private static readonly int m_RunHash = Animator.StringToHash("Base Layer.Run");

	private readonly NetworkVariable<SpecialMoveAnimationState> m_SpecialMoveState = new NetworkVariable<SpecialMoveAnimationState>(
		default,
		NetworkVariableReadPermission.Everyone,
		NetworkVariableWritePermission.Owner);
	private SpecialMoveAnimationState m_LocalSpecialMoveState;
	private bool[] m_SpecialMoveAnimatorWasActive;
	private bool[] m_TentacleAnimatorsWereActive;

	/// <summary>
	/// Aimの完了後に解放待ちの移動を開始できるかを取得します。
	/// </summary>
	public bool IsSpecialMoveAimComplete
	{
		get
		{
			if (m_LocalSpecialMoveState.m_Phase != ESpecialMoveAnimationPhase.Aim) return false;
			Animator animator = GetActivePlayerAnimator();
			if (animator == null) return false;
			AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
			return state.fullPathHash == m_SpecialMoveStandbyHash ||
				(state.fullPathHash == m_SpecialMoveAimHash && state.normalizedTime >= 1f);
		}
	}

	private double SpecialMoveTime => IsSpawned ? NetworkManager.ServerTime.Time : Time.timeAsDouble;

	private void InitializeSpecialMoveAnimation()
	{
		m_SpecialMoveAnimatorWasActive = new bool[m_Animators != null ? m_Animators.Length : 0];
		m_TentacleAnimatorsWereActive = new bool[m_TentacleAnimators != null ? m_TentacleAnimators.Length : 0];
	}

	/// <summary>
	/// 移動ボタン押下時に両モデルのAimを開始します。
	/// </summary>
	public bool BeginSpecialMoveAim()
	{
		Animator animator = GetActivePlayerAnimator();
		if ((IsSpawned && !IsOwner) || animator == null || !animator.HasState(0, m_SpecialMoveAimHash))
		{
			return false;
		}
		SetSpecialMovePhase(ESpecialMoveAnimationPhase.Aim);
		return true;
	}

	/// <summary>
	/// 予備浮上または空中追加移動の開始時に両モデルのJumpを開始します。
	/// </summary>
	public void PlaySpecialMoveJump()
	{
		SetSpecialMovePhase(ESpecialMoveAnimationPhase.Jump);
	}

	/// <summary>
	/// 予備浮上後にFallを開始し、空中追加移動の取り消し時には飛行表示へ戻します。
	/// </summary>
	public void PlaySpecialMoveFall()
	{
		SetSpecialMovePhase(ESpecialMoveAnimationPhase.Fall);
	}

	/// <summary>
	/// 飛行から着地したときにLandingを開始します。
	/// </summary>
	public void PlaySpecialMoveLanding()
	{
		if (m_LocalSpecialMoveState.m_Phase != ESpecialMoveAnimationPhase.None)
		{
			SetSpecialMovePhase(ESpecialMoveAnimationPhase.Landing);
		}
	}

	/// <summary>
	/// 行動禁止・スタン・復帰時に特殊移動の表示を解除します。
	/// </summary>
	public void CancelSpecialMoveAnimation()
	{
		if (m_LocalSpecialMoveState.m_Phase != ESpecialMoveAnimationPhase.None)
		{
			SetSpecialMovePhase(ESpecialMoveAnimationPhase.None);
		}
	}

	private void SetSpecialMovePhase(ESpecialMoveAnimationPhase phase)
	{
		if (IsSpawned && !IsOwner) return;
		SpecialMoveAnimationState state = new SpecialMoveAnimationState
		{
			m_Phase = phase,
			m_StartedAt = SpecialMoveTime,
			m_Version = m_LocalSpecialMoveState.m_Version + 1
		};
		if (IsSpawned)
		{
			m_SpecialMoveState.Value = state;
		}
		else
		{
			ApplySpecialMoveState(state);
		}
	}

	private void HandleSpecialMoveChanged(SpecialMoveAnimationState previousValue, SpecialMoveAnimationState newValue)
	{
		ApplySpecialMoveState(newValue);
	}

	private void ApplySpecialMoveState(SpecialMoveAnimationState state)
	{
		m_LocalSpecialMoveState = state;
		ApplyBallState();
		ApplySpecialMoveToActiveAnimators(m_Animators, m_SpecialMoveAnimatorWasActive, false, false);
		ApplySpecialMoveToActiveAnimators(m_TentacleAnimators, m_TentacleAnimatorsWereActive, true, false);
	}

	private void LateUpdate()
	{
		// モデル切り替えで有効になったAnimatorにも、現在の段階と経過時間を反映します。
		ApplySpecialMoveToActiveAnimators(m_Animators, m_SpecialMoveAnimatorWasActive, false, true);
		ApplySpecialMoveToActiveAnimators(m_TentacleAnimators, m_TentacleAnimatorsWereActive, true, true);
	}

	private void ApplySpecialMoveToActiveAnimators(Animator[] animators, bool[] previouslyActive, bool isTentacle, bool onlyNewlyActive)
	{
		if (animators == null) return;
		for (int i = 0; i < animators.Length; i++)
		{
			bool active = IsAnimatorActive(animators[i]);
			if (active && (!onlyNewlyActive || !previouslyActive[i]))
			{
				ApplySpecialMoveToAnimator(animators[i], isTentacle);
			}
			previouslyActive[i] = active;
		}
	}

	private void ApplySpecialMoveToAnimator(Animator animator, bool isTentacle)
	{
		if (!isTentacle)
		{
			// 非表示中の更新を省いた通常移動・所持状態も、切り替え先へ反映します。
			animator.SetBool(m_IsMovingHash, IsMoving);
			animator.SetBool(m_HasBallHash, m_BallCarrier != null && m_BallCarrier.HasBall);
		}
		int stateHash;
		switch (m_LocalSpecialMoveState.m_Phase)
		{
			case ESpecialMoveAnimationPhase.Aim:
				stateHash = m_SpecialMoveAimHash;
				break;
			case ESpecialMoveAnimationPhase.Jump:
				stateHash = m_SpecialMoveJumpHash;
				break;
			case ESpecialMoveAnimationPhase.Landing when !isTentacle:
				stateHash = m_SpecialMoveLandingHash;
				break;
			case ESpecialMoveAnimationPhase.Fall when !isTentacle:
				stateHash = m_SpecialMoveFallHash;
				break;
			default:
				stateHash = isTentacle ? m_IdleHash : GetLocomotionStateHash();
				break;
		}
		if (!animator.HasState(0, stateHash)) return;
		animator.Play(stateHash, 0, 0f);
		animator.Update(0f);
		if (m_LocalSpecialMoveState.m_Phase == ESpecialMoveAnimationPhase.None ||
			(isTentacle && m_LocalSpecialMoveState.m_Phase == ESpecialMoveAnimationPhase.Landing)) return;

		// 途中参加と非表示モデルの再表示では、同期された開始時刻まで再生を進めます。
		float elapsed = (float)Math.Max(0d, SpecialMoveTime - m_LocalSpecialMoveState.m_StartedAt);
		if (elapsed > 0f)
		{
			animator.Update(elapsed);
			animator.Update(0f);
		}
	}

	private int GetLocomotionStateHash()
	{
		if (IsMoving) return m_RunHash;
		return m_BallCarrier != null && m_BallCarrier.HasBall ? m_IdleBallHash : m_IdleHash;
	}

	private Animator GetActivePlayerAnimator()
	{
		if (m_Animators == null) return null;
		foreach (Animator animator in m_Animators)
		{
			if (IsAnimatorActive(animator)) return animator;
		}
		return null;
	}

	private static bool IsAnimatorActive(Animator animator)
	{
		return animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null;
	}
}
