using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 이 오브젝트 밑의 벽 가운데 플레이어 근처에 있는 것을 반투명하게 만든다.
    /// 위에서 내려다보는 화면이라 앞쪽 벽이 플레이어를 가리기 때문이다.
    ///
    /// 누구 근처인지는 보는 사람마다 다르므로 각자 자기 기기에서만 계산한다. 서버에 알릴
    /// 것이 없다. 벽마다 머티리얼을 복제해 쓰는데, 벽이 쓰는 머티리얼을 청소기 같은 다른
    /// 물건도 함께 쓰고 있어 원본을 고치면 그쪽까지 투명해진다.
    ///
    /// Renderer의 첫 머티리얼만 다룬다. 벽은 머티리얼 하나짜리 상자다.
    /// </summary>
    public sealed class WallFader : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Tooltip("이 반경 안에 든 벽이 반투명해진다.")]
        [Min(0.1f)] [SerializeField] private float _fadeRadius = 4f;
        [Tooltip("반투명해진 벽의 불투명도. 0이면 완전히 사라진다.")]
        [Range(0f, 1f)] [SerializeField] private float _fadedAlpha = 0.2f;
        [Tooltip("1초에 바뀌는 불투명도. 벽이 툭 사라지지 않게 한다.")]
        [Min(0.1f)] [SerializeField] private float _fadeSpeed = 5f;

        private Renderer[] _walls;
        private Material[] _opaqueMaterials;
        private Material[] _fadeMaterials;
        private float[] _alphas;
        private Transform _localPlayer;

        private void Awake()
        {
            _walls = GetComponentsInChildren<Renderer>();
            _opaqueMaterials = new Material[_walls.Length];
            _fadeMaterials = new Material[_walls.Length];
            _alphas = new float[_walls.Length];
            for (int index = 0; index < _walls.Length; index++)
            {
                _opaqueMaterials[index] = _walls[index].sharedMaterial;
                _alphas[index] = 1f;
            }
        }

        private void OnDestroy()
        {
            // 복제한 머티리얼은 에셋이 아니라 이 컴포넌트가 만든 것이므로 직접 치운다.
            foreach (Material material in _fadeMaterials)
            {
                if (material != null)
                    Destroy(material);
            }
        }

        private void Update()
        {
            if (_localPlayer == null)
                _localPlayer = FindLocalPlayer();
            if (_localPlayer == null)
                return;

            Vector3 position = _localPlayer.position;
            float squaredRadius = _fadeRadius * _fadeRadius;
            float step = _fadeSpeed * Time.deltaTime;
            for (int index = 0; index < _walls.Length; index++)
            {
                // 벽 하나가 길게 뻗어 있어 중심까지의 거리로는 가까운 끝을 놓친다.
                float target = _walls[index].bounds.SqrDistance(position) <= squaredRadius
                    ? _fadedAlpha
                    : 1f;
                if (Mathf.Approximately(_alphas[index], target))
                    continue;

                _alphas[index] = Mathf.MoveTowards(_alphas[index], target, step);
                ApplyAlpha(index);
            }
        }

        private void ApplyAlpha(int index)
        {
            Renderer wall = _walls[index];
            if (_alphas[index] >= 1f)
            {
                // 다 돌아왔으면 원래 머티리얼로 되돌려 불투명하게 그린다.
                wall.sharedMaterial = _opaqueMaterials[index];
                return;
            }

            if (_fadeMaterials[index] == null)
                _fadeMaterials[index] = CreateFadeMaterial(_opaqueMaterials[index]);

            Material fadeMaterial = _fadeMaterials[index];
            Color color = fadeMaterial.GetColor(BaseColorId);
            color.a = _alphas[index];
            fadeMaterial.SetColor(BaseColorId, color);
            wall.sharedMaterial = fadeMaterial;
        }

        /// <summary>
        /// URP Lit 머티리얼을 반투명으로 바꾼 복제본을 만든다.
        /// 인스펙터에서 Surface Type을 바꿀 때 URP가 손대는 값들을 그대로 맞춰 준다.
        /// </summary>
        private static Material CreateFadeMaterial(Material opaqueMaterial)
        {
            var material = new Material(opaqueMaterial);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        /// <summary>
        /// 내 캐릭터를 찾는다. 네트워크가 돌지 않으면 볼 사람이 없으므로 아무것도 하지 않는다.
        /// </summary>
        private static Transform FindLocalPlayer()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening)
                return null;

            NetworkObject player = manager.LocalClient.PlayerObject;
            return player != null ? player.transform : null;
        }
    }
}
