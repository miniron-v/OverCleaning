using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 화면 위에 엄마가 돌아올 때까지 남은 시간을 막대로 보여 준다. 시간은 GameRound가 매 프레임 넘겨 준다.
    /// 막대는 초마다 한 칸씩 줄며 튀고, 급박해지면 붉게 번쩍이며 흔들리고 화면 가장자리가 물든다.
    /// 그림은 프리팹에서, 색과 타이밍은 아래 값으로 고친다.
    /// </summary>
    public sealed class RoundTimerBar : MonoBehaviour
    {
        [Header("그림")]
        [SerializeField] private GameObject _content;
        [Tooltip("튀고 흔들리는 막대 전체.")]
        [SerializeField] private RectTransform _bar;
        [Tooltip("남은 만큼 길이가 줄어드는 채움. 오른쪽 앵커로 길이를 맞춘다.")]
        [SerializeField] private RectTransform _fill;
        [SerializeField] private Image _fillImage;
        [Tooltip("채움이 줄어든 자리에 잠시 남는 자국.")]
        [SerializeField] private RectTransform _trail;
        [SerializeField] private TMP_Text _timeText;
        [SerializeField] private TMP_Text _detailsText;
        [SerializeField] private Image _vignette;
        [SerializeField] private TMP_Text _urgentNotice;

        [Header("색")]
        [SerializeField] private Color _calmColor = new Color(0.35f, 0.85f, 0.45f);
        [SerializeField] private Color _hurryColor = new Color(1f, 0.78f, 0.2f);
        [SerializeField] private Color _urgentColor = new Color(1f, 0.22f, 0.18f);
        [Tooltip("급박할 때 초가 바뀌는 순간 글자가 띠는 색.")]
        [SerializeField] private Color _urgentTextColor = new Color(1f, 0.85f, 0.3f);

        [Header("타이밍")]
        [Tooltip("이만큼 남으면 급박하게 강조한다(초).")]
        [SerializeField] private float _urgentSeconds = 30f;
        [Tooltip("초가 바뀔 때 튀는 연출이 가라앉는 시간(초).")]
        [SerializeField] private float _tickDuration = 0.3f;
        [Tooltip("초가 바뀔 때 커지는 정도. 평소 / 급박할 때.")]
        [SerializeField] private float _tickPop = 0.04f;
        [SerializeField] private float _urgentTickPop = 0.1f;
        [Tooltip("자국이 채움을 따라잡는 빠르기.")]
        [SerializeField] private float _trailSpeed = 6f;
        [Tooltip("급박할 때 흔들림 세기. 30초 / 0초.")]
        [SerializeField] private float _shakeStart = 2f;
        [SerializeField] private float _shakeEnd = 9f;
        [Tooltip("가장자리 물듦의 진하기. 30초 / 0초.")]
        [SerializeField] private float _vignetteStart = 0.25f;
        [SerializeField] private float _vignetteEnd = 0.65f;

        [Header("급박 알림")]
        [SerializeField] private float _noticeDuration = 2.4f;
        [SerializeField] private float _noticeFadeDuration = 0.4f;
        [Tooltip("나타난 뒤 시간(초)에 따른 크기.")]
        [SerializeField] private AnimationCurve _noticeScale = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 6f), new Keyframe(0.2f, 1.12f), new Keyframe(0.3f, 1f));
        [Tooltip("흔들리는 각도. 1초에 걸쳐 잦아든다.")]
        [SerializeField] private float _noticeWobble = 4f;

        private Vector2 _barPosition;
        private int _lastWholeSecond = -1;
        private float _tickTime = -10f;
        private float _urgentStartTime = -1f;
        private float _trailRatio = 1f;

        private void Awake()
        {
            _barPosition = _bar.anchoredPosition;
            _content.SetActive(false);
        }

        public void Hide() => _content.SetActive(false);

        public void Show(double remainingSeconds, float duration, string details)
        {
            _content.SetActive(true);
            float remaining = (float)remainingSeconds;
            float now = Time.unscaledTime;

            int wholeSecond = Mathf.CeilToInt(remaining);
            if (wholeSecond != _lastWholeSecond)
            {
                if (_lastWholeSecond >= 0)
                    _tickTime = now;
                _lastWholeSecond = wholeSecond;
            }

            bool urgent = remaining <= _urgentSeconds;
            if (urgent && _urgentStartTime < 0f)
                _urgentStartTime = now;
            // 0에서 1로, 끝에 가까울수록 커진다.
            float urgency = urgent ? 1f - remaining / _urgentSeconds : 0f;
            // 초가 바뀐 순간 1, 곧 0으로 가라앉는다.
            float punch = 1f - Mathf.Clamp01((now - _tickTime) / _tickDuration);

            // 막대는 초 단위로 한 칸씩 줄고, 줄어든 자리에 자국이 따라 들어온다.
            float ratio = Mathf.Clamp01(wholeSecond / duration);
            _trailRatio = Mathf.Max(ratio,
                Mathf.Lerp(_trailRatio, ratio, 1f - Mathf.Exp(-_trailSpeed * Time.unscaledDeltaTime)));
            SetLength(_fill, ratio);
            SetLength(_trail, _trailRatio);

            _fillImage.color = urgent
                ? Color.Lerp(_urgentColor, Color.white, 0.6f * punch)
                : Color.Lerp(_hurryColor, _calmColor, Mathf.InverseLerp(_urgentSeconds / duration, 1f, ratio));

            // 화면 너비를 다 쓰므로 막대는 세로로만 튄다. 글자는 고르게 튄다.
            float pop = 1f + (urgent ? _urgentTickPop : _tickPop) * punch;
            Vector2 shake = urgent ? Shake(now, Mathf.Lerp(_shakeStart, _shakeEnd, urgency) * (0.3f + 0.7f * punch)) : Vector2.zero;
            _bar.anchoredPosition = _barPosition + shake;
            _bar.localScale = new Vector3(1f, pop, 1f);
            _timeText.rectTransform.anchoredPosition = _barPosition + shake;
            _timeText.rectTransform.localScale = Vector3.one * pop;

            int minutes = wholeSecond / 60;
            int seconds = wholeSecond % 60;
            _timeText.text = minutes > 0 ? $"{minutes}분 {seconds}초" : $"{seconds}초";
            _timeText.color = urgent ? Color.Lerp(Color.white, _urgentTextColor, punch) : Color.white;
            _detailsText.text = details;

            _vignette.enabled = urgent;
            if (urgent)
            {
                Color tint = _urgentColor;
                tint.a = Mathf.Lerp(_vignetteStart, _vignetteEnd, urgency) * (0.35f + 0.65f * punch);
                _vignette.color = tint;
            }

            UpdateUrgentNotice(urgent ? now - _urgentStartTime : -1f);
        }

        /// <summary>급박해진 순간 튀어나왔다가 사라지는 알림. 음수면 감춘다.</summary>
        private void UpdateUrgentNotice(float elapsed)
        {
            bool visible = elapsed >= 0f && elapsed <= _noticeDuration;
            _urgentNotice.gameObject.SetActive(visible);
            if (!visible)
                return;

            RectTransform rect = _urgentNotice.rectTransform;
            rect.localScale = Vector3.one * Mathf.Max(0f, _noticeScale.Evaluate(elapsed));
            rect.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Sin(elapsed * 18f) * _noticeWobble * Mathf.Clamp01(1f - elapsed));
            _urgentNotice.alpha = Mathf.Clamp01((_noticeDuration - elapsed) / _noticeFadeDuration);
        }

        private static void SetLength(RectTransform rect, float ratio) =>
            rect.anchorMax = new Vector2(ratio, rect.anchorMax.y);

        private static Vector2 Shake(float time, float amplitude) => new Vector2(
            Mathf.PerlinNoise(time * 35f, 0f) - 0.5f,
            Mathf.PerlinNoise(0f, time * 35f) - 0.5f) * (amplitude * 2f);
    }
}
