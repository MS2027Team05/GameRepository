using System.Collections.Generic;
using UnityEngine;

// 箱の面上の移動と、辺を越える際の進行方向の折り返しを担当します。
internal static class CubeSurfaceLightningPath
{
    internal static Vector3 ClampToFace(Vector3 position, Vector3 normal, Vector3 halfSize)
    {
        for (int axis = 0; axis < 3; axis++)
        {
            position[axis] = normal[axis] != 0f
                ? normal[axis] * halfSize[axis]
                : Mathf.Clamp(position[axis], -halfSize[axis], halfSize[axis]);
        }

        return position;
    }

    internal static void Advance(ref Vector3 position, ref Vector3 normal,
        ref Vector3 direction, float distance, Vector3 halfSize, List<Vector3> corners)
    {
        // 極端な経路長や速度を設定した場合の処理時間を制限します。
        // 1回の呼び出しで256辺を越えた場合は、その位置で残りの移動を打ち切ります。
        for (int crossing = 0; crossing < 256 && distance > 0f; crossing++)
        {
            float edgeDistance = float.PositiveInfinity;
            int nextAxis = -1;
            for (int axis = 0; axis < 3; axis++)
            {
                if (normal[axis] != 0f || Mathf.Abs(direction[axis]) < 0.000001f)
                {
                    continue;
                }

                float boundary = direction[axis] > 0f ? halfSize[axis] : -halfSize[axis];
                float candidate = Mathf.Max(0f, (boundary - position[axis]) / direction[axis]);
                if (candidate < edgeDistance)
                {
                    edgeDistance = candidate;
                    nextAxis = axis;
                }
            }

            if (nextAxis < 0 || distance < edgeDistance)
            {
                position = ClampToFace(position + direction * distance, normal, halfSize);
                return;
            }

            position = ClampToFace(position + direction * edgeDistance, normal, halfSize);
            float sign = direction[nextAxis] > 0f ? 1f : -1f;
            position[nextAxis] = sign * halfSize[nextAxis];
            corners?.Add(position);
            distance = Mathf.Max(0f, distance - edgeDistance);

            Vector3 nextNormal = Vector3.zero;
            nextNormal[nextAxis] = sign;
            float edgeComponent = Mathf.Abs(direction[nextAxis]);
            direction[nextAxis] = 0f;
            direction -= normal * edgeComponent;
            normal = nextNormal;
        }
    }
}
