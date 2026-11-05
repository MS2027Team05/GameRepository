using System;
using UnityEngine;

/// <summary>
/// 等速直線移動(落下)・空中ホバリング・着地衝突処理を担当するモジュールのインターフェース。
/// </summary>
public interface IGravityMover
{
	/// <summary>
	/// 飛行移動から地面・壁面への着地が完了した際に発火するイベント。
	/// </summary>
	event Action OnLanded;

	/// <summary>
	/// 指定された方向へ等速直線移動(落下)を開始します。
	/// </summary>
	/// <param name="direction">進行方向ベクトル</param>
	void StartFalling(Vector3 direction);

	/// <summary>
	/// 落下先の着地点と着地面の法線を設定します。
	/// </summary>
	/// <param name="landingPoint">予測着地点</param>
	/// <param name="landingNormal">着地面の法線</param>
	void SetFallingTarget(Vector3 landingPoint, Vector3 landingNormal);

	/// <summary>
	/// 落下を停止し、空中で浮遊・静止(ホバリング)させます。
	/// </summary>
	void StopFalling();

	/// <summary>
	/// エイム中のホバリング状態を開始します。
	/// </summary>
	void StartAimHover();

	/// <summary>
	/// エイム中のホバリング状態を終了します。
	/// </summary>
	void StopAimHover();

	/// <summary>
	/// 移動速度の倍率を設定します(ボール所持時のデバフ等)。
	/// </summary>
	/// <param name="multiplier">速度倍率(通常時は1.0f、所持時は0.7f等)</param>
	void SetSpeedMultiplier(float multiplier);

	/// <summary>
	/// 指定された位置と回転へ移動し、移動・物理状態を初期化します。
	/// </summary>
	/// <param name="position">復帰先のワールド座標</param>
	/// <param name="rotation">復帰後のワールド回転</param>
	void ResetMovement(Vector3 position, Quaternion rotation);

	/// <summary>
	/// 現在等速直線移動(落下)中であるかを取得します。
	/// </summary>
	bool IsFalling { get; }
}
