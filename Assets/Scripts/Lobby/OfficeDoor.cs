using OverCleaning.Network;
using UnityEngine;

namespace OverCleaning.Lobby
{
    /// <summary>
    /// 양쪽으로 열리는 여닫이문. 플레이어가 문짝에 닿을 만큼만 밀려 열리고, 멀어지면 저절로 닫힌다.
    ///
    /// 물리 대신 동기화된 플레이어 위치로 각도를 계산하므로 모든 클라이언트에서 따로 돌려도 같게 보인다.
    /// 원격 플레이어는 물리를 맡지 않아 밀어도 힘이 전해지지 않기 때문이다.
    /// 문짝에는 막는 콜라이더를 두지 않는다. 각도를 위치로 정하므로 물리와 겹치면 떨린다.
    ///
    /// 열리는 방향은 닫혀 있을 때 처음 닿은 플레이어가 서 있던 쪽의 반대다.
    /// 통과하는 동안 플레이어가 반대편으로 넘어가도 다 닫히기 전에는 방향을 바꾸지 않는다.
    /// </summary>
    public sealed class OfficeDoor : MonoBehaviour
    {
        private const float ClosedAngle = 0.5f;

        [Tooltip("돌아가는 문짝. 경첩(이 오브젝트 자리)을 축으로 로컬 +X 방향으로 뻗어 있어야 한다.")]
        [SerializeField] private Transform _leaf;

        [Min(0.1f)] [SerializeField] private float _width = 2f;
        [Min(0f)] [SerializeField] private float _playerRadius = 0.4f;
        [Range(10f, 180f)] [SerializeField] private float _maxAngle = 100f;
        [Min(1f)] [SerializeField] private float _closeSpeed = 90f;

        private readonly Collider[] _overlapBuffer = new Collider[16];
        private float _angle;
        private int _openSign;

        private void Update()
        {
            float target = 0f;
            int count = Physics.OverlapSphereNonAlloc(transform.position, _width + _playerRadius, _overlapBuffer);
            for (int index = 0; index < count; index++)
            {
                Rigidbody body = _overlapBuffer[index].attachedRigidbody;
                if (body == null || !body.TryGetComponent(out NetworkPlayer _))
                    continue;

                Vector3 local = transform.InverseTransformPoint(body.position);
                if (_openSign == 0)
                {
                    int side = local.z >= 0f ? 1 : -1;
                    if (GetRequiredAngle(local, side) <= 0f)
                        continue;
                    _openSign = side;
                }
                target = Mathf.Max(target, GetRequiredAngle(local, _openSign));
            }

            target = Mathf.Min(target, _maxAngle);
            _angle = target > _angle ? target : Mathf.MoveTowards(_angle, target, _closeSpeed * Time.deltaTime);
            if (_angle < ClosedAngle && target <= 0f)
            {
                _angle = 0f;
                _openSign = 0;
            }

            // 양의 Y 회전은 문짝을 -Z 쪽으로 돌리므로, +Z 쪽에서 밀면 양의 각도가 된다.
            _leaf.localRotation = Quaternion.Euler(0f, _angle * _openSign, 0f);
        }

        /// <summary>
        /// 경첩 기준 로컬 위치의 플레이어를 피하려면 문짝이 openSign 쪽으로 몇 도 열려야 하는지.
        /// 문짝이 닿지 않는 거리면 0이다.
        /// </summary>
        private float GetRequiredAngle(Vector3 local, int openSign)
        {
            float distance = new Vector2(local.x, local.z).magnitude;
            if (distance > _width + _playerRadius || distance <= _playerRadius)
                return 0f;

            // 문짝이 열리는 방향으로 잰 플레이어의 각도에, 몸 반지름만큼 비켜설 각도를 더한다.
            float playerAngle = Mathf.Atan2(-openSign * local.z, local.x) * Mathf.Rad2Deg;
            // 경첩 뒤 벽 옆에 선 사람처럼 문짝이 다 열려도 닿지 않는 자리는 무시한다.
            if (playerAngle > _maxAngle)
                return 0f;
            float clearance = Mathf.Asin(_playerRadius / distance) * Mathf.Rad2Deg;
            return Mathf.Max(0f, playerAngle + clearance);
        }
    }
}
