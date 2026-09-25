using UnityEngine;

/// <summary>
/// エイムモード時のレティクル表示・軌道予測線描画を担当するモジュールのインターフェース。
/// </summary>
public interface IAimGuide
{
    /// <summary>
    /// 照準UIおよび軌道予測線の表示/非表示を切り替えます。
    /// </summary>
    /// <param name="show">表示する場合はtrue</param>
    void ShowAimGuide(bool show);

    /// <summary>
    /// エイム中のカメラ正面方向ベクトルを渡し、予測線をリアルタイム更新します。
    /// </summary>
    /// <param name="direction">カメラの正面方向ベクトル</param>
    void UpdateAimDirection(Vector3 direction);
}
