using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// クライアント主権(Owner権限)でTransformを同期するコンポーネント。
/// 操作端末側で遅延なく移動・回転させ、その結果をサーバーおよび他クライアントへ同期します。
/// </summary>
[DisallowMultipleComponent]
public class ClientNetworkTransform : NetworkTransform
{
    /// <summary>
    /// サーバー主権とするかどうかを指定します。
    /// false を返却することで、Owner(クライアント)権限での同期が有効になります。
    /// </summary>
    /// <returns>常にfalse(クライアント主権)</returns>
    protected override bool OnIsServerAuthoritative()
    {
        return false;
    }
}
