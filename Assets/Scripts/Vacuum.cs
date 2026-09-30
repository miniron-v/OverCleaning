using OverCleaning.Interaction;
using UnityEngine;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 청소기 본체. 먼지통과 흡입, 놓을 자리 검사를 스스로 맡는다.
    /// 씬에 놓인 물건이라 같은 씬의 DustField와 TrashCan을 직접 참조할 수 있다.
    ///
    /// 들고 다니기는 상호작용으로 한다. 들린 동안에는 든 사람의 자식이 되므로
    /// 그 사람의 상호작용 반경에 계속 잡혀 같은 키로 내려놓을 수 있다.
    /// 충돌 검사에서는 든 사람의 몸을 장애물로 보지 않는다. 들면 당연히 겹치기 때문이다.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class Vacuum : MonoBehaviour, IInteractable
    {
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

        private BoxCollider _bodyCollider;
        private DustBin _dustBin;
        private MaterialPropertyBlock _nozzleProperties;
        private readonly Collider[] _overlapBuffer = new Collider[32];
        private readonly RaycastHit[] _castBuffer = new RaycastHit[32];
        private Transform _groundParent;
        private Rigidbody _holderBody;
        private bool _suctionPaused;
        private float _suctionElapsedTime;

        public bool IsHeld => _holderBody != null;
        public bool IsRunning { get; private set; }
        public Vector3 SuctionPosition => _suctionPoint.position;
        public float SuctionRadius => _suctionRadius;
        public int StoredDustCount => _dustBin.StoredCount;
        public int ReservedDustCount => _dustBin.ReservedCount;
        public int DustCapacity => _dustBin.Capacity;
        public bool IsFull => _dustBin.IsFull;

        public bool CanInteract => isActiveAndEnabled;
        public string Prompt => IsHeld ? "청소기 내려놓기" : "청소기 들기";

        private void Awake()
        {
            _bodyCollider = GetComponent<BoxCollider>();
            _dustBin = new DustBin(_dustCapacity);
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

        private void LateUpdate()
        {
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
        /// 들고 있으면 내려놓고, 아니면 든다.
        /// 자기 캐릭터를 조작하는 클라이언트에서만 불리므로 드는 사람은 늘 그 클라이언트의 플레이어다.
        /// </summary>
        public void Interact()
        {
            if (IsHeld)
                TryDrop();
            else if (VacuumCarrier.LocalBody == null)
                Debug.LogWarning("청소기를 들 사람을 찾지 못했습니다. Player 프리팹의 VacuumCarrier가 켜져 있는지 확인하세요.", this);
            else
                TryPickUp(VacuumCarrier.LocalBody);
        }

        /// <summary>
        /// 든 사람에게 붙인다. 놓을 자리가 없으면 실패한다.
        /// 원래 있던 부모를 기억해 두었다가 내려놓을 때 그 자리로 돌려준다.
        /// </summary>
        public bool TryPickUp(Rigidbody holderBody)
        {
            if (IsHeld || holderBody == null || !isActiveAndEnabled)
                return false;
            // 들어 올리는 동안 든 사람과 겹치는 것은 당연하므로 장애물로 보지 않는다.
            if (!CanPlaceVacuum(holderBody.position, holderBody, true))
                return false;

            SetVacuumParent(holderBody.transform, holderBody.position);
            transform.localPosition = Vector3.zero;
            _holderBody = holderBody;
            UpdateRunning();
            Physics.SyncTransforms();
            return true;
        }

        /// <summary>바닥에 내려놓는다. 앞이 막혔거나 바닥이 없으면 실패한다.</summary>
        public bool TryDrop()
        {
            if (!IsHeld)
                return false;

            Rigidbody holderBody = _holderBody;
            // 사람 몸과 겹치지 않도록 조금 앞에 내려놓는다.
            Vector3 destination = holderBody.position + transform.forward * 0.3f;
            if (!CanPlaceVacuum(destination, holderBody, false))
            {
                Debug.LogWarning("앞에 장애물이 있거나 플레이어와 겹쳐 청소기를 내려놓을 수 없습니다. 조금 물러나서 다시 시도하세요.", this);
                return false;
            }
            if (!HasGroundSupport(destination))
            {
                Debug.LogWarning("청소기를 내려놓을 위치에 평평한 바닥이 없습니다.", this);
                return false;
            }

            // 기억해 둔 부모가 사람 밑이면 씬 루트에 놓는다. 사람을 따라다니는 자리이기 때문이다.
            Transform groundParent = _groundParent;
            if (groundParent != null &&
                (groundParent.IsChildOf(holderBody.transform) || groundParent.GetComponentInParent<Rigidbody>() != null))
                groundParent = null;

            SetVacuumParent(groundParent, destination);
            _holderBody = null;
            UpdateRunning();
            return true;
        }

        /// <summary>든 사람이 보는 쪽으로 돌린다. 매 물리 프레임 불린다.</summary>
        public void AimAt(Vector3 facingDirection)
        {
            if (!IsHeld || facingDirection.sqrMagnitude < 0.000001f)
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
                _trashCan.CanReceiveDust(HandPosition, _holderBody);
        }

        /// <summary>쓰레기통에 먼지를 넘기고 먼지통을 비운다.</summary>
        public bool TryEmptyDustBin()
        {
            if (!CanEmptyDustBin() || !_trashCan.TryReceiveDust(HandPosition, _holderBody, StoredDustCount))
                return false;

            _dustBin.Empty();
            _suctionElapsedTime = 0f;
            UpdateNozzleColor();
            return true;
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
            return IsRunning && _dustBin.TryReserve();
        }

        internal void ReleaseDustReservation()
        {
            _dustBin.CancelReservation();
        }

        internal void CompleteDustSuction()
        {
            _dustBin.CompleteReservation();
            UpdateRunning();
        }

        /// <summary>든 사람의 손 높이. 쓰레기통까지의 거리를 재는 기준이다.</summary>
        private Vector3 HandPosition => _holderBody.position + Vector3.up * 0.5f;

        private void UpdateSuction()
        {
            if (!IsRunning || _dustField == null || !_dustField.isActiveAndEnabled || !_dustBin.HasSpace)
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
