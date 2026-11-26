using UnityEngine;

/// <summary>
/// ゲームパッド操作時のエイムアシスト(入力減衰および緩やかな追従補正)を提供するインターフェース。
/// </summary>
public interface IAimAssist
{
	/// <summary>
	/// 現在のロックオン対象とスティック入力に基づいて、視点入力の減衰倍率およびカメラ追従の補正角度(度)を算出します。
	/// </summary>
	/// <param name="cameraTransform">現在描画に使用しているカメラのTransform</param>
	/// <param name="currentUp">現在のカメラ基準上方向ベクトル(接地面法線)</param>
	/// <param name="lookInput">感度適用後の視点操作入力</param>
	/// <param name="moveInput">左スティック等の移動入力</param>
	/// <param name="isUsingGamepad">直近の操作デバイスがゲームパッドであるか</param>
	/// <param name="deltaTime">フレーム経過時間(秒)</param>
	/// <param name="slowdownMultiplier">視点入力に掛ける減衰倍率(0.0〜1.0)</param>
	/// <param name="assistYawDelta">フレームあたりの水平(currentUp軸)追従補正角(度)</param>
	/// <param name="assistPitchDelta">フレームあたりの垂直(Pitch)追従補正角(度)</param>
	void EvaluateAimAssist(
		Transform cameraTransform,
		Vector3 currentUp,
		Vector2 lookInput,
		Vector2 moveInput,
		bool isUsingGamepad,
		float deltaTime,
		out float slowdownMultiplier,
		out float assistYawDelta,
		out float assistPitchDelta);
}
