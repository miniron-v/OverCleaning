using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace OverCleaning.Network
{
    /// <summary>
    /// 접속한 사람마다 플레이어를 하나씩 스폰한다.
    /// 이 컴포넌트가 있는 씬에서만 만들어, 시작 화면에는 플레이어가 생기지 않는다.
    ///
    /// 프리팹을 NetworkManager에 등록하지 않고 여기서 들고 있는 이유가 있다.
    /// 등록해두면 호스트가 접속하는 순간 Netcode가 시작 화면에서 먼저 스폰해버린다.
    /// </summary>
    public sealed class PlayerSpawner : MonoBehaviour
    {
        [Tooltip("스폰할 플레이어 프리팹. NetworkObject가 있어야 한다.")]
        [SerializeField] private GameObject _playerPrefab;

        [Tooltip("스폰 위치. 비어 있으면 이 오브젝트 자리에 겹쳐서 만든다.")]
        [SerializeField] private Transform[] _spawnPoints;

        private NetworkManager _manager;

        private IEnumerator Start()
        {
            _manager = NetworkManager.Singleton;
            if (_manager == null)
            {
                Debug.LogWarning("NetworkManager가 없어 플레이어를 스폰하지 못했습니다.", this);
                yield break;
            }

            // 스폰은 서버만 한다. 참가자는 서버가 만든 것을 받기만 한다.
            if (!_manager.IsServer)
                yield break;

            // 이전 씬의 플레이어가 정리되기를 한 프레임 기다린다.
            // 아직 남아 있으면 이미 있는 것으로 보고 새로 만들지 않는다.
            yield return null;

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

            if (_playerPrefab == null)
            {
                Debug.LogError("PlayerSpawner에 플레이어 프리팹이 지정되어 있지 않습니다.", this);
                return;
            }

            GetSpawnPose(clientId, out Vector3 position, out Quaternion rotation);
            GameObject player = Instantiate(_playerPrefab, position, rotation);
            // 씬이 바뀌면 함께 사라지게 한다. 남겨두면 다음 씬에 이전 위치 그대로 따라와
            // 스폰 지점도 무시되고 키 배치를 다시 정할 기회도 없다.
            player.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, destroyWithScene: true);
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
