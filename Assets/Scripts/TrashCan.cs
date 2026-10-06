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
    ///
    /// 계속 밀고 있으면 넘어지고 모아둔 먼지가 쏟아진다. 넘어진 동안에는 먼지를 받지 못하고,
    /// 상호작용으로 다시 세워야 한다.
    /// </summary>
    public sealed class TrashCan : CarriableItem
    {
        [Min(0.1f)] [SerializeField] private float _interactionRadius = 1.5f;
        [Tooltip("내려놓을 때 든 사람에게서 밀어낼 거리. 쓰레기통이 넓어 청소기보다 멀리 둔다.")]
        [Min(0.1f)] [SerializeField] private float _dropDistance = 1.2f;
        [Tooltip("들었을 때 올릴 높이. 너무 높이 들면 상호작용 반경에서 벗어나 내려놓지 못한다.")]
        [SerializeField] private Vector3 _heldLocalPosition = new Vector3(0f, 1.5f, 0f);

        [Tooltip("넘어질 때 먼지를 쏟을 DustField.")]
        [SerializeField] private DustField _dustField;
        [Tooltip("넘어지기까지 계속 밀고 있어야 하는 시간(초).")]
        [Min(0.1f)] [SerializeField] private float _tipOverDuration = 1.5f;
        [Tooltip("미는 것으로 칠 최소 충격량. 스치는 접촉과 구분한다. 올리면 더 세게 밀어야 한다.")]
        [Min(0.01f)] [SerializeField] private float _minimumPushImpulse = 2f;
        [Tooltip("넘어졌을 때 먼지가 쏟아질 반경.")]
        [Min(0.1f)] [SerializeField] private float _spillRadius = 2f;

        private readonly NetworkVariable<int> _receivedDust = new NetworkVariable<int>();

        /// <summary>서버가 정한다. 넘어져 있는지와 어느 쪽으로 넘어졌는지.</summary>
        private readonly NetworkVariable<bool> _isTippedOver = new NetworkVariable<bool>();
        private readonly NetworkVariable<Vector3> _fallDirection = new NetworkVariable<Vector3>();

        private Vector3 _pushDirection;
        private float _pushElapsedTime;
        private bool _isPushed;
        private bool _tipOverRequested;

        /// <summary>서버에서만 쓴다. 넘어지기 전의 자리. 세울 때 이 자리로 되돌린다.</summary>
        private Vector3 _uprightPosition;

        public int ReceivedDustCount => _receivedDust.Value;
        public bool IsTippedOver => _isTippedOver.Value;

        protected override string ItemName => "쓰레기통";
        protected override Vector3 HeldLocalPosition => _heldLocalPosition;

        public override string Prompt => IsTippedOver ? "쓰레기통 세우기" : base.Prompt;

        public override void OnNetworkSpawn()
        {
            _isTippedOver.OnValueChanged += OnTippedOverChanged;
            base.OnNetworkSpawn();
        }

        public override void OnNetworkDespawn()
        {
            _isTippedOver.OnValueChanged -= OnTippedOverChanged;
            base.OnNetworkDespawn();
        }

        public override void Interact()
        {
            // 넘어져 있으면 드는 것이 아니라 세우는 것이 먼저다.
            if (!IsTippedOver)
            {
                base.Interact();
                return;
            }
            if (IsSpawned)
                RequestStandUpRpc();
        }

        /// <summary>넘어져 있으면 들 수 없다. 세우는 것이 먼저다.</summary>
        protected override bool CanServerPickUp() => !IsTippedOver;

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
            // 들려 있거나 넘어져 있는 동안에는 받지 않는다.
            if (IsHeld || IsTippedOver || !isActiveAndEnabled || playerBody == null ||
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

        /// <summary>
        /// 미는 힘이 이어지는 동안에만 시간을 쌓는다.
        /// OnCollisionStay는 물리 단계가 끝난 뒤에 오므로 한 단계 늦게 반영된다.
        ///
        /// 미는 판정은 미는 사람의 기기에서 한다. 남의 캐릭터는 위치만 따라올 뿐이라
        /// 그 기기에서는 밀어붙이는 힘이 제대로 잡히지 않는다.
        /// </summary>
        private void FixedUpdate()
        {
            if (!_isPushed)
            {
                // 서버가 요청을 물리쳤을 수도 있다. 손을 뗐다가 다시 밀면 새로 시도한다.
                _pushElapsedTime = 0f;
                _tipOverRequested = false;
                return;
            }

            // 다음 물리 단계에서 다시 밀려야 이어진다. 손을 떼면 처음부터 다시다.
            _isPushed = false;
            _pushElapsedTime += Time.fixedDeltaTime;
            if (_pushElapsedTime < _tipOverDuration || _tipOverRequested)
                return;

            // 서버가 넘어졌다고 알려줄 때까지 같은 요청을 거듭 보내지 않는다.
            _tipOverRequested = true;
            _pushElapsedTime = 0f;
            RequestTipOverRpc(_pushDirection);
        }

        private void OnCollisionStay(Collision collision)
        {
            if (!IsSpawned || IsHeld || IsTippedOver || collision.rigidbody == null)
                return;
            // 내 캐릭터가 미는 것만 센다. 남의 캐릭터는 각자의 기기가 센다.
            NetworkObject pusher = collision.rigidbody.GetComponent<NetworkObject>();
            if (pusher == null || !pusher.IsLocalPlayer)
                return;
            // 밀어붙이면 속도는 물리 해석에서 깎이지만 충격량은 계속 발생한다.
            // 스쳐 지나가는 접촉과 버티고 미는 것을 이것으로 가른다.
            if (collision.impulse.magnitude < _minimumPushImpulse)
                return;

            Vector3 direction = _bodyCollider.bounds.center - collision.rigidbody.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.000001f)
                return;

            _pushDirection = direction.normalized;
            _isPushed = true;
        }

        /// <summary>넘어뜨리기를 서버가 판정한다. 넘어지는 방향은 민 사람이 알려준다.</summary>
        [Rpc(SendTo.Server)]
        private void RequestTipOverRpc(Vector3 pushDirection)
        {
            if (_isTippedOver.Value || IsHeld)
                return;

            // 넘어지는 방향은 네 방향 중 미는 쪽에 가장 가까운 것으로 맞춘다. 상자 모서리를
            // 축으로 정확히 눕힐 수 있고, 위에서 내려다보는 화면에서는 차이가 드러나지 않는다.
            _fallDirection.Value = Mathf.Abs(pushDirection.x) >= Mathf.Abs(pushDirection.z)
                ? new Vector3(Mathf.Sign(pushDirection.x), 0f, 0f)
                : new Vector3(0f, 0f, Mathf.Sign(pushDirection.z));
            _isTippedOver.Value = true;

            SpillReceivedDust();
        }

        [Rpc(SendTo.Server)]
        private void RequestStandUpRpc()
        {
            _isTippedOver.Value = false;
        }

        private void OnTippedOverChanged(bool previousValue, bool newValue)
        {
            _tipOverRequested = newValue;
            _pushElapsedTime = 0f;

            // 자세는 서버만 바꾼다. 서버가 권한자라 NetworkTransform이 모든 기기에
            // (늦게 들어온 사람 포함) 그대로 맞춰 준다.
            if (IsServer)
                ApplyTipPose(newValue);
        }

        /// <summary>
        /// 넘어진 자세를 만들거나 되돌린다. 선 자세에서 밑 모서리를 축 삼아 눕히고,
        /// 세울 때는 넘어지기 전 자리로 되돌린다.
        /// </summary>
        private void ApplyTipPose(bool tipped)
        {
            if (!tipped)
            {
                transform.SetPositionAndRotation(_uprightPosition, Quaternion.identity);
                Physics.SyncTransforms();
                return;
            }

            _uprightPosition = transform.position;
            transform.rotation = Quaternion.identity;
            Physics.SyncTransforms();

            Vector3 fallDirection = _fallDirection.Value;
            if (fallDirection.sqrMagnitude < 0.000001f)
                return;

            Bounds bounds = _bodyCollider.bounds;
            Vector3 pivot = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) +
                Vector3.Scale(fallDirection, bounds.extents);
            transform.RotateAround(pivot, Vector3.Cross(Vector3.up, fallDirection), 90f);
            Physics.SyncTransforms();
        }

        /// <summary>서버에서만 불린다. 모아둔 먼지를 쏟아 바닥에 되돌린다.</summary>
        private void SpillReceivedDust()
        {
            int spillCount = _receivedDust.Value;
            if (spillCount <= 0)
                return;

            _receivedDust.Value = 0;
            if (_dustField == null)
                return;

            // 좌표를 하나하나 보내는 대신 시드를 내려보낸다. 같은 시드로 계산하면 모두 같은
            // 자리에 같은 번호로 먼지가 놓인다. DustField가 먼지 배치를 맞추는 방식과 같다.
            SpillDustRpc(_bodyCollider.bounds.center, spillCount, Random.Range(1, int.MaxValue));
        }

        [Rpc(SendTo.Everyone)]
        private void SpillDustRpc(Vector3 center, int count, int seed)
        {
            if (_dustField != null)
                _dustField.SpillDust(center, _spillRadius, count, seed);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.5f, _interactionRadius);
        }
    }
}
