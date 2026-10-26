using System;
using System.Collections.Generic;
using UnityEngine;

namespace Stage
{
	/// <summary>
	/// チーム間Prefabの置換マッピングペアを定義する構造体です。
	/// </summary>
	[Serializable]
	public struct PrefabMappingPair
	{
		[Tooltip("Aチーム(コピー元)側のPrefabアセット")]
		[SerializeField]
		private GameObject m_SourcePrefab;

		[Tooltip("Bチーム(コピー先)側のPrefabアセット")]
		[SerializeField]
		private GameObject m_TargetPrefab;

		public GameObject SourcePrefab => m_SourcePrefab;
		public GameObject TargetPrefab => m_TargetPrefab;

		public PrefabMappingPair(GameObject sourcePrefab, GameObject targetPrefab)
		{
			m_SourcePrefab = sourcePrefab;
			m_TargetPrefab = targetPrefab;
		}
	}

	/// <summary>
	/// ステージ地形の点対称(180度回転)同期を行うための設定・データ保持コンポーネントです。
	/// Inspector上でコピー元・コピー先TransformおよびPrefab置換マッピングを設定して同期を実行します。
	/// </summary>
	[ExecuteInEditMode]
	[AddComponentMenu("Stage/Stage Symmetric Synchronizer")]
	public sealed class StageSymmetricSynchronizer : MonoBehaviour
	{
		[Tooltip("コピー元となる親Transform(例: Aチーム地形ルート)")]
		[SerializeField]
		private Transform m_SourceRoot;

		[Tooltip("コピー先となる親Transform(例: Bチーム地形ルート)")]
		[SerializeField]
		private Transform m_TargetRoot;

		[Tooltip("点対称の中心座標(通常は原点 (0, 0, 0))")]
		[SerializeField]
		private Vector3 m_CenterPoint;

		[Tooltip("点対称の回転軸(通常はY軸 (0, 1, 0))")]
		[SerializeField]
		private Vector3 m_RotationAxis;

		[Tooltip("点対称の回転角度(通常は180度)")]
		[SerializeField]
		private float m_RotationAngle;

		[Tooltip("チーム別Prefabの置換マッピングリスト")]
		[SerializeField]
		private List<PrefabMappingPair> m_PrefabMappings;

		[Tooltip("非アクティブなオブジェクトも同期対象に含めるか")]
		[SerializeField]
		private bool m_IncludeInactive;

		[Tooltip("同期実行前にコピー先の既存子オブジェクトを全削除するか")]
		[SerializeField]
		private bool m_ClearTargetBeforeSync;

		[Tooltip("マッピングに存在しない共通オブジェクトも点対称に複製するか")]
		[SerializeField]
		private bool m_SyncNonMappedObjects;

		public Transform SourceRoot
		{
			get => m_SourceRoot;
			set => m_SourceRoot = value;
		}

		public Transform TargetRoot
		{
			get => m_TargetRoot;
			set => m_TargetRoot = value;
		}

		public Vector3 CenterPoint
		{
			get => m_CenterPoint;
			set => m_CenterPoint = value;
		}

		public Vector3 RotationAxis
		{
			get => m_RotationAxis;
			set => m_RotationAxis = value;
		}

		public float RotationAngle
		{
			get => m_RotationAngle;
			set => m_RotationAngle = value;
		}

		public List<PrefabMappingPair> PrefabMappings => m_PrefabMappings;

		public bool IncludeInactive
		{
			get => m_IncludeInactive;
			set => m_IncludeInactive = value;
		}

		public bool ClearTargetBeforeSync
		{
			get => m_ClearTargetBeforeSync;
			set => m_ClearTargetBeforeSync = value;
		}

		public bool SyncNonMappedObjects
		{
			get => m_SyncNonMappedObjects;
			set => m_SyncNonMappedObjects = value;
		}

		private void Reset()
		{
			m_CenterPoint = Vector3.zero;
			m_RotationAxis = Vector3.up;
			m_RotationAngle = 180f;
			m_IncludeInactive = true;
			m_ClearTargetBeforeSync = true;
			m_SyncNonMappedObjects = true;
			m_PrefabMappings = new List<PrefabMappingPair>();
		}
	}
}
