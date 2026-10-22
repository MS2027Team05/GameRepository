using UnityEngine;

/// <summary>
/// プレイヤーの復帰処理を実行するためのインターフェース。
/// 場外判定とプレイヤー固有の状態初期化を分離します。
/// </summary>
public interface IPlayerRespawnHandler
{
	/// <summary>
	/// 指定された位置と回転へプレイヤーを復帰させます。
	/// </summary>
	/// <param name="position">復帰先のワールド座標</param>
	/// <param name="rotation">復帰後のワールド回転</param>
	void Respawn(Vector3 position, Quaternion rotation);
}
