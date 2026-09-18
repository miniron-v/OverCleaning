using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OverCleaning.Network
{
    /// <summary>
    /// 시작 화면. 진입과 동시에 Unity Services 로그인을 시작하고, 완료 전에는 버튼을 잠근다.
    /// 방 코드 입력은 참가를 누른 뒤에만 겹침 화면으로 보여준다.
    /// </summary>
    public sealed class StartScreen : MonoBehaviour
    {
        [SerializeField] private TMP_InputField _nicknameField;
        [SerializeField] private TMP_Text _messageText;
        [SerializeField] private Button _createRoomButton;
        [SerializeField] private Button _joinRoomButton;
        [SerializeField] private Button _retryButton;

        [Header("방 코드 입력")]
        [SerializeField] private GameObject _joinRoomPanel;
        [SerializeField] private TMP_InputField _roomCodeField;
        [SerializeField] private Button _confirmJoinButton;
        [SerializeField] private Button _cancelJoinButton;

        private bool _isReady;
        private bool _isBusy;

        public string Nickname => _nicknameField != null ? _nicknameField.text.Trim() : string.Empty;
        public string RoomCode => _roomCodeField != null ? _roomCodeField.text.Trim() : string.Empty;

        private void Awake()
        {
            _createRoomButton.onClick.AddListener(() => _ = CreateRoomAsync());
            _joinRoomButton.onClick.AddListener(OpenJoinPanel);
            _retryButton.onClick.AddListener(() => _ = SignInAsync());
            _confirmJoinButton.onClick.AddListener(() => _ = JoinRoomAsync());
            _cancelJoinButton.onClick.AddListener(CloseJoinPanel);
            _nicknameField.onValueChanged.AddListener(_ => UpdateInteractable());
            _roomCodeField.onValueChanged.AddListener(_ => UpdateInteractable());
            _joinRoomPanel.SetActive(false);
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
                SetMessage("닉네임을 입력하세요");
            }
            catch (Exception exception)
            {
                SetMessage($"연결 실패: {exception.Message}");
                _retryButton.gameObject.SetActive(true);
                Debug.LogException(exception, this);
            }

            UpdateInteractable();
        }

        private void OpenJoinPanel()
        {
            _roomCodeField.text = string.Empty;
            _joinRoomPanel.SetActive(true);
            _roomCodeField.Select();
            UpdateInteractable();
        }

        private void CloseJoinPanel()
        {
            _joinRoomPanel.SetActive(false);
            UpdateInteractable();
        }

        private async Task CreateRoomAsync()
        {
            await RunSessionTaskAsync("방을 만드는 중...", () => GameSession.CreateAsync(Nickname));
        }

        private async Task JoinRoomAsync()
        {
            await RunSessionTaskAsync("방에 참가하는 중...", () => GameSession.JoinAsync(RoomCode, Nickname));
        }

        /// <summary>
        /// 방 생성과 참가는 실패 처리와 버튼 잠금이 같으므로 한곳에서 처리한다.
        /// </summary>
        private async Task RunSessionTaskAsync(string progressMessage, Func<Task> sessionTask)
        {
            if (_isBusy)
                return;

            _isBusy = true;
            SetMessage(progressMessage);
            UpdateInteractable();
            try
            {
                await sessionTask();
                // 씬을 넘어가면 이 오브젝트는 사라지므로 잠금을 되돌리지 않는다.
                // 참가자는 호스트가 있는 씬으로 Netcode가 알아서 옮겨준다.
                GameSession.EnterRoom();
                SetMessage("방으로 들어가는 중...");
                return;
            }
            catch (Exception exception)
            {
                SetMessage($"실패: {exception.Message}");
                Debug.LogException(exception, this);
            }

            _isBusy = false;
            UpdateInteractable();
        }

        /// <summary>
        /// 로그인이 끝나고 닉네임이 입력된 뒤에만 방 버튼을 열어준다.
        /// 참가 확정은 방 코드까지 있어야 한다.
        /// </summary>
        private void UpdateInteractable()
        {
            bool canAct = _isReady && !_isBusy && !string.IsNullOrWhiteSpace(Nickname);
            _createRoomButton.interactable = canAct;
            _joinRoomButton.interactable = canAct;
            _confirmJoinButton.interactable = canAct && !string.IsNullOrWhiteSpace(RoomCode);
            _cancelJoinButton.interactable = !_isBusy;
            _nicknameField.interactable = !_isBusy;
            _roomCodeField.interactable = !_isBusy;
        }

        private void SetMessage(string message)
        {
            if (_messageText != null)
                _messageText.text = message;
        }
    }
}
