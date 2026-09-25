using OverCleaning.Interaction;
using UnityEngine;

namespace OverCleaning.Lobby
{
    /// <summary>
    /// 대기방에 놓인 맵 선택 오브젝트. 상호작용하면 단계 선택의 주도권을 잡거나 내려놓는다.
    /// 누군가 고르는 중이면 다른 사람은 상호작용할 수 없다.
    /// </summary>
    public sealed class StageSelectObject : MonoBehaviour, IInteractable
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private StageSelection _selection;

        [Tooltip("누가 고르는 중인지 색으로 알려줄 부분. 비워두면 색을 바꾸지 않는다.")]
        [SerializeField] private Renderer _indicatorRenderer;

        [SerializeField] private Color _idleColor = new Color(0.35f, 0.75f, 1f, 1f);
        [SerializeField] private Color _selectingColor = new Color(1f, 0.82f, 0.2f, 1f);

        private MaterialPropertyBlock _indicatorProperties;

        public bool CanInteract => _selection != null && _selection.IsSpawned &&
                                   (!_selection.IsOpen || _selection.IsLocalController);

        public string Prompt => _selection != null && _selection.IsLocalController ? "선택 취소" : "맵 선택";

        private void Awake()
        {
            _indicatorProperties = new MaterialPropertyBlock();
            if (_selection == null)
            {
                Debug.LogError("StageSelectObject에 StageSelection이 지정되어 있지 않습니다.", this);
                return;
            }
            _selection.StateChanged += UpdateIndicator;
        }

        private void Start()
        {
            UpdateIndicator();
        }

        private void OnDestroy()
        {
            if (_selection != null)
                _selection.StateChanged -= UpdateIndicator;
        }

        public void Interact()
        {
            if (CanInteract)
                _selection.ToggleControl();
        }

        private void UpdateIndicator()
        {
            if (_indicatorRenderer == null)
                return;
            bool isSelecting = _selection != null && _selection.IsOpen;
            _indicatorRenderer.GetPropertyBlock(_indicatorProperties);
            _indicatorProperties.SetColor(BaseColorId, isSelecting ? _selectingColor : _idleColor);
            _indicatorRenderer.SetPropertyBlock(_indicatorProperties);
        }
    }
}
