using Unity.Netcode;
using UnityEngine;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 이 오브젝트 밑의 벽을, 내 캐릭터를 중심으로 한 구 안에 든 부분만 반투명하게 만든다.
    /// 위에서 내려다보는 화면이라 앞쪽 벽이 플레이어를 가리기 때문이다.
    ///
    /// 벽 전체가 아니라 픽셀 단위로 가리는 것은 셰이더(WallSphereFade)가 한다. 여기서는
    /// 벽 머티리얼을 그 셰이더로 바꿔 끼우고, 매 프레임 내 위치를 전역으로 알려 준다.
    /// 누구 근처인지는 보는 사람마다 다르므로 각자 자기 기기에서만 계산한다.
    /// </summary>
    public sealed class WallFader : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int FadedAlphaId = Shader.PropertyToID("_FadedAlpha");

        /// <summary>내 캐릭터 위치(xyz)와 반경(w). 모든 벽이 같은 값을 읽는 전역이다.</summary>
        private static readonly int FadeCenterId = Shader.PropertyToID("_WallFadeCenter");

        [Tooltip("벽에 바꿔 끼울 WallSphereFade 셰이더. 직접 참조해야 빌드에서도 빠지지 않는다.")]
        [SerializeField] private Shader _fadeShader;
        [Tooltip("내 캐릭터를 중심으로 이 반경(구)에 든 벽 부분만 반투명해진다.")]
        [Min(0.1f)] [SerializeField] private float _fadeRadius = 3f;
        [Tooltip("반투명해진 부분의 불투명도. 0이면 완전히 사라진다.")]
        [Range(0f, 1f)] [SerializeField] private float _fadedAlpha = 0.2f;

        private Renderer[] _walls;
        private Material[] _fadeMaterials;
        private Transform _localPlayer;

        private void Awake()
        {
            Shader shader = _fadeShader != null ? _fadeShader : Shader.Find("OverCleaning/WallSphereFade");
            if (shader == null)
            {
                Debug.LogError("WallSphereFade 셰이더를 찾지 못했습니다. WallFader의 Fade Shader를 지정하세요.", this);
                enabled = false;
                return;
            }

            // 원본 머티리얼은 다른 물건도 함께 쓰므로 벽마다 복제해 바꿔 끼운다.
            _walls = GetComponentsInChildren<Renderer>();
            _fadeMaterials = new Material[_walls.Length];
            for (int index = 0; index < _walls.Length; index++)
            {
                Material original = _walls[index].sharedMaterial;
                Material fadeMaterial = new Material(shader);
                if (original != null && original.HasProperty(BaseColorId))
                    fadeMaterial.SetColor(BaseColorId, original.GetColor(BaseColorId));
                fadeMaterial.SetFloat(FadedAlphaId, _fadedAlpha);
                _fadeMaterials[index] = fadeMaterial;
                _walls[index].sharedMaterial = fadeMaterial;
            }
        }

        private void OnDestroy()
        {
            // 전역을 꺼 두고, 복제한 머티리얼은 에셋이 아니라 여기서 만든 것이므로 직접 치운다.
            Shader.SetGlobalVector(FadeCenterId, Vector4.zero);
            if (_fadeMaterials == null)
                return;
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

            // 바닥이 아니라 몸통 높이를 중심으로 잡아야 벽의 가려지는 높이가 자연스럽다.
            Vector3 position = _localPlayer.position + Vector3.up * 0.9f;
            Shader.SetGlobalVector(FadeCenterId,
                new Vector4(position.x, position.y, position.z, _fadeRadius));
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
