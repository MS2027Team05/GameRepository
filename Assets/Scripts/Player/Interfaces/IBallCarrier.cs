using UnityEngine;

/// <summary>
/// ボール所持・単押しパス・長押し追尾パス・強制ドロップを担当するモジュールのインターフェース。
/// </summary>
public interface IBallCarrier
{
	/// <summary>
	/// 現在ボールを保持しているかどうかを取得します。
	/// </summary>
	bool HasBall { get; }

	/// <summary>
	/// 追尾パスの長押しチャージ中であるかを取得します。
	/// </summary>
	bool IsPassCharging { get; }

	/// <summary>
	/// 追尾パスのチャージ進捗率(0.0〜1.0)を取得します。
	/// </summary>
	float PassChargeRatio { get; }

	/// <summary>
	/// 追尾パスのチャージが満タンに達しているかを取得します。
	/// </summary>
	bool IsPassChargeFull { get; }

	/// <summary>
	/// 被タックル時や電撃被弾時などにボールを強制的にドロップ(手放す)させます。
	/// </summary>
	void ForceReleaseBall();

	/// <summary>
	/// 指定方向へ即座に通常パス・シュートを射出します(単押し時)。
	/// </summary>
	/// <param name="direction">射出方向ベクトル</param>
	void Shoot(Vector3 direction);

	/// <summary>
	/// 長押しによる追尾パスチャージおよびキャンセル案内UIの表示を開始します。
	/// </summary>
	void BeginPassCharge();

	/// <summary>
	/// 追尾パスチャージの状態を毎フレーム更新します。
	/// </summary>
	/// <param name="deltaTime">経過時間(秒)</param>
	void UpdatePassCharge(float deltaTime);

	/// <summary>
	/// 追尾パスチャージを途中キャンセルし、ボールを保持したまま関連UIを非表示にします。
	/// </summary>
	void CancelPassCharge();

	/// <summary>
	/// チャージ満タン時の離し操作によって、ロックオン対象(味方または相手ゴール)へ追尾パスを射出します。
	/// </summary>
	/// <param name="targetObject">追尾対象のGameObject</param>
	/// <param name="targetType">追尾対象の種類(味方プレイヤー / 相手ゴール)</param>
	void ShootHoming(GameObject targetObject, ELockOnTargetType targetType);
}
