using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OverCleaning.Network
{
    /// <summary>
    /// 스폰된 플레이어에서 자기 것이 아닌 조작을 꺼준다.
    /// 입력 컴포넌트가 모든 인스턴스에서 반응하면 남의 캐릭터까지 함께 움직인다.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkPlayer : NetworkBehaviour
    {
        [SerializeField] private MonoBehaviour[] _ownerOnlyBehaviours;
        [SerializeField] private PlayerInput _playerInput;

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
        }
    }
}
