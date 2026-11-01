using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace OverCleaning.Network
{
    /// <summary>
    /// 테스트 씬 전용. 로비를 거치지 않고 Play를 누르면 바로 혼자 호스트를 시작한다.
    /// Relay 없이 로컬에서만 돌므로 로그인도 방 코드도 필요 없다.
    ///
    /// 기본 포트가 막혀 있으면 호스트 시작이 실패한다. 다른 인스턴스(MPPM 가상
    /// 플레이어)가 호스트일 수 있으니 먼저 참가를 시도하고, 시간 안에 붙지 못하면
    /// 죽은 포트(지난 플레이가 흘린 소켓)로 보고 임시 포트로 혼자 연다.
    /// 그대로 두면 네트워크가 꺼진 채라 플레이어가 아예 스폰되지 않는다.
    ///
    /// PlayerSpawner보다 먼저 돌아야 한다. 스포너가 먼저 깨어나 서버가 아니라고 보면
    /// 그대로 포기해 플레이어가 스폰되지 않는다. Awake가 아니라 Start인 것도 이유가 있다.
    /// 호스트 시작은 씬의 네트워크 오브젝트를 바로 스폰시키는데, 그보다 앞서 모든
    /// Awake가 끝나 있어야 한다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class TestAutoHost : MonoBehaviour
    {
        /// <summary>참가가 이 시간(초) 안에 붙지 못하면 호스트가 없는 죽은 포트로 본다.</summary>
        private const float JoinTimeout = 3f;

        private void Start()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || manager.IsListening)
                return;

            if (!manager.StartHost())
                StartCoroutine(JoinOrHostAlone(manager));
        }

        /// <summary>
        /// 실패한 호스트 시작의 뒷정리가 끝난 다음 프레임에 참가자로 붙어 보고,
        /// 붙지 못하면 임시 포트로 혼자 시작한다. 접속에 성공하면 호스트의 씬 동기화가
        /// 사라졌던 네트워크 오브젝트를 다시 채워 준다.
        /// </summary>
        private static IEnumerator JoinOrHostAlone(NetworkManager manager)
        {
            yield return null;
            if (manager == null || manager.IsListening == false)
                manager.StartClient();

            float deadline = Time.realtimeSinceStartup + JoinTimeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (manager.IsConnectedClient)
                    yield break;
                yield return null;
            }

            // 호스트가 없다. 지난 플레이가 흘린 소켓이 포트를 쥐고 있을 뿐이다.
            manager.Shutdown();
            while (manager.ShutdownInProgress)
                yield return null;
            yield return null;

            var transport = manager.GetComponent<UnityTransport>();
            if (transport != null)
                transport.SetConnectionData("127.0.0.1", (ushort)Random.Range(7800, 7900));

            if (manager.StartHost())
                Debug.LogWarning("기본 포트가 묶여 있어 임시 포트로 혼자 시작했습니다. " +
                    "가상 플레이어와 함께 테스트하려면 에디터를 재시작하세요.");
            else
                Debug.LogError("테스트 호스트를 열지 못했습니다. 에디터를 재시작하세요.");
        }
    }
}
