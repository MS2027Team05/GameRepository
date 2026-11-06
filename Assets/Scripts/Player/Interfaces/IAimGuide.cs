using System;
using UnityEngine;

/// <summary>
/// レティクル表示・着地点評価・地上予備浮上・飛行中着地マーカー追跡を担当するモジュールのインターフェース。
/// </summary>
public interface IAimGuide
{
	/// <summary>
	/// 現在の照準先に有効な着地点が存在し移動可能であるかを取得します。
	/// </summary>
	bool CanFall { get; }

	/// <summary>
	/// 地上からの移動開始に伴う予備浮上動作中であるかを取得します。
	/// </summary>
	bool IsTakingOff { get; }

	/// <summary>
	/// 評価された目標着地点のワールド座標を取得します。
	/// </summary>
	Vector3 TargetLandingPoint { get; }

	/// <summary>
	/// 評価された目標着地面の法線ベクトルを取得します。
	/// </summary>
	Vector3 TargetLandingNormal { get; }

	/// <summary>
	/// 現在位置から目標着地点までの距離を取得します(リソース消費量の計算等に使用)。
	/// </summary>
	float TargetDistance { get; }

	/// <summary>
	/// 現在位置から目標着地点へ向かう正規化移動方向ベクトルを取得します。
	/// </summary>
	Vector3 AimMoveDirection { get; }

	/// <summary>
	/// 照準UIおよび軌道予測線の表示/非表示を切り替えます(旧エイムフロー互換用)。
	/// </summary>
	/// <param name="show">表示する場合はtrue</param>
	void ShowAimGuide(bool show);

	/// <summary>
	/// エイムをキャンセルし、プレイヤーとカメラを開始時の位置に戻します(旧エイムフロー互換用)。
	/// </summary>
	void CancelAimGuide();

	/// <summary>
	/// 移動によってエイムを終了し、プレイヤーの位置を維持してカメラを通常位置に戻します(旧エイムフロー互換用)。
	/// </summary>
	void EndAimGuideForMovement();

	/// <summary>
	/// エイム中のカメラ正面方向ベクトルを渡し、予測線をリアルタイム更新します(旧エイムフロー互換用)。
	/// </summary>
	/// <param name="direction">カメラの正面方向ベクトル</param>
	void UpdateAimDirection(Vector3 direction);

	/// <summary>
	/// 常時表示する中央レティクルの表示/非表示を切り替えます。
	/// </summary>
	/// <param name="active">表示する場合はtrue</param>
	void SetConstantReticleActive(bool active);

	/// <summary>
	/// 常時中央レティクルを基準に着地点・法線・距離の評価を毎フレーム更新します。
	/// </summary>
	/// <param name="aimDirection">カメラ正面の照準方向ベクトル</param>
	void UpdateConstantReticle(Vector3 aimDirection);

	/// <summary>
	/// 地上からの移動開始時に立ち面法線方向へ予備浮上を行い、完了後にコールバックを発火します。
	/// </summary>
	/// <param name="onTakeoffCompleted">予備浮上が完了した際に呼ばれるコールバック</param>
	void StartGroundTakeoff(Action onTakeoffCompleted);

	/// <summary>
	/// スタンや場外復帰等により、進行中の地上予備浮上を即座に中断します。
	/// </summary>
	void CancelGroundTakeoff();

	/// <summary>
	/// 飛行開始時に着地点マーカーの表示を継続し、法線角度制限の基準となる初期法線を記録します。
	/// </summary>
	/// <param name="initialPoint">移動確定時の初期着地点座標</param>
	/// <param name="initialNormal">移動確定時の初期着地面法線</param>
	void BeginLandingTracking(Vector3 initialPoint, Vector3 initialNormal);

	/// <summary>
	/// 飛行中の空中WASD(左スティック)入力に基づき、初期着地面法線との許容角度内で着地点をシフトさせます。
	/// </summary>
	/// <param name="moveInput">移動入力ベクトル</param>
	/// <param name="cameraTransform">基準カメラのTransform</param>
	/// <param name="shiftedPoint">更新後の着地点座標</param>
	/// <param name="shiftedNormal">更新後の着地面法線</param>
	/// <returns>着地点のシフト更新が行われた場合はtrue</returns>
	bool TryShiftLandingTarget(Vector2 moveInput, Transform cameraTransform, out Vector3 shiftedPoint, out Vector3 shiftedNormal);

	/// <summary>
	/// 着地完了・停止時に飛行中の着地点マーカー追跡を終了し非表示にします。
	/// </summary>
	void EndLandingTracking();
}
