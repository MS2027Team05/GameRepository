using UnityEngine;

/// <summary>
/// 重力落下・ホバリング・物理移動を担当するモジュールのインターフェース。
/// </summary>
public interface IGravityMover
{
    /// <summary>
    /// 指定された方向へ等速直線移動(落下)を開始します。
    /// </summary>
    /// <param name="direction">進行方向ベクトル</param>
    void StartFalling(Vector3 direction);

    /// <summary>
    /// 落下を停止し、空中で浮遊・静止(ホバリング)させます。
    /// </summary>
    void StopFalling();

    /// <summary>
    /// 移動速度の倍率を設定します(ボール所持時のデバフ等)。
    /// </summary>
    /// <param name="multiplier">速度倍率(通常時は1.0f、所持時は0.7f等)</param>
    void SetSpeedMultiplier(float multiplier);
}
