using UnityEngine;

/// <summary>
/// カメラ演出およびカメラ基準の移動方向基底を提供するインターフェース。
/// カメラシェイクやFOV変更などの具体的な処理は実装側に委譲します。
/// </summary>
public interface ICameraEffect
{
	/// <summary>
	/// 高速移動開始時のカメラ演出を再生します。
	/// FOV拡大などを開始します。
	/// </summary>
	void PlayFallEffect();

	/// <summary>
	/// 演出中に変更されたFOVを通常状態へ戻します。
	/// 外部の移動処理などから呼び出します。
	/// </summary>
	void RestoreFOV();

	/// <summary>
	/// 指定された接地法線に対して、上下角の特異点や反転を含まない移動用の前方・右方単位ベクトルを取得します。
	/// </summary>
	/// <param name="groundNormal">現在の接地法線ベクトル</param>
	/// <param name="forwardOnPlane">接平面上の前方単位ベクトル</param>
	/// <param name="rightOnPlane">接平面上の右方単位ベクトル</param>
	/// <returns>有効な移動基底を算出できた場合はtrue</returns>
	bool TryGetMovementBasis(Vector3 groundNormal, out Vector3 forwardOnPlane, out Vector3 rightOnPlane);
}
