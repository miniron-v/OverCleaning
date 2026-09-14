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
        [SerializeField] private InputAction _vacuumAction =
            new InputAction("Vacuum", InputActionType.Button, "<Keyboard>/space");

        private PlayerMovement _playerMovement;
        private MaterialPropertyBlock _nozzleProperties;
        private Rigidbody _rigidbody;
        private readonly Collider[] _overlapBuffer = new Collider[32];

        public bool IsRunning { get; private set; }
        public Vector3 SuctionPosition => _suctionPoint.position;
        public float SuctionRadius => _suctionRadius;

        private void Awake()
        {
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
            UpdateNozzleColor();
        }

        private void OnEnable() => _vacuumAction.Enable();

        private void OnDisable()
        {
            _vacuumAction.Disable();
            SetRunning(false);
        }

        private void OnDestroy() => _vacuumAction.Dispose();

        private void FixedUpdate()
        {
            RotateWithoutOverlap();
        }

        private void LateUpdate()
        {
            SetRunning(Application.isFocused && _vacuumAction.IsPressed());
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
                if (_overlapBuffer[index].attachedRigidbody != _rigidbody)
                    return true;
            }

            return false;
        }

        private void SetRunning(bool running)
        {
            if (IsRunning == running)
                return;
            IsRunning = running;
            UpdateNozzleColor();
        }

        private void UpdateNozzleColor()
        {
            if (_nozzleRenderer == null || _nozzleProperties == null)
                return;
            _nozzleRenderer.GetPropertyBlock(_nozzleProperties);
            _nozzleProperties.SetColor("_BaseColor", IsRunning ? Color.green : Color.gray);
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
