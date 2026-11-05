/// <summary>
/// ボール非所持時の電撃チャージ・発射(ボール奪取/敵スタン)を担当するモジュールのインターフェース。
/// </summary>
public interface IElectricAction
{
	/// <summary>
	/// 現在電撃のチャージ中であるかを取得します。
	/// </summary>
	bool IsCharging { get; }

	/// <summary>
	/// 現在のチャージ進捗率(0.0〜1.0)を取得します。
	/// </summary>
	float ChargeRatio { get; }

	/// <summary>
	/// 電撃発射に必要なリソース消費量を取得します。
	/// </summary>
	float RequiredCost { get; }

	/// <summary>
	/// 電撃チャージおよびキャンセル案内UIの表示を開始します。
	/// </summary>
	void BeginCharge();

	/// <summary>
	/// チャージ状態を毎フレーム更新します。
	/// </summary>
	/// <param name="deltaTime">経過時間(秒)</param>
	void UpdateCharge(float deltaTime);

	/// <summary>
	/// チャージを途中キャンセルし、関連UIを非表示にします。
	/// </summary>
	void CancelCharge();

	/// <summary>
	/// チャージボタン離し時に呼び出され、満タンかつ対象有効であれば電撃を発射します。
	/// </summary>
	/// <returns>電撃が発射された場合はtrue、未満タンまたは対象不在で不発の場合はfalse</returns>
	bool TryReleaseAndFire();
}

