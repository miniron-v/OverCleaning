using Unity.Netcode;
using UnityEngine;

namespace OverCleaning.Network
{
    /// <summary>
    /// 테스트 씬 전용. 로비를 거치지 않고 Play를 누르면 바로 혼자 호스트를 시작한다.
    /// Relay 없이 로컬에서만 돌므로 로그인도 방 코드도 필요 없다.
    ///
    /// PlayerSpawner보다 먼저 돌아야 한다. 스포너가 먼저 깨어나 서버가 아니라고 보면
    /// 그대로 포기해 플레이어가 스폰되지 않는다. Awake가 아니라 Start인 것도 이유가 있다.
    /// 호스트 시작은 씬의 네트워크 오브젝트를 바로 스폰시키는데, 그보다 앞서 모든
    /// Awake가 끝나 있어야 한다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class TestAutoHost : MonoBehaviour
    {
        private void Start()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && !manager.IsListening)
                manager.StartHost();
        }
    }
}
