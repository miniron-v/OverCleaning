using OverCleaning.Stages;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OverCleaning.Lobby
{
    /// <summary>
    /// 단계 선택 화면. StageSelection의 상태를 그대로 그리기만 하고, 버튼은 요청으로 넘긴다.
    /// 주도권이 없는 사람도 같은 화면을 보지만 시작 버튼 말고는 버튼이 보이지 않는다.
    /// </summary>
    public sealed class StageSelectPanel : MonoBehaviour
    {
        private const string StarCharacter = "★";

        [SerializeField] private StageSelection _selection;

        [Tooltip("열고 닫을 모달 전체. 이 컴포넌트가 붙은 오브젝트와 달라야 꺼진 동안에도 상태를 받는다.")]
        [SerializeField] private GameObject _modalRoot;

        [Header("상단")]
        [SerializeField] private TMP_Text _controllerText;
        [SerializeField] private Button _closeButton;

        [Header("스테이지")]
        [SerializeField] private TMP_Text _stageNumberText;
        [SerializeField] private TMP_Text _stageNameText;
        [SerializeField] private Image _thumbnailImage;
        [SerializeField] private GameObject _thumbnailPlaceholder;
        [SerializeField] private TMP_Text[] _starTexts;
        [SerializeField] private TMP_Text _goalText;
        [SerializeField] private TMP_Text _pageText;

        [Header("조작")]
        [SerializeField] private Button _previousButton;
        [SerializeField] private Button _nextButton;
        [SerializeField] private Button _startButton;

        [Header("색")]
        [SerializeField] private Color _filledStarColor = new Color(1f, 0.82f, 0.2f, 1f);
        [SerializeField] private Color _emptyStarColor = new Color(1f, 1f, 1f, 0.2f);
        [SerializeField] private Color _emptyThumbnailColor = new Color(0.3f, 0.32f, 0.36f, 1f);

        private void Awake()
        {
            _modalRoot.SetActive(false);
            if (_selection == null)
            {
                Debug.LogError("StageSelectPanel에 StageSelection이 지정되어 있지 않습니다.", this);
                return;
            }

            _closeButton.onClick.AddListener(_selection.RequestClose);
            _previousButton.onClick.AddListener(() => _selection.RequestBrowse(-1));
            _nextButton.onClick.AddListener(() => _selection.RequestBrowse(1));
            _startButton.onClick.AddListener(_selection.RequestStartStage);
            _selection.StateChanged += Refresh;
        }

        private void Start()
        {
            // 스폰 알림이 이 컴포넌트보다 먼저 지나갔을 수 있으므로 현재 상태로 한 번 맞춘다.
            Refresh();
        }

        private void OnDestroy()
        {
            if (_selection != null)
                _selection.StateChanged -= Refresh;
        }

        private void Refresh()
        {
            bool isOpen = _selection != null && _selection.IsOpen;
            _modalRoot.SetActive(isOpen);
            if (!isOpen)
                return;

            bool canControl = _selection.IsLocalController;
            _controllerText.text = canControl ? "내가 고르는 중" : $"{GetControllerName()} 님이 고르는 중";

            StageDefinition stage = _selection.CurrentStage;
            ShowStage(stage);

            int index = _selection.StageIndex;
            int count = _selection.StageCount;
            _pageText.text = count > 0 ? $"{index + 1} / {count}" : "스테이지 없음";

            // 누를 수 없는 버튼은 숨긴다. 시작 버튼만은 남겨 무엇을 기다리는지 알 수 있게 한다.
            _closeButton.gameObject.SetActive(canControl);
            _previousButton.gameObject.SetActive(canControl && index > 0);
            _nextButton.gameObject.SetActive(canControl && index < count - 1);
            _startButton.interactable = canControl && stage != null;
        }

        private void ShowStage(StageDefinition stage)
        {
            _stageNumberText.text = stage != null ? $"STAGE {stage.Number}" : string.Empty;
            _stageNameText.text = stage != null ? stage.DisplayName : string.Empty;
            _goalText.text = stage != null ? $"목표: {stage.Goal}" : string.Empty;

            Sprite thumbnail = stage != null ? stage.Thumbnail : null;
            _thumbnailImage.sprite = thumbnail;
            _thumbnailImage.color = thumbnail != null ? Color.white : _emptyThumbnailColor;
            _thumbnailPlaceholder.SetActive(thumbnail == null);

            int stars = StageProgress.GetStars(stage);
            for (int index = 0; index < _starTexts.Length; index++)
            {
                _starTexts[index].text = StarCharacter;
                _starTexts[index].color = index < stars ? _filledStarColor : _emptyStarColor;
            }
        }

        private string GetControllerName()
        {
            string nickname = _selection.ControllerNickname;
            return string.IsNullOrEmpty(nickname) ? "다른 플레이어" : nickname;
        }
    }
}
