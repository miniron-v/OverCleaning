using System.Collections;
using System.Text;
using TMPro;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OverCleaning.Network
{
    /// <summary>
    /// 대기방. 방 코드와 참가자 목록을 보여준다.
    /// 스테이지 시작은 맵 선택 오브젝트로 여는 단계 선택(StageSelection)이 맡는다.
    /// </summary>
    public sealed class RoomScreen : MonoBehaviour
    {
        /// <summary>세션 참가가 끝나기를 기다리는 한계 시간.</summary>
        private const float SessionWaitSeconds = 10f;

        [SerializeField] private TMP_Text _roomCodeText;
        [SerializeField] private TMP_Text _playerListText;
        [SerializeField] private Button _copyCodeButton;
        [SerializeField] private Button _leaveButton;

        private ISession _session;
        private Coroutine _copyFeedback;

        private IEnumerator Start()
        {
            // 참가자는 세션 참가가 끝나기 전에 서버를 따라 이 씬에 도착한다.
            // 세션이 채워질 때까지 기다린 뒤 화면을 꾸민다.
            float deadline = Time.time + SessionWaitSeconds;
            while (GameSession.Current == null && Time.time < deadline)
                yield return null;

            _session = GameSession.Current;
            if (_session == null)
            {
                // 세션 없이 이 씬을 직접 열었거나 참가가 실패한 경우다.
                Debug.LogWarning("세션이 없어 시작 화면으로 돌아갑니다.", this);
                SceneManager.LoadScene(GameSession.StartSceneName);
                yield break;
            }

            _copyCodeButton.onClick.AddListener(CopyCode);
            _leaveButton.onClick.AddListener(() => _ = LeaveAsync());

            _session.PlayerJoined += OnPlayerChanged;
            _session.PlayerLeaving += OnPlayerChanged;
            Refresh();
        }

        private void OnDestroy()
        {
            if (_session == null)
                return;
            _session.PlayerJoined -= OnPlayerChanged;
            _session.PlayerLeaving -= OnPlayerChanged;
        }

        private void OnPlayerChanged(string playerId) => Refresh();

        private void Refresh()
        {
            _roomCodeText.text = $"방 코드: {_session.Code}";
            _playerListText.text = BuildPlayerList();
        }

        private string BuildPlayerList()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"참가자 {_session.Players.Count}/{GameSession.MaxPlayers}");
            foreach (IReadOnlyPlayer player in _session.Players)
            {
                string nickname = GameSession.GetNickname(player);
                if (string.IsNullOrEmpty(nickname))
                    nickname = "이름 없음";
                bool isHost = player.Id == _session.Host;
                builder.AppendLine(isHost ? $"{nickname} (방장)" : nickname);
            }

            return builder.ToString();
        }

        private void CopyCode()
        {
            GUIUtility.systemCopyBuffer = _session.Code;
            if (_copyFeedback != null)
                StopCoroutine(_copyFeedback);
            _copyFeedback = StartCoroutine(ShowCopiedMessage());
        }

        /// <summary>
        /// 복사는 화면에 변화가 없어 눌렸는지 알기 어렵다. 잠시 문구를 바꿔 알려준다.
        /// </summary>
        private IEnumerator ShowCopiedMessage()
        {
            _roomCodeText.text = "복사했습니다";
            yield return new WaitForSeconds(1f);
            _roomCodeText.text = $"방 코드: {_session.Code}";
            _copyFeedback = null;
        }

        private async System.Threading.Tasks.Task LeaveAsync()
        {
            _leaveButton.interactable = false;
            await GameSession.LeaveAsync();
            SceneManager.LoadScene(GameSession.StartSceneName);
        }
    }
}
