using Unity.Netcode;
using UnityEngine;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 빗자루. 들고서 들기 키를 꾹 누르고 있으면 쓸어 담는 지점 근처의 쓰레기를
    /// 일정 간격으로 담는다. 짧게 누르면 여느 물건처럼 내려놓는다.
    /// 담을 쓰레기를 고르는 것은 든 사람의 기기, 확정은 서버다. 청소기와 같은 분담이다.
    /// </summary>
    public sealed class Broom : CarriableItem
    {
        [SerializeField] private LitterField _litterField;
        [Tooltip("쓸어 담는 기준 지점. 비우면 물건의 위치를 쓴다.")]
        [SerializeField] private Transform _sweepPoint;
        [Min(0.1f)] [SerializeField] private float _sweepRadius = 1.5f;
        [Tooltip("이 간격(초)마다 범위 안 쓰레기를 담는다.")]
        [Min(0.05f)] [SerializeField] private float _sweepInterval = 0.5f;
        [Tooltip("한 번에 담는 쓰레기 수.")]
        [Min(1)] [SerializeField] private int _littersPerSweep = 2;
        [Tooltip("보는 쪽으로 도는 반응 속도. 클수록 빨리 따라붙는다.")]
        [Min(0.1f)] [SerializeField] private float _aimSmoothing = 10f;

        /// <summary>서버가 정한다. 담은 쓰레기 수. 모두가 같은 숫자를 본다.</summary>
        private readonly NetworkVariable<int> _storedLitter = new NetworkVariable<int>();

        private readonly int[] _litterBuffer = new int[16];
        private bool _isSweeping;
        private float _sweepElapsedTime;

        public int StoredLitterCount => _storedLitter.Value;

        protected override string ItemName => "빗자루";

        /// <summary>
        /// 든 사람은 빗자루가 실제로 향한 쪽을 본다. 빗자루가 부드럽게 도는 동안
        /// 몸도 같이 따라 돌아, 몸만 먼저 홱 돌아 보이지 않는다.
        /// </summary>
        public override Vector3 HeldFacingDirection => transform.forward;

        private Vector3 SweepPosition => _sweepPoint != null ? _sweepPoint.position : transform.position;

        private bool HasLitterInRange =>
            _litterField != null && _litterField.isActiveAndEnabled &&
            _litterField.GetLittersInRange(SweepPosition, _sweepRadius, _litterBuffer) > 0;

        /// <summary>쓰레기 옆에서 꾹 누르면 쓸어 담는다.</summary>
        public override bool CanHoldUse => IsHeld && HasLitterInRange;

        public override void BeginHoldUse()
        {
            _isSweeping = true;
            _sweepElapsedTime = 0f;
        }

        public override void EndHoldUse()
        {
            _isSweeping = false;
        }

        protected override void OnCarryChanged()
        {
            _isSweeping = false;
        }

        /// <summary>든 사람이 보는 쪽으로 부드럽게 돈다. 작은 도구라 경로 검사는 하지 않는다.</summary>
        public override void AimAt(Vector3 facingDirection)
        {
            if (!IsHeld || !IsOwner || facingDirection.sqrMagnitude < 0.000001f)
                return;

            Quaternion target = Quaternion.LookRotation(facingDirection, Vector3.up);
            float alpha = 1f - Mathf.Exp(-_aimSmoothing * Time.fixedDeltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, alpha);
        }

        private void LateUpdate()
        {
            // 담을 쓰레기를 고르는 것은 든 사람의 기기만 한다. 각자 고르면 같은 것을 두 번 담는다.
            if (!_isSweeping || !IsOwner || !IsHeld || _litterField == null)
                return;

            _sweepElapsedTime += Time.deltaTime;
            if (_sweepElapsedTime < _sweepInterval)
                return;

            // 프레임 지연 뒤 여러 회차를 한꺼번에 담지 않는다.
            _sweepElapsedTime %= _sweepInterval;
            int count = _litterField.GetLittersInRange(SweepPosition, _sweepRadius, _litterBuffer);
            int take = Mathf.Min(count, _littersPerSweep);
            for (int index = 0; index < take; index++)
                CollectLitterRpc(_litterBuffer[index]);
        }

        /// <summary>담았다고 서버에 알린다. 확정과 전적은 서버가 맡는다.</summary>
        [Rpc(SendTo.Server)]
        private void CollectLitterRpc(int litterIndex, RpcParams rpcParams = default)
        {
            if (!IsFromHolder(rpcParams) || _litterField == null)
                return;
            if (!_litterField.ServerCollect(litterIndex))
                return;

            _storedLitter.Value++;
            PlayerScore score = HolderBody != null ? HolderBody.GetComponent<PlayerScore>() : null;
            if (score != null)
                score.ServerAddCleanedDust(1);
        }

        private void OnGUI()
        {
            if (!IsHeldByLocalPlayer)
                return;
            string hint = _isSweeping ? "쓸어 담는 중..." :
                HasLitterInRange ? "들기 키 꾹: 쓸어 담기 / 짧게: 내려놓기" :
                "쓰레기 가까이에서 꾹 누르면 담습니다 (짧게 눌러 내려놓기)";
            GUI.Box(new Rect(16f, 16f, 360f, 48f), $"담은 쓰레기 {StoredLitterCount}\n{hint}");
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(SweepPosition, _sweepRadius);
        }
    }
}
