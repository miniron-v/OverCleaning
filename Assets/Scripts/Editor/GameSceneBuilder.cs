using OverCleaning.Network;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OverCleaning.EditorTools
{
    /// <summary>
    /// 인게임 씬에 네트워크 진행에 필요한 오브젝트를 만든다.
    /// 화면 UI는 없고, 플레이어 스폰과 게임 진행 담당만 둔다.
    /// </summary>
    public static class GameSceneBuilder
    {
        [MenuItem("OverCleaning/Build Game Scene Objects")]
        public static void Build()
        {
            if (Object.FindFirstObjectByType<GameScreen>() == null)
                new GameObject("GameScreen", typeof(GameScreen));

            GameObject spawner = SpawnerBuilder.CreateIfMissing();
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            if (spawner != null)
                Selection.activeGameObject = spawner;
            Debug.Log("인게임 오브젝트를 만들었습니다. 스폰 지점을 원하는 자리로 옮긴 뒤 씬을 저장하세요.");
        }
    }
}
