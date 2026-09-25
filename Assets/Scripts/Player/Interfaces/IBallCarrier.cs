/// <summary>
/// ボール所持・パス・シュート・ドロップを担当するモジュールのインターフェース。
/// </summary>
public interface IBallCarrier
{
    /// <summary>
    /// 現在ボールを保持しているかどうかを取得します。
    /// </summary>
    bool HasBall { get; }

    /// <summary>
    /// 被タックル時などにボールを強制的にドロップ(手放す)させます。
    /// </summary>
    void ForceReleaseBall();
}
