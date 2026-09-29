// サンプル用
// 参考元: https://anogame.net/netcode-for-gameobjects-tutorial/

// NetworkのTransform周りはここを参照: https://synamon.hatenablog.com/entry/ngo-introduce-networktransform
// 現状SampleにもClientNetworkTransformがなかったのでここ参照?: https://xrdnk.hateblo.jp/entry/2021/11/16/090000

using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class NetworkManagerUI : MonoBehaviour
{
	[FormerlySerializedAs("hostButton")]
	[SerializeField] private Button m_HostButton;
	[FormerlySerializedAs("clientButton")]
	[SerializeField] private Button m_ClientButton;

	private void Awake()
	{
		m_HostButton.onClick.AddListener(() =>
		{
			NetworkManager.Singleton.StartHost();
		});
		m_ClientButton.onClick.AddListener(() =>
		{
			NetworkManager.Singleton.StartClient();
		});
	}
}
