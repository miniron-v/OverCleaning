using OverCleaning.Network;
using UnityEngine;

namespace OverCleaning.Lobby
{
    /// <summary>
    /// 대기방 마을의 풀숲. 사람이 지나가면 풀잎이 몸에서 비켜 양옆으로 벌어지고,
    /// 달리는 차가 지나가면 닿은 풀잎이 뜯겨 날아갔다가 잠시 뒤 다시 자란다.
    ///
    /// 문(OfficeDoor)처럼 동기화된 위치만 보고 화면마다 따로 그린다. 보기만 하는 것이라 화면마다 조금 달라도 된다.
    /// 풀잎에는 콜라이더를 두지 않는다. 지나가는 것을 막지 않는다.
    /// </summary>
    public sealed class GrassBush : MonoBehaviour
    {
        [Tooltip("풀잎들. 뿌리(피벗)가 바닥에 있고 위(+Y)로 자라 있어야 한다.")]
        [SerializeField] private Transform[] _blades;

        [Tooltip("풀숲 가운데에서 풀잎이 퍼져 있는 반지름. 이 안팎의 사람과 차만 살핀다.")]
        [Min(0.1f)] [SerializeField] private float _radius = 1.5f;

        [Header("사람")]
        [Tooltip("사람 중심에서 이 거리 안의 풀잎이 비킨다.")]
        [Min(0.1f)] [SerializeField] private float _pushRadius = 0.9f;
        [Range(0f, 90f)] [SerializeField] private float _maxBendAngle = 70f;
        [Tooltip("휘거나 돌아오는 빠르기. 클수록 빨리 따라간다.")]
        [Min(0.1f)] [SerializeField] private float _bendSharpness = 12f;

        [Header("차")]
        [Tooltip("이보다 빠르게 달리는 차만 풀을 뜯는다(m/s).")]
        [Min(0f)] [SerializeField] private float _carMinSpeed = 3f;
        [Tooltip("차 몸체에서 이 거리 안의 풀잎이 뜯긴다.")]
        [Min(0f)] [SerializeField] private float _carReach = 0.3f;
        [Tooltip("뜯긴 풀잎 자리에서 날려 보낼 풀 조각. Simulation Space가 World여야 한다.")]
        [SerializeField] private ParticleSystem _flyingGrass;
        [Min(1)] [SerializeField] private int _piecesPerBlade = 3;
        [Tooltip("뜯긴 풀잎이 차 속도의 이만큼으로 함께 날아간다.")]
        [Min(0f)] [SerializeField] private float _carFling = 0.5f;
        [Min(0f)] [SerializeField] private float _flingUpSpeed = 4f;
        [Min(0f)] [SerializeField] private float _regrowDelay = 6f;
        [Min(0.1f)] [SerializeField] private float _regrowDuration = 1.5f;

        private readonly Collider[] _overlapBuffer = new Collider[16];
        private readonly Vector3[] _pushers = new Vector3[16];
        private readonly Collider[] _cars = new Collider[4];
        private readonly Vector3[] _carVelocities = new Vector3[4];

        private Quaternion[] _restRotations;
        private Vector3[] _restScales;
        private Quaternion[] _bends;

        /// <summary>뜯긴 풀잎이 다시 자라기 시작하는 시각. 뜯긴 적 없으면 0이다.</summary>
        private float[] _regrowStartTimes;

        private void Awake()
        {
            _restRotations = new Quaternion[_blades.Length];
            _restScales = new Vector3[_blades.Length];
            _bends = new Quaternion[_blades.Length];
            _regrowStartTimes = new float[_blades.Length];
            for (int index = 0; index < _blades.Length; index++)
            {
                _restRotations[index] = _blades[index].rotation;
                _restScales[index] = _blades[index].localScale;
                _bends[index] = Quaternion.identity;
            }
        }

        private void Update()
        {
            FindNearby(out int pusherCount, out int carCount);
            float bendBlend = 1f - Mathf.Exp(-_bendSharpness * Time.deltaTime);
            for (int index = 0; index < _blades.Length; index++)
            {
                Transform blade = _blades[index];
                if (TryCut(index, carCount))
                    continue;
                UpdateRegrowth(index);

                _bends[index] = Quaternion.Slerp(_bends[index], GetPushBend(blade.position, pusherCount), bendBlend);
                blade.rotation = _bends[index] * _restRotations[index];
            }
        }

        /// <summary>풀숲 가까이의 걷는 사람과 달리는 차를 모은다. 차에 탄 사람은 콜라이더가 꺼져 있어 잡히지 않는다.</summary>
        private void FindNearby(out int pusherCount, out int carCount)
        {
            pusherCount = 0;
            carCount = 0;
            int count = Physics.OverlapSphereNonAlloc(transform.position, _radius + _pushRadius, _overlapBuffer);
            for (int index = 0; index < count; index++)
            {
                Collider candidate = _overlapBuffer[index];
                Rigidbody body = candidate.attachedRigidbody;
                if (body == null)
                    continue;

                if (body.TryGetComponent(out NetworkPlayer _) && pusherCount < _pushers.Length)
                {
                    _pushers[pusherCount++] = body.position;
                }
                else if (body.TryGetComponent(out Car car) && carCount < _cars.Length &&
                         car.Velocity.sqrMagnitude >= _carMinSpeed * _carMinSpeed)
                {
                    _cars[carCount] = candidate;
                    _carVelocities[carCount] = car.Velocity;
                    carCount++;
                }
            }
        }

        /// <summary>
        /// 가장 가까운 사람에게서 멀어지는 쪽으로 눕힌다. 지나가는 사람의 양옆 풀잎은 저마다 바깥으로 누워 길이 갈라진다.
        /// </summary>
        private Quaternion GetPushBend(Vector3 bladeRoot, int pusherCount)
        {
            Vector3 away = Vector3.zero;
            float strength = 0f;
            for (int index = 0; index < pusherCount; index++)
            {
                Vector3 offset = bladeRoot - _pushers[index];
                offset.y = 0f;
                float pushStrength = 1f - offset.magnitude / _pushRadius;
                if (pushStrength <= strength)
                    continue;
                strength = pushStrength;
                away = offset;
            }

            if (strength <= 0f || away.sqrMagnitude < 0.000001f)
                return Quaternion.identity;
            Vector3 axis = Vector3.Cross(Vector3.up, away.normalized);
            return Quaternion.AngleAxis(_maxBendAngle * strength, axis);
        }

        /// <summary>달리는 차 몸체에 닿은 풀잎을 뜯어 날린다. 뜯겼거나 다시 자라는 중이면 true다.</summary>
        private bool TryCut(int index, int carCount)
        {
            if (Time.time < _regrowStartTimes[index])
                return true;

            Vector3 root = _blades[index].position;
            for (int carIndex = 0; carIndex < carCount; carIndex++)
            {
                if ((_cars[carIndex].ClosestPoint(root) - root).sqrMagnitude > _carReach * _carReach)
                    continue;
                Cut(index, _carVelocities[carIndex]);
                return true;
            }

            return false;
        }

        private void Cut(int index, Vector3 carVelocity)
        {
            Transform blade = _blades[index];
            _regrowStartTimes[index] = Time.time + _regrowDelay;
            _bends[index] = Quaternion.identity;
            blade.localScale = Vector3.zero;
            if (_flyingGrass == null)
                return;

            ParticleSystem.EmitParams pieces = new ParticleSystem.EmitParams { position = blade.position };
            for (int piece = 0; piece < _piecesPerBlade; piece++)
            {
                pieces.velocity = carVelocity * _carFling + Vector3.up * _flingUpSpeed + Random.insideUnitSphere * 2f;
                _flyingGrass.Emit(pieces, 1);
            }
        }

        /// <summary>뜯긴 자리에서 다시 자라 원래 크기로 돌아온다.</summary>
        private void UpdateRegrowth(int index)
        {
            float startTime = _regrowStartTimes[index];
            if (startTime <= 0f)
                return;

            float progress = (Time.time - startTime) / _regrowDuration;
            if (progress >= 1f)
            {
                progress = 1f;
                _regrowStartTimes[index] = 0f;
            }
            _blades[index].localScale = _restScales[index] * progress;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, _radius);
        }
    }
}
