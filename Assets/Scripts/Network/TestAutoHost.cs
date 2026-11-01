using System.Collections;
using System.Net;
using System.Net.Sockets;
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
                // 비었는지는 우리가 미리 재 본다. NGO에 바인드를 시켰다 실패하면
                // 전송 실패 에러와 정리 경고가 콘솔에 쏟아지기 때문이다.
                if (IsPortFree(port))
                {
                    if (manager.StartHost())
                    {
                        if (offset > 0)
                            Debug.LogWarning($"기본 포트가 묶여 있어 {port}에서 호스트를 시작했습니다.");
                        yield break;
                    }

                    // 재는 사이 남이 가로챈 드문 경합이다. 다음 칸으로 넘어간다.
                    yield return null;
                    continue;
                }

                // 누가 잡고 있다. 진짜 호스트라면 참가가 붙고, 지난 플레이가 흘린
                // 죽은 소켓이라면 시간 안에 붙지 못한다.
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

        /// <summary>
        /// 포트가 비어 있는지 가벼운 소켓으로 묶어 보고 바로 놓아 확인한다.
        /// 놓은 뒤 NGO가 묶기까지의 찰나에 남이 가로챌 수 있지만, 로컬 테스트에서는
        /// 그 경합이 사실상 없고 생겨도 다음 칸으로 넘어가면 그만이다.
        /// </summary>
        private static bool IsPortFree(ushort port)
        {
            try
            {
                using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                probe.Bind(new IPEndPoint(IPAddress.Loopback, port));
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }
    }
}
