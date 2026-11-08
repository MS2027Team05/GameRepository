using UnityEngine;

/// <summary>
/// 移動する足場(動く床・往復ブロック等)が現在の移動速度を接地判定モジュールへ提供するためのインターフェース。
/// 接地中のプレイヤーが足場の移動速度を自身の速度ベクトルへ加算するために参照します。
/// </summary>
public interface IMovingSurface
{
	/// <summary>
	/// この足場オブジェクトの現在のワールド線形速度(m/s)を取得します。
	/// </summary>
	Vector3 SurfaceVelocity { get; }

	/// <summary>
	/// 指定した接触ワールド座標における足場の速度(回転による角速度成分を含む)を取得します。
	/// </summary>
	/// <param name="worldPoint">接触ワールド座標</param>
	/// <returns>接触点での速度</returns>
	Vector3 GetVelocityAtPoint(Vector3 worldPoint);
}

