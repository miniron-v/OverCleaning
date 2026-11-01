using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace OverCleaning.Network
{
    /// <summary>
    /// 테스트 씬 전용. 로비를 거치지 않고 Play를 누르면 바로 혼자 호스트를 시작한다.
    /// Relay 없이 로컬에서만 돌므로 로그인도 방 코드도 필요 없다.
    ///
    /// 다른 인스턴스(MPPM 가상 플레이어 등)가 이미 호스트로 포트를 잡고 있으면
    /// 호스트 시작이 실패하는데, 그대로 두면 네트워크가 꺼지며 씬의 모든 네트워크
    /// 오브젝트(플레이어 포함)가 사라진다. 그때는 그 호스트에 참가자로 붙는다.
    /// 먼저 켠 쪽이 호스트가 되므로 가상 플레이어로 바로 멀티 테스트가 된다.
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
            if (manager == null || manager.IsListening)
                return;

            if (!manager.StartHost())
                StartCoroutine(JoinAsClient(manager));
        }

        /// <summary>
        /// 실패한 호스트 시작의 뒷정리가 끝난 다음 프레임에 참가자로 붙는다.
        /// 접속하면 호스트의 씬 동기화가 사라졌던 네트워크 오브젝트를 다시 채워 준다.
        /// </summary>
        private static IEnumerator JoinAsClient(NetworkManager manager)
        {
            yield return null;
            if (manager != null && !manager.IsListening)
                manager.StartClient();
        }
    }
}
