using UnityEngine;

/// <summary>
/// プレイヤー操作に伴うカメラシェイクやFOV演出を担当するクラス(担当Bモジュールの骨組みスタブ)。
/// </summary>
public class PlayerCameraEffect : MonoBehaviour
{
    /// <summary>
    /// 落下開始時のカメラ演出(FOV拡大・画面シェイク等)を再生します。
    /// </summary>
    public virtual void PlayFallEffect()
    {
        // 担当BにてCinemachine ImpulseやTween等の詳細演出を実装予定
    }
}
