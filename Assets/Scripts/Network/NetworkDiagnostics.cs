using System;
using System.IO;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OverCleaning.Network
{
    /// <summary>
    /// 특정 환경에서 한 사람만 팅기는 원인을 찾기 위해 실행마다 따로 로그 파일을 남긴다.
    /// Player.log는 다음 실행 때 덮어써지고 시각이 없으므로, 시각과 함께 접속 상태·지연·멈춤을 따로 기록한다.
    /// 파일 위치: %USERPROFILE%\AppData\LocalLow\{회사}\{제품}\NetworkLogs
    /// </summary>
    public sealed class NetworkDiagnostics : MonoBehaviour
    {
        private const string FolderName = "NetworkLogs";
        private const int KeptFileCount = 10;
        private const float StatusIntervalSeconds = 5f;

        /// <summary>이보다 긴 프레임은 하트비트가 밀려 끊김으로 이어질 수 있어 따로 남긴다.</summary>
        private const float HitchSeconds = 0.5f;

        private static readonly object _writeLock = new object();
        private static StreamWriter _writer;

        private NetworkManager _networkManager;
        private ISession _session;
        private float _nextStatusTime;
        private int _framesSinceStatus;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Create()
        {
            OpenLogFile();
            GameObject host = new GameObject(nameof(NetworkDiagnostics));
            DontDestroyOnLoad(host);
            host.AddComponent<NetworkDiagnostics>();
        }

        private static void OpenLogFile()
        {
            try
            {
                string folder = Path.Combine(Application.persistentDataPath, FolderName);
                Directory.CreateDirectory(folder);
                DeleteOldFiles(folder);

                string path = Path.Combine(folder, $"{DateTime.Now:yyyyMMdd_HHmmss}_{System.Diagnostics.Process.GetCurrentProcess().Id}.log");
                // 강제 종료되어도 마지막 줄까지 남도록 줄마다 바로 쓴다.
                _writer = new StreamWriter(path, false) { AutoFlush = true };
                Application.logMessageReceivedThreaded += OnLogMessage;

                Write($"버전 {Application.version} / Unity {Application.unityVersion}");
                Write($"OS {SystemInfo.operatingSystem} / CPU {SystemInfo.processorType} / " +
                      $"RAM {SystemInfo.systemMemorySize}MB / GPU {SystemInfo.graphicsDeviceName}");
                Debug.Log($"네트워크 진단 로그: {path}");
            }
            catch (Exception exception)
            {
                // 진단 기록 실패로 게임까지 막히면 안 되므로 경고만 남긴다.
                Debug.LogWarning($"네트워크 진단 로그를 열지 못했습니다: {exception.Message}");
            }
        }

        private static void DeleteOldFiles(string folder)
        {
            // 파일 이름이 시각이라 이름순이 곧 시간순이다. 새로 만들 하나를 위해 자리를 비워둔다.
            string[] files = Directory.GetFiles(folder, "*.log").OrderByDescending(file => file).ToArray();
            foreach (string file in files.Skip(KeptFileCount - 1))
                File.Delete(file);
        }

        private static void OnLogMessage(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Log)
                Write(message);
            else if (type == LogType.Warning)
                Write($"[{type}] {message}");
            else
                Write($"[{type}] {message}\n{stackTrace}");
        }

        /// <summary>로그 콜백은 다른 스레드에서도 오므로 잠그고 쓴다.</summary>
        private static void Write(string line)
        {
            lock (_writeLock)
            {
                _writer?.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {line}");
            }
        }

        private void Awake()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void Update()
        {
            // NetworkManager와 세션은 시작 화면 이후에 생기므로 생길 때를 기다려 이벤트를 단다.
            if (_networkManager == null && NetworkManager.Singleton != null)
                WatchNetworkManager(NetworkManager.Singleton);
            if (_session != GameSession.Current)
                WatchSession(GameSession.Current);

            float deltaTime = Time.unscaledDeltaTime;
            if (deltaTime > HitchSeconds)
                Write($"[멈춤] 프레임 {deltaTime:F2}초");

            _framesSinceStatus++;
            if (Time.unscaledTime >= _nextStatusTime)
            {
                WriteStatus();
                _nextStatusTime = Time.unscaledTime + StatusIntervalSeconds;
                _framesSinceStatus = 0;
            }
        }

        private void WatchNetworkManager(NetworkManager manager)
        {
            _networkManager = manager;
            manager.OnConnectionEvent += OnConnectionEvent;
            manager.OnTransportFailure += () => Write("[전송 실패] Transport가 실패해 연결이 종료됩니다.");
            manager.OnClientStopped += wasHost =>
                Write($"[중지] 클라이언트 중지 (호스트였나: {wasHost}) 사유: {DescribeDisconnectReason()}");
            manager.OnServerStopped += wasHost => Write($"[중지] 서버 중지 (호스트였나: {wasHost})");

            if (manager.NetworkConfig.NetworkTransport is UnityTransport transport)
            {
                Write($"Transport 설정: 하트비트 {transport.HeartbeatTimeoutMS}ms, " +
                      $"끊김 판정 {transport.DisconnectTimeoutMS}ms, 송신 큐 {transport.MaxPacketQueueSize}, " +
                      $"최대 페이로드 {transport.MaxPayloadSize}");
            }
        }

        private void OnConnectionEvent(NetworkManager manager, ConnectionEventData data)
        {
            string line = $"[접속] {data.EventType} clientId={data.ClientId} (나={manager.LocalClientId})";
            if (data.EventType == ConnectionEvent.ClientDisconnected)
                line += $" 사유: {DescribeDisconnectReason()}";
            Write(line);
        }

        private string DescribeDisconnectReason()
        {
            string reason = _networkManager != null ? _networkManager.DisconnectReason : null;
            return string.IsNullOrEmpty(reason) ? "(없음: 타임아웃이나 전송 끊김일 가능성)" : reason;
        }

        private void WatchSession(ISession session)
        {
            _session = session;
            if (session == null)
            {
                Write("[세션] 세션 없음");
                return;
            }

            Write($"[세션] 코드 {session.Code}, 방장인가: {session.IsHost}, 내 ID: {session.CurrentPlayer?.Id}");
            session.StateChanged += state => Write($"[세션] 상태 변경: {state}");
            session.RemovedFromSession += () => Write("[세션] 세션에서 제거됨");
            session.Deleted += () => Write("[세션] 세션이 삭제됨");
            session.SessionHostChanged += hostId => Write($"[세션] 방장 변경: {hostId}");
            session.PlayerHasLeft += playerId => Write($"[세션] 참가자 나감: {playerId}");
        }

        /// <summary>끊기기 직전의 흐름을 보려고 지연과 프레임을 주기적으로 남긴다.</summary>
        private void WriteStatus()
        {
            if (_networkManager == null || !_networkManager.IsListening)
                return;

            float framesPerSecond = _framesSinceStatus / StatusIntervalSeconds;
            NetworkTransport transport = _networkManager.NetworkConfig.NetworkTransport;
            string roundTrips;
            if (_networkManager.IsServer)
            {
                roundTrips = string.Join(", ", _networkManager.ConnectedClientsIds
                    .Where(clientId => clientId != _networkManager.LocalClientId)
                    .Select(clientId => $"{clientId}:{transport.GetCurrentRtt(clientId)}ms"));
            }
            else
            {
                roundTrips = $"서버:{transport.GetCurrentRtt(NetworkManager.ServerClientId)}ms";
            }

            Write($"[상태] FPS {framesPerSecond:F0}, RTT [{roundTrips}], " +
                  $"메모리 {GC.GetTotalMemory(false) / (1024 * 1024)}MB");
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Write($"[씬] {scene.name} 로드");

        private void OnApplicationFocus(bool hasFocus) =>
            Write($"[창] 포커스 {(hasFocus ? "얻음" : "잃음")} (백그라운드 실행: {Application.runInBackground})");

        private void OnApplicationPause(bool isPaused) => Write($"[창] 일시정지 {isPaused}");

        private void OnApplicationQuit()
        {
            Write("[종료] 정상 종료");
            lock (_writeLock)
            {
                Application.logMessageReceivedThreaded -= OnLogMessage;
                _writer?.Dispose();
                _writer = null;
            }
        }
    }
}
