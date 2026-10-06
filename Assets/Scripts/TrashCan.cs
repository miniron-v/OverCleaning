using Unity.Netcode;
using UnityEngine;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 버린 먼지를 모으는 쓰레기통. 누적량은 모두가 같은 숫자를 봐야 하므로 서버가 갖는다.
    /// 거리와 벽 판정은 계산일 뿐이라 어디서 불러도 되지만, 실제 누적은 서버에서만 일어난다.
    ///
    /// 들고 옮길 수 있다. 드는 동안에는 먼지를 받지 않는다.
    /// 청소기를 쥔 손과 쓰레기통을 든 손이 같아서, 들고 다니며 버릴 수는 없다.
    /// </summary>
    public sealed class TrashCan : CarriableItem
    {
        [Min(0.1f)] [SerializeField] private float _interactionRadius = 1.5f;
        [Tooltip("내려놓을 때 든 사람에게서 밀어낼 거리. 쓰레기통이 넓어 청소기보다 멀리 둔다.")]
        [Min(0.1f)] [SerializeField] private float _dropDistance = 1.2f;
        [Tooltip("들었을 때 올릴 높이. 너무 높이 들면 상호작용 반경에서 벗어나 내려놓지 못한다.")]
        [SerializeField] private Vector3 _heldLocalPosition = new Vector3(0f, 1.5f, 0f);

        private readonly NetworkVariable<int> _receivedDust = new NetworkVariable<int>();

        public int ReceivedDustCount => _receivedDust.Value;

        protected override string ItemName => "쓰레기통";
        protected override Vector3 HeldLocalPosition => _heldLocalPosition;

        /// <summary>
        /// 쓰레기통은 청소기와 달리 든 사람 쪽으로 돌지 않으므로 제 앞이 아니라
        /// 든 사람이 보는 쪽에 내려놓는다.
        /// </summary>
        protected override Vector3 DropOffset
        {
            get
            {
                PlayerMovement holder = HolderBody.GetComponent<PlayerMovement>();
                Vector3 direction = holder != null ? holder.FacingDirection : transform.forward;
                return direction * _dropDistance;
            }
        }

        public bool CanReceiveDust(Vector3 position, Rigidbody playerBody)
        {
            // 들려 있는 동안에는 받지 않는다.
            if (IsHeld || !isActiveAndEnabled || playerBody == null ||
                _bodyCollider == null || !_bodyCollider.enabled)
                return false;
            if (Vector3.Distance(position, _bodyCollider.ClosestPoint(position)) > _interactionRadius)
                return false;

            // 가까워도 벽 반대편에 있는 쓰레기통에는 버릴 수 없다.
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
                if (!IsIgnoredCollider(_castBuffer[index].collider, playerBody))
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
