using UnityEngine;

/// <summary>
/// ボールへ作用する疑似重力の対象を管理します。
/// </summary>
/// <remarks>
/// 飛行中は固定された一点を重力源として使用します。
///
/// AttachSurfaceへバウンドした後は、
/// 接触したローカルな表面の法線方向を追跡し、
/// そのSurfaceを地面として疑似重力を計算します。
/// </remarks>
public sealed class BallSurfaceGravity
{
	/// <summary>
	/// 現在使用している重力対象の種類です。
	/// </summary>
	private enum EGravityTargetType
	{
		None,
		FixedPoint,
		LocalSurface
	}

	private const float m_MinimumDirectionMagnitude = 0.001f;
	private const float m_SurfaceRaySpread = 0.4f;
	private const float m_MinimumNormalDot = -0.05f;

	private EGravityTargetType m_TargetType;

	private Collider m_TargetCollider;

	private Vector3 m_TargetPoint;

	private Vector3 m_SurfacePoint;
	private Vector3 m_SurfaceNormal;

	/// <summary>
	/// 現在の重力対象が、
	/// バウンド後のローカルSurfaceであるか取得します。
	/// </summary>
	public bool IsLocalSurfaceTarget
	{
		get
		{
			return m_TargetType ==
				EGravityTargetType.LocalSurface &&
				IsTargetValid();
		}
	}

	/// <summary>
	/// Surface上の一点を固定された重力源として設定します。
	/// </summary>
	/// <remarks>
	/// 主にシュート後の空中飛行中に使用します。
	/// </remarks>
	/// <param name="targetCollider">
	/// 重力対象となるColliderです。
	/// </param>
	/// <param name="targetPoint">
	/// 重力源として使用するSurface上の座標です。
	/// </param>
	public void SetFixedPoint(
		Collider targetCollider,
		Vector3 targetPoint)
	{
		m_TargetCollider =
			targetCollider;

		m_TargetPoint =
			targetPoint;

		m_SurfacePoint =
			Vector3.zero;

		m_SurfaceNormal =
			Vector3.zero;

		m_TargetType =
			EGravityTargetType.FixedPoint;
	}

	/// <summary>
	/// 接触したローカルなSurfaceを
	/// 重力対象として設定します。
	/// </summary>
	/// <remarks>
	/// ステージ全体が1つのMeshColliderでも、
	/// 接触地点と法線を基準にすることで
	/// 接触した周辺だけを現在の地面として扱います。
	/// </remarks>
	/// <param name="targetCollider">
	/// 接触したColliderです。
	/// </param>
	/// <param name="contactPoint">
	/// 接触地点です。
	/// </param>
	/// <param name="contactNormal">
	/// 接触面の法線です。
	/// </param>
	public void SetLocalSurface(
		Collider targetCollider,
		Vector3 contactPoint,
		Vector3 contactNormal)
	{
		if (targetCollider == null ||
			contactNormal.sqrMagnitude <=
			Mathf.Epsilon)
		{
			Clear();
			return;
		}

		m_TargetCollider =
			targetCollider;

		m_SurfacePoint =
			contactPoint;

		m_SurfaceNormal =
			contactNormal.normalized;

		m_TargetPoint =
			Vector3.zero;

		m_TargetType =
			EGravityTargetType.LocalSurface;
	}

	/// <summary>
	/// 現在保存している重力対象を解除します。
	/// </summary>
	public void Clear()
	{
		m_TargetCollider =
			null;

		m_TargetPoint =
			Vector3.zero;

		m_SurfacePoint =
			Vector3.zero;

		m_SurfaceNormal =
			Vector3.zero;

		m_TargetType =
			EGravityTargetType.None;
	}

	/// <summary>
	/// 現在の重力対象が使用可能か確認します。
	/// </summary>
	/// <returns>
	/// 使用可能な場合はtrueです。
	/// </returns>
	public bool IsTargetValid()
	{
		if (m_TargetType ==
			EGravityTargetType.None)
		{
			return false;
		}

		return m_TargetCollider != null &&
			m_TargetCollider.enabled &&
			m_TargetCollider.gameObject.activeInHierarchy;
	}

	/// <summary>
	/// 現在位置から重力対象へ向かう方向と
	/// Surfaceまでの距離を取得します。
	/// </summary>
	/// <param name="currentPosition">
	/// 現在のボール位置です。
	/// </param>
	/// <param name="surfaceTrackDistance">
	/// LocalSurfaceを追跡する最大距離です。
	/// </param>
	/// <param name="gravityDirection">
	/// 計算された重力方向です。
	/// </param>
	/// <param name="distance">
	/// 現在位置からSurfaceまでの距離です。
	/// </param>
	/// <returns>
	/// 重力情報を取得できた場合はtrueです。
	/// </returns>
	public bool TryGetGravityData(
		Vector3 currentPosition,
		float surfaceTrackDistance,
		out Vector3 gravityDirection,
		out float distance)
	{
		gravityDirection =
			Vector3.zero;

		distance =
			0.0f;

		if (!IsTargetValid())
		{
			return false;
		}

		switch (m_TargetType)
		{
			case EGravityTargetType.FixedPoint:
				return TryGetFixedPointGravity(
					currentPosition,
					out gravityDirection,
					out distance);

			case EGravityTargetType.LocalSurface:
				return TryGetLocalSurfaceGravity(
					currentPosition,
					surfaceTrackDistance,
					out gravityDirection,
					out distance);

			default:
				return false;
		}
	}

	/// <summary>
	/// 固定点へ向かう重力方向と距離を計算します。
	/// </summary>
	private bool TryGetFixedPointGravity(
		Vector3 currentPosition,
		out Vector3 gravityDirection,
		out float distance)
	{
		Vector3 toGravityPoint =
			m_TargetPoint -
			currentPosition;

		distance =
			toGravityPoint.magnitude;

		if (distance <=
			m_MinimumDirectionMagnitude)
		{
			gravityDirection =
				Vector3.zero;

			return true;
		}

		gravityDirection =
			toGravityPoint /
			distance;

		return true;
	}

	/// <summary>
	/// 現在追跡しているLocalSurfaceから、
	/// 重力方向とSurfaceまでの距離を計算します。
	/// </summary>
	private bool TryGetLocalSurfaceGravity(
		Vector3 currentPosition,
		float surfaceTrackDistance,
		out Vector3 gravityDirection,
		out float distance)
	{
		TryUpdateLocalSurface(
			currentPosition,
			surfaceTrackDistance);

		if (m_SurfaceNormal.sqrMagnitude <=
			Mathf.Epsilon)
		{
			gravityDirection =
				Vector3.zero;

			distance =
				0.0f;

			return false;
		}

		Vector3 normalizedSurfaceNormal =
			m_SurfaceNormal.normalized;

		// Surfaceの外向き法線とは逆方向を
		// ボールにとっての重力方向として扱います。
		gravityDirection =
			-normalizedSurfaceNormal;

		// 接触地点そのものへの直線距離ではなく、
		// 現在のSurfaceからどれだけ浮いているかを求めます。
		distance =
			Mathf.Abs(
				Vector3.Dot(
					currentPosition -
					m_SurfacePoint,
					normalizedSurfaceNormal));

		return true;
	}

	/// <summary>
	/// 現在のSurface法線を基準に複数方向へRayを飛ばし、
	/// 近くにある同一Collider上のSurfaceを追跡します。
	/// </summary>
	private void TryUpdateLocalSurface(
		Vector3 currentPosition,
		float searchDistance)
	{
		if (m_TargetCollider == null ||
			searchDistance <= 0.0f ||
			m_SurfaceNormal.sqrMagnitude <=
			Mathf.Epsilon)
		{
			return;
		}

		Vector3 gravityDirection =
			-m_SurfaceNormal.normalized;

		Vector3 tangent =
			Vector3.Cross(
				gravityDirection,
				Vector3.up);

		// GravityDirectionとVector3.upが平行だった場合は、
		// Vector3.rightを使って接線方向を生成します。
		if (tangent.sqrMagnitude <=
			Mathf.Epsilon)
		{
			tangent =
				Vector3.Cross(
					gravityDirection,
					Vector3.right);
		}

		tangent.Normalize();

		Vector3 bitangent =
			Vector3.Cross(
				gravityDirection,
				tangent).normalized;

		bool hasHit =
			false;

		RaycastHit nearestHit =
			default;

		float nearestDistance =
			float.MaxValue;

		// 現在の重力方向。
		TrySurfaceRay(
			currentPosition,
			gravityDirection,
			searchDistance,
			ref hasHit,
			ref nearestHit,
			ref nearestDistance);

		// 周辺Surfaceも追跡できるよう、
		// 接線方向へ少し広げたRayも使用します。
		TrySurfaceRay(
			currentPosition,
			(gravityDirection +
				tangent *
				m_SurfaceRaySpread).normalized,
			searchDistance,
			ref hasHit,
			ref nearestHit,
			ref nearestDistance);

		TrySurfaceRay(
			currentPosition,
			(gravityDirection -
				tangent *
				m_SurfaceRaySpread).normalized,
			searchDistance,
			ref hasHit,
			ref nearestHit,
			ref nearestDistance);

		TrySurfaceRay(
			currentPosition,
			(gravityDirection +
				bitangent *
				m_SurfaceRaySpread).normalized,
			searchDistance,
			ref hasHit,
			ref nearestHit,
			ref nearestDistance);

		TrySurfaceRay(
			currentPosition,
			(gravityDirection -
				bitangent *
				m_SurfaceRaySpread).normalized,
			searchDistance,
			ref hasHit,
			ref nearestHit,
			ref nearestDistance);

		TrySurfaceRay(
			currentPosition,
			(gravityDirection +
				(tangent + bitangent) *
				m_SurfaceRaySpread).normalized,
			searchDistance,
			ref hasHit,
			ref nearestHit,
			ref nearestDistance);

		TrySurfaceRay(
			currentPosition,
			(gravityDirection +
				(tangent - bitangent) *
				m_SurfaceRaySpread).normalized,
			searchDistance,
			ref hasHit,
			ref nearestHit,
			ref nearestDistance);

		TrySurfaceRay(
			currentPosition,
			(gravityDirection +
				(-tangent + bitangent) *
				m_SurfaceRaySpread).normalized,
			searchDistance,
			ref hasHit,
			ref nearestHit,
			ref nearestDistance);

		TrySurfaceRay(
			currentPosition,
			(gravityDirection -
				(tangent + bitangent) *
				m_SurfaceRaySpread).normalized,
			searchDistance,
			ref hasHit,
			ref nearestHit,
			ref nearestDistance);

		if (!hasHit)
		{
			// 一時的にRayがSurfaceから外れても、
			// 直前のSurface情報は維持します。
			return;
		}

		m_SurfacePoint =
			nearestHit.point;

		m_SurfaceNormal =
			nearestHit.normal.normalized;
	}

	/// <summary>
	/// 現在重力対象となっているColliderだけにRaycastし、
	/// 追跡対象として使用できるSurfaceを探します。
	/// </summary>
	private void TrySurfaceRay(
		Vector3 origin,
		Vector3 direction,
		float searchDistance,
		ref bool hasHit,
		ref RaycastHit nearestHit,
		ref float nearestDistance)
	{
		if (direction.sqrMagnitude <=
			Mathf.Epsilon)
		{
			return;
		}

		Ray ray =
			new Ray(
				origin,
				direction.normalized);

		if (!m_TargetCollider.Raycast(
			ray,
			out RaycastHit hit,
			searchDistance))
		{
			return;
		}

		Vector3 hitNormal =
			hit.normal.normalized;

		Vector3 previousNormal =
			m_SurfaceNormal.normalized;

		// 同じ巨大Meshの反対側などへ
		// Surfaceが突然切り替わらないようにします。
		float normalDot =
			Vector3.Dot(
				hitNormal,
				previousNormal);

		if (normalDot <
			m_MinimumNormalDot)
		{
			return;
		}

		if (hit.distance >=
			nearestDistance)
		{
			return;
		}

		hasHit =
			true;

		nearestHit =
			hit;

		nearestDistance =
			hit.distance;
	}
}
