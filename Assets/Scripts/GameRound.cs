using Unity.Netcode;
using UnityEngine;
using OverCleaning.Network;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 한 판의 흐름을 맡는다. 제한시간 안에 먼지를 다 치우면 성공, 시간이 다하면 실패다.
    ///
    /// 판정은 서버가 한다. 남은 시간은 서버 시간 기준이라 모두 같은 숫자를 보고,
    /// 남은 먼지는 각 기기의 DustField가 이미 같게 맞춰져 있어 그대로 보여 주면 된다.
    /// 결과 화면을 잠시 보여 준 뒤 전원을 대기방으로 돌려보낸다.
    /// </summary>
    public sealed class GameRound : NetworkBehaviour
    {
        private enum RoundState
        {
            Playing,
            Cleared,
            Failed,
        }

        [Tooltip("한 판의 제한시간(초).")]
        [Min(10f)] [SerializeField] private float _roundDuration = 180f;
        [Tooltip("결과 화면을 보여 주고 대기방으로 돌아가기까지의 시간(초).")]
        [Min(1f)] [SerializeField] private float _resultDuration = 8f;
        [SerializeField] private DustField _dustField;
        [SerializeField] private GameScreen _gameScreen;

        /// <summary>서버가 정한다. 판이 끝나는 서버 시각.</summary>
        private readonly NetworkVariable<double> _endTime = new NetworkVariable<double>();

        private readonly NetworkVariable<int> _state =
            new NetworkVariable<int>((int)RoundState.Playing);

        /// <summary>서버에서만 쓴다. 결과 화면을 닫고 대기방으로 돌아갈 시각.</summary>
        private double _returnTime;

        private RoundState State => (RoundState)_state.Value;

        private double RemainingSeconds =>
            IsSpawned ? System.Math.Max(0.0, _endTime.Value - NetworkManager.ServerTime.Time) : 0.0;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
                _endTime.Value = NetworkManager.ServerTime.Time + _roundDuration;
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer)
                return;

            if (State == RoundState.Playing)
            {
                if (_dustField != null && _dustField.IsBuilt && _dustField.RemainingDustCount == 0)
                    FinishRound(RoundState.Cleared);
                else if (RemainingSeconds <= 0.0)
                    FinishRound(RoundState.Failed);
                return;
            }

            if (NetworkManager.ServerTime.Time >= _returnTime && _gameScreen != null)
            {
                // 두 번 부르지 않도록 돌아갈 시각을 미뤄 둔다. 씬이 바뀌면 같이 사라진다.
                _returnTime = double.MaxValue;
                _gameScreen.EndGame();
            }
        }

        private void FinishRound(RoundState result)
        {
            _state.Value = (int)result;
            _returnTime = NetworkManager.ServerTime.Time + _resultDuration;
        }

        private void OnGUI()
        {
            if (!IsSpawned)
                return;

            if (State == RoundState.Playing)
                DrawPlayingDashboard();
            else
                DrawResultDashboard();
        }

        private void DrawPlayingDashboard()
        {
            double remaining = RemainingSeconds;
            int minutes = (int)remaining / 60;
            int seconds = (int)remaining % 60;
            int remainingDust = _dustField != null ? _dustField.RemainingDustCount : 0;
            GUI.Box(new Rect((Screen.width - 280f) * 0.5f, 12f, 280f, 34f),
                $"남은 시간 {minutes}:{seconds:00}   |   남은 먼지 {remainingDust}");
        }

        private void DrawResultDashboard()
        {
            const float width = 420f;
            PlayerScore[] scores = FindObjectsByType<PlayerScore>(FindObjectsSortMode.None);
            System.Array.Sort(scores, (left, right) => left.OwnerClientId.CompareTo(right.OwnerClientId));

            float height = 110f + scores.Length * 24f;
            float panelLeft = (Screen.width - width) * 0.5f;
            float panelTop = (Screen.height - height) * 0.5f;
            GUI.Box(new Rect(panelLeft, panelTop, width, height), string.Empty);

            string title = State == RoundState.Cleared ? "청소 완료!" : "시간 초과 — 실패";
            GUI.Label(new Rect(panelLeft, panelTop + 14f, width, 24f),
                $"<size=18><b>{title}</b></size>",
                new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, richText = true });

            GUI.Label(new Rect(panelLeft + 24f, panelTop + 48f, width - 48f, 20f),
                "<b>이름                 청소한 먼지        충돌</b>",
                new GUIStyle(GUI.skin.label) { richText = true });
            for (int index = 0; index < scores.Length; index++)
            {
                PlayerScore score = scores[index];
                GUI.Label(new Rect(panelLeft + 24f, panelTop + 70f + index * 24f, width - 48f, 20f),
                    $"{score.Nickname,-16} {score.CleanedDustCount,8}개 {score.CrashCount,10}회");
            }

            GUI.Label(new Rect(panelLeft, panelTop + height - 26f, width, 20f),
                "잠시 후 대기방으로 돌아갑니다...",
                new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter });
        }
    }
}
