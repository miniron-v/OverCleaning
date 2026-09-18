using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OverCleaning.Network
{
    /// <summary>
    /// 시작 화면. 진입과 동시에 Unity Services 로그인을 시작하고, 완료 전에는 버튼을 잠근다.
    /// 방 만들기·참가는 다음 작업에서 연결한다.
    /// </summary>
    public sealed class StartScreen : MonoBehaviour
    {
        [SerializeField] private TMP_InputField _nicknameField;
        [SerializeField] private TMP_Text _messageText;
        [SerializeField] private Button _createRoomButton;
        [SerializeField] private Button _joinRoomButton;
        [SerializeField] private Button _retryButton;

        private bool _isReady;

        public string Nickname => _nicknameField != null ? _nicknameField.text.Trim() : string.Empty;

        private void Awake()
        {
            _createRoomButton.onClick.AddListener(OnCreateRoomClicked);
            _joinRoomButton.onClick.AddListener(OnJoinRoomClicked);
            _retryButton.onClick.AddListener(() => _ = SignInAsync());
            _nicknameField.onValueChanged.AddListener(_ => UpdateInteractable());
        }

        private async void Start()
        {
            await SignInAsync();
        }

        private async Task SignInAsync()
        {
            _isReady = false;
            SetMessage("서버에 연결하는 중...");
            _retryButton.gameObject.SetActive(false);
            UpdateInteractable();
            try
            {
                await GameServices.SignInAsync();
                _isReady = true;
                SetMessage("연결됨");
            }
            catch (Exception exception)
            {
                SetMessage($"연결 실패: {exception.Message}");
                _retryButton.gameObject.SetActive(true);
                Debug.LogException(exception, this);
            }

            UpdateInteractable();
        }

        /// <summary>
        /// 로그인이 끝나고 닉네임이 입력된 뒤에만 방 버튼을 열어준다.
        /// </summary>
        private void UpdateInteractable()
        {
            bool canEnterRoom = _isReady && !string.IsNullOrWhiteSpace(Nickname);
            _createRoomButton.interactable = canEnterRoom;
            _joinRoomButton.interactable = canEnterRoom;
        }

        private void SetMessage(string message)
        {
            if (_messageText != null)
                _messageText.text = message;
        }

        private void OnCreateRoomClicked()
        {
            Debug.Log($"방 만들기는 아직 연결되지 않았습니다. 닉네임: {Nickname}");
        }

        private void OnJoinRoomClicked()
        {
            Debug.Log($"방 참가는 아직 연결되지 않았습니다. 닉네임: {Nickname}");
        }
    }
}
