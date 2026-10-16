using UnityEngine;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 머리 위에 플레이어 이름을 띄운다. 누가 누구인지 가릴 수 있게 모든 기기에서
    /// 모든 캐릭터에 그린다. 내 이름은 색을 달리해 내 캐릭터를 한눈에 찾게 한다.
    /// 이름은 결과 화면용으로 이미 동기화되는 PlayerScore의 것을 그대로 읽는다.
    /// </summary>
    public sealed class PlayerNameTag : MonoBehaviour
    {
        private static readonly Color MineColor = new Color(1f, 0.88f, 0.4f);
        private static readonly Color OthersColor = Color.white;
        private static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.7f);

        [Tooltip("이름이 떠 있는 높이. 기절 별보다 위에 둔다.")]
        [SerializeField] private float _height = 2.6f;

        private PlayerScore _score;
        private Camera _statusCamera;
        private GUIStyle _style;

        private void Awake()
        {
            _score = GetComponent<PlayerScore>();
        }

        private void OnGUI()
        {
            if (_score == null || !_score.IsSpawned)
                return;
            if (_statusCamera == null)
                _statusCamera = Camera.main;
            if (_statusCamera == null)
                return;

            Vector3 screenPosition =
                _statusCamera.WorldToScreenPoint(transform.position + Vector3.up * _height);
            if (screenPosition.z <= 0f)
                return;

            _style ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };

            Rect rect = new Rect(screenPosition.x - 80f, Screen.height - screenPosition.y - 11f, 160f, 22f);
            string nickname = _score.Nickname;

            // 밝은 바닥 위에서도 읽히도록 그림자를 깔고 쓴다.
            Color previousColor = GUI.color;
            GUI.color = ShadowColor;
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), nickname, _style);
            GUI.color = _score.IsOwner ? MineColor : OthersColor;
            GUI.Label(rect, nickname, _style);
            GUI.color = previousColor;
        }
    }
}
