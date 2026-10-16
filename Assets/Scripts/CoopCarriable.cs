using OverCleaning.Interaction;
using Unity.Netcode;
using UnityEngine;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 여럿이 함께 들어야 하는 무거운 물건. 정한 인원이 다 잡기 전에는 꿈쩍도 하지 않고,
    /// 다 잡으면 살짝 떠올라 잡은 사람들의 가운데를 따라간다. 한 명이라도 놓거나
    /// 멀어지면 그 자리에 내려앉는다.
    ///
    /// 자리는 서버가 옮긴다. 사람들의 위치는 이미 동기화되므로 서버가 가운데를 재서
    /// NetworkTransform으로 내려보내면 모두 같은 모습을 본다. 한 사람에게 붙이는
    /// CarriableItem과 달리 주인이 없어서 부모도 소유권도 바꾸지 않는다.
    /// </summary>
    public sealed class CoopCarriable : NetworkBehaviour, IInteractable
    {
        [Tooltip("함께 들어야 하는 인원.")]
        [Min(2)] [SerializeField] private int _requiredLifters = 2;
        [Tooltip("이보다 가까워야 잡을 수 있고, 들다가 멀어지면 놓친다.")]
        [Min(0.1f)] [SerializeField] private float _holdRange = 1.6f;
        [Tooltip("들렸을 때 떠오르는 높이.")]
        [Min(0f)] [SerializeField] private float _liftHeight = 0.25f;
        [Tooltip("가운데를 따라가는 속도. 클수록 빨리 따라붙는다.")]
        [Min(0.1f)] [SerializeField] private float _followSharpness = 6f;
        [Tooltip("안내 문구에 쓸 이름.")]
        [SerializeField] private string _itemName = "소파";

        /// <summary>잡고 있는 사람들. 서버가 넣고 빼며 모두에게 내려간다.</summary>
        private readonly NetworkList<ulong> _lifterClientIds = new NetworkList<ulong>();

        private BoxCollider _bodyCollider;

        /// <summary>서버에서만 쓴다. 내려앉을 높이와, 다 잡힌 순간 가운데에서의 간격.</summary>
        private float _restHeight;
        private Vector3 _carryOffset;
        private bool _carryEngaged;

        public bool IsFullyLifted => IsSpawned && _lifterClientIds.Count >= _requiredLifters;

        private bool IsLocalPlayerLifting =>
            IsSpawned && _lifterClientIds.Contains(NetworkManager.LocalClientId);

        /// <summary>
        /// 잡고 있으면 놓기, 아니면 자리가 남아 있고 손이 비어 있을 때만 잡기 대상이 된다.
        /// </summary>
        public bool CanInteract
        {
            get
            {
                if (!isActiveAndEnabled || !IsSpawned)
                    return false;
                if (IsLocalPlayerLifting)
                    return true;
                return _lifterClientIds.Count < _requiredLifters && !IsLocalPlayerCarryingItem;
            }
        }

        public string Prompt => IsLocalPlayerLifting
            ? $"{_itemName} 놓기"
            : $"{_itemName} 들기 ({_lifterClientIds.Count}/{_requiredLifters})";

        /// <summary>청소기나 쓰레기통을 든 손으로는 함께 들 수 없다.</summary>
        private static bool IsLocalPlayerCarryingItem
        {
            get
            {
                NetworkManager manager = NetworkManager.Singleton;
                if (manager == null || !manager.IsListening || manager.SpawnManager == null)
                    return false;
                NetworkObject player = manager.SpawnManager.GetLocalPlayerObject();
                return player != null && player.GetComponentInChildren<CarriableItem>() != null;
            }
        }

        private void Awake()
        {
            _bodyCollider = GetComponent<BoxCollider>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
                _restHeight = transform.position.y;
        }

        public void Interact()
        {
            if (IsSpawned)
                ToggleLiftRpc();
        }

        [Rpc(SendTo.Server)]
        private void ToggleLiftRpc(RpcParams rpcParams = default)
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;
            int index = _lifterClientIds.IndexOf(senderClientId);
            if (index >= 0)
            {
                _lifterClientIds.RemoveAt(index);
                return;
            }

            if (_lifterClientIds.Count >= _requiredLifters)
                return;
            Rigidbody body = FindPlayerBody(senderClientId);
            if (body == null || !IsInHoldRange(body) ||
                body.GetComponentInChildren<CarriableItem>() != null)
                return;

            _lifterClientIds.Add(senderClientId);
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer)
                return;

            PruneLifters();

            if (_lifterClientIds.Count >= _requiredLifters)
            {
                FollowLifters();
                return;
            }

            // 인원이 빠지면 그 자리에 내려앉는다.
            _carryEngaged = false;
            Vector3 position = transform.position;
            if (position.y > _restHeight + 0.001f)
            {
                position.y = Mathf.Lerp(position.y, _restHeight, 1f - Mathf.Exp(-8f * Time.deltaTime));
                transform.position = position;
            }
        }

        /// <summary>나갔거나 멀어졌거나 다른 것을 집은 사람은 잡기에서 빠진다.</summary>
        private void PruneLifters()
        {
            for (int index = _lifterClientIds.Count - 1; index >= 0; index--)
            {
                Rigidbody body = FindPlayerBody(_lifterClientIds[index]);
                if (body == null || !IsInHoldRange(body) ||
                    body.GetComponentInChildren<CarriableItem>() != null)
                    _lifterClientIds.RemoveAt(index);
            }
        }

        /// <summary>
        /// 다 잡힌 순간의 간격을 지키며 잡은 사람들의 가운데를 따라간다.
        /// 간격을 지키지 않으면 잡는 순간 물건이 가운데로 튀어 온다.
        /// </summary>
        private void FollowLifters()
        {
            Vector3 midpoint = Vector3.zero;
            for (int index = 0; index < _lifterClientIds.Count; index++)
            {
                Rigidbody body = FindPlayerBody(_lifterClientIds[index]);
                if (body == null)
                    return;
                midpoint += body.position;
            }
            midpoint /= _lifterClientIds.Count;

            if (!_carryEngaged)
            {
                _carryEngaged = true;
                _carryOffset = transform.position - midpoint;
                _carryOffset.y = 0f;
            }

            Vector3 target = midpoint + _carryOffset;
            target.y = _restHeight + _liftHeight;
            float alpha = 1f - Mathf.Exp(-_followSharpness * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, target, alpha);
        }

        private bool IsInHoldRange(Rigidbody body)
        {
            Vector3 hand = body.position + Vector3.up * 0.5f;
            return Vector3.Distance(hand, _bodyCollider.ClosestPoint(hand)) <= _holdRange;
        }

        private Rigidbody FindPlayerBody(ulong clientId)
        {
            if (NetworkManager == null || NetworkManager.SpawnManager == null)
                return null;
            NetworkObject player = NetworkManager.SpawnManager.GetPlayerNetworkObject(clientId);
            return player != null ? player.GetComponent<Rigidbody>() : null;
        }
    }
}
