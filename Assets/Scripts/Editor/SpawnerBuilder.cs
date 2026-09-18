using OverCleaning.Network;
using UnityEditor;
using UnityEngine;

namespace OverCleaning.EditorTools
{
    /// <summary>
    /// 플레이어를 스폰할 오브젝트를 씬에 만든다. 룸과 인게임이 함께 쓴다.
    /// </summary>
    internal static class SpawnerBuilder
    {
        private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";

        /// <summary>
        /// 스포너와 스폰 지점을 둔다. 지점을 나눠두지 않으면 참가자가 모두 한 자리에 겹친다.
        /// 이미 있으면 스폰 지점은 그대로 두고 프리팹 참조만 다시 맞춘다.
        /// </summary>
        internal static GameObject CreateIfMissing()
        {
            PlayerSpawner existing = Object.FindFirstObjectByType<PlayerSpawner>();
            if (existing != null)
            {
                AssignPlayerPrefab(existing);
                return existing.gameObject;
            }

            GameObject spawnerObject = new GameObject("PlayerSpawner", typeof(PlayerSpawner));
            Transform[] points = new Transform[GameSession.MaxPlayers];
            for (int index = 0; index < points.Length; index++)
            {
                GameObject point = new GameObject($"SpawnPoint {index + 1}");
                point.transform.SetParent(spawnerObject.transform, false);
                // 기준점을 중심으로 가로로 늘어세운다. 자리는 씬에 맞게 직접 옮긴다.
                float offset = (index - (points.Length - 1) * 0.5f) * 2f;
                point.transform.position = new Vector3(offset, 0f, 0f);
                points[index] = point.transform;
            }

            PlayerSpawner spawner = spawnerObject.GetComponent<PlayerSpawner>();
            SerializedObject serialized = new SerializedObject(spawner);
            SerializedProperty spawnPoints = serialized.FindProperty("_spawnPoints");
            spawnPoints.arraySize = points.Length;
            for (int index = 0; index < points.Length; index++)
                spawnPoints.GetArrayElementAtIndex(index).objectReferenceValue = points[index];
            serialized.ApplyModifiedPropertiesWithoutUndo();

            AssignPlayerPrefab(spawner);
            return spawnerObject;
        }

        private static void AssignPlayerPrefab(PlayerSpawner spawner)
        {
            SerializedObject serialized = new SerializedObject(spawner);
            serialized.FindProperty("_playerPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(spawner);
        }
    }
}
