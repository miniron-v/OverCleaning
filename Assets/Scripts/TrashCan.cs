using UnityEngine;

namespace OverCleaning.InGame
{
    [RequireComponent(typeof(Collider))]
    public sealed class TrashCan : MonoBehaviour
    {
        [Min(0.1f)] [SerializeField] private float _interactionRadius = 1.5f;
        [SerializeField] private LayerMask _obstacleLayers = ~0;

        private Collider _bodyCollider;
        private readonly RaycastHit[] _castBuffer = new RaycastHit[32];

        public int ReceivedDustCount { get; private set; }

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

        public bool TryReceiveDust(Vector3 position, Rigidbody playerBody, int amount)
        {
            if (amount <= 0 || !CanReceiveDust(position, playerBody))
                return false;
            ReceivedDustCount += amount;
            return true;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.5f, _interactionRadius);
        }
    }
}
