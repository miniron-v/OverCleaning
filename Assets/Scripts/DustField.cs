using System.Collections.Generic;
using UnityEngine;

namespace OverCleaning.InGame
{
    public sealed class DustField : MonoBehaviour
    {
        private const float SurfaceOffset = 0.02f;
        private const int PlacementAttemptsPerParticle = 20;

        [Tooltip("Map/Floor 아래에서 먼지를 생성할 바닥 Collider만 등록하세요. 이름은 검사하지 않습니다.")]
        [SerializeField] private Collider[] _floors = new Collider[0];
        [Range(0f, 1f)] [SerializeField] private float _clusterProbability = 0.8f;
        [Min(1)] [SerializeField] private int _clustersPerFloor = 3;
        [Min(0.1f)] [SerializeField] private float _clusterRadius = 1.4f;
        [Tooltip("생성을 피할 벽과 장애물 레이어. 대상 바닥 자체는 제외합니다.")]
        [SerializeField] private LayerMask _obstacleLayers = ~0;
        [Min(0f)] [SerializeField] private float _edgeMargin = 0.1f;
        [Min(0)] [SerializeField] private int _dustCount = 300;
        [Tooltip("먼지 크기의 최솟값과 최댓값.")]
        [SerializeField] private Vector2 _dustSizeRange = new Vector2(0.1f, 0.25f);
        [SerializeField] private Color[] _dustColors =
        {
            new Color(0.22f, 0.16f, 0.1f),
            new Color(0.4f, 0.3f, 0.18f),
            new Color(0.3f, 0.28f, 0.25f),
        };
        [SerializeField] private Material _dustMaterial;
        [Tooltip("먼지마다 목록에서 모양을 무작위로 선택합니다. 빈 목록은 기본 원형을 사용합니다. 흰색 이미지에 Dust Colors가 곱해집니다.")]
        [SerializeField] private Texture2D[] _dustTextures = new Texture2D[0];

        private readonly Collider[] _overlapBuffer = new Collider[32];
        private readonly List<Collider> _activeFloors = new List<Collider>();
        private Vector3[][] _clusterCenters;
        private float _totalFloorArea;
        private ParticleSystem[] _particleSystems;
        private int[] _textureIndices;
        private ParticleSystem.Particle[] _renderBuffer;
        private ParticleSystem.Particle[] _particles;
        private Material[] _runtimeMaterials;
        private Texture2D _defaultTexture;

        public int RemainingDustCount { get; private set; }

        private void Start()
        {
            ValidateSettings();
            Physics.SyncTransforms();
            CacheFloors();
            if (_activeFloors.Count == 0 || _dustMaterial == null)
            {
                Debug.LogError("DustField에 활성 바닥 Collider 목록과 먼지 Material을 지정하세요.", this);
                return;
            }

            ConfigureParticleSystems();
            GenerateDust();
        }

        private void CacheFloors()
        {
            _activeFloors.Clear();
            _totalFloorArea = 0f;
            foreach (Collider floor in _floors)
            {
                if (floor == null || !floor.enabled || !floor.gameObject.activeInHierarchy ||
                    _activeFloors.Contains(floor))
                    continue;
                float area = floor.bounds.size.x * floor.bounds.size.z;
                if (area <= 0f)
                    continue;
                _activeFloors.Add(floor);
                _totalFloorArea += area;
            }

            _clusterCenters = new Vector3[_activeFloors.Count][];
            for (int index = 0; index < _activeFloors.Count; index++)
            {
                Bounds bounds = _activeFloors[index].bounds;
                _clusterCenters[index] = new Vector3[_clustersPerFloor];
                for (int cluster = 0; cluster < _clustersPerFloor; cluster++)
                    _clusterCenters[index][cluster] = new Vector3(
                        Random.Range(bounds.min.x, bounds.max.x), bounds.max.y,
                        Random.Range(bounds.min.z, bounds.max.z));
            }
        }

        private int ChooseFloorIndex()
        {
            float selection = Random.value * _totalFloorArea;
            for (int index = 0; index < _activeFloors.Count; index++)
            {
                Bounds bounds = _activeFloors[index].bounds;
                selection -= bounds.size.x * bounds.size.z;
                if (selection <= 0f)
                    return index;
            }

            return _activeFloors.Count - 1;
        }

        private Vector3 SamplePosition(int floorIndex, float inset)
        {
            Bounds bounds = _activeFloors[floorIndex].bounds;
            if (Random.value < _clusterProbability)
            {
                Vector3[] centers = _clusterCenters[floorIndex];
                Vector2 offset = Random.insideUnitCircle * _clusterRadius;
                return centers[Random.Range(0, centers.Length)] + new Vector3(offset.x, 0f, offset.y);
            }

            return new Vector3(
                Random.Range(bounds.min.x + inset, bounds.max.x - inset),
                bounds.max.y,
                Random.Range(bounds.min.z + inset, bounds.max.z - inset));
        }

        private void ConfigureParticleSystems()
        {
            var textures = new List<Texture2D>();
            if (_dustTextures != null)
            {
                foreach (Texture2D texture in _dustTextures)
                {
                    if (texture != null)
                        textures.Add(texture);
                }
            }
            if (textures.Count == 0)
            {
                _defaultTexture = CreateDefaultTexture();
                textures.Add(_defaultTexture);
            }

            _particleSystems = new ParticleSystem[textures.Count];
            _runtimeMaterials = new Material[textures.Count];
            for (int index = 0; index < textures.Count; index++)
            {
                var child = new GameObject($"Dust {textures[index].name}");
                child.transform.SetParent(transform, false);
                _particleSystems[index] = child.AddComponent<ParticleSystem>();
                ConfigureParticleSystem(_particleSystems[index]);
                _runtimeMaterials[index] = CreateDustMaterial(textures[index]);
                var particleRenderer = child.GetComponent<ParticleSystemRenderer>();
                particleRenderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                particleRenderer.sharedMaterial = _runtimeMaterials[index];
            }
        }

        private void ConfigureParticleSystem(ParticleSystem particleSystem)
        {
            particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particleSystem.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startSpeed = 0f;
            main.startLifetime = float.PositiveInfinity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Max(1, _dustCount);
            var emission = particleSystem.emission;
            emission.enabled = false;
            var shape = particleSystem.shape;
            shape.enabled = false;
        }

        private Material CreateDustMaterial(Texture2D texture)
        {
            // 모양별 인스턴스를 사용하므로 공유 Material 에셋은 변경하지 않습니다.
            var material = new Material(_dustMaterial);
            material.SetTexture("_BaseMap", texture);
            return material;
        }

        private static Texture2D CreateDefaultTexture()
        {
            const int resolution = 64;
            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, true)
            {
                name = "Default Dust Circle",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    Vector2 point = new Vector2(x, y) / (resolution - 1f) * 2f - Vector2.one;
                    float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, point.magnitude));
                    pixels[y * resolution + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(true, true);
            return texture;
        }

        private void OnDestroy()
        {
            if (_runtimeMaterials != null)
            {
                foreach (Material material in _runtimeMaterials)
                {
                    if (material != null)
                        Destroy(material);
                }
            }
            if (_particleSystems != null)
            {
                foreach (ParticleSystem particleSystem in _particleSystems)
                {
                    if (particleSystem != null)
                        Destroy(particleSystem.gameObject);
                }
            }
            if (_defaultTexture != null)
                Destroy(_defaultTexture);
        }

        private void GenerateDust()
        {
            _particles = new ParticleSystem.Particle[_dustCount];
            _textureIndices = new int[_dustCount];
            _renderBuffer = new ParticleSystem.Particle[_dustCount];
            RemainingDustCount = 0;
            for (int index = 0; index < _dustCount; index++)
            {
                float size = Random.Range(_dustSizeRange.x, _dustSizeRange.y);
                if (TryFindPosition(ChooseFloorIndex(), size, out Vector3 position))
                {
                    _particles[RemainingDustCount] = CreateParticle(position, size);
                    _textureIndices[RemainingDustCount] = Random.Range(0, _particleSystems.Length);
                    RemainingDustCount++;
                }
            }

            UpdateRenderedParticles();
            if (RemainingDustCount < _dustCount)
                Debug.LogWarning($"먼지 {_dustCount}개 중 {RemainingDustCount}개 생성: 바닥 크기나 장애물 배치를 확인하세요.", this);
        }

        private void UpdateRenderedParticles()
        {
            // 전체 수량은 한 번만 관리하고, 텍스처별 렌더러에 해당 먼지만 전달합니다.
            for (int textureIndex = 0; textureIndex < _particleSystems.Length; textureIndex++)
            {
                int count = 0;
                for (int index = 0; index < RemainingDustCount; index++)
                {
                    if (_textureIndices[index] == textureIndex)
                        _renderBuffer[count++] = _particles[index];
                }

                _particleSystems[textureIndex].SetParticles(_renderBuffer, count);
                _particleSystems[textureIndex].Pause();
            }
        }

        private bool TryFindPosition(int floorIndex, float size, out Vector3 position)
        {
            Collider floor = _activeFloors[floorIndex];
            Bounds bounds = floor.bounds;
            // 회전한 사각 파티클의 모서리까지 포함하는 보수적인 검사 범위입니다.
            float halfExtent = size * Mathf.Sqrt(2f) * 0.5f;
            float inset = halfExtent + _edgeMargin;
            position = default;
            if (bounds.size.x <= inset * 2f || bounds.size.z <= inset * 2f)
                return false;

            for (int attempt = 0; attempt < PlacementAttemptsPerParticle; attempt++)
            {
                Vector3 candidate = SamplePosition(floorIndex, inset);
                if (!TryGetFloorPoint(floor, candidate, out Vector3 surface))
                    continue;
                if (!IsFootprintSupported(floor, surface, inset))
                    continue;

                candidate = surface + Vector3.up * SurfaceOffset;
                if (OverlapsObstacle(floor, candidate, halfExtent))
                    continue;

                position = candidate;
                return true;
            }

            return false;
        }

        private bool TryGetFloorPoint(Collider floor, Vector3 origin, out Vector3 point)
        {
            origin.y = floor.bounds.max.y + 1f;
            bool hitFloor = floor.Raycast(new Ray(origin, Vector3.down),
                out RaycastHit hit, floor.bounds.size.y + 2f);
            point = hit.point;
            return hitFloor && hit.normal.y > 0.999f;
        }

        private bool IsFootprintSupported(Collider floor, Vector3 center, float halfExtent)
        {
            // 회전한 바닥의 AABB 밖 모서리나 표면 높이가 다른 위치를 제외합니다.
            for (int x = -1; x <= 1; x += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = center + new Vector3(x * halfExtent, 0f, z * halfExtent);
                    if (!TryGetFloorPoint(floor, corner, out Vector3 point) || Mathf.Abs(point.y - center.y) > 0.01f)
                        return false;
                }
            }

            return true;
        }

        private bool OverlapsObstacle(Collider floor, Vector3 position, float halfExtent)
        {
            int count = Physics.OverlapBoxNonAlloc(position,
                new Vector3(halfExtent, SurfaceOffset, halfExtent), _overlapBuffer,
                Quaternion.identity, _obstacleLayers, QueryTriggerInteraction.Ignore);
            // 버퍼가 가득 차면 누락된 장애물이 있을 수 있으므로 이 위치는 사용하지 않습니다.
            if (count == _overlapBuffer.Length)
                return true;
            for (int index = 0; index < count; index++)
            {
                if (_overlapBuffer[index] != floor)
                    return true;
            }

            return false;
        }

        private ParticleSystem.Particle CreateParticle(Vector3 position, float size)
        {
            return new ParticleSystem.Particle
            {
                position = position,
                startSize = size,
                startColor = _dustColors[Random.Range(0, _dustColors.Length)],
                rotation = Random.Range(0f, 360f),
                startLifetime = float.PositiveInfinity,
                remainingLifetime = float.PositiveInfinity,
            };
        }

        /// <summary>월드 좌표 범위 내 먼지를 최대 지정 수량만큼 제거하고 실제 제거량을 반환합니다.</summary>
        public int RemoveDustInRange(Vector3 center, float radius, int maximumCount)
        {
            if (_particleSystems == null || radius <= 0f || maximumCount <= 0)
                return 0;

            int removedCount = 0;
            float squaredRadius = radius * radius;
            for (int index = RemainingDustCount - 1; index >= 0 && removedCount < maximumCount; index--)
            {
                if ((_particles[index].position - center).sqrMagnitude > squaredRadius)
                    continue;

                RemainingDustCount--;
                _particles[index] = _particles[RemainingDustCount];
                _textureIndices[index] = _textureIndices[RemainingDustCount];
                removedCount++;
            }

            if (removedCount > 0)
                UpdateRenderedParticles();
            return removedCount;
        }

        private void OnValidate() => ValidateSettings();

        private void ValidateSettings()
        {
            if (_floors == null)
                _floors = new Collider[0];
            _clusterProbability = Mathf.Clamp01(_clusterProbability);
            _clustersPerFloor = Mathf.Max(1, _clustersPerFloor);
            _clusterRadius = Mathf.Max(0.1f, _clusterRadius);
            _edgeMargin = Mathf.Max(0f, _edgeMargin);
            _dustCount = Mathf.Max(0, _dustCount);
            _dustSizeRange.x = Mathf.Max(0.01f, _dustSizeRange.x);
            _dustSizeRange.y = Mathf.Max(_dustSizeRange.x, _dustSizeRange.y);
            if (_dustColors == null || _dustColors.Length == 0)
                _dustColors = new[] { new Color(0.22f, 0.16f, 0.1f) };
        }

        private void OnDrawGizmosSelected()
        {
            if (_floors == null)
                return;
            Gizmos.color = Color.yellow;
            foreach (Collider floor in _floors)
            {
                if (floor != null)
                    Gizmos.DrawWireCube(floor.bounds.center, floor.bounds.size);
            }
        }
    }
}
