using Unity.Netcode;
using UnityEngine;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 버린 먼지를 모으는 쓰레기통. 누적량은 모두가 같은 숫자를 봐야 하므로 서버가 갖는다.
    /// 거리와 벽 판정은 계산일 뿐이라 어디서 불러도 되지만, 실제 누적은 서버에서만 일어난다.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class TrashCan : NetworkBehaviour
    {
        [Min(0.1f)] [SerializeField] private float _interactionRadius = 1.5f;
        [SerializeField] private LayerMask _obstacleLayers = ~0;

        private Collider _bodyCollider;
        private readonly RaycastHit[] _castBuffer = new RaycastHit[32];

        private readonly NetworkVariable<int> _receivedDust = new NetworkVariable<int>();

        public int ReceivedDustCount => _receivedDust.Value;

        private void Awake() => _bodyCollider = GetComponent<Collider>();

        public bool CanReceiveDust(Vector3 position, Rigidbody playerBody)
        {
            if (!isActiveAndEnabled || playerBody == null || _bodyCollider == null || !_bodyCollider.enabled)
                return false;
            if (Vector3.Distance(position, _bodyCollider.ClosestPoint(position)) > _interactionRadius)
                return false;

            // 가까워도 벽 반대편에 있는 쓰레기통에는 버릴 수 없습니다.
            Vector3 destination = _bodyCollider.bounds.center;
            destination.y = _bodyCollider.bounds.max.y + 0.05f;
            Vector3 direction = destination - position;
            if (direction.sqrMagnitude < 0.000001f)
                return true;
            int count = Physics.RaycastNonAlloc(position, direction.normalized, _castBuffer,
                direction.magnitude, _obstacleLayers, QueryTriggerInteraction.Ignore);
            if (count == _castBuffer.Length)
                return false;
            for (int index = 0; index < count; index++)
            {
                Collider obstacle = _castBuffer[index].collider;
                if (obstacle != _bodyCollider && obstacle.attachedRigidbody != playerBody)
                    return false;
            }

            return true;
        }

        /// <summary>먼지를 받아 쌓는다. 서버에서만 통한다.</summary>
        public bool TryReceiveDust(Vector3 position, Rigidbody playerBody, int amount)
        {
            if (!IsServer || amount <= 0 || !CanReceiveDust(position, playerBody))
                return false;
            _receivedDust.Value += amount;
            return true;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.5f, _interactionRadius);
        }
    }
}
