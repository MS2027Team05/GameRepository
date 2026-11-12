using System;
using UnityEngine;

/// <summary>
/// ゴール範囲へのボール本体の進入をサーバー側で通知します。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class GoalArea : MonoBehaviour
{
	/// <summary>
	/// ネットワーク生成済みのボール本体が進入した際に通知します。
	/// </summary>
	public event Action<BallController> BallEntered;

	private void Awake()
	{
		Collider[] colliders = GetComponents<Collider>();
		if (colliders.Length != 1 || !colliders[0].isTrigger)
		{
			Debug.LogError("[GoalArea] 同じオブジェクトにIs Triggerを有効にしたColliderを1つ設定してください。", this);
			enabled = false;
		}
	}

	private void OnTriggerEnter(Collider other)
	{
		// 無効なMonoBehaviourにも物理通知が届く場合があるため、明示的に除外します。
		if (!isActiveAndEnabled)
		{
			return;
		}

		BallController ball = other.GetComponentInParent<BallController>();
		if (ball == null || !ball.IsSpawned || !ball.IsServer || !ball.IsCatchCollider(other))
		{
			return;
		}

		BallEntered?.Invoke(ball);
	}
}
