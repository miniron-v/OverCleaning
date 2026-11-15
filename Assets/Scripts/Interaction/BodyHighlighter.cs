using UnityEngine;

namespace OverCleaning.Interaction
{
    /// <summary>
    /// 물건의 몸통 Renderer들을 제 색에서 흰 쪽으로 끌어올려 밝히는 도우미.
    /// 보는 사람 기기만의 연출이라 Material 복제 없이 PropertyBlock으로 칠한다.
    /// 만들 때 자식 Renderer와 제 색을 한 번 모아 두고, 켜고 끄기만 한다.
    /// </summary>
    public sealed class BodyHighlighter
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>제 색에서 흰색으로 끌어올리는 비율. 물건 색이 달라도 "밝아졌다"로 읽힌다.</summary>
        private const float Brightening = 0.45f;

        private readonly Renderer[] _renderers;
        private readonly Color[] _baseColors;
        private readonly MaterialPropertyBlock _properties = new MaterialPropertyBlock();

        public BodyHighlighter(Component root)
        {
            _renderers = root.GetComponentsInChildren<Renderer>(true);
            _baseColors = new Color[_renderers.Length];
            for (int index = 0; index < _renderers.Length; index++)
            {
                Material material = _renderers[index].sharedMaterial;
                _baseColors[index] = material != null && material.HasColor(BaseColorId)
                    ? material.GetColor(BaseColorId)
                    : Color.white;
            }
        }

        /// <summary>
        /// 켜면 밝히고 끄면 통째로 되돌린다. 제 PropertyBlock을 쓰는 물건(청소기 노즐)은
        /// 끈 뒤에 제 색을 다시 칠해야 한다.
        /// </summary>
        public void SetHighlighted(bool highlighted)
        {
            for (int index = 0; index < _renderers.Length; index++)
            {
                Renderer bodyRenderer = _renderers[index];
                if (bodyRenderer == null)
                    continue;
                if (highlighted)
                {
                    Color color = Color.Lerp(_baseColors[index], Color.white, Brightening);
                    bodyRenderer.GetPropertyBlock(_properties);
                    _properties.SetColor(BaseColorId, color);
                    bodyRenderer.SetPropertyBlock(_properties);
                }
                else
                {
                    bodyRenderer.SetPropertyBlock(null);
                }
            }
        }
    }
}
