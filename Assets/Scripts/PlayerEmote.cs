using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 머리 위 감정 표현. U 도와줘, I 화남, O 슬픔, P 행복.
    /// 내 캐릭터에서 키를 읽어 모두에게 알리고, 각 기기가 그 사람 머리 위에 그린다.
    /// 키 셔플과 무관한 고정 키라 입력 액션을 거치지 않고 바로 읽는다.
    /// </summary>
    public sealed class PlayerEmote : NetworkBehaviour
    {
        /// <summary>나타날 때 커졌다 줄어드는 시간(초).</summary>
        private const float PopDuration = 0.18f;

        [Tooltip("감정 그림. 순서대로 도와줘(U), 화남(I), 슬픔(O), 행복(P).")]
        [SerializeField] private Texture2D[] _emotes = new Texture2D[0];
        [Tooltip("떠 있는 높이. 이름표보다 위에 둔다.")]
        [SerializeField] private float _height = 3.05f;
        [Tooltip("감정이 떠 있는 시간(초).")]
        [Min(0.5f)] [SerializeField] private float _duration = 2.5f;

        private Camera _statusCamera;
        private int _currentEmote = -1;
        private float _showTime;

        private void Update()
        {
            if (!IsOwner || !IsSpawned)
                return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.uKey.wasPressedThisFrame)
                ShowEmoteRpc(0);
            else if (keyboard.iKey.wasPressedThisFrame)
                ShowEmoteRpc(1);
            else if (keyboard.oKey.wasPressedThisFrame)
                ShowEmoteRpc(2);
            else if (keyboard.pKey.wasPressedThisFrame)
                ShowEmoteRpc(3);
        }

        /// <summary>감정은 그 사람의 기분이므로 본인만 띄울 수 있다.</summary>
        [Rpc(SendTo.Everyone, RequireOwnership = true)]
        private void ShowEmoteRpc(int emoteIndex)
        {
            if (emoteIndex < 0 || emoteIndex >= _emotes.Length || _emotes[emoteIndex] == null)
                return;
            _currentEmote = emoteIndex;
            _showTime = Time.time;
        }

        private void OnGUI()
        {
            if (_currentEmote < 0 || Time.time > _showTime + _duration)
                return;
            if (_statusCamera == null)
                _statusCamera = Camera.main;
            if (_statusCamera == null)
                return;

            Vector3 screenPosition =
                _statusCamera.WorldToScreenPoint(transform.position + Vector3.up * _height);
            if (screenPosition.z <= 0f)
                return;

            // 나타날 때 크게 터졌다가 제 크기로 줄어들고, 끝날 때 옅어진다.
            float age = Time.time - _showTime;
            float size = 72f * Mathf.Lerp(1.5f, 1f, Mathf.Clamp01(age / PopDuration));
            float remaining = _showTime + _duration - Time.time;
            Color previousColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(remaining / 0.3f));
            GUI.DrawTexture(
                new Rect(screenPosition.x - size * 0.5f,
                    Screen.height - screenPosition.y - size * 0.5f, size, size),
                _emotes[_currentEmote], ScaleMode.ScaleToFit, true);
            GUI.color = previousColor;
        }
    }
}
