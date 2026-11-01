using Unity.Netcode;
using UnityEngine;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 빗자루. 들고서 들기 키를 꾹 누르고 있으면 빗자루질을 하고, 쓸어 내는 지점
    /// 근처의 먼지 — 청소기가 빨아들이는 바로 그 먼지 — 를 일정 간격으로 쓸어 치운다.
    /// 짧게 누르면 여느 물건처럼 내려놓는다.
    ///
    /// 치울 먼지를 고르는 것은 든 사람의 기기, 확정은 서버다. 먼지 번호는 모든
    /// 기기에서 같으므로, 내 화면에서 치운 먼지를 서버가 남의 화면에서도 지워 준다.
    /// 청소기와 같은 분담이고, 휘젓는 움직임은 NetworkTransform을 타서 모두가 본다.
    /// </summary>
    public sealed class Broom : CarriableItem
    {
        [SerializeField] private DustField _dustField;
        [Tooltip("쓸어 내는 기준 지점. 비우면 물건의 위치를 쓴다.")]
        [SerializeField] private Transform _sweepPoint;
        [Min(0.1f)] [SerializeField] private float _sweepRadius = 1.5f;
        [Tooltip("이 간격(초)마다 범위 안 먼지를 쓸어 치운다.")]
        [Min(0.05f)] [SerializeField] private float _sweepInterval = 0.5f;
        [Tooltip("한 번에 쓸어 치우는 먼지 수. 청소기보다 느긋한 것이 빗자루답다.")]
        [Min(1)] [SerializeField] private int _dustPerSweep = 4;
        [Tooltip("보는 쪽으로 도는 반응 속도. 클수록 빨리 따라붙는다.")]
        [Min(0.1f)] [SerializeField] private float _aimSmoothing = 10f;

        /// <summary>서버가 정한다. 쓸어 치운 먼지 수. 모두가 같은 숫자를 본다.</summary>
        private readonly NetworkVariable<int> _sweptDust = new NetworkVariable<int>();

        /// <summary>서버가 정한다. 쓸고 있는지. 빗자루질 중 표시를 모두가 본다.</summary>
        private readonly NetworkVariable<bool> _sweeping = new NetworkVariable<bool>();

        private readonly int[] _dustIdBuffer = new int[16];
        private bool _isSweeping;
        private float _sweepElapsedTime;
        private float _swingPhase;
        private Camera _statusCamera;

        public int SweptDustCount => _sweptDust.Value;

        protected override string ItemName => "빗자루";

        /// <summary>
        /// 든 사람은 빗자루가 실제로 향한 쪽을 본다. 빗자루가 부드럽게 도는 동안
        /// 몸도 같이 따라 돌아, 몸만 먼저 홱 돌아 보이지 않는다.
        /// </summary>
        public override Vector3 HeldFacingDirection => transform.forward;

        private Vector3 SweepPosition => _sweepPoint != null ? _sweepPoint.position : transform.position;

        private bool HasDustInRange =>
            _dustField != null && _dustField.isActiveAndEnabled &&
            _dustField.GetDustIdsInRange(SweepPosition, _sweepRadius, _dustIdBuffer) > 0;

        /// <summary>
        /// 들고 꾹 누르면 어디서든 빗자루질을 한다. 먼지가 없어도 휘저어지는 모습이
        /// 보여야 쓰는 느낌이 나고, 치우기는 범위에 먼지가 있을 때만 된다.
        /// </summary>
        public override bool CanHoldUse => IsHeld;

        public override void BeginHoldUse()
        {
            _isSweeping = true;
            _sweepElapsedTime = 0f;
            if (IsSpawned && IsHeldByLocalPlayer)
                SetSweepingRpc(true);
        }

        public override void EndHoldUse()
        {
            _isSweeping = false;
            if (IsSpawned && IsHeldByLocalPlayer)
                SetSweepingRpc(false);
        }

        protected override void OnCarryChanged()
        {
            _isSweeping = false;
            _swingPhase = 0f;
            // 내려놓이면 쓸기 표시도 꺼진다. 든 사람이 사라져 RPC가 못 오는 경우의 뒷정리다.
            if (IsServer && !IsHeld)
                _sweeping.Value = false;
        }

        [Rpc(SendTo.Server)]
        private void SetSweepingRpc(bool sweeping, RpcParams rpcParams = default)
        {
            if (IsFromHolder(rpcParams))
                _sweeping.Value = sweeping;
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
            // 치울 먼지를 고르는 것은 든 사람의 기기만 한다. 각자 고르면 같은 것을 두 번 센다.
            if (!IsOwner || !IsHeld)
                return;

            UpdateSwing();

            if (!_isSweeping || _dustField == null || !_dustField.isActiveAndEnabled)
                return;

            _sweepElapsedTime += Time.deltaTime;
            if (_sweepElapsedTime < _sweepInterval)
                return;

            // 프레임 지연 뒤 여러 회차를 한꺼번에 치우지 않는다.
            _sweepElapsedTime %= _sweepInterval;
            int count = _dustField.GetDustIdsInRange(SweepPosition, _sweepRadius, _dustIdBuffer);
            int take = Mathf.Min(count, _dustPerSweep);
            for (int index = 0; index < take; index++)
            {
                // 내 화면에서 바로 치우고 서버에 알린다. 청소기가 빤 먼지를 알리는 흐름과 같다.
                _dustField.RemoveDustById(_dustIdBuffer[index]);
                ReportSweptDustRpc(_dustIdBuffer[index]);
            }
        }

        /// <summary>
        /// 쓸는 동안 빗자루가 좌우로 휘저어진다. 멈추면 제자리로 돌아온다.
        /// 위치는 NetworkTransform이 나르므로 다른 화면에도 같은 모습이 보인다.
        /// </summary>
        private void UpdateSwing()
        {
            if (_isSweeping)
                _swingPhase += Time.deltaTime * 9f;
            Vector3 target = _isSweeping
                ? transform.localRotation * new Vector3(Mathf.Sin(_swingPhase) * 0.3f, 0f, 0f)
                : Vector3.zero;
            transform.localPosition = Vector3.Lerp(transform.localPosition, target, 0.35f);
        }

        /// <summary>쓸어 치웠다고 서버에 알린다. 수량 확정과 전적, 남의 화면 정리는 서버가 맡는다.</summary>
        [Rpc(SendTo.Server)]
        private void ReportSweptDustRpc(int dustId, RpcParams rpcParams = default)
        {
            if (!IsFromHolder(rpcParams))
                return;

            _sweptDust.Value++;
            PlayerScore score = HolderBody != null ? HolderBody.GetComponent<PlayerScore>() : null;
            if (score != null)
                score.ServerAddCleanedDust(1);
            RemoveDustRpc(dustId);
        }

        /// <summary>쓸어 낸 사람은 이미 지웠으므로 그 사람만 빼고 보낸다.</summary>
        [Rpc(SendTo.NotOwner)]
        private void RemoveDustRpc(int dustId)
        {
            if (_dustField != null)
                _dustField.RemoveDustById(dustId);
        }

        private void OnGUI()
        {
            DrawSweepingStatus();

            if (!IsHeldByLocalPlayer)
                return;
            string hint = _isSweeping ? "쓸어 치우는 중..." :
                HasDustInRange ? "들기 키 꾹: 쓸어 치우기 / 짧게: 내려놓기" :
                "꾹 누르면 빗자루질을 하고, 먼지 위에서는 쓸려 나갑니다 (짧게: 내려놓기)";
            GUI.Box(new Rect(16f, 16f, 360f, 48f), $"쓸어 치운 먼지 {SweptDustCount}\n{hint}");
        }

        /// <summary>
        /// 빗자루질 중 표시. 서버가 내려주는 값이라 든 사람뿐 아니라 모든 기기에서 보인다.
        /// </summary>
        private void DrawSweepingStatus()
        {
            if (!IsSpawned || !_sweeping.Value || !IsHeld)
                return;
            if (_statusCamera == null)
                _statusCamera = Camera.main;
            if (_statusCamera == null)
                return;

            Vector3 screenPosition = _statusCamera.WorldToScreenPoint(
                HolderBody.position + Vector3.up * 2.3f);
            if (screenPosition.z <= 0f)
                return;
            GUI.Box(new Rect(screenPosition.x - 70f, Screen.height - screenPosition.y - 24f, 140f, 24f),
                "빗자루질 중...");
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(SweepPosition, _sweepRadius);
        }
    }
}
