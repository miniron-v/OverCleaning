using Unity.Netcode;
using UnityEngine;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 플레이어끼리 부딪치면 잠깐 기절한다. 들고 있던 물건을 그 자리에 떨어뜨리고,
    /// 몸에 붙어 있던 털먼지가 주변 바닥에 흩어진다. 달리는 차에 치이면 훨씬 멀리 튕겨난다.
    ///
    /// 부딪침은 각자 자기 캐릭터의 기기에서 재고, 한쪽이 보고하면 서버가 양쪽을 함께
    /// 기절시킨다. 기절한 모습(머리 위 별)은 모든 기기에서 그려야 하므로 이 컴포넌트는
    /// 자기 캐릭터가 아니어도 꺼지지 않고, 자기 것만 해야 하는 일은 안에서 가린다.
    /// </summary>
    public sealed class PlayerCrash : NetworkBehaviour
    {
        [Tooltip("부딪친 뒤 움직이지 못하는 시간(초).")]
        [Min(0.1f)] [SerializeField] private float _stunDuration = 1.2f;
        [Tooltip("부딪친 반대쪽으로 튕겨나는 속도.")]
        [Min(0f)] [SerializeField] private float _knockbackSpeed = 5f;

        /// <summary>튕겨난 속도가 1초에 줄어드는 비율. 기절이 끝날 즈음이면 거의 멈춘다.</summary>
        private const float KnockbackDamping = 4f;

        /// <summary>
        /// 차에 치이면 사람과 부딪칠 때보다 이만큼 빠르게, 그만큼 멀리 튕겨난다.
        /// 5 × 4 = 20으로 차의 속도와 같은 빠르기로 튕겨나고, 20 ÷ 감쇠 4 = 약 5m로 차 길이(4.4m)보다 조금 더 날아간다.
        /// </summary>
        private const float CarKnockbackMultiplier = 4f;
        [Tooltip("부딪칠 때 주변에 흩어질 털먼지 수. 흩어진 먼지는 다시 빨아들여야 한다.")]
        [Min(0)] [SerializeField] private int _furDustCount = 10;
        [Min(0.1f)] [SerializeField] private float _furDustRadius = 1.5f;
        [Tooltip("흩어질 털먼지 모양. 아무 이미지나 지정할 수 있고, 비우면 바닥 먼지 모양 중 아무거나 쓴다.")]
        [SerializeField] private Texture2D _furDustTexture;

        /// <summary>서버가 정한다. 이 시각(서버 시간)까지 기절이다.</summary>
        private readonly NetworkVariable<double> _stunnedUntil = new NetworkVariable<double>();

        private PlayerMovement _playerMovement;
        private Rigidbody _body;
        private DustField _dustField;
        private Camera _statusCamera;
        private bool _movementFrozen;

        /// <summary>서버 확정이 도착하기 전까지의 로컬 예측. 없으면 확정이 오기까지
        /// 몇 프레임 동안 이동 코드가 튕겨난 속도를 덮어써 버린다.</summary>
        private float _predictedStunEndTime;

        public bool IsStunned => IsSpawned && NetworkManager.ServerTime.Time < _stunnedUntil.Value;

        private bool IsMovementFrozen => IsStunned || Time.time < _predictedStunEndTime;

        private void Awake()
        {
            _playerMovement = GetComponent<PlayerMovement>();
            _body = GetComponent<Rigidbody>();
        }

        private void Update() => UpdateMovementFrozen();

        private void UpdateMovementFrozen()
        {
            if (!IsOwner || _playerMovement == null)
                return;

            // 기절 동안 조작을 멈춘다. 끝나면 돌려준다. 튕겨난 속도는 지우지 않는다.
            bool frozen = IsMovementFrozen;
            if (frozen == _movementFrozen)
                return;
            _movementFrozen = frozen;
            _playerMovement.enabled = !frozen;
            if (frozen)
                _playerMovement.ResetInputState();
        }

        private void FixedUpdate()
        {
            if (!IsOwner || !_movementFrozen)
                return;

            // 튕겨난 속도를 서서히 줄인다. 기절이 끝날 즈음이면 거의 멈춘다.
            float decay = Mathf.Exp(-KnockbackDamping * Time.fixedDeltaTime);
            Vector3 velocity = _body.linearVelocity;
            _body.linearVelocity = new Vector3(velocity.x * decay, velocity.y, velocity.z * decay);
        }

        private void OnCollisionEnter(Collision collision)
        {
            // 부딪침은 자기 캐릭터의 기기에서만 센다. 남의 캐릭터는 위치만 따라올 뿐이다.
            if (!IsSpawned || !IsOwner || IsMovementFrozen || collision.rigidbody == null)
                return;
            PlayerCrash other = collision.rigidbody.GetComponent<PlayerCrash>();
            if (other == null)
                return;

            // 부딪친 반대쪽으로 튕겨난다. 서버 확정을 기다리지 않고 바로 몸이 반응해야
            // 부딪친 느낌이 난다.
            ApplyKnockback(_body.position - collision.rigidbody.position, _knockbackSpeed);

            ReportCrashRpc(other);
        }

        /// <summary>
        /// 둘이 부딪친 것이므로 서버가 양쪽을 함께 기절시킨다. 멈춰 있던 쪽의 기기에서는
        /// 상대가 튕겨나간 뒤의 위치만 받아 충돌이 아예 잡히지 않을 수 있기 때문에,
        /// 한쪽의 보고만으로 둘 다 처리한다. 양쪽이 다 보고하면 뒤의 것은 무시된다.
        /// </summary>
        [Rpc(SendTo.Server)]
        private void ReportCrashRpc(NetworkBehaviourReference otherReference, RpcParams rpcParams = default)
        {
            // 자기 캐릭터의 부딪침만 받아 준다.
            if (rpcParams.Receive.SenderClientId != OwnerClientId)
                return;

            if (otherReference.TryGet(out PlayerCrash other))
            {
                ServerApplyCrash(_body.position - other._body.position, _knockbackSpeed);
                other.ServerApplyCrash(other._body.position - _body.position, other._knockbackSpeed);
            }
            else
            {
                ServerApplyCrash(Vector3.zero, _knockbackSpeed);
            }
        }

        /// <summary>
        /// 서버에서만 부른다. 치임은 운전자 화면에서 잡고 날아갈 방향도 거기서 정한다(Car).
        /// 치인 사람만 기절하고, 차와 운전자는 그대로 달린다.
        /// </summary>
        internal void ServerApplyCarHit(Vector3 direction)
        {
            ServerApplyCrash(direction, _knockbackSpeed * CarKnockbackMultiplier);
        }

        /// <summary>서버에서만 부른다. 기절시키고, 든 것을 떨어뜨리고, 털먼지를 흩는다.</summary>
        private void ServerApplyCrash(Vector3 knockbackDirection, float knockbackSpeed)
        {
            if (IsStunned)
                return;

            _stunnedUntil.Value = NetworkManager.ServerTime.Time + _stunDuration;

            // 전적에 더한다. 전적은 한 판 동안만 세므로 판이 없는 대기방에서는 세지 않는다.
            PlayerScore score = GetComponent<PlayerScore>();
            if (score != null && FindAnyObjectByType<GameRound>() != null)
                score.ServerAddCrash();

            // 들고 있던 것은 그 자리에 떨어뜨린다.
            CarriableItem heldItem = GetComponentInChildren<CarriableItem>();
            if (heldItem != null)
                heldItem.ServerForceDrop();

            SpillFurDustRpc(_body.position, Random.Range(1, int.MaxValue));

            // 제 기기에서 충돌을 못 잡은 쪽도 튕겨나도록 주인에게 알린다.
            KnockbackRpc(knockbackDirection, knockbackSpeed);
        }

        [Rpc(SendTo.Owner)]
        private void KnockbackRpc(Vector3 direction, float speed)
        {
            // 제 기기에서 이미 예측으로 튕겨났으면 두 번 하지 않는다.
            if (Time.time < _predictedStunEndTime)
                return;
            ApplyKnockback(direction, speed);
        }

        private void ApplyKnockback(Vector3 direction, float speed)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.000001f)
                _body.linearVelocity = direction.normalized * speed;
            _predictedStunEndTime = Time.time + _stunDuration;
            // RPC는 프레임 초반에 오므로 Update까지 기다리면 그 사이 이동 코드가 튕겨난 속도를 덮어쓴다.
            UpdateMovementFrozen();
        }

        /// <summary>털먼지는 모두의 바닥에 같은 자리로 흩어져야 한다. 시드로 맞춘다.</summary>
        [Rpc(SendTo.Everyone)]
        private void SpillFurDustRpc(Vector3 center, int seed)
        {
            if (_furDustCount <= 0)
                return;
            if (_dustField == null)
                _dustField = FindFirstObjectByType<DustField>();
            if (_dustField != null)
                _dustField.SpillDust(center, _furDustRadius, _furDustCount, seed, _furDustTexture);
        }

        private void OnGUI()
        {
            if (!IsStunned)
                return;
            if (_statusCamera == null)
                _statusCamera = Camera.main;
            if (_statusCamera == null)
                return;

            Vector3 screenPosition = _statusCamera.WorldToScreenPoint(transform.position + Vector3.up * 2.2f);
            if (screenPosition.z <= 0f)
                return;

            // 머리 위에서 별이 빙글빙글 돈다.
            float spin = (float)(NetworkManager.ServerTime.Time * 4.0);
            for (int index = 0; index < 3; index++)
            {
                float angle = spin + index * Mathf.PI * 2f / 3f;
                float x = screenPosition.x + Mathf.Cos(angle) * 28f;
                float y = Screen.height - screenPosition.y + Mathf.Sin(angle) * 9f;
                GUI.Label(new Rect(x - 10f, y - 10f, 20f, 20f), "★");
            }
        }
    }
}
