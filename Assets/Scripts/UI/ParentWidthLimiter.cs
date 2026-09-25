using UnityEngine;

namespace OverCleaning.UI
{
    /// <summary>
    /// 원하는 가로 크기를 쓰되, 부모 폭의 일정 비율을 넘지 않게 줄인다.
    /// 좁은 화면에서 옆에 붙은 패널이 화면을 다 덮지 않게 할 때 쓴다.
    /// 앵커의 가로 두 점이 같아야 한다(가로로 늘어나는 앵커면 크기가 앵커를 따른다).
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public sealed class ParentWidthLimiter : MonoBehaviour
    {
        [Min(0f)] [SerializeField] private float _preferredWidth = 760f;

        [Tooltip("부모 폭에 대한 최대 비율. 0.5면 부모의 절반을 넘지 않는다.")]
        [Range(0.1f, 1f)] [SerializeField] private float _maxParentRatio = 0.5f;

        private RectTransform _rectTransform;

        private void OnEnable()
        {
            _rectTransform = (RectTransform)transform;
            Apply();
        }

        // 부모 크기가 바뀌어도 자식에게는 알림이 오지 않으므로 매 프레임 확인한다. 값이 같으면 건드리지 않는다.
        private void LateUpdate() => Apply();

        private void Apply()
        {
            if (!(transform.parent is RectTransform parent))
                return;

            float width = Mathf.Min(_preferredWidth, parent.rect.width * _maxParentRatio);
            if (!Mathf.Approximately(_rectTransform.rect.width, width))
                _rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        }
    }
}
