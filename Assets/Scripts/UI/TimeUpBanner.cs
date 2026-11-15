using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 시간이 다했을 때 화면 가운데에 '도착!'을 내려찍는다. 화면이 어두워지고 띠가 펼쳐진 뒤
    /// 글자가 크게 내려와 박히며 화면이 흔들린다. 게임이 멈춘 뒤라 실제 시간으로 움직인다.
    /// 그림은 프리팹에서, 타이밍은 아래 값과 곡선으로 고친다.
    /// </summary>
    public sealed class TimeUpBanner : MonoBehaviour
    {
        [Header("그림")]
        [SerializeField] private GameObject _content;
        [SerializeField] private Image _dim;
        [Tooltip("흔들림을 받는 묶음.")]
        [SerializeField] private RectTransform _shakeRoot;
        [SerializeField] private RectTransform _ribbon;
        [SerializeField] private TMP_Text _title;

        [Header("타이밍")]
        [SerializeField] private float _dimAlpha = 0.55f;
        [SerializeField] private float _dimFadeDuration = 0.3f;
        [Tooltip("시간(초)에 따른 띠의 가로 크기.")]
        [SerializeField] private AnimationCurve _ribbonWidth = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 6f), new Keyframe(0.22f, 1.08f), new Keyframe(0.3f, 1f));
        [Tooltip("시간(초)에 따른 글자 크기. 크게 내려와 박힌 뒤 출렁인다.")]
        [SerializeField] private AnimationCurve _titleScale = new AnimationCurve(
            new Keyframe(0f, 3f, 0f, 0f), new Keyframe(0.35f, 1f, -12f, 0f),
            new Keyframe(0.42f, 1.08f), new Keyframe(0.52f, 0.96f), new Keyframe(0.64f, 1f));
        [SerializeField] private float _titleFadeDuration = 0.12f;
        [Tooltip("글자가 박히는 순간(초). 이때부터 화면이 흔들린다.")]
        [SerializeField] private float _impactTime = 0.35f;
        [SerializeField] private float _shakeStrength = 24f;
        [Tooltip("흔들림이 잦아드는 빠르기.")]
        [SerializeField] private float _shakeDamping = 8f;

        private float _startTime;

        private void Awake() => _content.SetActive(false);

        public void Play()
        {
            _startTime = Time.unscaledTime;
            _content.SetActive(true);
            Update();
        }

        public void Hide() => _content.SetActive(false);

        private void Update()
        {
            if (!_content.activeSelf)
                return;

            float elapsed = Time.unscaledTime - _startTime;
            Color dim = _dim.color;
            dim.a = _dimAlpha * Mathf.Clamp01(elapsed / _dimFadeDuration);
            _dim.color = dim;

            _ribbon.localScale = new Vector3(Mathf.Max(0f, _ribbonWidth.Evaluate(elapsed)), 1f, 1f);
            _title.rectTransform.localScale = Vector3.one * _titleScale.Evaluate(elapsed);
            _title.alpha = Mathf.Clamp01(elapsed / _titleFadeDuration);

            float afterImpact = elapsed - _impactTime;
            float amplitude = afterImpact > 0f ? _shakeStrength * Mathf.Exp(-afterImpact * _shakeDamping) : 0f;
            _shakeRoot.anchoredPosition = new Vector2(
                Mathf.PerlinNoise(elapsed * 35f, 0f) - 0.5f,
                Mathf.PerlinNoise(0f, elapsed * 35f) - 0.5f) * (amplitude * 2f);
        }
    }
}
