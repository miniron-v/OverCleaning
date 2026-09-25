using UnityEngine;

namespace OverCleaning.Stages
{
    /// <summary>
    /// 스테이지별로 얻은 별 개수를 이 기기에 저장한다.
    /// 클리어 판정이 생기면 그 자리에서 RecordStars를 부른다.
    /// </summary>
    public static class StageProgress
    {
        public static int GetStars(StageDefinition stage)
        {
            if (stage == null)
                return 0;
            return Mathf.Clamp(PlayerPrefs.GetInt(BuildKey(stage), 0), 0, StageDefinition.MaxStars);
        }

        /// <summary>
        /// 이번 기록이 이전보다 좋을 때만 남긴다. 다시 해서 못한 결과로 별이 줄어들면 안 된다.
        /// </summary>
        public static void RecordStars(StageDefinition stage, int stars)
        {
            if (stage == null)
                return;
            stars = Mathf.Clamp(stars, 0, StageDefinition.MaxStars);
            if (stars <= GetStars(stage))
                return;
            PlayerPrefs.SetInt(BuildKey(stage), stars);
            PlayerPrefs.Save();
        }

        private static string BuildKey(StageDefinition stage) => $"Stage{stage.Number}.Stars";
    }
}
