using System.Collections.Generic;
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
    /// 잡은 게 무의미해진다. 다 같이 들면 느린 걸음으로 함께 옮기되, 각자 잡은
    /// 자리에서 벗어나지 못한다 — 놓기 전에는 물건과 떨어질 수 없고, 사람들만
    /// 앞서가며 물건이 공중에 떠서 따라오는 모습이 나오지 않는다.
    /// 제 캐릭터는 제 기기가 움직이므로 묶고 붙드는 것도 각자의 기기가 한다.
    /// </summary>
    [DefaultExecutionOrder(100)] // PlayerMovement가 속도를 정한 뒤에 붙드는 보정을 한다.
    public sealed class CoopCarriable : NetworkBehaviour, IInteractable, IHighlightable
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
        [Tooltip("다 같이 들고 옮기는 동안의 이동 속도 배율. 물건이 따라올 수 있는 만큼만 걷게 한다.")]
        [Range(0.1f, 1f)] [SerializeField] private float _carrySpeedMultiplier = 0.55f;
        [Tooltip("든 동안 잡은 자리에서 벗어날 수 있는 거리. 작을수록 딱 붙는다.")]
        [Min(0.05f)] [SerializeField] private float _tetherRadius = 0.8f;
        [Tooltip("들려 가는 동안 막혀야 하는 벽과 장애물 레이어.")]
        [SerializeField] private LayerMask _obstacleLayers = ~0;

        /// <summary>잡고 있는 사람들. 서버가 넣고 빼며 모두에게 내려간다.</summary>
        private readonly NetworkList<ulong> _lifterClientIds = new NetworkList<ulong>();

        /// <summary>씬에 살아 있는 함께 들기 물건들. 누가 무엇을 잡았는지 어느 기기서나 찾게 한다.</summary>
        private static readonly List<CoopCarriable> Active = new List<CoopCarriable>();

        /// <summary>
        /// 이 사람이 잡고 있는 함께 들기 물건. 없으면 null이다. 잡은 목록이 동기화되는
        /// 값이라 어느 기기에서 물어도 같은 답이 나온다 — 남의 화면에서도 잡은 사람이
        /// 물건을 바라보게 하는 데 쓴다.
        /// </summary>
        public static Transform FindLiftedBy(ulong clientId)
        {
            foreach (CoopCarriable carriable in Active)
            {
                if (carriable != null && carriable.IsSpawned &&
                    carriable._lifterClientIds.Contains(clientId))
                    return carriable.transform;
            }

            return null;
        }

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

        /// <summary>이 기기에서만 쓴다. 내 캐릭터에 잡기 효과(묶임·감속·바라보기)를 걸었는지.</summary>
        private bool _localLifting;

        /// <summary>이 기기에서만 쓴다. 든 동안 붙들려 있는지와, 물건 기준 잡은 자리.</summary>
        private bool _localTethered;
        private Vector3 _localGrabOffset;

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

        private BodyHighlighter _highlighter;

        private void Awake()
        {
            _bodyCollider = GetComponent<BoxCollider>();
            _highlighter = new BodyHighlighter(this);
        }

        /// <summary>내 상호작용 대상으로 잡히면 몸통이 밝아진다. 보는 사람 기기만의 연출이다.</summary>
        public void SetHighlighted(bool highlighted)
        {
            _highlighter?.SetHighlighted(highlighted);
        }

        public override void OnNetworkSpawn()
        {
            Active.Add(this);
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
            Vector3 desired = Vector3.Lerp(transform.position, target, alpha);
            transform.position = ClampAgainstObstacles(transform.position, desired);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, _lifterAxisRotation * _carryLocalRotation, alpha);
        }

        private readonly RaycastHit[] _castBuffer = new RaycastHit[8];

        /// <summary>
        /// 가려는 자리까지 몸통으로 쓸어 보고, 벽에 걸리면 그 앞까지만 간다.
        /// 든 사람들은 비켜 가도 물건이 벽을 지나치지 않는다. 잡은 사람과
        /// 자기 자신은 장애물로 치지 않는다.
        /// </summary>
        private Vector3 ClampAgainstObstacles(Vector3 from, Vector3 to)
        {
            Vector3 displacement = to - from;
            float distance = displacement.magnitude;
            if (distance < 0.0001f)
                return to;

            Vector3 scale = transform.lossyScale;
            Vector3 halfExtents = Vector3.Scale(_bodyCollider.size, scale) * 0.45f;
            Vector3 center = from + transform.rotation *
                Vector3.Scale(_bodyCollider.center, scale);
            Vector3 direction = displacement / distance;
            int count = Physics.BoxCastNonAlloc(center, halfExtents, direction, _castBuffer,
                transform.rotation, distance, _obstacleLayers, QueryTriggerInteraction.Ignore);
            float allowed = distance;
            for (int index = 0; index < count; index++)
            {
                Collider hit = _castBuffer[index].collider;
                if (hit == _bodyCollider || IsLifterCollider(hit))
                    continue;
                allowed = Mathf.Min(allowed, Mathf.Max(0f, _castBuffer[index].distance - 0.02f));
            }

            return from + direction * allowed;
        }

        private bool IsLifterCollider(Collider other)
        {
            Rigidbody body = other.attachedRigidbody;
            if (body == null)
                return false;
            NetworkObject player = body.GetComponent<NetworkObject>();
            return player != null && _lifterClientIds.Contains(player.OwnerClientId);
        }

        /// <summary>
        /// 잡은 동안 내 캐릭터에 잡기 효과를 건다. 혼자 잡으면 제자리에 묶이고,
        /// 다 같이 들면 느린 걸음으로 함께 옮긴다. 잡는 내내 물건 쪽을 바라본다.
        /// 놓거나 잡기가 풀리면(밀려남, 끊김 포함) 바로 모두 풀어 준다.
        /// </summary>
        private void FixedUpdate()
        {
            bool lifting = IsSpawned && IsLocalPlayerLifting;
            if (!lifting && !_localLifting)
                return;

            Rigidbody body = LocalPlayerBody;
            PlayerMovement movement = body != null ? body.GetComponent<PlayerMovement>() : null;
            if (movement == null)
                return;

            _localLifting = lifting;
            // 잠금은 기절·차와 같은 이유별 잠금을 쓴다. 서로의 잠금을 풀지 않는다.
            // 물건을 바라보는 것은 PlayerModelFacing이 동기화된 잡기 목록에서 스스로 읽는다.
            movement.SetLocked(this, lifting && !IsFullyLifted);
            movement.CoopSpeedMultiplier = lifting && IsFullyLifted ? _carrySpeedMultiplier : 1f;

            UpdateTether(body, lifting && IsFullyLifted);
        }

        /// <summary>
        /// 든 동안 내 캐릭터를 잡은 자리에 붙들어 둔다. 잡은 자리는 물건 기준이라
        /// 물건이 돌면 함께 돈다. 여유 반경을 벗어나면 바깥으로 나가는 속도만 지우고
        /// 자리로 되돌리므로, 줄을 따라 도는 움직임(회전 운반)은 그대로 된다.
        /// 물건이 가운데를 따라오면 자리도 함께 나아가 운반은 평소처럼 된다.
        /// </summary>
        private void UpdateTether(Rigidbody body, bool shouldTether)
        {
            if (shouldTether != _localTethered)
            {
                _localTethered = shouldTether;
                if (shouldTether)
                {
                    // 잡은 자리는 물건 겉면 바로 옆으로 당겨 잡는다. 멀찍이서 잡았던
                    // 간격을 그대로 기억하면 떨어진 채 떠 가는 모습이 유지된다.
                    Vector3 closest = _bodyCollider.ClosestPoint(body.position);
                    Vector3 away = body.position - closest;
                    away.y = 0f;
                    Vector3 direction = away.sqrMagnitude > 0.0001f
                        ? away.normalized
                        : (body.position - transform.position).normalized;
                    Vector3 anchorWorld = closest + direction * 0.5f;
                    _localGrabOffset = Quaternion.Inverse(transform.rotation) *
                        (anchorWorld - transform.position);
                    _localGrabOffset.y = 0f;
                }
            }

            if (!_localTethered)
                return;

            Vector3 anchor = transform.position + transform.rotation * _localGrabOffset;
            Vector3 offset = body.position - anchor;
            offset.y = 0f;
            float distance = offset.magnitude;
            if (distance <= _tetherRadius)
                return;

            // 줄에 걸렸다. 위치를 직접 되돌리면 보간과 싸워 덜덜거리므로 속도로만 다룬다.
            // 바깥으로 나가는 속도 성분을 지워 옆걸음과 회전은 그대로 두고,
            // 넘친 만큼 자리로 끌어오는 스프링 속도를 더해 부드럽게 끌려온다.
            Vector3 outward = offset / distance;
            Vector3 velocity = body.linearVelocity;
            float outwardSpeed = velocity.x * outward.x + velocity.z * outward.z;
            if (outwardSpeed > 0f)
                velocity -= outward * outwardSpeed;
            float overshoot = distance - _tetherRadius;
            velocity -= outward * Mathf.Min(overshoot * 7f, 4.5f);
            body.linearVelocity = velocity;
        }

        public override void OnNetworkDespawn()
        {
            Active.Remove(this);

            // 판이 끝나며 사라져도 효과가 걸린 채 남지 않게 풀어 둔다.
            _localTethered = false;
            if (!_localLifting)
                return;
            _localLifting = false;
            Rigidbody body = LocalPlayerBody;
            PlayerMovement movement = body != null ? body.GetComponent<PlayerMovement>() : null;
            if (movement == null)
                return;
            movement.SetLocked(this, false);
            movement.CoopSpeedMultiplier = 1f;
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
