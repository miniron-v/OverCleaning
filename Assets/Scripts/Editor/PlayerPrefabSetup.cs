using OverCleaning.InGame;
using OverCleaning.Input;
using OverCleaning.Network;
// PlayerMovement는 네임스페이스 없이 전역에 선언되어 있어 별도 using이 없다.
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OverCleaning.EditorTools
{
    /// <summary>
    /// Player 프리팹을 네트워크로 스폰할 수 있게 바꾸고, NetworkManager에 등록한다.
    /// </summary>
    public static class PlayerPrefabSetup
    {
        private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";

        [MenuItem("OverCleaning/Setup Player Prefab For Network")]
        public static void Setup()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("플레이어 프리팹",
                    $"프리팹을 찾지 못했습니다: {PlayerPrefabPath}", "확인");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                AddNetworkComponents(root);
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            ClearNetworkManagerPlayerPrefab();
            Debug.Log("Player 프리팹을 네트워크용으로 설정했습니다.");
        }

        private static void AddNetworkComponents(GameObject root)
        {
            if (root.GetComponent<NetworkObject>() == null)
                root.AddComponent<NetworkObject>();

            if (root.GetComponent<NetworkTransform>() == null)
            {
                // 이동은 각자 자기 캐릭터에서 계산하므로 소유자가 위치를 써서 보내는 방식을 쓴다.
                NetworkTransform transformSync = root.AddComponent<NetworkTransform>();
                transformSync.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
                // 바닥에 붙어 도는 게임이라 세로 이동과 크기는 보내지 않는다.
                transformSync.SyncPositionY = false;
                transformSync.SyncScaleX = false;
                transformSync.SyncScaleY = false;
                transformSync.SyncScaleZ = false;
            }

            NetworkPlayer networkPlayer = root.GetComponent<NetworkPlayer>();
            if (networkPlayer == null)
                networkPlayer = root.AddComponent<NetworkPlayer>();

            SerializedObject serialized = new SerializedObject(networkPlayer);
            serialized.FindProperty("_playerInput").objectReferenceValue = root.GetComponent<PlayerInput>();
            serialized.FindProperty("_keyShuffle").objectReferenceValue =
                root.GetComponent<KeyShuffleController>();

            SerializedProperty behaviours = serialized.FindProperty("_ownerOnlyBehaviours");
            MonoBehaviour[] ownerOnly =
            {
                root.GetComponent<PlayerMovement>(),
                root.GetComponent<PlayerVacuum>(),
                root.GetComponent<KeyShuffleController>(),
            };

            behaviours.arraySize = ownerOnly.Length;
            for (int index = 0; index < ownerOnly.Length; index++)
                behaviours.GetArrayElementAtIndex(index).objectReferenceValue = ownerOnly[index];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 접속한 사람마다 이 프리팹이 자동으로 스폰되도록 NetworkManager에 등록한다.
        /// </summary>
        /// <summary>
        /// NetworkManager의 Player Prefab 칸을 비운다.
        /// 값이 있으면 호스트가 접속하는 순간 Netcode가 시작 화면에서 플레이어를 스폰해버린다.
        /// 스폰은 각 씬의 PlayerSpawner가 맡는다.
        /// </summary>
        private static void ClearNetworkManagerPlayerPrefab()
        {
            NetworkManager manager = Object.FindFirstObjectByType<NetworkManager>();
            if (manager == null)
                return;

            SerializedObject serialized = new SerializedObject(manager);
            SerializedProperty playerPrefab = serialized.FindProperty("NetworkConfig.PlayerPrefab");
            if (playerPrefab.objectReferenceValue == null)
                return;

            playerPrefab.objectReferenceValue = null;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(manager);
            Debug.Log("NetworkManager의 Player Prefab을 비웠습니다. 씬을 저장하세요.");
        }
    }
}
