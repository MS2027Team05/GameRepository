// サンプル用
// 参考元: https://anogame.net/netcode-for-gameobjects-tutorial/

// NetworkのTransform周りはここを参照: https://synamon.hatenablog.com/entry/ngo-introduce-networktransform
// 現状SampleにもClientNetworkTransformがなかったのでここ参照?: https://xrdnk.hateblo.jp/entry/2021/11/16/090000

using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
public class NetworkManagerUI : MonoBehaviour
{
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;
    private void Awake()
    {
        hostButton.onClick.AddListener(() =>
        {
            NetworkManager.Singleton.StartHost();
        });
        clientButton.onClick.AddListener(() =>
        {
            NetworkManager.Singleton.StartClient();
        });
    }
}
