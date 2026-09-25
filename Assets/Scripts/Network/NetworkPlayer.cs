using OverCleaning.Input;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OverCleaning.Network
{
    /// <summary>
    /// 스폰된 플레이어에서 자기 것이 아닌 조작을 꺼주고, 자기 것이면 키 배치를 정한다.
    /// 입력 컴포넌트가 모든 인스턴스에서 반응하면 남의 캐릭터까지 함께 움직인다.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkPlayer : NetworkBehaviour
    {
        [SerializeField] private MonoBehaviour[] _ownerOnlyBehaviours;
        [SerializeField] private PlayerInput _playerInput;
        [SerializeField] private KeyShuffleController _keyShuffle;

        public override void OnNetworkSpawn()
        {
            bool isMine = IsOwner;

            if (_playerInput != null)
                _playerInput.enabled = isMine;
            foreach (MonoBehaviour behaviour in _ownerOnlyBehaviours)
            {
                if (behaviour != null)
                    behaviour.enabled = isMine;
            }

            // 다른 사람의 캐릭터는 서버가 보내주는 위치만 따르면 되므로 물리를 맡기지 않는다.
            if (TryGetComponent(out Rigidbody body))
                body.isKinematic = !isMine;

            if (isMine)
                ApplyKeyLayout();
        }

        /// <summary>
        /// 대기방에서는 기본 배치를 쓰고, 그 밖의 스테이지 씬에서는 키를 섞는다.
        /// 씬을 옮기면 플레이어도 새로 스폰되므로 스폰될 때 한 번만 정하면 된다.
        /// 위에서 컴포넌트를 켠 뒤라야 셔플이 먹는다.
        ///
        /// 스폰되는 순간에는 활성 씬이 아직 이전 씬일 수 있으므로,
        /// 이 오브젝트가 실제로 놓인 씬을 기준으로 판단한다.
        /// </summary>
        private void ApplyKeyLayout()
        {
            if (_keyShuffle == null)
            {
                Debug.LogWarning("KeyShuffleController 참조가 비어 있어 키 배치를 정하지 못했습니다.", this);
                return;
            }

            // 스테이지마다 씬이 다를 수 있으므로 하나뿐인 대기방을 기준으로 가른다.
            if (gameObject.scene.name == GameSession.RoomSceneName)
                _keyShuffle.ResetToDefault();
            else
                _keyShuffle.ShuffleKeys();
        }
    }
}
