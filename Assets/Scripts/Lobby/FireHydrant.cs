using Unity.Netcode;
using UnityEngine;

namespace OverCleaning.Lobby
{
    /// <summary>
    /// 대기방 마을의 소화전. 달리는 차로 들이받으면 한동안 물이 솟구치고, 그 물이 옆 물웅덩이를 채운다.
    /// 물이 그치면 웅덩이는 천천히 말라 사라진다. 마르는 중에 다시 들이받으면 남은 웅덩이에서부터 다시 찬다.
    /// 웅덩이는 정해진 빠르기로 차고 마르며, 다 차면 더 커지지 않는다.
    ///
    /// 들이받음은 차가 물리를 맡는 운전자 화면에서만 잡히므로 운전자가 서버에 알린다.
    /// 서버는 터진 시각과 그때 남아 있던 웅덩이 양만 정해 내려주고, 물줄기와 웅덩이 크기는 그 값으로 화면마다 똑같이 그린다.
    /// 콜라이더와 같은 오브젝트에 붙인다. 리지드바디가 없는 콜라이더의 부딪침은 그 오브젝트에만 알려진다.
    /// </summary>
    public sealed class FireHydrant : NetworkBehaviour
    {
        [Tooltip("이보다 빠르게 들이받아야 터진다(m/s).")]
        [Min(0f)] [SerializeField] private float _minHitSpeed = 5f;

        [Tooltip("물이 솟구치는 시간(초).")]
        [Min(0.1f)] [SerializeField] private float _sprayDuration = 9f;

        [Tooltip("빈 웅덩이가 다 차는 시간(초). 물이 솟는 동안만 찬다.")]
        [Min(0.1f)] [SerializeField] private float _fillDuration = 9f;

        [Tooltip("다 찬 웅덩이가 다 마르는 시간(초). 물이 그친 뒤에만 마른다.")]
        [Min(0.1f)] [SerializeField] private float _dryDuration = 30f;

        [Tooltip("솟구치는 물줄기. 터진 동안만 재생한다.")]
        [SerializeField] private ParticleSystem _spray;

        [SerializeField] private WaterPuddle _puddle;

        /// <summary>서버가 정한다. 마지막으로 터진 서버 시각.</summary>
        private readonly NetworkVariable<double> _burstTime = new NetworkVariable<double>(double.NegativeInfinity);

        /// <summary>서버가 정한다. 마지막으로 터질 때 남아 있던 웅덩이 양(0~1). 여기서부터 다시 찬다.</summary>
        private readonly NetworkVariable<float> _burstWetness = new NetworkVariable<float>();

        private double SecondsSinceBurst => NetworkManager.ServerTime.Time - _burstTime.Value;

        private bool IsSpraying => IsSpawned && SecondsSinceBurst < _sprayDuration;

        private void OnCollisionEnter(Collision collision)
        {
            if (!IsSpawned || collision.rigidbody == null || !collision.rigidbody.TryGetComponent(out Car car))
                return;
            // 차는 운전자 화면에서만 물리로 움직이므로 그 화면만 알린다.
            if (car.IsOwner && collision.relativeVelocity.magnitude >= _minHitSpeed)
                BurstRpc();
        }

        [Rpc(SendTo.Server)]
        private void BurstRpc()
        {
            // 솟는 중에 또 받아도 처음부터 다시 세지 않는다.
            if (IsSpraying)
                return;
            // 남은 양을 먼저 정해 두어, 시각만 바뀐 채로 그려지는 순간이 없게 한다.
            _burstWetness.Value = GetWetness();
            _burstTime.Value = NetworkManager.ServerTime.Time;
        }

        private void Update()
        {
            bool isSpraying = IsSpraying;
            if (_spray != null && isSpraying != _spray.isEmitting)
            {
                if (isSpraying)
                    _spray.Play();
                else
                    _spray.Stop();
            }

            if (_puddle != null)
                _puddle.Wetness = GetWetness();
        }

        /// <summary>터질 때 남아 있던 양에서 솟는 동안 차오르고(1에서 멈춤), 그친 뒤에는 그때 양에서 마른다.</summary>
        private float GetWetness()
        {
            if (!IsSpawned)
                return 0f;
            // 화면마다 서버 시간을 어림하므로 터진 직후에는 잠깐 음수일 수 있다.
            double seconds = System.Math.Max(0.0, SecondsSinceBurst);
            float startWetness = _burstWetness.Value;
            if (seconds < _sprayDuration)
                return Mathf.Min(1f, startWetness + (float)(seconds / _fillDuration));

            float peakWetness = Mathf.Min(1f, startWetness + _sprayDuration / _fillDuration);
            return Mathf.Clamp01(peakWetness - (float)((seconds - _sprayDuration) / _dryDuration));
        }
    }
}
