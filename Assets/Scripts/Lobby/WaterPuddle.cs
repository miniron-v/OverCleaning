using UnityEngine;

namespace OverCleaning.Lobby
{
    /// <summary>
    /// 소화전 물이 고여 생기는 물웅덩이. 얼마나 찼는지(Wetness)는 소화전이 정하고, 웅덩이는 그만큼 넓어진다.
    /// 달리는 차가 웅덩이를 지나가면 바퀴 양옆으로 물이 튄다.
    ///
    /// 동기화된 차 위치만 보고 화면마다 따로 튀긴다. 보기만 하는 것이라 화면마다 조금 달라도 된다.
    /// 콜라이더를 두지 않는다. 지나가는 것을 막지 않는다.
    /// </summary>
    public sealed class WaterPuddle : MonoBehaviour
    {
        [Tooltip("물웅덩이 모양. 다 찼을 때의 크기로 두면 덜 찼을 때 가로세로만 줄인다.")]
        [SerializeField] private Transform _surface;

        [Tooltip("다 찼을 때의 반지름. 차 중심이 이 안에 들어와야 물이 튄다.")]
        [Min(0.1f)] [SerializeField] private float _radius = 3f;

        [Tooltip("튀는 물방울. Simulation Space가 World여야 한다.")]
        [SerializeField] private ParticleSystem _splash;

        [Tooltip("이보다 빠르게 달리는 차만 물을 튀긴다(m/s).")]
        [Min(0f)] [SerializeField] private float _carMinSpeed = 3f;

        [Tooltip("차가 1m 갈 때마다 튀는 물방울 수.")]
        [Min(0f)] [SerializeField] private float _dropsPerMeter = 6f;

        [Tooltip("차 중심에서 바퀴까지의 옆 거리. 물이 여기서 튄다.")]
        [Min(0f)] [SerializeField] private float _wheelOffset = 1f;

        [Min(0f)] [SerializeField] private float _sideSpeed = 3f;
        [Min(0f)] [SerializeField] private float _upSpeed = 4f;

        private readonly Collider[] _overlapBuffer = new Collider[8];
        private Vector3 _fullScale;
        private float _wetness = -1f;

        /// <summary>남은 물방울 몫. 한 프레임에 한 방울이 안 되는 만큼을 다음 프레임으로 넘긴다.</summary>
        private float _pendingDrops;

        /// <summary>얼마나 찼는지(0~1). 0이면 웅덩이가 보이지 않고 물도 튀지 않는다.</summary>
        public float Wetness
        {
            get => Mathf.Max(0f, _wetness);
            set
            {
                value = Mathf.Clamp01(value);
                if (Mathf.Approximately(value, _wetness))
                    return;
                _wetness = value;
                _surface.gameObject.SetActive(value > 0f);
                _surface.localScale = new Vector3(_fullScale.x * value, _fullScale.y, _fullScale.z * value);
            }
        }

        private void Awake()
        {
            _fullScale = _surface.localScale;
            Wetness = 0f;
        }

        private void Update()
        {
            float radius = _radius * Wetness;
            if (radius <= 0f || _splash == null)
                return;

            int count = Physics.OverlapSphereNonAlloc(transform.position, radius, _overlapBuffer);
            for (int index = 0; index < count; index++)
            {
                Rigidbody body = _overlapBuffer[index].attachedRigidbody;
                if (body == null || !body.TryGetComponent(out Car car))
                    continue;

                Vector3 offset = car.transform.position - transform.position;
                offset.y = 0f;
                Vector3 velocity = car.Velocity;
                velocity.y = 0f;
                if (offset.sqrMagnitude > radius * radius || velocity.sqrMagnitude < _carMinSpeed * _carMinSpeed)
                    continue;
                Splash(car.transform, velocity);
                // 차 콜라이더가 여럿이어도 한 번만 튀긴다.
                break;
            }
        }

        /// <summary>왼쪽이나 오른쪽 바퀴에서 바깥쪽 위로 물방울을 날린다.</summary>
        private void Splash(Transform car, Vector3 velocity)
        {
            _pendingDrops += velocity.magnitude * Time.deltaTime * _dropsPerMeter;
            int drops = (int)_pendingDrops;
            _pendingDrops -= drops;

            ParticleSystem.EmitParams drop = new ParticleSystem.EmitParams();
            for (int index = 0; index < drops; index++)
            {
                float side = Random.value < 0.5f ? -1f : 1f;
                Vector3 position = car.position + car.right * (side * _wheelOffset);
                position.y = transform.position.y;
                drop.position = position;
                drop.velocity = car.right * (side * _sideSpeed) + Vector3.up * _upSpeed + Random.insideUnitSphere;
                _splash.Emit(drop, 1);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(transform.position, _radius);
        }
    }
}
