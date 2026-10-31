using UnityEngine;

/// <summary>
/// ロックオン対象の検索モード定義。
/// </summary>
public enum ELockOnSearchMode
{
	None,
	NonBallHolder,
	BallHolder
}

/// <summary>
/// ロックオン対象の種類定義。
/// </summary>
public enum ELockOnTargetType
{
	None,
	Ball,
	EnemyPlayer,
	AllyPlayer,
	EnemyGoal
}

/// <summary>
/// ロックオン対象の検索・捕捉を担当するモジュールのインターフェース。
/// 電撃アクション(非所持時)および追尾パスアクション(所持時)から共通利用します。
/// </summary>
public interface ILockOnProvider
{
	/// <summary>
	/// 現在捕捉している対象の種類を取得します。
	/// </summary>
	ELockOnTargetType CurrentTargetType { get; }

	/// <summary>
	/// 現在捕捉している対象のGameObjectを取得します。
	/// </summary>
	GameObject CurrentTargetObject { get; }

	/// <summary>
	/// 有効なロックオン対象が存在するかどうかを取得します。
	/// </summary>
	bool HasValidTarget { get; }

	/// <summary>
	/// 検索モード(非所持: ボール優先+敵 / 所持: 味方+相手ゴール)を設定します。
	/// </summary>
	/// <param name="mode">適用する検索モード</param>
	void SetSearchMode(ELockOnSearchMode mode);

	/// <summary>
	/// 現在の検索モードに基づいてロックオン対象を更新します。
	/// </summary>
	void UpdateTargetSearch();

	/// <summary>
	/// 現在のロックオン対象を解除し、マーカー表示等をクリアします。
	/// </summary>
	void ClearTarget();
}

