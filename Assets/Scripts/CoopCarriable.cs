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
    ///
    /// 혼자 잡은 동안은 제자리에 묶인다 — 소파는 꿈쩍도 않는데 사람만 돌아다니면
    /// 잡은 게 무의미해진다. 다 같이 들면 풀려서 함께 걸으며 옮긴다. 제 캐릭터는
    /// 제 기기가 움직이므로 묶는 것도 각자의 기기가 한다. 놓으면 바로 풀린다.
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
        [Tooltip("일부만 잡았을 때 잡은 쪽이 들리는 각도(도). 자리는 그대로고 기울기만 바뀐다.")]
        [Min(0f)] [SerializeField] private float _partialTiltAngle = 7f;

        /// <summary>잡고 있는 사람들. 서버가 넣고 빼며 모두에게 내려간다.</summary>
        private readonly NetworkList<ulong> _lifterClientIds = new NetworkList<ulong>();

        private BoxCollider _bodyCollider;

        /// <summary>서버에서만 쓴다. 내려앉을 높이·방향과, 다 잡힌 순간의 자세.</summary>
        private float _restHeight;
        private Quaternion _restRotation = Quaternion.identity;
        private bool _carryEngaged;

        /// <summary>
        /// 서버에서만 쓴다. 잡은 사람들을 잇는 선을 기준 틀로 삼아, 다 잡힌 순간의
        /// 간격과 각도를 그 틀 안에서 기억한다. 사람들이 선 채로 돌면 틀이 돌고,
        /// 물건도 간격과 각도를 지킨 채 함께 돈다.
        /// </summary>
        private Quaternion _lifterAxisRotation;
        private Vector3 _carryLocalOffset;
        private Quaternion _carryLocalRotation;

        /// <summary>이 기기에서만 쓴다. 내 캐릭터를 제자리에 묶어 두었는지.</summary>
        private bool _localLocked;

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
            if (!IsServer)
                return;
            _restHeight = transform.position.y;
            _restRotation = transform.rotation;
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

            if (_carryEngaged)
            {
                // 함께 들기가 끝났다. 내려앉은 지금 방향이 새 제자리다.
                _carryEngaged = false;
                _restRotation = transform.rotation;
            }

            UpdateRestPose();
        }

        /// <summary>
        /// 다 들리기 전의 자세. 일부만 잡았으면 잡은 쪽 모서리가 살짝 들려 "잡혔다"는
        /// 티를 내고, 아무도 없으면 제자리에 내려앉는다. 자리는 그대로다 — 기울기만 바뀐다.
        /// </summary>
        private void UpdateRestPose()
        {
            Quaternion targetRotation = _restRotation;
            float targetHeight = _restHeight;

            if (_lifterClientIds.Count > 0)
            {
                Vector3 midpoint = Vector3.zero;
                int counted = 0;
                for (int index = 0; index < _lifterClientIds.Count; index++)
                {
                    Rigidbody body = FindPlayerBody(_lifterClientIds[index]);
                    if (body == null)
                        continue;
                    midpoint += body.position;
                    counted++;
                }

                if (counted > 0)
                {
                    Vector3 towardLifter = midpoint / counted - transform.position;
                    towardLifter.y = 0f;
                    if (towardLifter.sqrMagnitude > 0.0001f)
                    {
                        // 잡은 쪽이 올라가는 축으로 기울이고, 반대쪽 모서리가
                        // 바닥을 파고들지 않게 그만큼 살짝 띄운다.
                        Vector3 axis = Vector3.Cross(Vector3.up, towardLifter.normalized);
                        targetRotation = Quaternion.AngleAxis(-_partialTiltAngle, axis) * _restRotation;
                        targetHeight = _restHeight + 0.08f;
                    }
                }
            }

            float alpha = 1f - Mathf.Exp(-8f * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, alpha);
            Vector3 position = transform.position;
            position.y = Mathf.Lerp(position.y, targetHeight, alpha);
            transform.position = position;
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
        /// 다 잡힌 순간의 간격과 각도를 지키며 잡은 사람들의 가운데를 따라간다.
        /// 간격을 지키지 않으면 잡는 순간 물건이 가운데로 튀어 오고,
        /// 각도를 지키지 않으면 사람들이 돌아도 물건이 돌지 않는다.
        /// </summary>
        private void FollowLifters()
        {
            Vector3 midpoint = Vector3.zero;
            Vector3 firstLifter = Vector3.zero;
            Vector3 lastLifter = Vector3.zero;
            for (int index = 0; index < _lifterClientIds.Count; index++)
            {
                Rigidbody body = FindPlayerBody(_lifterClientIds[index]);
                if (body == null)
                    return;
                midpoint += body.position;
                if (index == 0)
                    firstLifter = body.position;
                lastLifter = body.position;
            }
            midpoint /= _lifterClientIds.Count;

            // 잡은 사람들을 잇는 선이 기준 틀이다. 겹쳐 서서 선이 사라지는 동안은
            // 마지막 틀을 그대로 써서 물건이 튀지 않게 한다.
            Vector3 axis = lastLifter - firstLifter;
            axis.y = 0f;
            bool hasAxis = axis.sqrMagnitude > 0.0001f;

            if (!_carryEngaged)
            {
                _carryEngaged = true;
                _lifterAxisRotation = hasAxis
                    ? Quaternion.LookRotation(axis, Vector3.up)
                    : Quaternion.identity;
                Quaternion inverseAxis = Quaternion.Inverse(_lifterAxisRotation);
                Vector3 offset = transform.position - midpoint;
                offset.y = 0f;
                _carryLocalOffset = inverseAxis * offset;
                // 일부만 잡아 기울어 있던 자세가 아니라 반듯한 제 자세 기준으로 든다.
                // 다 잡히면 기운 채 올라가지 않고 수평으로 펴지며 떠오른다.
                _carryLocalRotation = inverseAxis * _restRotation;
            }
            else if (hasAxis)
            {
                _lifterAxisRotation = Quaternion.LookRotation(axis, Vector3.up);
            }

            Vector3 target = midpoint + _lifterAxisRotation * _carryLocalOffset;
            target.y = _restHeight + _liftHeight;
            float alpha = 1f - Mathf.Exp(-_followSharpness * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, target, alpha);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, _lifterAxisRotation * _carryLocalRotation, alpha);
        }

        /// <summary>
        /// 혼자 잡은 동안 내 캐릭터를 제자리에 묶는다. 다 같이 들면 풀려서
        /// 함께 옮길 수 있고, 놓거나 잡기가 풀리면(밀려남, 끊김 포함) 바로 풀어 준다.
        /// </summary>
        private void FixedUpdate()
        {
            bool shouldLock = IsSpawned && IsLocalPlayerLifting && !IsFullyLifted;
            if (shouldLock == _localLocked)
                return;

            PlayerMovement movement = LocalPlayerMovement;
            if (movement == null)
                return;
            _localLocked = shouldLock;
            movement.MovementLocked = shouldLock;
        }

        public override void OnNetworkDespawn()
        {
            // 판이 끝나며 사라져도 묶인 채 남지 않게 풀어 둔다.
            if (_localLocked)
            {
                PlayerMovement movement = LocalPlayerMovement;
                if (movement != null)
                    movement.MovementLocked = false;
                _localLocked = false;
            }
        }

        private static PlayerMovement LocalPlayerMovement
        {
            get
            {
                Rigidbody body = LocalPlayerBody;
                return body != null ? body.GetComponent<PlayerMovement>() : null;
            }
        }

        private static Rigidbody LocalPlayerBody
        {
            get
            {
                NetworkManager manager = NetworkManager.Singleton;
                if (manager == null || !manager.IsListening || manager.SpawnManager == null)
                    return null;
                NetworkObject player = manager.SpawnManager.GetLocalPlayerObject();
                return player != null ? player.GetComponent<Rigidbody>() : null;
            }
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
