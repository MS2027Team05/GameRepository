using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Owner側でプレイヤーのリソース消費・遅延急速回復・パスボーナスを管理します。
/// Host確定のパスボーナスはPlayerBallCarrierのOwnerRpcを経由して受け取ります。
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class PlayerResourceController : NetworkBehaviour, IPlayerResource
{
	[Header("リソース容量")]
	[SerializeField] private float m_MaxResource;
	[SerializeField] private float m_InitialResource;

	[Header("移動アクション消費")]
	[SerializeField] private float m_MoveBaseCost;
	[SerializeField] private float m_MoveCostPerMeter;
	[SerializeField] private float m_MaxMoveCost;

	[Header("遅延急速回復")]
	// アクション終了から、自然回復を開始するまでの無行動待機秒数
	[SerializeField] private float m_RecoveryDelay;

	// 急速回復量(リソース単位/秒。既存Inspectorの設定値を引き継ぐ)
	[SerializeField] private float m_RecoveryPerSecond;

	[Header("味方パス受取ボーナス")]
	[SerializeField] private float m_PassReceiveBonus;

	[Header("自プレイヤー専用ゲージUI")]
	[SerializeField] private PlayerResourceGaugeUI m_ResourceGauge;

	private IGravityMover m_GravityMover;
	private IAimGuide m_AimGuide;
	private IElectricAction m_ElectricAction;
	private PlayerController m_PlayerController;
	private float m_CurrentResource;
	private float m_IdleElapsedTime;

	public float CurrentResource => m_CurrentResource;
	public float MaxResource => Mathf.Max(0.0f, m_MaxResource);
	public event Action<float, float> OnResourceChanged;

	private void Awake()
	{
		m_GravityMover = GetComponent<IGravityMover>();
		m_AimGuide = GetComponent<IAimGuide>();
		m_ElectricAction = GetComponent<IElectricAction>();
		m_PlayerController = GetComponent<PlayerController>();
	}

	public override void OnNetworkSpawn()
	{
		if (m_ResourceGauge != null)
		{
			// Player Prefab内の子Canvasを想定し、他プレイヤーのHUDは非表示にします。
			m_ResourceGauge.gameObject.SetActive(IsOwner);
		}

		if (!IsOwner)
		{
			return;
		}

		m_CurrentResource = Mathf.Clamp(m_InitialResource, 0.0f, MaxResource);
		m_IdleElapsedTime = 0.0f;

		if (m_ResourceGauge != null)
		{
			OnResourceChanged += m_ResourceGauge.SetResource;
		}

		NotifyResourceChanged();
	}

	public override void OnNetworkDespawn()
	{
		if (m_ResourceGauge != null)
		{
			OnResourceChanged -= m_ResourceGauge.SetResource;
			m_ResourceGauge.gameObject.SetActive(false);
		}
	}

	private void Update()
	{
		if (!IsSpawned || !IsOwner)
		{
			return;
		}

		// スタン中は自然回復せず、待機時間も進めません。
		// スタン直前の経過時間を維持し、解除後に残り時間から再開します。
		if (m_PlayerController != null &&
			m_PlayerController.CurrentState == PlayerController.EPlayerState.Stunned)
		{
			return;
		}

		// 移動・予備浮上・L1チャージ中は回復せず、無行動待機時間をリセットします。
		if (IsActionActive())
		{
			m_IdleElapsedTime = 0.0f;
			return;
		}

		float deltaTime = Mathf.Max(0.0f, Time.deltaTime);
		float recoveryDelay = Mathf.Max(0.0f, m_RecoveryDelay);
		float previousIdleTime = m_IdleElapsedTime;
		m_IdleElapsedTime += deltaTime;

		if (m_CurrentResource >= MaxResource || deltaTime <= 0.0f)
		{
			return;
		}

		// 待機時間中は回復しません。フレーム途中で待機時間を超えた場合は、
		// 超過した時間だけ回復させ、1フレーム分の過剰回復を防ぎます。
		float waitDuration = Mathf.Clamp(recoveryDelay - previousIdleTime, 0.0f, deltaTime);
		float recoveryDuration = deltaTime - waitDuration;
		float recoveryAmount = Mathf.Max(0.0f, m_RecoveryPerSecond) * recoveryDuration;
		AddResource(recoveryAmount);
	}

	/// <summary>飛行・予備浮上・L1チャージ中は回復せず、無行動待機時間をリセットします。</summary>
	private bool IsActionActive()
	{
		return (m_GravityMover != null && m_GravityMover.IsFalling) ||
			(m_AimGuide != null && m_AimGuide.IsTakingOff) ||
			(m_ElectricAction != null && m_ElectricAction.IsCharging) ||
			(m_PlayerController != null &&
			m_PlayerController.CurrentState == PlayerController.EPlayerState.ChargingSubAction);
	}

	/// <summary>移動距離に応じた消費量を求め、1回分の消費上限を適用します。</summary>
	public float CalculateMoveCost(float distance)
	{
		float safeDistance = Mathf.Max(0.0f, distance);
		float baseCost = Mathf.Max(0.0f, m_MoveBaseCost);
		float distanceCost = safeDistance * Mathf.Max(0.0f, m_MoveCostPerMeter);
		return Mathf.Min(baseCost + distanceCost, Mathf.Max(0.0f, m_MaxMoveCost));
	}

	public bool CanAfford(float cost)
	{
		if (!IsSpawned || !IsOwner || float.IsNaN(cost) || float.IsInfinity(cost) || cost < 0.0f)
		{
			return false;
		}

		return m_CurrentResource >= cost;
	}

	public bool TryConsume(float cost)
	{
		if (!CanAfford(cost))
		{
			return false;
		}

		if (cost > 0.0f)
		{
			m_CurrentResource = Mathf.Max(0.0f, m_CurrentResource - cost);
			NotifyResourceChanged();
		}

		NotifyActionPerformed();
		return true;
	}

	public void AddResource(float amount)
	{
		if (!IsSpawned || !IsOwner || float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0.0f)
		{
			return;
		}

		float nextValue = Mathf.Min(MaxResource, m_CurrentResource + amount);
		if (nextValue == m_CurrentResource)
		{
			return;
		}

		m_CurrentResource = nextValue;
		NotifyResourceChanged();
	}

	public void NotifyActionPerformed()
	{
		if (!IsSpawned || !IsOwner)
		{
			return;
		}

		m_IdleElapsedTime = 0.0f;
	}

	/// <summary>Hostがパス成立を認めた場合のみOwner側から呼び出されます。</summary>
	public void ApplyFriendlyPassBonus()
	{
		AddResource(Mathf.Max(0.0f, m_PassReceiveBonus));
	}

	private void NotifyResourceChanged()
	{
		OnResourceChanged?.Invoke(m_CurrentResource, MaxResource);
	}
}
