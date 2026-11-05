using UnityEngine;

/// <summary>
/// Concave EdgeでのSphere衝突位置計算と反射を担当します。
/// </summary>
public sealed partial class BallSurfaceSimulation
{
	/// <summary>
	/// Concave Edgeで隣面へSphereが入り込む前の
	/// 実際の衝突位置を求めて反射します。
	/// </summary>
	private bool TryHandleConcaveEdgeCollision(
		Vector3 segmentStartPoint,
		Vector3 targetPoint,
		Vector3 edgePoint,
		float crossingFraction,
		Vector3 currentNormal,
		Vector3 neighborNormal,
		out float collisionFraction)
	{
		collisionFraction =
			0.0f;

		float centerOffset =
			m_BallRadius +
				m_Height;

		Vector3 startCenter =
			segmentStartPoint +
			currentNormal *
				centerOffset;

		Vector3 edgeCenter =
			edgePoint +
			currentNormal *
				centerOffset;

		float startClearance =
			Vector3.Dot(
				startCenter -
					edgePoint,
				neighborNormal) -
			m_BallRadius;

		float edgeClearance =
			Vector3.Dot(
				edgeCenter -
					edgePoint,
				neighborNormal) -
			m_BallRadius;

		// Edge地点まで進んでもBall半径分の空間が残っている場合は、
		// この簡易Concave衝突では処理しません。
		if (edgeClearance >
			m_MinimumCalculationValue)
		{
			return false;
		}

		float fractionToEdge;

		if (startClearance <=
			m_MinimumCalculationValue)
		{
			fractionToEdge =
				0.0f;
		}
		else
		{
			float clearanceDifference =
				startClearance -
					edgeClearance;

			if (clearanceDifference <=
				m_MinimumCalculationValue)
			{
				return false;
			}

			fractionToEdge =
				Mathf.Clamp01(
					startClearance /
						clearanceDifference);
		}

		collisionFraction =
			Mathf.Clamp01(
				crossingFraction *
					fractionToEdge);

		Vector3 collisionSurfacePoint =
			Vector3.LerpUnclamped(
				segmentStartPoint,
				targetPoint,
				collisionFraction);

		collisionSurfacePoint =
			ProjectPointToPlane(
				collisionSurfacePoint,
				segmentStartPoint,
				currentNormal);

		Vector3 worldVelocity =
			m_SurfaceVelocity +
			currentNormal *
				m_VerticalVelocity;

		float incomingNormalSpeed =
			Vector3.Dot(
				worldVelocity,
				neighborNormal);

		if (incomingNormalSpeed >=
			-m_MinimumCalculationValue)
		{
			return false;
		}

		Vector3 normalVelocity =
			neighborNormal *
				incomingNormalSpeed;

		Vector3 tangentialVelocity =
			worldVelocity -
				normalVelocity;

		Vector3 reflectedVelocity =
			tangentialVelocity *
				m_BounceTangentialRetention +
			neighborNormal *
				(
					-incomingNormalSpeed *
					m_BounceNormalRetention
				);

		m_SurfacePoint =
			collisionSurfacePoint;

		m_VerticalVelocity =
			Vector3.Dot(
				reflectedVelocity,
				currentNormal);

		m_SurfaceVelocity =
			Vector3.ProjectOnPlane(
				reflectedVelocity,
				currentNormal);

		if (m_Height <=
				m_MinimumHeight &&
			m_VerticalVelocity <
				0.0f)
		{
			m_VerticalVelocity =
				0.0f;
		}

		// 次の反復で同じPlaneへ数値誤差で再侵入しないよう、
		// Source Surface上で隣面の外向き方向へ微小に戻します。
		Vector3 separationDirection =
			Vector3.ProjectOnPlane(
				neighborNormal,
				currentNormal);

		if (separationDirection.sqrMagnitude >
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			m_SurfacePoint +=
				separationDirection.normalized *
					m_BarycentricTolerance;
		}

		return true;
	}
}
