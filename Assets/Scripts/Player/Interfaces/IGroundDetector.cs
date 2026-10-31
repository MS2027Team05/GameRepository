using UnityEngine;

/// <summary>
/// 足元の地面・壁・角の物理検知および法線・高低差の計測を担当するモジュールのインターフェース。
/// </summary>
public interface IGroundDetector
{
	/// <summary>
	/// 地面または壁面に接地しているかを取得します。
	/// </summary>
	bool IsGrounded { get; }

	/// <summary>
	/// 現在の接地面法線ベクトルを取得します。
	/// </summary>
	Vector3 CurrentGroundNormal { get; }

	/// <summary>
	/// 足元理想位置と地面接触点との高低差(浮きギャップ)を取得します。
	/// </summary>
	float GroundGap { get; }

	/// <summary>
	/// 指定した移動方向・速度に基づいて足元・角・壁面の接地状態を更新します。
	/// </summary>
	/// <param name="moveDir">現在の進行方向ベクトル</param>
	/// <param name="speed">現在の移動速度</param>
	void DetectGround(Vector3 moveDir, float speed);
}
