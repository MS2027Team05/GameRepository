using UnityEngine;

/// <summary>
/// Flat / Smooth Edgeの隣Triangle遷移とSmooth Normal補間を担当します。
/// </summary>
public sealed partial class BallSurfaceSimulation
{
	/// <summary>
	/// Flat / Smooth Edgeを越えて隣Triangleへ移行します。
	/// </summary>
	/// <remarks>
	/// 既存のAdvanceSurfaceMotion内にあった処理を分離しただけで、
	/// 速度維持・法線回転・SurfacePoint更新の計算内容は変更しません。
	/// </remarks>
	private bool TryTransitionFlatOrSmoothEdge(
		Vector3 crossingPoint,
		Vector3 currentNormal,
		int neighborTriangleIndex,
		Vector3 neighborVertexA,
		Vector3 neighborNormal)
	{
		float surfaceSpeed =
			m_SurfaceVelocity.magnitude;

		Quaternion transitionRotation =
			Quaternion.FromToRotation(
				currentNormal,
				neighborNormal);

		Vector3 nextVelocity =
			transitionRotation *
				m_SurfaceVelocity;

		nextVelocity =
			Vector3.ProjectOnPlane(
				nextVelocity,
				neighborNormal);

		if (nextVelocity.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			return false;
		}

		nextVelocity =
			nextVelocity.normalized *
				surfaceSpeed;

		m_CurrentTriangleIndex =
			neighborTriangleIndex;

		m_SurfaceVelocity =
			nextVelocity;

		m_SurfacePoint =
			ProjectPointToPlane(
				crossingPoint,
				neighborVertexA,
				neighborNormal);

		return true;
	}

	/// <summary>
	/// Triangle頂点に保存されたSmooth Normalを
	/// Barycentric補間し、現在位置のWorld Normalを取得します。
	/// </summary>
	private bool TryGetSmoothWorldNormal(
		int triangleIndex,
		Vector3 surfacePoint,
		out Vector3 surfaceNormal)
	{
		surfaceNormal =
			Vector3.zero;

		if (!TryGetTriangleWorldData(
			triangleIndex,
			out Vector3 vertexA,
			out Vector3 vertexB,
			out Vector3 vertexC,
			out Vector3 triangleNormal))
		{
			return false;
		}

		if (!TryCalculateBarycentric(
			surfacePoint,
			vertexA,
			vertexB,
			vertexC,
			out Vector3 barycentric))
		{
			return false;
		}

		m_MeshData.GetTriangleLocalCornerNormals(
			triangleIndex,
			out Vector3 localNormalA,
			out Vector3 localNormalB,
			out Vector3 localNormalC);

		Vector3 worldNormalA =
			TransformNormalToWorld(
				localNormalA);

		Vector3 worldNormalB =
			TransformNormalToWorld(
				localNormalB);

		Vector3 worldNormalC =
			TransformNormalToWorld(
				localNormalC);

		Vector3 interpolatedNormal =
			worldNormalA *
				barycentric.x +
			worldNormalB *
				barycentric.y +
			worldNormalC *
				barycentric.z;

		if (interpolatedNormal.sqrMagnitude <=
			m_MinimumCalculationValue *
				m_MinimumCalculationValue)
		{
			surfaceNormal =
				triangleNormal;

			return true;
		}

		surfaceNormal =
			interpolatedNormal.normalized;

		// Winding不整合や数値誤差で内向きになった場合は
		// 幾何Triangle法線と同じ側へそろえます。
		if (Vector3.Dot(
			surfaceNormal,
			triangleNormal) <
			0.0f)
		{
			surfaceNormal =
				-surfaceNormal;
		}

		return true;
	}

	/// <summary>
	/// 非一様Scaleにも対応してLocal NormalをWorldへ変換します。
	/// </summary>
	private Vector3 TransformNormalToWorld(
		Vector3 localNormal)
	{
		if (localNormal.sqrMagnitude <=
			Mathf.Epsilon)
		{
			return Vector3.zero;
		}

		Matrix4x4 normalMatrix =
			m_SurfaceSource.transform.worldToLocalMatrix.transpose;

		Vector3 worldNormal =
			normalMatrix.MultiplyVector(
				localNormal);

		if (worldNormal.sqrMagnitude <=
			Mathf.Epsilon)
		{
			return Vector3.zero;
		}

		return worldNormal.normalized;
	}
}
