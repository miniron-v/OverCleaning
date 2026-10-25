using Unity.Netcode;
using UnityEngine;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 문지르는 청소 도구. 창문닦이와 물걸레가 대상 종류만 다르게 이것을 쓴다.
    /// 들고 있는 동안 닦는 지점 근처의 제 종류 얼룩을 일정 간격으로 한 번씩 닦는다.
    /// 닦는 틱은 든 사람의 기기에서 세고, 깎는 것은 서버가 한다. 청소기와 같은 분담이다.
    /// </summary>
    public sealed class Wiper : CarriableItem
    {
        [Tooltip("안내 문구에 쓸 이름. 예: 창문닦이, 물걸레.")]
        [SerializeField] private string _itemName = "물걸레";
        [Tooltip("지울 수 있는 얼룩의 종류.")]
        [SerializeField] private GrimeKind _targetKind = GrimeKind.Floor;
        [SerializeField] private GrimeField _grimeField;
        [Tooltip("닦는 기준 지점. 비우면 물건의 위치를 쓴다.")]
        [SerializeField] private Transform _wipePoint;
        [Min(0.1f)] [SerializeField] private float _wipeRadius = 1.2f;
        [Tooltip("이 간격(초)마다 범위 안 얼룩을 한 번씩 닦는다.")]
        [Min(0.05f)] [SerializeField] private float _wipeInterval = 0.4f;
        [Tooltip("보는 쪽으로 도는 반응 속도. 클수록 빨리 따라붙는다.")]
        [Min(0.1f)] [SerializeField] private float _aimSmoothing = 10f;

        private readonly int[] _spotBuffer = new int[16];
        private float _wipeElapsedTime;

        protected override string ItemName => _itemName;

        /// <summary>
        /// 든 사람은 걸레가 실제로 향한 쪽을 본다. 걸레가 부드럽게 도는 동안
        /// 몸도 같이 따라 돌아, 몸만 먼저 홱 돌아 보이지 않는다.
        /// </summary>
        public override Vector3 HeldFacingDirection => transform.forward;

        private Vector3 WipePosition => _wipePoint != null ? _wipePoint.position : transform.position;

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

        private void LateUpdate()
        {
            // 얼룩을 고르는 것은 든 사람의 기기만 한다. 각자 고르면 같은 얼룩을 두 번 닦는다.
            if (!IsOwner || !IsHeld || _grimeField == null || !_grimeField.isActiveAndEnabled)
            {
                _wipeElapsedTime = 0f;
                return;
            }

            _wipeElapsedTime += Time.deltaTime;
            if (_wipeElapsedTime < _wipeInterval)
                return;

            // 프레임 지연 뒤 여러 회차를 한꺼번에 닦지 않는다.
            _wipeElapsedTime %= _wipeInterval;
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

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(WipePosition, _wipeRadius);
        }
    }
}
