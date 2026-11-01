using Unity.Netcode;
using UnityEngine;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 문지르는 청소 도구. 창문닦이와 물걸레가 대상 종류만 다르게 이것을 쓴다.
    ///
    /// 닦는 방식은 대상에 따라 다르다. 바닥 얼룩(물걸레)은 들고 문질러야 한다 —
    /// 움직인 거리가 쌓일 때마다 걸레질 한 번으로 치고 근처 얼룩을 닦는다.
    /// 창문 얼룩(창문닦이)은 얼룩 앞에서 들기 키를 누를 때마다 한 번씩 닦는다.
    /// 연타할수록 빨리 지워지고, 얼룩이 없는 곳에서 짧게 누르면 여느 물건처럼 내려놓는다.
    ///
    /// 닦는 판정은 든 사람의 기기에서 세고, 깎는 것은 서버가 한다. 청소기와 같은 분담이다.
    /// 문지르고 찍는 움직임은 위치 동기화(NetworkTransform)를 타므로 모두가 같이 본다.
    /// </summary>
    public sealed class Wiper : CarriableItem
    {
        /// <summary>연타로 꾹 찍는 연출 한 번의 길이(초).</summary>
        private const float TapDipDuration = 0.18f;

        [Tooltip("안내 문구에 쓸 이름. 예: 창문닦이, 물걸레.")]
        [SerializeField] private string _itemName = "물걸레";
        [Tooltip("지울 수 있는 얼룩의 종류. 바닥은 문질러 닦고, 창문은 연타로 닦는다.")]
        [SerializeField] private GrimeKind _targetKind = GrimeKind.Floor;
        [SerializeField] private GrimeField _grimeField;
        [Tooltip("닦는 기준 지점. 비우면 물건의 위치를 쓴다.")]
        [SerializeField] private Transform _wipePoint;
        [Min(0.1f)] [SerializeField] private float _wipeRadius = 1.2f;
        [Tooltip("물걸레 전용. 이만큼(m) 문지를 때마다 근처 얼룩을 한 번씩 닦는다. 클수록 힘들다.")]
        [Min(0.1f)] [SerializeField] private float _rubDistancePerWipe = 1.3f;
        [Tooltip("보는 쪽으로 도는 반응 속도. 클수록 빨리 따라붙는다.")]
        [Min(0.1f)] [SerializeField] private float _aimSmoothing = 10f;

        private readonly int[] _spotBuffer = new int[16];
        private Vector3 _lastWipePosition;
        private bool _hasLastWipePosition;
        private float _rubAccumulated;
        private float _scrubPhase;
        private float _tapDipRemaining;

        protected override string ItemName => _itemName;

        /// <summary>
        /// 든 사람은 걸레가 실제로 향한 쪽을 본다. 걸레가 부드럽게 도는 동안
        /// 몸도 같이 따라 돌아, 몸만 먼저 홱 돌아 보이지 않는다.
        /// </summary>
        public override Vector3 HeldFacingDirection => transform.forward;

        private Vector3 WipePosition => _wipePoint != null ? _wipePoint.position : transform.position;

        /// <summary>창문 얼룩만 연타로 닦는다. 바닥 얼룩은 문질러야 한다.</summary>
        private bool IsTapMode => _targetKind == GrimeKind.Window;

        private bool HasSpotsInRange =>
            _grimeField != null && _grimeField.isActiveAndEnabled &&
            _grimeField.GetSpotsInRange(WipePosition, _wipeRadius, _targetKind, _spotBuffer) > 0;

        /// <summary>창문닦이는 얼룩 앞에서 짧게 누를 때마다 한 번씩 닦는다.</summary>
        public override bool CanTapUse => IsTapMode && IsHeld && HasSpotsInRange;

        public override void TapUse()
        {
            if (!IsOwner || !IsHeld)
                return;
            WipeSpotsInRange();
            _tapDipRemaining = TapDipDuration;
        }

        /// <summary>
        /// 든 사람이 보는 쪽으로 부드럽게 돈다. 청소기와 달리 몸에 붙는 작은 도구라
        /// 벽에 걸릴 일이 없으므로 경로 검사는 하지 않는다.
        /// </summary>
        public override void AimAt(Vector3 facingDirection)
        {
            if (!IsHeld || !IsOwner || facingDirection.sqrMagnitude < 0.000001f)
                return;

            Quaternion target = Quaternion.LookRotation(facingDirection, Vector3.up);
            float alpha = 1f - Mathf.Exp(-_aimSmoothing * Time.fixedDeltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, alpha);
        }

        protected override void OnCarryChanged()
        {
            _hasLastWipePosition = false;
            _rubAccumulated = 0f;
            _scrubPhase = 0f;
            _tapDipRemaining = 0f;
        }

        private void LateUpdate()
        {
            // 닦는 판정은 든 사람의 기기만 한다. 각자 세면 같은 얼룩을 두 번 닦는다.
            if (!IsOwner || !IsHeld)
                return;

            if (IsTapMode)
                UpdateTapDip();
            else
                UpdateRub();
        }

        /// <summary>
        /// 물걸레질. 문지른 거리가 쌓일 때마다 근처 얼룩을 닦는다. 걸레가 움직임을 따라
        /// 앞뒤로 문질러지고, 멈추면 제자리로 돌아온다.
        /// </summary>
        private void UpdateRub()
        {
            Vector3 current = WipePosition;
            if (!_hasLastWipePosition)
            {
                _lastWipePosition = current;
                _hasLastWipePosition = true;
                return;
            }

            Vector3 moved = current - _lastWipePosition;
            moved.y = 0f;
            _lastWipePosition = current;
            float distance = moved.magnitude;

            if (_grimeField != null && _grimeField.isActiveAndEnabled)
            {
                _rubAccumulated += distance;
                if (_rubAccumulated >= _rubDistancePerWipe)
                {
                    _rubAccumulated %= _rubDistancePerWipe;
                    WipeSpotsInRange();
                }
            }

            // 움직인 만큼 문지르는 동작이 진행된다. 서 있으면 동작도 잦아든다.
            _scrubPhase += distance * 6f;
            float speed = Time.deltaTime > 0f ? distance / Time.deltaTime : 0f;
            float stroke = Mathf.Clamp01(speed * 0.5f);
            Vector3 target = transform.localRotation *
                new Vector3(0f, 0f, Mathf.Sin(_scrubPhase) * 0.09f * stroke);
            transform.localPosition = Vector3.Lerp(transform.localPosition, target, 0.5f);
        }

        /// <summary>창문닦이를 꾹 찍는 연출. 닦을 때마다 아래로 살짝 내려갔다 돌아온다.</summary>
        private void UpdateTapDip()
        {
            if (_tapDipRemaining <= 0f && transform.localPosition.sqrMagnitude < 0.000001f)
                return;

            _tapDipRemaining = Mathf.Max(0f, _tapDipRemaining - Time.deltaTime);
            float progress = 1f - _tapDipRemaining / TapDipDuration;
            float dip = Mathf.Sin(progress * Mathf.PI) * 0.16f;
            transform.localPosition = new Vector3(0f, -dip, 0f);
        }

        private void WipeSpotsInRange()
        {
            if (_grimeField == null || !_grimeField.isActiveAndEnabled)
                return;
            int count = _grimeField.GetSpotsInRange(WipePosition, _wipeRadius, _targetKind, _spotBuffer);
            for (int index = 0; index < count; index++)
                WipeSpotRpc(_spotBuffer[index]);
        }

        /// <summary>닦았다고 서버에 알린다. 깎기와 전적은 서버가 맡는다.</summary>
        [Rpc(SendTo.Server)]
        private void WipeSpotRpc(int spotIndex, RpcParams rpcParams = default)
        {
            if (!IsFromHolder(rpcParams) || _grimeField == null)
                return;
            if (!_grimeField.ServerWipe(spotIndex, 1))
                return;

            // 마지막 한 번을 닦아 지운 사람의 전적에 더한다.
            PlayerScore score = HolderBody != null ? HolderBody.GetComponent<PlayerScore>() : null;
            if (score != null)
                score.ServerAddCleanedDust(1);
        }

        private void OnGUI()
        {
            if (!IsHeldByLocalPlayer)
                return;
            string hint = IsTapMode
                ? HasSpotsInRange ? "들기 키 연타: 닦기!" : "창문 얼룩 앞에서 들기 키를 연타하세요 (짧게: 내려놓기)"
                : "얼룩 위에서 움직이면 문질러 닦입니다 (짧게: 내려놓기)";
            GUI.Box(new Rect(16f, 16f, 360f, 44f), $"{_itemName}\n{hint}");
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(WipePosition, _wipeRadius);
        }
    }
}
