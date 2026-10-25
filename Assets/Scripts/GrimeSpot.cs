using UnityEngine;

namespace OverCleaning.InGame
{
    /// <summary>얼룩의 종류. 도구마다 지울 수 있는 종류가 다르다.</summary>
    public enum GrimeKind
    {
        /// <summary>창문에 붙은 얼룩. 창문닦이로 지운다.</summary>
        Window,

        /// <summary>바닥에 눌어붙은 얼룩. 물걸레로 지운다.</summary>
        Floor,
    }

    /// <summary>
    /// 씬에 손으로 놓는 얼룩 하나. 상태 관리와 동기화는 GrimeField가 맡고,
    /// 여기는 종류와 겉모습만 둔다. 닦일수록 흐려지다가 다 닦이면 꺼진다.
    /// </summary>
    public sealed class GrimeSpot : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Tooltip("이 얼룩을 지울 수 있는 도구의 종류.")]
        [SerializeField] private GrimeKind _kind = GrimeKind.Floor;
        [Tooltip("다 지워지기까지 받아야 하는 닦기 횟수.")]
        [Min(1)] [SerializeField] private int _wipesToClean = 5;
        [Tooltip("닦일수록 알파를 낮춰 흐려질 Renderer. 비우면 자식에서 찾는다.")]
        [SerializeField] private Renderer _renderer;

        private MaterialPropertyBlock _properties;
        private Color _baseColor = Color.white;

        public GrimeKind Kind => _kind;
        public int WipesToClean => _wipesToClean;

        private void Awake()
        {
            if (_renderer == null)
                _renderer = GetComponentInChildren<Renderer>();
            if (_renderer == null)
                return;
            _properties = new MaterialPropertyBlock();
            if (_renderer.sharedMaterial != null && _renderer.sharedMaterial.HasColor(BaseColorId))
                _baseColor = _renderer.sharedMaterial.GetColor(BaseColorId);
        }

        /// <summary>남은 횟수에 맞춰 겉모습을 바꾼다. 0이면 아예 꺼진다.</summary>
        public void ApplyRemaining(int remainingWipes)
        {
            gameObject.SetActive(remainingWipes > 0);
            if (remainingWipes <= 0 || _renderer == null || _properties == null)
                return;
            Color color = _baseColor;
            color.a *= (float)remainingWipes / _wipesToClean;
            _renderer.GetPropertyBlock(_properties);
            _properties.SetColor(BaseColorId, color);
            _renderer.SetPropertyBlock(_properties);
        }
    }
}
