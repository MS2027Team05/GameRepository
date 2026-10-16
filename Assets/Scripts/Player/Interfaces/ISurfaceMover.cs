using UnityEngine;

/// <summary>
/// 地上・曲面・壁面での歩行移動・運動量転送・姿勢回転を担当するモジュールのインターフェース。
/// </summary>
public interface ISurfaceMover
{
	/// <summary>
	/// 地上移動用の入力ベクトルと基準となるカメラのTransformを設定します。
	/// </summary>
	/// <param name="moveInput">入力ベクトル(WASD/スティック)</param>
	/// <param name="cameraTransform">視点基準となるカメラのTransform</param>
	void SetMoveInput(Vector2 moveInput, Transform cameraTransform);

	/// <summary>
	/// 移動速度の倍率を設定します(ボール所持時のデバフ等)。
	/// </summary>
	/// <param name="multiplier">速度倍率(通常時は1.0f、所持時は0.7f等)</param>
	void SetSpeedMultiplier(float multiplier);

	/// <summary>
	/// 現在の入力・接地法線・ギャップに基づいて地上・曲面歩行物理移動および姿勢回転を更新します。
	/// </summary>
	void UpdateSurfaceMovement();

	/// <summary>
	/// 現在の接平面進行方向ベクトルを取得します。
	/// </summary>
	Vector3 CurrentMoveDirection { get; }

	/// <summary>
	/// 現在の接平面走破速度の絶対値を取得します。
	/// </summary>
	float CurrentPlaneSpeed { get; }
}
