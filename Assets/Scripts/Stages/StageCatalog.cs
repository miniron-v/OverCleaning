using UnityEngine;

namespace OverCleaning.Stages
{
    /// <summary>
    /// 플레이 가능한 스테이지 목록. 단계 선택 화면은 이 순서대로 넘겨 본다.
    /// 네트워크로는 목록 대신 인덱스만 주고받으므로 모든 클라이언트가 같은 에셋을 써야 한다.
    /// </summary>
    [CreateAssetMenu(menuName = "OverCleaning/Stage Catalog", fileName = "StageCatalog")]
    public sealed class StageCatalog : ScriptableObject
    {
        [SerializeField] private StageDefinition[] _stages;

        public int Count => _stages != null ? _stages.Length : 0;

        /// <summary>범위를 벗어나면 null이다.</summary>
        public StageDefinition Get(int index)
        {
            if (index < 0 || index >= Count)
                return null;
            return _stages[index];
        }
    }
}
