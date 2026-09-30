using OverCleaning.Interaction;
using Unity.Netcode;
using UnityEngine;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 청소기 본체. 먼지통과 흡입, 놓을 자리 검사를 스스로 맡는다.
    /// 씬에 놓인 물건이라 같은 씬의 DustField와 TrashCan을 직접 참조할 수 있다.
    ///
    /// 누가 들고 있는지는 서버가 정한다. 상호작용은 요청일 뿐이고, 자리 검사도 서버가 해서
    /// 동시에 눌러도 먼저 도착한 한 명만 든다. 나머지는 서버가 내려준 값을 보고 각자
    /// 같은 모습을 만든다 — 든 사람의 자식으로 붙이고, 내려놓은 자리에 세운다.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class Vacuum : NetworkBehaviour, IInteractable
    {
        /// <summary>아무도 들고 있지 않을 때의 클라이언트 ID.</summary>
        public const ulong NoHolder = ulong.MaxValue;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private Transform _suctionPoint;
        [SerializeField] private Renderer _nozzleRenderer;
        [SerializeField] private DustField _dustField;
        [SerializeField] private TrashCan _trashCan;
        [Tooltip("먼지를 가리거나 청소기를 놓지 못하게 막는 벽과 장애물 레이어.")]
        [SerializeField] private LayerMask _obstacleLayers = ~0;
        [Min(0.01f)] [SerializeField] private float _suctionRadius = 1f;
        [Min(0.01f)] [SerializeField] private float _suctionInterval = 0.25f;
        [Min(1)] [SerializeField] private int _dustPerSuction = 5;
        [Min(0.01f)] [SerializeField] private float _suctionDuration = 0.2f;
        [Min(1)] [SerializeField] private int _dustCapacity = 50;

        /// <summary>서버가 정한다. 들고 있는 사람.</summary>
        private readonly NetworkVariable<ulong> _holderClientId = new NetworkVariable<ulong>(NoHolder);

        /// <summary>서버가 정한다. 내려놓은 자리. 늦게 들어온 사람도 같은 자리에 세울 수 있다.</summary>
        private readonly NetworkVariable<Vector3> _restPosition = new NetworkVariable<Vector3>();

        /// <summary>
        /// 든 사람이 정한다. 청소기가 향한 방향.
        /// 바라보는 방향은 든 사람만 알기에 그 사람이 계산해서 알려준다.
        /// </summary>
        private readonly NetworkVariable<float> _aimYaw =
            new NetworkVariable<float>(writePerm: NetworkVariableWritePermission.Owner);

        /// <summary>서버가 정한다. 먼지통에 든 확정 수량. 모두가 같은 숫자를 본다.</summary>
        private readonly NetworkVariable<int> _storedDust = new NetworkVariable<int>();

        private BoxCollider _bodyCollider;
        private MaterialPropertyBlock _nozzleProperties;

        /// <summary>
        /// 흡입 연출이 끝나기를 기다리는 수량. 빨아들이는 사람의 기기에만 있다.
        /// 확정 수량은 서버에서 오므로, 도착하기 전에 용량을 넘겨 빨지 않도록 이것만 따로 센다.
        /// </summary>
        private int _reservedDust;

        private readonly Collider[] _overlapBuffer = new Collider[32];
        private readonly RaycastHit[] _castBuffer = new RaycastHit[32];
        private Transform _groundParent;
        private Rigidbody _holderBody;
        private bool _suctionPaused;
        private float _suctionElapsedTime;

        public bool IsHeld => _holderBody != null;

        /// <summary>이 기기의 플레이어가 들고 있는지.</summary>
        public bool IsHeldByLocalPlayer =>
            IsSpawned && _holderClientId.Value == NetworkManager.LocalClientId;

        public bool IsRunning { get; private set; }
        public Vector3 SuctionPosition => _suctionPoint.position;
        public float SuctionRadius => _suctionRadius;
        public int StoredDustCount => _storedDust.Value;
        public int ReservedDustCount => _reservedDust;
        public int DustCapacity => _dustCapacity;
        public bool IsFull => _storedDust.Value >= _dustCapacity;
        private bool HasDustSpace => _storedDust.Value + _reservedDust < _dustCapacity;

        public bool CanInteract => isActiveAndEnabled;
        public string Prompt => IsHeldByLocalPlayer ? "청소기 내려놓기" : "청소기 들기";

        private void Awake()
        {
            _bodyCollider = GetComponent<BoxCollider>();
            _nozzleProperties = new MaterialPropertyBlock();
            _groundParent = transform.parent;

            if (_suctionPoint == null || _nozzleRenderer == null)
            {
                Debug.LogError("Vacuum의 흡입구와 노즐 Renderer를 지정하세요.", this);
                enabled = false;
                return;
            }

            UpdateNozzleColor();
        }

        public override void OnNetworkSpawn()
        {
            _holderClientId.OnValueChanged += OnHolderChanged;
            _restPosition.OnValueChanged += OnRestPositionChanged;
            _storedDust.OnValueChanged += OnStoredDustChanged;
            if (IsServer)
            {
                _restPosition.Value = transform.position;
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            }

            // 늦게 들어온 사람도 지금 들려 있는 모습을 그대로 보도록 현재 값으로 한 번 맞춘다.
            ApplyHolder();
        }

        public override void OnNetworkDespawn()
        {
            _holderClientId.OnValueChanged -= OnHolderChanged;
            _restPosition.OnValueChanged -= OnRestPositionChanged;
            _storedDust.OnValueChanged -= OnStoredDustChanged;
            if (IsServer && NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        private void LateUpdate()
        {
            // 든 사람이 아니면 그 사람이 알려준 방향을 그대로 따른다.
            if (IsHeld && !IsOwner)
                transform.rotation = Quaternion.Euler(0f, _aimYaw.Value, 0f);

            UpdateRunning();
            UpdateSuction();
        }

        private void OnDisable()
        {
            UpdateRunning();
            if (_dustField != null)
                _dustField.CancelSuctionFor(this);
        }

        /// <summary>
        /// 들고 있으면 내려놓기를, 아니면 들기를 서버에 요청한다.
        /// 자기 캐릭터를 조작하는 클라이언트에서만 불린다.
        /// </summary>
        public void Interact()
        {
            if (IsSpawned)
                RequestToggleHoldRpc();
        }

        /// <summary>
        /// 들기와 내려놓기를 서버가 판정한다. 누가 눌렀는지는 보낸 사람으로 알 수 있다.
        /// </summary>
        [Rpc(SendTo.Server)]
        private void RequestToggleHoldRpc(RpcParams rpcParams = default)
        {
            ulong senderClientId = rpcParams.Receive.SenderClientId;
            if (_holderClientId.Value == senderClientId)
                ServerDrop();
            else if (_holderClientId.Value == NoHolder)
                ServerPickUp(senderClientId);
        }

        private void ServerPickUp(ulong clientId)
        {
            Rigidbody holderBody = FindHolderBody(clientId);
            if (holderBody == null)
                return;
            // 들어 올리는 동안 든 사람과 겹치는 것은 당연하므로 장애물로 보지 않는다.
            if (!CanPlaceVacuum(holderBody.position, holderBody, true))
                return;

            _holderClientId.Value = clientId;
            // 방향은 든 사람이 계산해 알려주므로 그 사람에게 쓰기 권한을 넘긴다.
            NetworkObject.ChangeOwnership(clientId);
        }

        private void ServerDrop()
        {
            Rigidbody holderBody = _holderBody;
            if (holderBody == null)
            {
                ServerRelease(transform.position);
                return;
            }

            // 사람 몸과 겹치지 않도록 조금 앞에 내려놓는다.
            Vector3 destination = holderBody.position + transform.forward * 0.3f;
            if (!CanPlaceVacuum(destination, holderBody, false))
            {
                Debug.LogWarning("앞에 장애물이 있거나 플레이어와 겹쳐 청소기를 내려놓을 수 없습니다.", this);
                return;
            }
            if (!HasGroundSupport(destination))
            {
                Debug.LogWarning("청소기를 내려놓을 위치에 평평한 바닥이 없습니다.", this);
                return;
            }

            ServerRelease(destination);
        }

        private void ServerRelease(Vector3 destination)
        {
            _restPosition.Value = destination;
            _holderClientId.Value = NoHolder;
            NetworkObject.RemoveOwnership();
        }

        /// <summary>들고 있던 사람이 나가면 그 자리에 놓아둔다. 아니면 아무도 들 수 없게 된다.</summary>
        private void OnClientDisconnected(ulong clientId)
        {
            if (_holderClientId.Value == clientId)
                ServerRelease(transform.position);
        }

        private void OnHolderChanged(ulong previousClientId, ulong newClientId) => ApplyHolder();

        private void OnRestPositionChanged(Vector3 previousPosition, Vector3 newPosition) => ApplyHolder();

        /// <summary>
        /// 서버가 내려준 값대로 청소기를 놓는다. 모든 기기에서 똑같이 돈다.
        /// 든 사람과 내려놓은 자리가 각각 도착하므로, 어느 쪽이 와도 다시 맞춘다.
        /// </summary>
        private void ApplyHolder()
        {
            _holderBody = FindHolderBody(_holderClientId.Value);
            if (_holderBody != null)
            {
                SetVacuumParent(_holderBody.transform, _holderBody.position);
                transform.localPosition = Vector3.zero;
            }
            else
            {
                SetVacuumParent(_groundParent, _restPosition.Value);
            }

            UpdateRunning();
        }

        private Rigidbody FindHolderBody(ulong clientId)
        {
            if (clientId == NoHolder || NetworkManager == null || NetworkManager.SpawnManager == null)
                return null;
            NetworkObject player = NetworkManager.SpawnManager.GetPlayerNetworkObject(clientId);
            return player != null ? player.GetComponent<Rigidbody>() : null;
        }

        /// <summary>든 사람이 보는 쪽으로 돌린다. 든 사람의 기기에서만 불린다.</summary>
        public void AimAt(Vector3 facingDirection)
        {
            if (!IsHeld || !IsOwner || facingDirection.sqrMagnitude < 0.000001f)
                return;

            Quaternion start = transform.rotation;
            Quaternion target = Quaternion.LookRotation(facingDirection, Vector3.up);
            float angle = Quaternion.Angle(start, target);
            if (angle < 0.01f)
                return;

            // 최종 방향뿐 아니라 회전 경로도 검사해 얇은 벽을 가로질러 돌지 못하게 한다.
            int steps = Mathf.CeilToInt(angle / 5f);
            Vector3 scale = transform.lossyScale;
            Vector3 halfExtents = Vector3.Scale(_bodyCollider.size, scale) * 0.5f;
            Vector3 centerOffset = Vector3.Scale(_bodyCollider.center, scale);
            float reach = new Vector2(centerOffset.x, centerOffset.z).magnitude +
                          new Vector2(halfExtents.x, halfExtents.z).magnitude;
            float padding = reach * (angle / steps) * Mathf.Deg2Rad;

            for (int step = 1; step <= steps; step++)
            {
                Quaternion candidate = Quaternion.Slerp(start, target, (float)step / steps);
                if (OverlapsObstacle(candidate, halfExtents, centerOffset, padding))
                    break;
                transform.rotation = candidate;
            }

            _aimYaw.Value = transform.eulerAngles.y;
        }

        /// <summary>먼지를 버리는 동안 흡입을 멈춰 둔다.</summary>
        public void SetSuctionPaused(bool paused)
        {
            if (_suctionPaused == paused)
                return;
            _suctionPaused = paused;
            UpdateRunning();
            if (paused && _dustField != null)
                _dustField.CancelSuctionFor(this);
        }

        /// <summary>지금 이 자리에서 쓰레기통에 먼지를 버릴 수 있는가.</summary>
        public bool CanEmptyDustBin()
        {
            return IsHeld && StoredDustCount > 0 && _trashCan != null &&
                _trashCan.CanReceiveDust(HandPosition, _holderBody);
        }

        /// <summary>
        /// 쓰레기통에 먼지를 넘기고 먼지통을 비우도록 서버에 요청한다.
        /// 다 비웠는지는 서버가 내려주는 수량으로 알 수 있으므로 결과를 돌려주지 않는다.
        /// </summary>
        public void RequestEmptyDustBin()
        {
            if (IsSpawned && IsHeldByLocalPlayer)
                RequestEmptyDustBinRpc();
        }

        [Rpc(SendTo.Server)]
        private void RequestEmptyDustBinRpc(RpcParams rpcParams = default)
        {
            // 들고 있지 않은 사람이 보낸 요청은 버린다.
            if (_holderClientId.Value != rpcParams.Receive.SenderClientId)
                return;
            if (!CanEmptyDustBin() || _trashCan == null)
                return;
            if (!_trashCan.TryReceiveDust(HandPosition, _holderBody, _storedDust.Value))
                return;

            _storedDust.Value = 0;
        }

        /// <summary>
        /// 흡입 연출이 끝난 먼지를 서버에 알린다. 빨아들인 사람의 기기에서만 불린다.
        /// 서버가 수량을 올리고 나머지 기기에 그 먼지를 지우라고 전한다.
        /// </summary>
        [Rpc(SendTo.Server)]
        private void ReportDustRemovedRpc(int dustId, RpcParams rpcParams = default)
        {
            if (_holderClientId.Value != rpcParams.Receive.SenderClientId)
                return;

            if (_storedDust.Value < _dustCapacity)
                _storedDust.Value++;
            RemoveDustRpc(dustId);
        }

        /// <summary>빨아들인 사람은 이미 지웠으므로 그 사람만 빼고 보낸다.</summary>
        [Rpc(SendTo.NotOwner)]
        private void RemoveDustRpc(int dustId)
        {
            if (_dustField != null)
                _dustField.RemoveDustById(dustId);
        }

        private void OnStoredDustChanged(int previousCount, int newCount)
        {
            if (newCount == 0)
                _suctionElapsedTime = 0f;
            UpdateRunning();
            UpdateNozzleColor();
        }

        /// <summary>먼지가 흡입구에서 보이는가. 벽 반대편의 먼지는 빨리지 않는다.</summary>
        public bool CanReachDust(Vector3 position)
        {
            if (!IsRunning)
                return false;
            Vector3 displacement = position - SuctionPosition;
            if (displacement.sqrMagnitude < 0.000001f)
                return true;

            int count = Physics.RaycastNonAlloc(SuctionPosition, displacement.normalized, _castBuffer,
                displacement.magnitude, _obstacleLayers, QueryTriggerInteraction.Ignore);
            if (count == _castBuffer.Length)
                return false;
            for (int index = 0; index < count; index++)
            {
                if (!IsIgnoredCollider(_castBuffer[index].collider, _holderBody))
                    return false;
            }

            return true;
        }

        internal bool TryReserveDust()
        {
            if (!IsRunning || !HasDustSpace)
                return false;
            _reservedDust++;
            return true;
        }

        internal void ReleaseDustReservation()
        {
            if (_reservedDust > 0)
                _reservedDust--;
        }

        internal void CompleteDustSuction(int dustId)
        {
            ReleaseDustReservation();
            // 수량을 직접 올리지 않는다. 서버가 확정해 모두에게 같은 숫자를 내려준다.
            ReportDustRemovedRpc(dustId);
        }

        /// <summary>든 사람의 손 높이. 쓰레기통까지의 거리를 재는 기준이다.</summary>
        private Vector3 HandPosition => _holderBody.position + Vector3.up * 0.5f;

        private void UpdateSuction()
        {
            // 먼지를 실제로 없애는 것은 든 사람의 기기에서만 한다. 각자 없애면 화면이 갈라진다.
            if (!IsOwner || !IsRunning || _dustField == null || !_dustField.isActiveAndEnabled ||
                !HasDustSpace)
                return;

            _suctionElapsedTime += Time.deltaTime;
            if (_suctionElapsedTime < _suctionInterval)
                return;

            // 프레임 지연 뒤 여러 회차를 한꺼번에 흡입하지 않는다.
            _suctionElapsedTime %= _suctionInterval;
            _dustField.BeginSuction(this, _dustPerSuction, _suctionDuration);
        }

        private void UpdateRunning()
        {
            bool running = IsHeld && !IsFull && !_suctionPaused && isActiveAndEnabled;
            if (IsRunning == running)
                return;
            IsRunning = running;
            _suctionElapsedTime = 0f;
            UpdateNozzleColor();
        }

        private void SetVacuumParent(Transform parent, Vector3 position)
        {
            // 재등록하여 놓은 Collider가 사람의 복합 Collider로 남지 않게 한다.
            _bodyCollider.enabled = false;
            transform.SetParent(parent, true);
            transform.position = position;
            _bodyCollider.enabled = true;
            Physics.SyncTransforms();
        }

        private bool CanPlaceVacuum(Vector3 destination, Rigidbody ignoredBody, bool ignoreBodyAtDestination)
        {
            Physics.SyncTransforms();
            Vector3 halfExtents = Vector3.Scale(_bodyCollider.size, transform.lossyScale) * 0.5f;
            Quaternion rotation = transform.rotation;
            Vector3 currentCenter = transform.TransformPoint(_bodyCollider.center);
            Vector3 displacement = destination - transform.position;
            int count = Physics.OverlapBoxNonAlloc(currentCenter + displacement, halfExtents,
                _overlapBuffer, rotation, _obstacleLayers, QueryTriggerInteraction.Ignore);
            if (count == _overlapBuffer.Length)
                return false;
            for (int index = 0; index < count; index++)
            {
                if (!IsIgnoredCollider(_overlapBuffer[index], ignoreBodyAtDestination ? ignoredBody : null))
                    return false;
            }

            // 들거나 놓을 때 위치만 바꾸어 얇은 벽 반대편으로 통과하지 않도록 검사한다.
            if (displacement.sqrMagnitude < 0.000001f)
                return true;
            count = Physics.BoxCastNonAlloc(currentCenter, halfExtents, displacement.normalized,
                _castBuffer, rotation, displacement.magnitude, _obstacleLayers, QueryTriggerInteraction.Ignore);
            if (count == _castBuffer.Length)
                return false;
            for (int index = 0; index < count; index++)
            {
                if (!IsIgnoredCollider(_castBuffer[index].collider, ignoredBody))
                    return false;
            }

            return true;
        }

        private bool IsIgnoredCollider(Collider other, Rigidbody ignoredBody)
        {
            return other == _bodyCollider || (ignoredBody != null && other.attachedRigidbody == ignoredBody);
        }

        private bool HasGroundSupport(Vector3 destination)
        {
            Vector3 halfExtents = _bodyCollider.size * 0.5f;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = _bodyCollider.center +
                        new Vector3(x * halfExtents.x, -halfExtents.y + 0.1f, z * halfExtents.z);
                    Vector3 origin = destination + transform.rotation *
                        Vector3.Scale(corner, transform.lossyScale);
                    int count = Physics.RaycastNonAlloc(origin, Vector3.down, _castBuffer,
                        0.2f, _obstacleLayers, QueryTriggerInteraction.Ignore);
                    bool supported = false;
                    for (int index = 0; index < count; index++)
                    {
                        if (!IsIgnoredCollider(_castBuffer[index].collider, _holderBody) &&
                            _castBuffer[index].normal.y > 0.99f)
                            supported = true;
                    }
                    if (!supported)
                        return false;
                }
            }

            return true;
        }

        private bool OverlapsObstacle(Quaternion rotation, Vector3 halfExtents, Vector3 centerOffset, float padding)
        {
            // FixedUpdate에서는 보간된 화면 위치 대신 Rigidbody의 물리 위치를 사용한다.
            Vector3 pivotPosition = _holderBody.position + _holderBody.rotation * transform.localPosition;
            Vector3 center = pivotPosition + rotation * centerOffset;
            halfExtents += new Vector3(padding, 0f, padding);
            int count = Physics.OverlapBoxNonAlloc(center, halfExtents, _overlapBuffer,
                rotation, _obstacleLayers, QueryTriggerInteraction.Ignore);
            if (count == _overlapBuffer.Length)
                return true;
            for (int index = 0; index < count; index++)
            {
                if (!IsIgnoredCollider(_overlapBuffer[index], _holderBody))
                    return true;
            }

            return false;
        }

        private void UpdateNozzleColor()
        {
            if (_nozzleRenderer == null || _nozzleProperties == null)
                return;
            _nozzleRenderer.GetPropertyBlock(_nozzleProperties);
            _nozzleProperties.SetColor(BaseColorId, IsFull ? Color.red : IsRunning ? Color.green : Color.gray);
            _nozzleRenderer.SetPropertyBlock(_nozzleProperties);
        }

        private void OnDrawGizmosSelected()
        {
            if (_suctionPoint == null)
                return;
            Gizmos.color = IsRunning ? Color.green : Color.yellow;
            Gizmos.DrawWireSphere(_suctionPoint.position, _suctionRadius);
            Gizmos.DrawRay(_suctionPoint.position, _suctionPoint.forward * _suctionRadius);
        }
    }
}
