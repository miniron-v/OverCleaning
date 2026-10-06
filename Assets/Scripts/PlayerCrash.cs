using Unity.Netcode;
using UnityEngine;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 플레이어끼리 부딪치면 잠깐 기절한다. 들고 있던 물건을 그 자리에 떨어뜨리고,
    /// 몸에 붙어 있던 털먼지가 주변 바닥에 흩어진다.
    ///
    /// 부딪침은 각자 자기 캐릭터의 기기에서 재고, 기절은 서버가 확정한다.
    /// 기절한 모습(머리 위 별)은 모든 기기에서 그려야 하므로 이 컴포넌트는
    /// 자기 캐릭터가 아니어도 꺼지지 않고, 자기 것만 해야 하는 일은 안에서 가린다.
    /// </summary>
    public sealed class PlayerCrash : NetworkBehaviour
    {
        [Tooltip("부딪친 뒤 움직이지 못하는 시간(초).")]
        [Min(0.1f)] [SerializeField] private float _stunDuration = 1.2f;
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

        public bool IsStunned => IsSpawned && NetworkManager.ServerTime.Time < _stunnedUntil.Value;

        private void Awake()
        {
            _playerMovement = GetComponent<PlayerMovement>();
            _body = GetComponent<Rigidbody>();
        }

        private void Update()
        {
            if (!IsOwner || _playerMovement == null)
                return;

            // 기절 동안 조작을 멈춘다. 끝나면 돌려준다.
            bool frozen = IsStunned;
            if (frozen == _movementFrozen)
                return;
            _movementFrozen = frozen;
            _playerMovement.enabled = !frozen;
            if (frozen)
            {
                _playerMovement.ResetInputState();
                _body.linearVelocity = new Vector3(0f, _body.linearVelocity.y, 0f);
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            // 부딪침은 자기 캐릭터의 기기에서만 센다. 남의 캐릭터는 위치만 따라올 뿐이다.
            if (!IsSpawned || !IsOwner || IsStunned || collision.rigidbody == null)
                return;
            if (collision.rigidbody.GetComponent<PlayerCrash>() == null)
                return;

            ReportCrashRpc();
        }

        [Rpc(SendTo.Server)]
        private void ReportCrashRpc(RpcParams rpcParams = default)
        {
            // 자기 캐릭터의 부딪침만 받아 준다. 이미 기절 중이면 무시한다.
            if (rpcParams.Receive.SenderClientId != OwnerClientId || IsStunned)
                return;

            _stunnedUntil.Value = NetworkManager.ServerTime.Time + _stunDuration;

            // 들고 있던 것은 그 자리에 떨어뜨린다.
            CarriableItem heldItem = GetComponentInChildren<CarriableItem>();
            if (heldItem != null)
                heldItem.ServerForceDrop();

            SpillFurDustRpc(_body.position, Random.Range(1, int.MaxValue));
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
