using System;

/// <summary>
/// プレイヤーのリソース(電力/スタミナ)の消費・回復管理を担当するモジュールのインターフェース。
/// </summary>
public interface IPlayerResource
{
	/// <summary>
	/// 現在のリソース残量を取得します。
	/// </summary>
	float CurrentResource { get; }

	/// <summary>
	/// リソースの最大容量を取得します。
	/// </summary>
	float MaxResource { get; }

	/// <summary>
	/// リソース残量が変化した際に発火するイベント(第1引数: 現在値, 第2引数: 最大値)。
	/// </summary>
	event Action<float, float> OnResourceChanged;

	/// <summary>
	/// 指定された移動距離に対する消費リソース量を算出します(固定消費・距離比例消費に対応)。
	/// </summary>
	/// <param name="distance">移動予定距離(メートル)</param>
	/// <returns>必要な消費リソース量</returns>
	float CalculateMoveCost(float distance);

	/// <summary>
	/// 指定コスト分のリソースが残っているか判定します。
	/// </summary>
	/// <param name="cost">必要コスト</param>
	/// <returns>発動可能な残量がある場合はtrue</returns>
	bool CanAfford(float cost);

	/// <summary>
	/// 指定コスト分のリソース消費を試みます。不足時は消費せずfalseを返します。
	/// </summary>
	/// <param name="cost">消費コスト</param>
	/// <returns>消費に成功した場合はtrue</returns>
	bool TryConsume(float cost);

	/// <summary>
	/// パス受取ボーナス等により指定量のリソースを回復します。
	/// </summary>
	/// <param name="amount">回復量</param>
	void AddResource(float amount);

	/// <summary>
	/// アクション実行を通知し、無行動経過による急速自然回復の待機タイマーをリセットします。
	/// </summary>
	void NotifyActionPerformed();
}

