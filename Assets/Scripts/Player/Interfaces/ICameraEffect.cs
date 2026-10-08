using UnityEngine;

/// <summary>
/// カメラ演出を制御するためのインターフェース。
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
}
