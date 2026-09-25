using UnityEngine;

namespace OverCleaning.Stages
{
    /// <summary>
    /// 스테이지 하나의 정보. 단계 선택 화면이 보여주고, 고르면 적힌 씬으로 넘어간다.
    /// </summary>
    [CreateAssetMenu(menuName = "OverCleaning/Stage Definition", fileName = "Stage")]
    public sealed class StageDefinition : ScriptableObject
    {
        /// <summary>한 스테이지에서 얻을 수 있는 별의 최대 개수.</summary>
        public const int MaxStars = 3;

        [Tooltip("화면에 보이는 스테이지 번호. 별 기록의 키로도 쓰므로 스테이지마다 달라야 한다.")]
        [Min(1)] [SerializeField] private int _number = 1;

        [SerializeField] private string _displayName;

        [Tooltip("비워두면 빈 자리로 보인다.")]
        [SerializeField] private Sprite _thumbnail;

        [Tooltip("시작할 씬 이름. Build Profiles의 씬 목록에 있어야 한다.")]
        [SerializeField] private string _sceneName;

        [Tooltip("별 3개를 얻기 위한 목표.")]
        [TextArea] [SerializeField] private string _goal;

        public int Number => _number;
        public string DisplayName => _displayName;
        public Sprite Thumbnail => _thumbnail;
        public string SceneName => _sceneName;
        public string Goal => _goal;
    }
}
