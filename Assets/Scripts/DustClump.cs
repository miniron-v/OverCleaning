using Unity.Netcode;
using UnityEngine;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 빗자루로 쓸어 모은 먼지 뭉치. 쓸어 올수록 커지고, 들어서 쓰레기통 옆에서
    /// 꾹 누르면 한 번에 쏟아 버린다. 비어 있으면 보이지도 집히지도 않는다.
    ///
    /// 수량은 서버가 정하고, 커지는 모습은 수량을 받은 각 기기가 그린다.
    /// 자리도 서버가 옮긴다 — 놓여 있는 동안 빗자루질하는 자리로 끌려와 모인다.
    /// </summary>
    public sealed class DustClump : CarriableItem
    {
        [Tooltip("쏟을 쓰레기통.")]
        [SerializeField] private TrashCan _trashCan;
        [Tooltip("커지는 몸통. 모인 수량의 세제곱근에 비례해 커진다.")]
        [SerializeField] private Transform _ball;
        [Tooltip("먼지 1개일 때의 지름(m).")]
        [Min(0.05f)] [SerializeField] private float _baseDiameter = 0.25f;

        /// <summary>서버가 정한다. 뭉친 먼지 수. 모두가 같은 크기를 본다.</summary>
        private readonly NetworkVariable<int> _storedDust = new NetworkVariable<int>();

        public int StoredDustCount => _storedDust.Value;

        protected override string ItemName => "뭉친 먼지";

        /// <summary>빈 뭉치는 집을 수 없다. 보이지도 않으므로 집혀서도 안 된다.</summary>
        public override bool CanInteract => StoredDustCount > 0 && base.CanInteract;

        /// <summary>안아 든 것처럼 몸 앞에 들린다.</summary>
        protected override Vector3 HeldLocalPosition => new Vector3(0f, 0.55f, 0.45f);

        public override void OnNetworkSpawn()
        {
            _storedDust.OnValueChanged += OnStoredDustChanged;
            base.OnNetworkSpawn();
            UpdateBody();
        }

        public override void OnNetworkDespawn()
        {
            _storedDust.OnValueChanged -= OnStoredDustChanged;
            base.OnNetworkDespawn();
        }

        private void OnStoredDustChanged(int previousCount, int newCount) => UpdateBody();

        /// <summary>들리거나 놓일 때 Collider가 다시 켜지므로, 빈 몸 숨김도 다시 맞춘다.</summary>
        protected override void OnCarryChanged() => UpdateBody();

        /// <summary>수량에 맞춰 몸집을 바꾼다. 비면 숨고, 쌓이면 세제곱근으로 커진다.</summary>
        private void UpdateBody()
        {
            bool hasDust = StoredDustCount > 0;
            if (_ball != null)
            {
                _ball.gameObject.SetActive(hasDust);
                if (hasDust)
                {
                    float diameter = _baseDiameter * Mathf.Pow(StoredDustCount, 1f / 3f);
                    _ball.localScale = Vector3.one * diameter;
                    _ball.localPosition = new Vector3(0f, diameter * 0.5f, 0f);
                }
            }

            // 빈 뭉치가 보이지 않는 벽처럼 길을 막거나 집히지 않게 한다.
            if (_bodyCollider != null)
                _bodyCollider.enabled = hasDust;
        }

        /// <summary>
        /// 서버에서만 부른다. 빗자루가 쓸어 온 먼지를 더하고, 놓여 있는 동안은
        /// 쓸던 자리로 끌어다 둔다. 빗자루질을 따라다니며 한 곳에 뭉치는 모습이 된다.
        /// </summary>
        internal void ServerAddDust(int amount, Vector3 gatherPosition)
        {
            if (!IsServer || amount <= 0)
                return;

            _storedDust.Value += amount;
            if (!IsHeld)
            {
                gatherPosition.y = transform.position.y;
                transform.position = gatherPosition;
            }
        }

        /// <summary>쓰레기통 옆에서 꾹 누르면 모은 먼지를 한 번에 쏟아 버린다.</summary>
        public override bool CanHoldUse =>
            IsHeld && StoredDustCount > 0 && _trashCan != null &&
            _trashCan.CanReceiveDust(HandPosition, HolderBody);

        public override void BeginHoldUse()
        {
            if (IsSpawned && IsHeldByLocalPlayer)
                DumpRpc();
        }

        /// <summary>든 사람의 손 높이. 쓰레기통까지의 거리를 재는 기준이다.</summary>
        private Vector3 HandPosition => HolderBody.position + Vector3.up * 0.5f;

        /// <summary>
        /// 쏟기는 서버가 확정한다. 쏟은 사람의 전적에 더하고, 다 비면 빈손이 어색하지
        /// 않게 바로 내려놓아 준다. 빈 뭉치는 내려놓이는 순간 보이지 않게 된다.
        /// </summary>
        [Rpc(SendTo.Server)]
        private void DumpRpc(RpcParams rpcParams = default)
        {
            if (!IsFromHolder(rpcParams) || StoredDustCount <= 0 || _trashCan == null)
                return;
            if (!_trashCan.TryReceiveDust(HandPosition, HolderBody, _storedDust.Value))
                return;

            PlayerScore score = HolderBody != null ? HolderBody.GetComponent<PlayerScore>() : null;
            if (score != null)
                score.ServerAddCleanedDust(_storedDust.Value);
            _storedDust.Value = 0;
            ServerForceDrop();
        }

        private void OnGUI()
        {
            if (!IsHeldByLocalPlayer || StoredDustCount <= 0)
                return;
            string hint = CanHoldUse ? "들기 키 꾹: 쓰레기통에 쏟기 / 짧게: 내려놓기"
                : "쓰레기통 가까이 가서 꾹 누르면 쏟습니다 (짧게: 내려놓기)";
            GUI.Box(new Rect(16f, 16f, 360f, 48f), $"뭉친 먼지 {StoredDustCount}\n{hint}");
        }
    }
}
