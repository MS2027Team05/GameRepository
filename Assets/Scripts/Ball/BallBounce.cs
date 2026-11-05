using UnityEngine;

/// <summary>
/// ボールのバウンド判定と反射速度の計算を担当します。
/// </summary>
public sealed class BallBounce
{
	/// <summary>
	/// AttachSurfaceへ接触した際、
	/// バウンドするだけの速度が残っているか確認します。
	/// </summary>
	/// <param name="collisionSpeed">
	/// 衝突直前のボール速度です。
	/// </param>
	/// <param name="bounceSpeedThreshold">
	/// バウンドとして扱う最低速度です。
	/// </param>
	/// <returns>
	/// バウンドする場合はtrueです。
	/// </returns>
	public bool ShouldBounce(
		float collisionSpeed,
		float bounceSpeedThreshold)
	{
		return collisionSpeed >=
			Mathf.Max(
				0.0f,
				bounceSpeedThreshold);
	}

	/// <summary>
	/// 入射速度を接線方向と法線方向へ分離し、
	/// それぞれ別の減衰率を適用して反射速度を計算します。
	/// </summary>
	/// <remarks>
	/// 浅い角度でSurfaceへ衝突した場合でも、
	/// MinimumNormalSpeedによって最低限の跳ね返りを保証します。
	///
	/// また、MaxNormalSpeedによって高速シュート時の
	/// 異常に大きな跳ね返りを制限します。
	/// </remarks>
	/// <param name="incomingVelocity">
	/// 衝突直前の速度です。
	/// </param>
	/// <param name="surfaceNormal">
	/// 接触面の法線です。
	/// </param>
	/// <param name="normalRetention">
	/// 法線方向に残す速度割合です。
	/// </param>
	/// <param name="tangentialRetention">
	/// 接線方向に残す速度割合です。
	/// </param>
	/// <param name="minimumNormalSpeed">
	/// バウンド時に最低限保証する法線方向速度です。
	/// </param>
	/// <param name="maxNormalSpeed">
	/// バウンド時の最大法線方向速度です。
	/// 0の場合は上限を設定しません。
	/// </param>
	/// <returns>
	/// バウンド後の速度です。
	/// </returns>
	public Vector3 CalculateBounceVelocity(
		Vector3 incomingVelocity,
		Vector3 surfaceNormal,
		float normalRetention,
		float tangentialRetention,
		float minimumNormalSpeed,
		float maxNormalSpeed)
	{
		if (incomingVelocity.sqrMagnitude <=
			Mathf.Epsilon)
		{
			return Vector3.zero;
		}

		if (surfaceNormal.sqrMagnitude <=
			Mathf.Epsilon)
		{
			return incomingVelocity;
		}

		Vector3 normal =
			surfaceNormal.normalized;

		// 法線方向の速度を取得します。
		// 負の値ならSurfaceへ向かっています。
		float incomingNormalSpeed =
			Vector3.Dot(
				incomingVelocity,
				normal);

		Vector3 normalVelocity =
			normal *
			incomingNormalSpeed;

		// 法線方向を除いた残りが、
		// Surfaceに沿って移動する接線方向速度です。
		Vector3 tangentialVelocity =
			incomingVelocity -
			normalVelocity;

		Vector3 nextTangentialVelocity =
			tangentialVelocity *
			Mathf.Clamp01(
				tangentialRetention);

		float bounceNormalSpeed =
			0.0f;

		// Surfaceへ向かっている場合だけ
		// 法線方向を反転させます。
		if (incomingNormalSpeed < 0.0f)
		{
			bounceNormalSpeed =
				-incomingNormalSpeed *
				Mathf.Clamp01(
					normalRetention);

			// 浅い入射角でも、
			// 最低限Surfaceから離れる速度を与えます。
			if (minimumNormalSpeed > 0.0f)
			{
				bounceNormalSpeed =
					Mathf.Max(
						bounceNormalSpeed,
						minimumNormalSpeed);
			}

			// 高速衝突時に跳ねすぎないよう
			// 法線方向速度だけ上限を設定します。
			if (maxNormalSpeed > 0.0f)
			{
				bounceNormalSpeed =
					Mathf.Min(
						bounceNormalSpeed,
						maxNormalSpeed);
			}
		}

		Vector3 nextNormalVelocity =
			normal *
			bounceNormalSpeed;

		return nextTangentialVelocity +
			nextNormalVelocity;
	}
}
