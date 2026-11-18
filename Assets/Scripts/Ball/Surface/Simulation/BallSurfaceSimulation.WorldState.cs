using UnityEngine;

/// <summary>
/// BallSurface Physics中のBall本体のWorld状態を管理します。
/// </summary>
/// <remarks>
/// SurfacePoint / Height / Convex / ConcaveなどはTraversalを計算するための
/// 補助状態として扱い、Ball本体のWorld位置・World速度はここを共通基準にします。
///
/// Traversal切替そのものではWorldStateを変更しません。
/// </remarks>
public sealed partial class BallSurfaceSimulation
{
	private const float m_TransitionPositionTolerance = 0.001f;
	private const float m_TransitionVelocityTolerance = 0.001f;
	private const float m_TransitionEnergyTolerance = 0.0001f;
	private const float m_TransitionAngleTolerance = 0.1f;

	private bool m_HasWorldState;
	private Vector3 m_WorldCenter;
	private Vector3 m_WorldVelocity;

	/// <summary>
	/// Ball本体の現在World中心座標を取得します。
	/// </summary>
	public bool TryGetWorldCenter(
		out Vector3 worldCenter)
	{
		worldCenter =
			m_WorldCenter;

		return m_IsInitialized &&
			m_HasWorldState;
	}

	/// <summary>
	/// Ball本体の現在World速度を取得します。
	/// </summary>
	public bool TryGetWorldVelocity(
		out Vector3 worldVelocity)
	{
		worldVelocity =
			m_WorldVelocity;

		return m_IsInitialized &&
			m_HasWorldState;
	}

	/// <summary>
	/// Ball本体のWorld状態を更新します。
	/// </summary>
	private void SetWorldState(
		Vector3 worldCenter,
		Vector3 worldVelocity)
	{
		m_WorldCenter =
			worldCenter;

		m_WorldVelocity =
			worldVelocity;

		m_HasWorldState =
			true;
	}

	/// <summary>
	/// Face上の指定地点に対応するWorldStateを作ります。
	/// </summary>
	/// <remarks>
	/// Sharp Edgeへ入る直前のWorld状態を確定させるために使用します。
	/// 可能な場合は現在FaceのSmooth Normalを使用します。
	/// </remarks>
	private bool TrySetWorldStateFromFacePoint(
		int triangleIndex,
		Vector3 surfacePoint,
		Vector3 fallbackNormal)
	{
		Vector3 surfaceNormal =
			fallbackNormal;

		if (TryGetSmoothWorldNormal(
			triangleIndex,
			surfacePoint,
			out Vector3 smoothNormal) &&
			smoothNormal.sqrMagnitude >
				m_MinimumCalculationValue *
					m_MinimumCalculationValue)
		{
			surfaceNormal =
				smoothNormal;
		}

		if (surfaceNormal.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return false;
		}

		surfaceNormal.Normalize();

		float offsetDistance =
			Mathf.Max(
				m_BallRadius +
					m_Height,
				m_MinimumCalculationValue);

		Vector3 worldCenter =
			surfacePoint +
			surfaceNormal *
				offsetDistance;

		Vector3 worldVelocity =
			m_SurfaceVelocity +
			surfaceNormal *
				m_VerticalVelocity;

		SetWorldState(
			worldCenter,
			worldVelocity);

		return true;
	}

	/// <summary>
	/// 現在Traversalの補助状態からWorldStateを再構成します。
	/// </summary>
	private bool TryCalculateTraversalWorldState(
		out Vector3 worldCenter,
		out Vector3 worldVelocity)
	{
		worldCenter =
			Vector3.zero;

		worldVelocity =
			Vector3.zero;

		if (!m_IsInitialized)
		{
			return false;
		}

		// Concave Airborne中はWorldStateそのものがBall本体です。
		if (m_IsConcaveAirborneTransfer)
		{
			if (!m_HasWorldState)
			{
				return false;
			}

			worldCenter =
				m_WorldCenter;

			worldVelocity =
				m_WorldVelocity;

			return true;
		}

		if (!TryGetCurrentSurfaceNormal(
			out Vector3 surfaceNormal))
		{
			return false;
		}

		Vector3 ballCenterOffset;

		if (m_IsConvexVertexSpherePatch)
		{
			ballCenterOffset =
				GetConvexVertexSphereBallCenterOffset();
		}
		else if (m_IsConvexEdgeTransition)
		{
			ballCenterOffset =
				GetConvexBallCenterOffset();
		}
		else
		{
			float offsetDistance =
				Mathf.Max(
					m_BallRadius +
						m_Height,
					m_MinimumCalculationValue);

			ballCenterOffset =
				surfaceNormal *
				offsetDistance;
		}

		worldCenter =
			m_SurfacePoint +
			ballCenterOffset;

		worldVelocity =
			m_SurfaceVelocity +
			surfaceNormal *
				m_VerticalVelocity;

		return true;
	}

	/// <summary>
	/// 現在Traversalを進めた後の結果をWorldStateへ同期します。
	/// </summary>
	private bool RefreshWorldStateFromCurrentTraversal()
	{
		if (m_IsConcaveAirborneTransfer)
		{
			return m_HasWorldState;
		}

		if (!TryCalculateTraversalWorldState(
			out Vector3 worldCenter,
			out Vector3 worldVelocity))
		{
			return false;
		}

		SetWorldState(
			worldCenter,
			worldVelocity);

		return true;
	}

	/// <summary>
	/// Traversal切替だけで位置・速度・運動エネルギーが変化していないか確認します。
	/// </summary>
	/// <remarks>
	/// 重力やBounceなど、時間を進めた結果の変化はこの検証対象ではありません。
	/// </remarks>
	private void ValidateTransitionWorldState(
		Vector3 beforeCenter,
		Vector3 beforeVelocity,
		string transitionName)
	{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
		if (!m_HasWorldState)
		{
			Debug.LogWarning(
				$"[BallSurface][Transition] {transitionName}: WorldStateがありません。");

			return;
		}

		float positionDelta =
			Vector3.Distance(
				beforeCenter,
				m_WorldCenter);

		float velocityDelta =
			Vector3.Distance(
				beforeVelocity,
				m_WorldVelocity);

		float beforeEnergy =
			beforeVelocity.sqrMagnitude;

		float afterEnergy =
			m_WorldVelocity.sqrMagnitude;

		if (positionDelta >
				m_TransitionPositionTolerance ||
			velocityDelta >
				m_TransitionVelocityTolerance ||
			afterEnergy >
				beforeEnergy +
					m_TransitionEnergyTolerance)
		{
			Debug.LogWarning(
				$"[BallSurface][Transition] {transitionName}: " +
				$"PositionDelta={positionDelta:F6}, " +
				$"VelocityDelta={velocityDelta:F6}, " +
				$"EnergyBefore={beforeEnergy:F6}, " +
				$"EnergyAfter={afterEnergy:F6}");
		}
#endif
	}

	/// <summary>
	/// WorldStateを初期状態へ戻します。
	/// </summary>
	private void ResetWorldState()
	{
		m_HasWorldState =
			false;

		m_WorldCenter =
			Vector3.zero;

		m_WorldVelocity =
			Vector3.zero;
	}
}
