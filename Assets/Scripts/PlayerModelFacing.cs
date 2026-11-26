using Unity.Netcode;
using UnityEngine;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 캐릭터 모델을 실제로 이동한 방향으로 돌리되, 물건을 들었으면 그 물건 쪽을 본다.
    /// 루트를 돌리면 들고 있는 청소기가 충돌 검사 없이 함께 돌아가므로 모델만 돌린다.
    /// 이동은 위치 변화로, 든 물건은 자식 관계로 읽으므로 둘 다 입력 없이도
    /// 원격 플레이어가 같은 방향을 본다. 걷는 동작도 같은 위치 변화로 정한다.
    /// </summary>
    public sealed class PlayerModelFacing : MonoBehaviour
    {
        [Tooltip("돌릴 모델. 고를 수 있는 캐릭터 모델을 모두 자식으로 둔다.")]
        [SerializeField] private Transform _model;
        [Min(1f)] [SerializeField] private float _turnSpeed = 720f;

        /// <summary>1초에 이만큼(m) 넘게 움직이면 걷는 동작을 한다. 걷는 속도(5)보다 한참 작다.</summary>
        private const float WalkingSpeed = 0.5f;

        private static readonly int IsWalkingId = Animator.StringToHash("IsWalking");

        private Animator _animator;
        private Vector3 _previousPosition;

        /// <summary>
        /// 정해지면 이동 방향 대신 이 대상과 같은 쪽을 바로 본다. 예: 차에 탄 동안의 차.
        /// 차 위에서 생기는 위치 변화는 걸은 방향이 아니기 때문이다.
        /// </summary>
        public Transform FacingTarget { get; set; }

        /// <summary>
        /// 정해지면 모델을 이 자리에 둔다. 예: 의자에 앉은 동안의 앉는 자리.
        /// 높이는 다른 화면에 전해지지 않으므로, 몸은 바닥에 둔 채 모델만 올려 모든 화면에서 같게 그린다.
        /// </summary>
        public Transform SeatPoint { get; set; }

        private NetworkObject _networkObject;
        private Vector3 _modelLocalPosition;

        private void Awake()
        {
            _networkObject = GetComponent<NetworkObject>();
            _modelLocalPosition = _model.localPosition;
        }

        private void OnEnable()
        {
            _previousPosition = transform.position;
        }

        private void LateUpdate()
        {
            Vector3 delta = transform.position - _previousPosition;
            _previousPosition = transform.position;
            bool isWalking = FacingTarget == null &&
                             Vector3.ProjectOnPlane(delta, Vector3.up).magnitude > WalkingSpeed * Time.deltaTime;
            // 캐릭터를 바꾸면 켜진 모델이 달라지므로, 쓰던 동작이 꺼지면 켜진 모델의 것을 다시 찾는다.
            if (_animator == null || !_animator.isActiveAndEnabled)
                _animator = _model.GetComponentInChildren<Animator>();
            if (_animator != null)
                _animator.SetBool(IsWalkingId, isWalking);
            if (SeatPoint != null)
                _model.position = SeatPoint.position;
            else
                _model.localPosition = _modelLocalPosition;
            if (FacingTarget != null)
            {
                _model.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(FacingTarget.forward, Vector3.up), Vector3.up);
                return;
            }

            // 함께 드는 물건(소파)을 잡고 있으면 그쪽이 먼저다. 잡은 목록이 동기화되는
            // 값이라 남의 화면에서도 잡은 사람이 물건을 바라본다.
            // 든 물건이 있으면 그쪽을 본다. 물건이 보는 방향을 정해 주면(청소기의 실제 방향)
            // 그 방향을 따르고, 아니면 물건이 놓인 쪽을 본다. 장착한 물건이 몸 중심에 붙어
            // 방향을 알 수 없을 때만 이동 방향을 그대로 쓴다.
            Vector3 direction = Vector3.zero;
            Transform lifted = _networkObject != null && _networkObject.IsSpawned
                ? CoopCarriable.FindLiftedBy(_networkObject.OwnerClientId)
                : null;
            if (lifted != null)
                direction = lifted.position - transform.position;
            CarriableItem heldItem = direction.sqrMagnitude < 0.000001f
                ? GetComponentInChildren<CarriableItem>()
                : null;
            if (heldItem != null)
            {
                direction = heldItem.HeldFacingDirection;
                if (direction.sqrMagnitude < 0.000001f)
                    direction = heldItem.transform.position - transform.position;
            }
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.000001f)
            {
                direction = delta;
                direction.y = 0f;
            }
            if (direction.sqrMagnitude < 0.000001f)
                return;

            Quaternion target = Quaternion.LookRotation(direction, Vector3.up);
            _model.rotation = Quaternion.RotateTowards(_model.rotation, target, _turnSpeed * Time.deltaTime);
        }
    }
}
