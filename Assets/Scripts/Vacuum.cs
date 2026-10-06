using Unity.Netcode;
using UnityEngine;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 청소기 본체. 먼지통과 흡입을 스스로 맡는다.
    /// 씬에 놓인 물건이라 같은 씬의 DustField와 TrashCan을 직접 참조할 수 있다.
    /// 들고 나르기는 CarriableItem이 맡고, 여기에는 청소기만의 것이 남는다.
    /// </summary>
    public sealed class Vacuum : CarriableItem
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private Transform _suctionPoint;
        [SerializeField] private Renderer _nozzleRenderer;
        [SerializeField] private DustField _dustField;
        [SerializeField] private TrashCan _trashCan;
        [Min(0.01f)] [SerializeField] private float _suctionRadius = 1f;
        [Min(0.01f)] [SerializeField] private float _suctionInterval = 0.25f;
        [Min(1)] [SerializeField] private int _dustPerSuction = 5;
        [Min(0.01f)] [SerializeField] private float _suctionDuration = 0.2f;
        [Min(1)] [SerializeField] private int _dustCapacity = 50;
        [Tooltip("비우는 동안 초당 쓰레기통으로 옮기는 먼지 수. 적게 담겼으면 그만큼 빨리 끝난다.")]
        [Min(1f)] [SerializeField] private float _emptyDustPerSecond = 10f;

        /// <summary>서버가 정한다. 먼지통에 든 확정 수량. 모두가 같은 숫자를 본다.</summary>
        private readonly NetworkVariable<int> _storedDust = new NetworkVariable<int>();

        private MaterialPropertyBlock _nozzleProperties;

        /// <summary>
        /// 흡입 연출이 끝나기를 기다리는 수량. 빨아들이는 사람의 기기에만 있다.
        /// 확정 수량은 서버에서 오므로, 도착하기 전에 용량을 넘겨 빨지 않도록 이것만 따로 센다.
        /// </summary>
        private int _reservedDust;

        private bool _suctionPaused;
        private float _suctionElapsedTime;

        /// <summary>서버에서만 쓴다. 비우는 중인지와, 아직 1개가 안 된 비우기 진행분.</summary>
        private bool _serverEmptying;
        private float _emptyAccumulator;

        public bool IsRunning { get; private set; }
        public Vector3 SuctionPosition => _suctionPoint.position;
        public float SuctionRadius => _suctionRadius;
        public int StoredDustCount => _storedDust.Value;
        public int ReservedDustCount => _reservedDust;
        public int DustCapacity => _dustCapacity;
        public bool IsFull => _storedDust.Value >= _dustCapacity;
        private bool HasDustSpace => _storedDust.Value + _reservedDust < _dustCapacity;

        protected override string ItemName => "청소기";

        protected override void Awake()
        {
            base.Awake();
            _nozzleProperties = new MaterialPropertyBlock();

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
            _storedDust.OnValueChanged += OnStoredDustChanged;
            base.OnNetworkSpawn();
        }

        public override void OnNetworkDespawn()
        {
            _storedDust.OnValueChanged -= OnStoredDustChanged;
            base.OnNetworkDespawn();
        }

        protected override void OnCarryChanged()
        {
            UpdateRunning();
        }

        private void LateUpdate()
        {
            if (IsServer && _serverEmptying)
                UpdateServerEmptying();

            UpdateRunning();
            UpdateSuction();
        }

        private void OnDisable()
        {
            UpdateRunning();
            if (_dustField != null)
                _dustField.CancelSuctionFor(this);
        }

        /// <summary>든 사람이 보는 쪽으로 돌린다. 든 사람의 기기에서만 불린다.</summary>
        public override void AimAt(Vector3 facingDirection)
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
                _trashCan.CanReceiveDust(HandPosition, HolderBody);
        }

        /// <summary>비우기 시작을 서버에 알린다. 든 사람이 키를 누르고 있는 동안만 비워진다.</summary>
        public void RequestBeginEmptying()
        {
            if (IsSpawned && IsHeldByLocalPlayer)
                BeginEmptyingRpc();
        }

        /// <summary>키를 뗐다고 서버에 알린다. 그때까지 빠져나간 먼지는 그대로 버려진 것이다.</summary>
        public void RequestEndEmptying()
        {
            if (IsSpawned && IsHeldByLocalPlayer)
                EndEmptyingRpc();
        }

        [Rpc(SendTo.Server)]
        private void BeginEmptyingRpc(RpcParams rpcParams = default)
        {
            if (!IsFromHolder(rpcParams) || !CanEmptyDustBin())
                return;
            _serverEmptying = true;
            _emptyAccumulator = 0f;
        }

        [Rpc(SendTo.Server)]
        private void EndEmptyingRpc(RpcParams rpcParams = default)
        {
            if (IsFromHolder(rpcParams))
                _serverEmptying = false;
        }

        /// <summary>
        /// 서버에서 돈다. 누르고 있는 동안 초당 일정량씩 쓰레기통으로 옮기므로
        /// 적게 담겼으면 빨리 끝나고, 중간에 떼면 그때까지 빠진 만큼만 비워진다.
        /// 수량은 NetworkVariable이라 빠져나가는 모습이 모든 화면에 그대로 보인다.
        /// </summary>
        private void UpdateServerEmptying()
        {
            if (!CanEmptyDustBin())
            {
                _serverEmptying = false;
                return;
            }

            _emptyAccumulator += Time.deltaTime * _emptyDustPerSecond;
            int amount = Mathf.Min((int)_emptyAccumulator, _storedDust.Value);
            if (amount <= 0)
                return;

            _emptyAccumulator -= amount;
            if (_trashCan.TryReceiveDust(HandPosition, HolderBody, amount))
                _storedDust.Value -= amount;
            else
                _serverEmptying = false;
        }

        /// <summary>
        /// 흡입 연출이 끝난 먼지를 서버에 알린다. 빨아들인 사람의 기기에서만 불린다.
        /// 서버가 수량을 올리고 나머지 기기에 그 먼지를 지우라고 전한다.
        /// </summary>
        [Rpc(SendTo.Server)]
        private void ReportDustRemovedRpc(int dustId, RpcParams rpcParams = default)
        {
            if (!IsFromHolder(rpcParams))
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
                if (!IsIgnoredCollider(_castBuffer[index].collider, HolderBody))
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
        private Vector3 HandPosition => HolderBody.position + Vector3.up * 0.5f;

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

        private bool OverlapsObstacle(Quaternion rotation, Vector3 halfExtents, Vector3 centerOffset, float padding)
        {
            // FixedUpdate에서는 보간된 화면 위치 대신 Rigidbody의 물리 위치를 사용한다.
            Vector3 pivotPosition = HolderBody.position + HolderBody.rotation * transform.localPosition;
            Vector3 center = pivotPosition + rotation * centerOffset;
            halfExtents += new Vector3(padding, 0f, padding);
            int count = Physics.OverlapBoxNonAlloc(center, halfExtents, _overlapBuffer,
                rotation, _obstacleLayers, QueryTriggerInteraction.Ignore);
            if (count == _overlapBuffer.Length)
                return true;
            for (int index = 0; index < count; index++)
            {
                if (!IsIgnoredCollider(_overlapBuffer[index], HolderBody))
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
