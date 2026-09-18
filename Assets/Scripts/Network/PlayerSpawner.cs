using Unity.Netcode;
using UnityEngine;

namespace OverCleaning.Network
{
    /// <summary>
    /// 접속한 사람마다 플레이어를 하나씩 스폰한다.
    /// 자동 스폰을 끄고 이 컴포넌트가 있는 씬에서만 만들어, 시작 화면에는 플레이어가 생기지 않는다.
    /// </summary>
    public sealed class PlayerSpawner : MonoBehaviour
    {
        [Tooltip("스폰 위치. 비어 있으면 이 오브젝트 자리에 겹쳐서 만든다.")]
        [SerializeField] private Transform[] _spawnPoints;

        private NetworkManager _manager;

        private void Start()
        {
            _manager = NetworkManager.Singleton;
            if (_manager == null)
            {
                Debug.LogWarning("NetworkManager가 없어 플레이어를 스폰하지 못했습니다.", this);
                return;
            }

            // 스폰은 서버만 한다. 참가자는 서버가 만든 것을 받기만 한다.
            if (!_manager.IsServer)
                return;

            _manager.OnClientConnectedCallback += SpawnFor;
            foreach (ulong clientId in _manager.ConnectedClientsIds)
                SpawnFor(clientId);
        }

        private void OnDestroy()
        {
            if (_manager != null)
                _manager.OnClientConnectedCallback -= SpawnFor;
        }

        private void SpawnFor(ulong clientId)
        {
            // 씬을 다시 들어와도 이미 있는 플레이어를 또 만들지 않는다.
            if (_manager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) &&
                client.PlayerObject != null)
                return;

            GameObject prefab = _manager.NetworkConfig.PlayerPrefab;
            if (prefab == null)
            {
                Debug.LogError("NetworkManager에 Player Prefab이 등록되어 있지 않습니다.", this);
                return;
            }

            GetSpawnPose(clientId, out Vector3 position, out Quaternion rotation);
            GameObject player = Instantiate(prefab, position, rotation);
            player.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);
        }

        /// <summary>
        /// 접속 순서대로 스폰 지점을 돌려 쓴다. 지점이 없으면 이 오브젝트 자리에 만든다.
        /// </summary>
        private void GetSpawnPose(ulong clientId, out Vector3 position, out Quaternion rotation)
        {
            if (_spawnPoints == null || _spawnPoints.Length == 0)
            {
                position = transform.position;
                rotation = transform.rotation;
                return;
            }

            Transform point = _spawnPoints[(int)(clientId % (ulong)_spawnPoints.Length)];
            position = point.position;
            rotation = point.rotation;
        }
    }
}
