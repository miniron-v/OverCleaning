using UnityEngine;
using UnityEngine.InputSystem;

namespace OverCleaning.InGame
{
    [RequireComponent(typeof(PlayerMovement))]
    [DefaultExecutionOrder(100)]
    public sealed class PlayerVacuum : MonoBehaviour
    {
        [SerializeField] private Transform _vacuumPivot;
        [SerializeField] private Transform _suctionPoint;
        [SerializeField] private Renderer _nozzleRenderer;
        [SerializeField] private BoxCollider _vacuumCollider;
        [SerializeField] private LayerMask _obstacleLayers = ~0;
        [Min(0.01f)] [SerializeField] private float _suctionRadius = 1f;
        [SerializeField] private DustField _dustField;
        [Min(0.01f)] [SerializeField] private float _suctionInterval = 0.25f;
        [Min(1)] [SerializeField] private int _dustPerSuction = 5;
        [Min(0.01f)] [SerializeField] private float _suctionDuration = 0.2f;
        [Min(1)] [SerializeField] private int _dustCapacity = 50;
        [SerializeField] private TrashCan _trashCan;
        [Min(0.1f)] [SerializeField] private float _emptyDuration = 2f;
        [SerializeField] private InputAction _emptyAction =
            new InputAction("Empty Dust Bin", InputActionType.Button, "<Keyboard>/r");
        [Min(0.1f)] [SerializeField] private float _pickUpRadius = 1.5f;
        [SerializeField] private InputAction _carryAction =
            new InputAction("Carry Vacuum", InputActionType.Button, "<Keyboard>/e");

        private PlayerMovement _playerMovement;
        private MaterialPropertyBlock _nozzleProperties;
        private Rigidbody _rigidbody;
        private readonly Collider[] _overlapBuffer = new Collider[32];
        private readonly RaycastHit[] _castBuffer = new RaycastHit[32];
        private Transform _groundParent;
        private bool _carryRequested;
        private float _suctionElapsedTime;
        private DustBin _dustBin;
        private bool _canEmptyDustBin;
        private float _emptyElapsedTime;
        private Camera _statusCamera;

        public bool IsHeld { get; private set; }
        public bool IsRunning { get; private set; }
        public bool IsEmptying { get; private set; }
        public Vector3 SuctionPosition => _suctionPoint.position;
        public float SuctionRadius => _suctionRadius;
        public int StoredDustCount => _dustBin?.StoredCount ?? 0;
        public int DustCapacity => _dustBin?.Capacity ?? Mathf.Max(1, _dustCapacity);
        public bool IsFull => _dustBin != null && _dustBin.IsFull;

        private void Awake()
        {
            _dustBin = new DustBin(Mathf.Max(1, _dustCapacity));
            _statusCamera = Camera.main;
            _playerMovement = GetComponent<PlayerMovement>();
            _rigidbody = GetComponent<Rigidbody>();
            _nozzleProperties = new MaterialPropertyBlock();
            if (_vacuumPivot == null || _suctionPoint == null || _nozzleRenderer == null || _vacuumCollider == null)
            {
                Debug.LogError("PlayerVacuum의 회전축, 흡입구, 노즐 Renderer, Collider를 지정하세요.", this);
                enabled = false;
                return;
            }

            _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            IsHeld = _vacuumPivot.IsChildOf(transform);
            _groundParent = IsHeld ? transform.parent : _vacuumPivot.parent;
            SetRunning(IsHeld);
            UpdateNozzleColor();
        }

        private void OnEnable()
        {
            _carryAction.Enable();
            _emptyAction.Enable();
            SetRunning(IsHeld);
        }

        private void UpdateSuction()
        {
            if (!IsRunning || _dustField == null || !_dustField.isActiveAndEnabled ||
                !_dustBin.HasSpace)
                return;

            _suctionElapsedTime += Time.deltaTime;
            float interval = Mathf.Max(0.01f, _suctionInterval);
            if (_suctionElapsedTime < interval)
                return;

            // 프레임 지연 뒤 여러 회차를 한꺼번에 흡입하지 않습니다.
            _suctionElapsedTime %= interval;
            _dustField.BeginSuction(this, Mathf.Max(1, _dustPerSuction), Mathf.Max(0.01f, _suctionDuration));
        }

        public bool CanReachDust(Vector3 position)
        {
            if (!IsRunning || _suctionPoint == null)
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
                if (!IsIgnoredCollider(_castBuffer[index].collider, true))
                    return false;
            }

            return true;
        }

        private void OnDisable()
        {
            _carryAction.Disable();
            _emptyAction.Disable();
            _carryRequested = false;
            IsEmptying = false;
            SetRunning(false);
            if (_dustField != null)
                _dustField.CancelSuctionFor(this);
        }

        private void OnDestroy()
        {
            _carryAction.Dispose();
            _emptyAction.Dispose();
        }

        private void FixedUpdate()
        {
            if (_carryRequested)
            {
                _carryRequested = false;
                if (IsHeld)
                    TryDrop();
                else
                    TryPickUp();
            }
            if (IsHeld)
                RotateWithoutOverlap();
        }

        private void LateUpdate()
        {
            if (Application.isFocused && _carryAction.WasPressedThisFrame())
                _carryRequested = true;
            if (Application.isFocused && IsHeld && _emptyAction.WasPressedThisFrame())
                BeginEmptying();
            UpdateEmptying();
            _canEmptyDustBin = CanEmptyDustBin();
            SetRunning(IsHeld);
            UpdateSuction();
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
            SetRunning(IsHeld);
        }

        private bool CanEmptyDustBin()
        {
            return IsHeld && StoredDustCount > 0 && _trashCan != null &&
                _trashCan.CanReceiveDust(_rigidbody.position + Vector3.up * 0.5f, _rigidbody);
        }

        private void BeginEmptying()
        {
            if (IsEmptying || !CanEmptyDustBin())
                return;
            if (_dustField != null)
                _dustField.CancelSuctionFor(this);
            IsEmptying = true;
            _emptyElapsedTime = 0f;
            SetRunning(false);
        }

        private void UpdateEmptying()
        {
            if (!IsEmptying)
                return;
            if (!CanEmptyDustBin())
            {
                IsEmptying = false;
                return;
            }

            _emptyElapsedTime += Time.deltaTime;
            if (_emptyElapsedTime < Mathf.Max(0.1f, _emptyDuration))
                return;

            // 완료 시 위치를 다시 검사한 뒤 수거량 이전과 비우기를 함께 처리합니다.
            if (_trashCan.TryReceiveDust(_rigidbody.position + Vector3.up * 0.5f, _rigidbody, StoredDustCount))
                _dustBin.Empty();
            IsEmptying = false;
            _suctionElapsedTime = 0f;
            UpdateNozzleColor();
        }

        private void OnGUI()
        {
            string status = IsEmptying ? "먼지통 비우는 중" : IsFull ? "먼지통 가득 참 — 비워주세요" : IsRunning ? "청소 중" : "작동 정지";
            string emptyHint = !IsHeld ? "먼지를 버리려면 청소기를 들어주세요" :
                IsEmptying ? "쓰레기통 근처에서 기다려주세요" :
                StoredDustCount == 0 ? "먼지통이 비어 있습니다" :
                _canEmptyDustBin ? "R: 쓰레기통에 먼지 버리기" : "먼지를 버리려면 쓰레기통 가까이 가세요";
            GUI.Box(new Rect(16f, 16f, 360f, 105f),
                $"먼지통 {StoredDustCount}/{DustCapacity} (흡입 중: {_dustBin?.ReservedCount ?? 0})\n{status}\nE: 들기 / 내려놓기\n{emptyHint}");
            DrawEmptyingProgress();
        }

        private void DrawEmptyingProgress()
        {
            if (!IsEmptying || _statusCamera == null)
                return;
            Vector3 screenPosition = _statusCamera.WorldToScreenPoint(transform.position + Vector3.up * 2.3f);
            if (screenPosition.z <= 0f)
                return;

            float progress = Mathf.Clamp01(_emptyElapsedTime / Mathf.Max(0.1f, _emptyDuration));
            float left = screenPosition.x - 75f;
            float top = Screen.height - screenPosition.y - 40f;
            GUI.Box(new Rect(left, top, 150f, 40f), "먼지통 비우는 중...");
            Color previousColor = GUI.color;
            GUI.color = Color.gray;
            GUI.DrawTexture(new Rect(left + 8f, top + 25f, 134f, 8f), Texture2D.whiteTexture);
            GUI.color = Color.green;
            GUI.DrawTexture(new Rect(left + 8f, top + 25f, 134f * progress, 8f), Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private void TryPickUp()
        {
            Vector3 handPosition = _rigidbody.position + Vector3.up * 0.5f;
            if (Vector3.Distance(_vacuumCollider.ClosestPoint(handPosition), handPosition) > _pickUpRadius)
                return;
            if (!CanPlaceVacuum(_rigidbody.position, true))
                return;

            // 붙이기 전 경로를 검사하고, 회전은 기존 벽 검사로 처리합니다.
            SetVacuumParent(transform, _rigidbody.position);
            _vacuumPivot.localPosition = Vector3.zero;
            IsHeld = true;
            SetRunning(true);
            Physics.SyncTransforms();
        }

        private void TryDrop()
        {
            // 플레이어 몸과 겹치지 않도록 조금 앞에 내려놓습니다.
            Vector3 destination = _rigidbody.position + _vacuumPivot.forward * 0.3f;
            if (!CanPlaceVacuum(destination, false))
            {
                Debug.LogWarning("앞에 장애물이 있거나 플레이어와 겹쳐 청소기를 내려놓을 수 없습니다. 조금 물러나서 다시 시도하세요.", this);
                return;
            }
            if (!HasGroundSupport(destination))
            {
                Debug.LogWarning("청소기를 내려놓을 위치에 평평한 바닥이 없습니다.", this);
                return;
            }

            // 초기 배치나 재컴파일 시 저장된 부모가 Player여도 반드시 분리합니다.
            Transform groundParent = _groundParent;
            if (groundParent != null && (groundParent.IsChildOf(transform) || groundParent.GetComponentInParent<Rigidbody>() != null))
                groundParent = null;
            SetVacuumParent(groundParent, destination);
            IsHeld = false;
            SetRunning(false);
        }

        private void SetVacuumParent(Transform parent, Vector3 position)
        {
            // 재등록하여 놓은 Collider가 플레이어의 복합 Collider로 남지 않게 합니다.
            _vacuumCollider.enabled = false;
            _vacuumPivot.SetParent(parent, true);
            _vacuumPivot.position = position;
            _vacuumCollider.enabled = true;
            Physics.SyncTransforms();
        }

        private bool CanPlaceVacuum(Vector3 destination, bool ignorePlayerAtDestination)
        {
            Physics.SyncTransforms();
            Vector3 halfExtents = Vector3.Scale(_vacuumCollider.size, _vacuumPivot.lossyScale) * 0.5f;
            Quaternion rotation = _vacuumPivot.rotation;
            Vector3 currentCenter = _vacuumPivot.TransformPoint(_vacuumCollider.center);
            Vector3 displacement = destination - _vacuumPivot.position;
            int count = Physics.OverlapBoxNonAlloc(currentCenter + displacement, halfExtents,
                _overlapBuffer, rotation, _obstacleLayers, QueryTriggerInteraction.Ignore);
            if (count == _overlapBuffer.Length)
                return false;
            for (int index = 0; index < count; index++)
            {
                if (!IsIgnoredCollider(_overlapBuffer[index], ignorePlayerAtDestination))
                    return false;
            }

            // 들거나 놓을 때 위치만 바꾸어 얇은 벽 반대편으로 통과하지 않도록 검사합니다.
            if (displacement.sqrMagnitude < 0.000001f)
                return true;
            count = Physics.BoxCastNonAlloc(currentCenter, halfExtents, displacement.normalized,
                _castBuffer, rotation, displacement.magnitude, _obstacleLayers, QueryTriggerInteraction.Ignore);
            if (count == _castBuffer.Length)
                return false;
            for (int index = 0; index < count; index++)
            {
                if (!IsIgnoredCollider(_castBuffer[index].collider, true))
                    return false;
            }

            return true;
        }

        private bool IsIgnoredCollider(Collider other, bool ignorePlayer)
        {
            return other == _vacuumCollider || (ignorePlayer && other.attachedRigidbody == _rigidbody);
        }

        private bool HasGroundSupport(Vector3 destination)
        {
            Vector3 halfExtents = _vacuumCollider.size * 0.5f;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = _vacuumCollider.center +
                        new Vector3(x * halfExtents.x, -halfExtents.y + 0.1f, z * halfExtents.z);
                    Vector3 origin = destination + _vacuumPivot.rotation *
                        Vector3.Scale(corner, _vacuumPivot.lossyScale);
                    int count = Physics.RaycastNonAlloc(origin, Vector3.down, _castBuffer,
                        0.2f, _obstacleLayers, QueryTriggerInteraction.Ignore);
                    bool supported = false;
                    for (int index = 0; index < count; index++)
                    {
                        if (!IsIgnoredCollider(_castBuffer[index].collider, true) && _castBuffer[index].normal.y > 0.99f)
                            supported = true;
                    }
                    if (!supported)
                        return false;
                }
            }

            return true;
        }

        private void RotateWithoutOverlap()
        {
            Quaternion start = _vacuumPivot.rotation;
            Quaternion target = Quaternion.LookRotation(_playerMovement.FacingDirection, Vector3.up);
            float angle = Quaternion.Angle(start, target);
            if (angle < 0.01f)
                return;

            // 최종 방향뿐 아니라 회전 경로도 검사해 얇은 벽을 가로질러 돌지 못하게 합니다.
            int steps = Mathf.CeilToInt(angle / 5f);
            Vector3 scale = _vacuumPivot.lossyScale;
            Vector3 halfExtents = Vector3.Scale(_vacuumCollider.size, scale) * 0.5f;
            Vector3 centerOffset = Vector3.Scale(_vacuumCollider.center, scale);
            float reach = new Vector2(centerOffset.x, centerOffset.z).magnitude +
                          new Vector2(halfExtents.x, halfExtents.z).magnitude;
            float padding = reach * (angle / steps) * Mathf.Deg2Rad;

            for (int step = 1; step <= steps; step++)
            {
                Quaternion candidate = Quaternion.Slerp(start, target, (float)step / steps);
                if (OverlapsObstacle(candidate, halfExtents, centerOffset, padding))
                    break;
                _vacuumPivot.rotation = candidate;
            }
        }

        private bool OverlapsObstacle(Quaternion rotation, Vector3 halfExtents, Vector3 centerOffset, float padding)
        {
            // FixedUpdate에서는 보간된 화면 위치 대신 Rigidbody의 물리 위치를 사용합니다.
            Vector3 pivotPosition = _rigidbody.position + _rigidbody.rotation * _vacuumPivot.localPosition;
            Vector3 center = pivotPosition + rotation * centerOffset;
            halfExtents += new Vector3(padding, 0f, padding);
            int count = Physics.OverlapBoxNonAlloc(center, halfExtents, _overlapBuffer,
                rotation, _obstacleLayers, QueryTriggerInteraction.Ignore);
            if (count == _overlapBuffer.Length)
                return true;
            for (int index = 0; index < count; index++)
            {
                if (!IsIgnoredCollider(_overlapBuffer[index], true))
                    return true;
            }

            return false;
        }

        private void SetRunning(bool running)
        {
            running = running && IsHeld && !IsFull && !IsEmptying && isActiveAndEnabled;
            if (IsRunning == running)
                return;
            IsRunning = running;
            _suctionElapsedTime = 0f;
            UpdateNozzleColor();
        }

        private void UpdateNozzleColor()
        {
            if (_nozzleRenderer == null || _nozzleProperties == null)
                return;
            _nozzleRenderer.GetPropertyBlock(_nozzleProperties);
            _nozzleProperties.SetColor("_BaseColor", IsFull ? Color.red : IsRunning ? Color.green : Color.gray);
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
