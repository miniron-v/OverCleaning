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
            Countdown,
            Playing,
            Cleared,
            Failed,
        }

        [Tooltip("한 판의 제한시간(초).")]
        [Min(10f)] [SerializeField] private float _roundDuration = 180f;
        [Tooltip("시작 전 카운트다운(초). 다 세기 전에는 아무도 움직이지 못한다.")]
        [Min(1f)] [SerializeField] private float _countdownDuration = 3f;
        [Tooltip("확인을 누르지 않아도 대기방으로 돌아가는 시간(초). 자리 비운 사람을 위한 보조다.")]
        [Min(1f)] [SerializeField] private float _resultDuration = 20f;
        [SerializeField] private DustField _dustField;
        [Tooltip("얼룩이 있는 스테이지만 지정한다. 비우면 먼지만으로 판정한다.")]
        [SerializeField] private GrimeField _grimeField;
        [Tooltip("쓰레기가 있는 스테이지만 지정한다. 비우면 먼지만으로 판정한다.")]
        [SerializeField] private LitterField _litterField;
        [SerializeField] private GameScreen _gameScreen;

        /// <summary>서버가 정한다. 판이 끝나는 서버 시각.</summary>
        private readonly NetworkVariable<double> _endTime = new NetworkVariable<double>();

        private readonly NetworkVariable<int> _state =
            new NetworkVariable<int>((int)RoundState.Countdown);

        /// <summary>지금 씬의 판. 카운트다운 동안 이동을 묶는 데 쓴다.</summary>
        private static GameRound _current;

        /// <summary>시작 전 카운트다운 동안 참이다. 판이 없는 씬(대기방)에서는 늘 거짓이다.</summary>
        public static bool IsMovementLocked =>
            _current != null && _current.IsSpawned && _current.State == RoundState.Countdown;

        /// <summary>서버에서만 쓴다. 결과 화면을 닫고 대기방으로 돌아갈 시각.</summary>
        private double _returnTime;

        private RoundState State => (RoundState)_state.Value;

        private double RemainingSeconds =>
            IsSpawned ? System.Math.Max(0.0, _endTime.Value - NetworkManager.ServerTime.Time) : 0.0;

        public override void OnNetworkSpawn()
        {
            _current = this;
            // 처음에는 카운트다운이 끝나는 시각을 담는다. 플레이로 넘어갈 때 제한시간으로 바뀐다.
            if (IsServer)
                _endTime.Value = NetworkManager.ServerTime.Time + _countdownDuration;
        }

        public override void OnNetworkDespawn()
        {
            if (_current == this)
                _current = null;
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer)
                return;

            if (State == RoundState.Countdown)
            {
                // 다 센 뒤부터 제한시간이 돈다. 카운트다운만큼 플레이 시간이 깎이지 않는다.
                if (RemainingSeconds <= 0.0)
                {
                    _state.Value = (int)RoundState.Playing;
                    _endTime.Value = NetworkManager.ServerTime.Time + _roundDuration;
                }
                return;
            }

            if (State == RoundState.Playing)
            {
                if (IsEverythingCleaned())
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

        /// <summary>
        /// 지정된 청소 대상이 모두 비었는가. 대상마다 깔리기 전의 0개를 다 치운 것으로
        /// 오해하지 않도록 IsBuilt를 함께 본다. 아무 대상도 없으면 거짓이다.
        /// </summary>
        private bool IsEverythingCleaned()
        {
            if (_dustField == null && _grimeField == null && _litterField == null)
                return false;
            if (_dustField != null && (!_dustField.IsBuilt || _dustField.RemainingDustCount > 0))
                return false;
            if (_grimeField != null && (!_grimeField.IsBuilt || _grimeField.RemainingCount > 0))
                return false;
            if (_litterField != null && (!_litterField.IsBuilt || _litterField.RemainingCount > 0))
                return false;
            return true;
        }

        private void FinishRound(RoundState result)
        {
            _state.Value = (int)result;
            _returnTime = NetworkManager.ServerTime.Time + _resultDuration;
        }

        /// <summary>누가 눌렀든 확인 한 번이면 전원이 돌아간다. 판정은 서버가 한다.</summary>
        [Rpc(SendTo.Server)]
        private void RequestReturnRpc()
        {
            if (State == RoundState.Cleared || State == RoundState.Failed)
                _returnTime = NetworkManager.ServerTime.Time;
        }

        private void OnGUI()
        {
            if (!IsSpawned)
                return;

            if (State == RoundState.Countdown)
                DrawCountdown();
            else if (State == RoundState.Playing)
                DrawPlayingDashboard();
            else
                DrawResultDashboard();
        }

        /// <summary>시작 전 남은 숫자를 화면 가운데에 크게 센다.</summary>
        private void DrawCountdown()
        {
            int count = Mathf.Max(1, Mathf.CeilToInt((float)RemainingSeconds));
            GUI.Label(new Rect(0f, Screen.height * 0.5f - 80f, Screen.width, 160f),
                $"<size=96><b>{count}</b></size>",
                new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, richText = true });
        }

        private void DrawPlayingDashboard()
        {
            double remaining = RemainingSeconds;
            int minutes = (int)remaining / 60;
            int seconds = (int)remaining % 60;
            string summary = $"남은 시간 {minutes}:{seconds:00}";
            if (_dustField != null)
                summary += $"   |   먼지 {_dustField.RemainingDustCount}";
            if (_grimeField != null)
                summary += $"   |   얼룩 {_grimeField.RemainingCount}";
            if (_litterField != null)
                summary += $"   |   쓰레기 {_litterField.RemainingCount}";
            GUI.Box(new Rect((Screen.width - 360f) * 0.5f, 12f, 360f, 34f), summary);
        }

        private void DrawResultDashboard()
        {
            const float width = 420f;
            PlayerScore[] scores = FindObjectsByType<PlayerScore>(FindObjectsSortMode.None);
            System.Array.Sort(scores, (left, right) => left.OwnerClientId.CompareTo(right.OwnerClientId));

            float height = 140f + scores.Length * 24f;
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

            if (GUI.Button(new Rect(panelLeft + (width - 120f) * 0.5f, panelTop + height - 58f, 120f, 30f),
                    "확인"))
                RequestReturnRpc();

            GUI.Label(new Rect(panelLeft, panelTop + height - 24f, width, 20f),
                "확인을 누르면 대기방으로 돌아갑니다",
                new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter });
        }
    }
}
