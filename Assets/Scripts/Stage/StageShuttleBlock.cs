using Unity.Netcode;
using UnityEngine;

namespace Stage
{
	/// <summary>
	/// 駅と駅の間を直線または円弧軌道で往復移動するブロック(キューブ)コンポーネント。
	/// Kinematic Rigidbodyによる物理駆動と、IMovingSurface実装によるプレイヤー接地追従を提供します。
	/// マルチプレイ接続時はServerTime、スタンドアロン時はTime.fixedTimeに基づく決定論的時刻計算で位置を同期します。
	/// </summary>
	[RequireComponent(typeof(Rigidbody))]
	public sealed class StageShuttleBlock : MonoBehaviour, IMovingSurface
	{
		public enum ETrajectoryMode : byte
		{
			Linear = 0,
			Arc = 1,
		}

		[Header("区間端点Transform(設定優先)")]
		[Tooltip("往復区間の始点Transform。未設定時は始点ワールド座標を使用します")]
		[SerializeField] private Transform m_PointATransform;

		[Tooltip("往復区間の終点Transform。未設定時は終点ワールド座標を使用します")]
		[SerializeField] private Transform m_PointBTransform;

		[Header("区間端点ワールド座標(Transform未指定時のフォールバック)")]
		[SerializeField] private Vector3 m_PointA = Vector3.zero;
		[SerializeField] private Vector3 m_PointB = Vector3.forward * 20f;

		[Header("軌道・移動設定")]
		[Tooltip("移動軌道モード(Linear: 直線補間, Arc: リング中心を基準とした円弧・楕円補間)")]
		[SerializeField] private ETrajectoryMode m_TrajectoryMode = ETrajectoryMode.Linear;

		[Tooltip("Arc軌道選択時の旋回中心座標(通常はリング中心 (0, 100, 0))")]
		[SerializeField] private Vector3 m_ArcCenter = Vector3.zero;

		[Tooltip("片道の移動にかける秒数(正の値)")]
		[SerializeField] private float m_OneWayDuration = 4.0f;

		[Tooltip("各端点に到着した際の停止待機秒数(0以上)")]
		[SerializeField] private float m_WaitDurationAtEnds = 1.0f;

		[Tooltip("加減速イージング(SmoothStep)を適用するか")]
		[SerializeField] private bool m_UseSmoothStep = true;

		[Tooltip("初期位相オフセット(0〜1)。複数ブロックのタイミングをずらす際に使用します")]
		[Range(0f, 1f)]
		[SerializeField] private float m_InitialPhase = 0.0f;

		private Rigidbody m_Rigidbody;
		private Vector3 m_CurrentVelocity = Vector3.zero;
		private Vector3 m_PreviousPosition = Vector3.zero;

		public Vector3 SurfaceVelocity => m_CurrentVelocity;

		public Vector3 PointA => m_PointATransform != null ? m_PointATransform.position : m_PointA;
		public Vector3 PointB => m_PointBTransform != null ? m_PointBTransform.position : m_PointB;

		public void SetWaypoints(Vector3 pointA, Vector3 pointB, Vector3 arcCenter, ETrajectoryMode mode = ETrajectoryMode.Linear)
		{
			m_PointA = pointA;
			m_PointB = pointB;
			m_ArcCenter = arcCenter;
			m_TrajectoryMode = mode;
		}

		public Vector3 GetVelocityAtPoint(Vector3 worldPoint)
		{
			return m_CurrentVelocity;
		}

		private void Awake()
		{
			m_Rigidbody = GetComponent<Rigidbody>();
			m_Rigidbody.isKinematic = true;
			m_Rigidbody.useGravity = false;
			m_Rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

			m_PreviousPosition = transform.position;
		}

		private void FixedUpdate()
		{
			float duration = Mathf.Max(0.1f, m_OneWayDuration);
			float wait = Mathf.Max(0f, m_WaitDurationAtEnds);
			float halfCycle = duration + wait;
			float fullCycle = halfCycle * 2.0f;

			double currentTime = GetCurrentSynchronizedTime();
			float cycleTime = (float)((currentTime + m_InitialPhase * fullCycle) % fullCycle);

			float t;
			if (cycleTime < duration)
			{
				// A -> B 移動中
				t = cycleTime / duration;
			}
			else if (cycleTime < halfCycle)
			{
				// B 端点で停止待機中
				t = 1.0f;
			}
			else if (cycleTime < halfCycle + duration)
			{
				// B -> A 復路移動中
				float backTime = cycleTime - halfCycle;
				t = 1.0f - (backTime / duration);
			}
			else
			{
				// A 端点で停止待機中
				t = 0.0f;
			}

			if (m_UseSmoothStep)
			{
				t = Mathf.SmoothStep(0f, 1f, t);
			}

			Vector3 start = PointA;
			Vector3 end = PointB;
			Vector3 targetPos;
			Quaternion targetRot = transform.rotation;

			if (m_TrajectoryMode == ETrajectoryMode.Arc)
			{
				Vector3 relA = start - m_ArcCenter;
				Vector3 relB = end - m_ArcCenter;
				Vector3 currentRel = Vector3.Slerp(relA, relB, t);
				targetPos = m_ArcCenter + currentRel;

				// 進行方向に沿って水平回転を合わせる
				Vector3 forwardTangent = Vector3.Cross(Vector3.up, currentRel.normalized);
				if (forwardTangent.sqrMagnitude > 0.001f)
				{
					targetRot = Quaternion.LookRotation(forwardTangent.normalized, Vector3.up);
				}
			}
			else
			{
				targetPos = Vector3.Lerp(start, end, t);
			}

			float dt = Time.fixedDeltaTime;
			if (dt > 0.0001f)
			{
				m_CurrentVelocity = (targetPos - m_PreviousPosition) / dt;
			}

			m_PreviousPosition = targetPos;
			m_Rigidbody.MovePosition(targetPos);
			m_Rigidbody.MoveRotation(targetRot);
		}

		private double GetCurrentSynchronizedTime()
		{
			if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
			{
				return NetworkManager.Singleton.ServerTime.Time;
			}

			return Time.fixedTimeAsDouble;
		}

#if UNITY_EDITOR
		private void OnDrawGizmosSelected()
		{
			Vector3 a = PointA;
			Vector3 b = PointB;

			Gizmos.color = Color.cyan;
			Gizmos.DrawLine(a, b);
			Gizmos.DrawWireSphere(a, 0.6f);
			Gizmos.DrawWireSphere(b, 0.6f);
		}
#endif
	}
}

