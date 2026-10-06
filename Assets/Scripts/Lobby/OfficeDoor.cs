using OverCleaning.Network;
using UnityEngine;

namespace OverCleaning.Lobby
{
    /// <summary>
    /// 양쪽으로 열리는 여닫이문 한 짝. 플레이어가 문짝에 닿을 만큼만 밀려 열리고, 멀어지면 저절로 닫힌다.
    ///
    /// 물리 대신 동기화된 플레이어 위치로 각도를 계산하므로 모든 클라이언트에서 따로 돌려도 같게 보인다.
    /// 원격 플레이어는 물리를 맡지 않아 밀어도 힘이 전해지지 않기 때문이다.
    /// 문짝에는 막는 콜라이더를 두지 않는다. 각도를 위치로 정하므로 물리와 겹치면 떨린다.
    ///
    /// 문짝은 지난 프레임에 플레이어의 어느 쪽에 있었는지에 따라 그 쪽으로만 밀려난다.
    /// 지나간 뒤 되돌아오는 사람은 열린 문짝을 앞에서 밀어 닫고, 닫힌 자리를 넘으면 반대로 연다.
    /// </summary>
    public sealed class OfficeDoor : MonoBehaviour
    {
        [Tooltip("돌아가는 문짝. 경첩(이 오브젝트 자리)을 축으로 로컬 +X 방향으로 뻗어 있어야 한다.")]
        [SerializeField] private Transform _leaf;

        [Min(0.1f)] [SerializeField] private float _width = 1f;
        [Min(0f)] [SerializeField] private float _playerRadius = 0.4f;
        [Range(10f, 180f)] [SerializeField] private float _maxAngle = 100f;
        [Min(1f)] [SerializeField] private float _closeSpeed = 90f;

        private readonly Collider[] _overlapBuffer = new Collider[16];

        /// <summary>양수면 로컬 -Z 쪽으로, 음수면 +Z 쪽으로 열린 각도.</summary>
        private float _angle;

        private void Update()
        {
            float angle = Mathf.MoveTowards(_angle, 0f, _closeSpeed * Time.deltaTime);
            int count = Physics.OverlapSphereNonAlloc(transform.position, _width + _playerRadius, _overlapBuffer);
            for (int index = 0; index < count; index++)
            {
                Rigidbody body = _overlapBuffer[index].attachedRigidbody;
                if (body == null || !body.TryGetComponent(out NetworkPlayer _))
                    continue;

                Vector3 local = transform.InverseTransformPoint(body.position);
                float distance = new Vector2(local.x, local.z).magnitude;
                if (distance > _width + _playerRadius || distance <= _playerRadius)
                    continue;

                // 문짝이 도는 방향(양의 Y 회전은 -Z 쪽)으로 잰 플레이어의 각도와, 몸이 가리는 각도의 절반.
                float playerAngle = Mathf.Atan2(-local.z, local.x) * Mathf.Rad2Deg;
                float halfBodyAngle = Mathf.Asin(_playerRadius / distance) * Mathf.Rad2Deg;
                if (_angle >= playerAngle)
                    angle = Mathf.Max(angle, playerAngle + halfBodyAngle);
                else
                    angle = Mathf.Min(angle, playerAngle - halfBodyAngle);
            }

            _angle = Mathf.Clamp(angle, -_maxAngle, _maxAngle);
            _leaf.localRotation = Quaternion.Euler(0f, _angle, 0f);
        }
    }
}
