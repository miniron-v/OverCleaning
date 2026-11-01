using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace OverCleaning.Network
{
    /// <summary>
    /// 테스트 씬 전용. Play를 누르면 로비 없이 바로 시작한다. 혼자면 호스트가 되고,
    /// 이미 호스트가 있으면(MPPM 가상 플레이어 등) 참가자로 들어간다.
    ///
    /// 포트는 사다리(기본 포트부터 차례로)를 탄다. 빈 포트면 내가 호스트, 누가 잡고
    /// 있으면 참가를 시도하고, 시간 안에 붙지 못하는 죽은 포트(지난 플레이가 흘린
    /// 소켓)면 다음 칸으로 넘어간다. 모든 인스턴스가 같은 사다리를 타므로 늦게 켠
    /// 쪽도 호스트가 어느 칸에 있든 찾아 들어간다.
    ///
    /// PlayerSpawner보다 먼저 돌아야 한다. 스포너가 먼저 깨어나 서버가 아니라고 보면
    /// 그대로 포기해 플레이어가 스폰되지 않는다. Awake가 아니라 Start인 것도 이유가 있다.
    /// 호스트 시작은 씬의 네트워크 오브젝트를 바로 스폰시키는데, 그보다 앞서 모든
    /// Awake가 끝나 있어야 한다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class TestAutoHost : MonoBehaviour
    {
        private const ushort BasePort = 7777;

        /// <summary>사다리 칸 수. 흘린 소켓이 아무리 쌓여도 이 안에서 빈 칸을 찾는다.</summary>
        private const int PortAttempts = 8;

        /// <summary>참가가 이 시간(초) 안에 붙지 못하면 호스트 없는 죽은 포트로 본다.</summary>
        private const float JoinTimeout = 2f;

        private void Start()
        {
            StartCoroutine(Bootstrap());
        }

        private static IEnumerator Bootstrap()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || manager.IsListening)
                yield break;

            var transport = manager.GetComponent<UnityTransport>();
            if (transport == null)
            {
                // 포트를 다룰 수 없는 구성이면 예전처럼 한 번만 시도한다.
                manager.StartHost();
                yield break;
            }

            for (int offset = 0; offset < PortAttempts; offset++)
            {
                ushort port = (ushort)(BasePort + offset);
                transport.SetConnectionData("127.0.0.1", port);

                // 빈 포트면 내가 호스트다. 혼자서도 바로 시작된다.
                if (manager.StartHost())
                {
                    if (offset > 0)
                        Debug.LogWarning($"기본 포트가 묶여 있어 {port}에서 호스트를 시작했습니다.");
                    yield break;
                }

                // 누가 잡고 있다. 그 호스트에 참가해 본다. 실패한 호스트 시작의
                // 뒷정리가 끝나도록 한 프레임 쉬고 시작한다.
                yield return null;
                manager.StartClient();
                float deadline = Time.realtimeSinceStartup + JoinTimeout;
                while (Time.realtimeSinceStartup < deadline && !manager.IsConnectedClient)
                    yield return null;
                if (manager.IsConnectedClient)
                    yield break;

                // 죽은 포트였다. 참가를 접고 다음 칸으로 넘어간다.
                manager.Shutdown();
                while (manager.ShutdownInProgress)
                    yield return null;
                yield return null;
            }

            Debug.LogError("호스트도 참가도 하지 못했습니다. 에디터를 재시작하세요.");
        }
    }
}
