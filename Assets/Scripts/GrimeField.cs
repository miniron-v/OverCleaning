using Unity.Netcode;
using UnityEngine;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 씬에 놓인 얼룩들의 상태를 맡는다. 남은 닦기 횟수는 서버가 깎아 NetworkList로
    /// 내려보내므로 모두(늦게 들어온 사람 포함)가 같은 얼룩을 본다.
    /// 얼룩 배치는 씬에 손으로 하고, 목록의 순서가 곧 얼룩의 번호다.
    /// 먼지처럼 수가 많지 않아서 시드 대신 상태를 그대로 동기화한다.
    /// </summary>
    public sealed class GrimeField : NetworkBehaviour
    {
        [Tooltip("씬에 놓은 얼룩들. 순서가 번호가 되므로 모든 기기가 같은 씬이어야 한다.")]
        [SerializeField] private GrimeSpot[] _spots = new GrimeSpot[0];

        [Tooltip("다 닦인 자리에서 터지는 반짝임의 재질. 비우면 반짝임 없이 지워진다.")]
        [SerializeField] private Material _sparkleMaterial;

        /// <summary>얼룩마다 남은 닦기 횟수. 서버가 깎아 내려보낸다.</summary>
        private readonly NetworkList<int> _remainingWipes = new NetworkList<int>();

        private ParticleSystem _sparkleSystem;
        private Material _sparkleRuntimeMaterial;
        private Texture2D _sparkleTexture;

        /// <summary>남은 횟수가 채워졌는가. 채워지기 전의 0개를 다 닦은 것으로 오해하지 않게 한다.</summary>
        public bool IsBuilt => IsSpawned && _remainingWipes.Count == _spots.Length;

        /// <summary>아직 남아 있는 얼룩 수. 채워지기 전에는 전부 남은 것으로 친다.</summary>
        public int RemainingCount
        {
            get
            {
                if (!IsBuilt)
                    return _spots.Length;
                int count = 0;
                for (int index = 0; index < _remainingWipes.Count; index++)
                {
                    if (_remainingWipes[index] > 0)
                        count++;
                }

                return count;
            }
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer && _remainingWipes.Count == 0)
            {
                foreach (GrimeSpot spot in _spots)
                    _remainingWipes.Add(spot != null ? spot.WipesToClean : 0);
            }

            _remainingWipes.OnListChanged += OnRemainingChanged;
            // 늦게 들어왔으면 이미 깎인 목록이 통째로 온다. 지금 값대로 겉모습을 맞춘다.
            ApplyAll();
        }

        public override void OnNetworkDespawn()
        {
            _remainingWipes.OnListChanged -= OnRemainingChanged;
        }

        private void OnRemainingChanged(NetworkListEvent<int> change)
        {
            if (change.Index < 0 || change.Index >= _spots.Length || _spots[change.Index] == null)
                return;

            GrimeSpot spot = _spots[change.Index];
            // 마지막 한 번이 닦여 사라지는 순간에만 반짝인다. 늦게 들어와 처음부터
            // 지워져 있던 얼룩이나 중간 단계에서는 터지지 않는다.
            if (change.Value == 0 && change.PreviousValue > 0)
                PlaySparkle(spot.transform.position);
            spot.ApplyRemaining(change.Value);
        }

        /// <summary>
        /// 다 닦인 자리에서 반짝임을 터뜨린다. 남은 횟수가 모두에게 내려오는 값이라
        /// 이 연출도 모든 기기에서 같은 자리에 터진다. 다 청소된 티가 나는 보상이다.
        /// </summary>
        private void PlaySparkle(Vector3 position)
        {
            if (_sparkleMaterial == null)
                return;
            if (_sparkleSystem == null)
                CreateSparkleSystem();

            var emitParams = new ParticleSystem.EmitParams();
            for (int index = 0; index < 14; index++)
            {
                // 위쪽으로 퍼지며 반짝인다. 연출일 뿐이라 기기마다 모양이 달라도 된다.
                Vector3 direction = (Random.onUnitSphere + Vector3.up).normalized;
                emitParams.position = position;
                emitParams.velocity = direction * Random.Range(0.6f, 1.4f);
                emitParams.startSize = Random.Range(0.1f, 0.2f);
                emitParams.startLifetime = Random.Range(0.35f, 0.65f);
                emitParams.startColor = new Color(1f, 0.95f, 0.55f);
                _sparkleSystem.Emit(emitParams, 1);
            }

            _sparkleSystem.Play();
        }

        private void CreateSparkleSystem()
        {
            var child = new GameObject("Sparkles");
            child.transform.SetParent(transform, false);
            _sparkleSystem = child.AddComponent<ParticleSystem>();
            _sparkleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _sparkleSystem.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startSpeed = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 256;
            var emission = _sparkleSystem.emission;
            emission.enabled = false;
            var shape = _sparkleSystem.shape;
            shape.enabled = false;

            // 끝으로 갈수록 작아지며 사라진다. 짧게 반짝하고 마는 느낌을 준다.
            var sizeOverLifetime = _sparkleSystem.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
                1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

            _sparkleTexture = CreateSparkleTexture();
            _sparkleRuntimeMaterial = new Material(_sparkleMaterial);
            _sparkleRuntimeMaterial.SetTexture("_BaseMap", _sparkleTexture);
            var sparkleRenderer = child.GetComponent<ParticleSystemRenderer>();
            sparkleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            sparkleRenderer.sharedMaterial = _sparkleRuntimeMaterial;
        }

        /// <summary>십자 빛살에 밝은 심이 있는 반짝 모양을 만든다.</summary>
        private static Texture2D CreateSparkleTexture()
        {
            const int resolution = 64;
            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, true)
            {
                name = "Sparkle",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    Vector2 point = new Vector2(x, y) / (resolution - 1f) * 2f - Vector2.one;
                    float spikeX = Mathf.Max(0f, 1f - Mathf.Abs(point.y) * 6f) * (1f - Mathf.Abs(point.x));
                    float spikeY = Mathf.Max(0f, 1f - Mathf.Abs(point.x) * 6f) * (1f - Mathf.Abs(point.y));
                    float core = Mathf.Max(0f, 1f - point.magnitude * 1.8f);
                    float alpha = Mathf.Clamp01(Mathf.Max(core, Mathf.Max(spikeX, spikeY)));
                    pixels[y * resolution + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(true, true);
            return texture;
        }

        // NetworkBehaviour도 OnDestroy에서 정리할 것이 있으므로 가리지 않고 이어서 부른다.
        public override void OnDestroy()
        {
            base.OnDestroy();
            if (_sparkleRuntimeMaterial != null)
                Destroy(_sparkleRuntimeMaterial);
            if (_sparkleTexture != null)
                Destroy(_sparkleTexture);
            if (_sparkleSystem != null)
                Destroy(_sparkleSystem.gameObject);
        }

        private void ApplyAll()
        {
            int count = Mathf.Min(_remainingWipes.Count, _spots.Length);
            for (int index = 0; index < count; index++)
            {
                if (_spots[index] != null)
                    _spots[index].ApplyRemaining(_remainingWipes[index]);
            }
        }

        /// <summary>범위 안에 아직 남아 있는, 이 종류의 얼룩 번호들을 담아 준다.</summary>
        public int GetSpotsInRange(Vector3 center, float radius, GrimeKind kind, int[] buffer)
        {
            if (!IsBuilt)
                return 0;

            int count = 0;
            float squaredRadius = radius * radius;
            for (int index = 0; index < _spots.Length && count < buffer.Length; index++)
            {
                GrimeSpot spot = _spots[index];
                if (spot == null || spot.Kind != kind || _remainingWipes[index] <= 0)
                    continue;
                if ((spot.transform.position - center).sqrMagnitude > squaredRadius)
                    continue;
                buffer[count++] = index;
            }

            return count;
        }

        /// <summary>
        /// 서버에서만 부른다. 얼룩을 한 번 닦고, 이번에 다 지워졌으면 참을 돌려준다.
        /// 누가 닦았는지의 전적은 부른 쪽(도구)이 챙긴다.
        /// </summary>
        internal bool ServerWipe(int index, int amount)
        {
            if (!IsServer || index < 0 || index >= _remainingWipes.Count)
                return false;
            if (_remainingWipes[index] <= 0)
                return false;

            _remainingWipes[index] = Mathf.Max(0, _remainingWipes[index] - amount);
            return _remainingWipes[index] == 0;
        }
    }
}
