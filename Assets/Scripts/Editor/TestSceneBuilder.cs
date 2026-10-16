using OverCleaning.InGame;
using OverCleaning.Network;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OverCleaning.EditorTools
{
    /// <summary>
    /// 오브젝트를 바로 굴려 볼 수 있는 테스트 씬을 만든다. 넓은 바닥에 청소기와 쓰레기통,
    /// 먼지밭, 장애물 벽 하나만 둔다. Play를 누르면 로비 없이 바로 혼자 호스트로 시작한다.
    ///
    /// 라운드 진행(GameRound)은 두지 않아 시간 제한 없이 마음껏 테스트한다.
    /// 여럿이 하는 테스트는 본 흐름(시작 화면 → 방 코드)으로 한다.
    /// </summary>
    public static class TestSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/ObjectTest.unity";

        [MenuItem("OverCleaning/Build Object Test Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            GameObject floor = BuildGround();
            DustField dustField = BuildDustField(floor.GetComponent<Collider>());
            TrashCan trashCan = BuildTrashCan(dustField);
            BuildVacuum(dustField, trashCan);
            SpawnerBuilder.CreateIfMissing();
            BuildNetworkManager();
            LivingRoomBuilder.TuneLighting();

            // 본 씬과 같은 각도로 바닥 전체가 보이게 둔다.
            Camera camera = Camera.main;
            if (camera != null)
                camera.transform.SetPositionAndRotation(
                    new Vector3(0f, 22f, -15f), Quaternion.Euler(55f, 0f, 0f));

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"오브젝트 테스트 씬을 만들었습니다: {ScenePath}. Play를 누르면 바로 시작합니다.");
        }

        private static GameObject BuildGround()
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Test Floor";
            floor.transform.position = new Vector3(0f, -0.05f, 0f);
            floor.transform.localScale = new Vector3(24f, 0.1f, 18f);
            floor.GetComponent<Renderer>().sharedMaterial =
                LivingRoomBuilder.GetWoodMaterial("Wood Floor Test", new Vector2(12f, 9f), Color.white);

            Material cream = LivingRoomBuilder.GetMaterial("Wall Cream", new Color(0.91f, 0.88f, 0.81f));
            CreateWall("Test Wall North", new Vector3(0f, 1f, 9.175f), new Vector3(24.7f, 2f, 0.35f), cream);
            CreateWall("Test Wall South", new Vector3(0f, 1f, -9.175f), new Vector3(24.7f, 2f, 0.35f), cream);
            CreateWall("Test Wall West", new Vector3(-12.175f, 1f, 0f), new Vector3(0.35f, 2f, 18.7f), cream);
            CreateWall("Test Wall East", new Vector3(12.175f, 1f, 0f), new Vector3(0.35f, 2f, 18.7f), cream);

            // 회전 차단과 문턱 끌기를 시험할 장애물 벽 하나.
            CreateWall("Test Obstacle", new Vector3(3f, 1f, 2f), new Vector3(0.3f, 2f, 5f), cream);
            return floor;
        }

        private static void CreateWall(string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.position = position;
            wall.transform.localScale = scale;
            wall.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static DustField BuildDustField(Collider floorCollider)
        {
            GameObject fieldObject = new GameObject("Dust Field", typeof(NetworkObject), typeof(DustField));
            DustField dustField = fieldObject.GetComponent<DustField>();

            SerializedObject serialized = new SerializedObject(dustField);
            SerializedProperty floors = serialized.FindProperty("_floors");
            floors.arraySize = 1;
            floors.GetArrayElementAtIndex(0).objectReferenceValue = floorCollider;
            serialized.FindProperty("_dustMaterial").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Dust.mat");
            SerializedProperty textures = serialized.FindProperty("_dustTextures");
            string[] texturePaths =
            {
                "Assets/Textures/Dust/DustGrain.png",
                "Assets/Textures/Dust/DustLint.png",
                "Assets/Textures/Dust/DustSoft.png",
            };
            textures.arraySize = texturePaths.Length;
            for (int index = 0; index < texturePaths.Length; index++)
                textures.GetArrayElementAtIndex(index).objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<Texture2D>(texturePaths[index]);
            serialized.FindProperty("_dustCount").intValue = 150;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return dustField;
        }

        private static TrashCan BuildTrashCan(DustField dustField)
        {
            GameObject binObject = new GameObject("Trash Can");
            binObject.transform.position = new Vector3(-6f, 0f, -5f);
            BoxCollider collider = binObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(1.1f, 1.3f, 1.1f);
            collider.center = new Vector3(0f, 0.65f, 0f);
            AddCarriableNetworking(binObject);

            TrashCan trashCan = binObject.AddComponent<TrashCan>();
            SerializedObject serialized = new SerializedObject(trashCan);
            serialized.FindProperty("_carryMode").enumValueIndex = (int)CarriableItem.CarryMode.Carry;
            serialized.FindProperty("_carrySpeedMultiplier").floatValue = 0.7f;
            serialized.FindProperty("_pickUpRange").floatValue = 0.8f;
            serialized.FindProperty("_dustField").objectReferenceValue = dustField;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            LivingRoomBuilder.RestyleTrashCan();
            return trashCan;
        }

        private static void BuildVacuum(DustField dustField, TrashCan trashCan)
        {
            GameObject vacuumObject = new GameObject("Vacuum");
            vacuumObject.transform.position = new Vector3(2f, 0f, -4f);
            BoxCollider collider = vacuumObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.8f, 0.96f, 1.2f);
            collider.center = new Vector3(0f, 0.5f, 0.85f);
            AddCarriableNetworking(vacuumObject);

            GameObject suctionPoint = new GameObject("Suction Point");
            suctionPoint.transform.SetParent(vacuumObject.transform, false);
            suctionPoint.transform.localPosition = new Vector3(0f, 0.03f, 1.4f);

            Vacuum vacuum = vacuumObject.AddComponent<Vacuum>();
            SerializedObject serialized = new SerializedObject(vacuum);
            serialized.FindProperty("_suctionPoint").objectReferenceValue = suctionPoint.transform;
            serialized.FindProperty("_dustField").objectReferenceValue = dustField;
            serialized.FindProperty("_trashCan").objectReferenceValue = trashCan;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // 겉모습을 그리면서 노즐 Renderer 연결까지 맞춰 준다.
            LivingRoomBuilder.RestyleVacuum();
        }

        /// <summary>들고 나르기에 필요한 네트워크 부품. 본 씬의 청소기와 같은 설정이다.</summary>
        private static void AddCarriableNetworking(GameObject item)
        {
            NetworkObject networkObject = item.AddComponent<NetworkObject>();
            networkObject.AutoObjectParentSync = true;

            NetworkTransform transformSync = item.AddComponent<NetworkTransform>();
            transformSync.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            transformSync.InLocalSpace = true;
            transformSync.SyncScaleX = false;
            transformSync.SyncScaleY = false;
            transformSync.SyncScaleZ = false;
        }

        private static void BuildNetworkManager()
        {
            GameObject managerObject = new GameObject("NetworkManager",
                typeof(NetworkManager), typeof(UnityTransport), typeof(TestAutoHost));
            NetworkManager manager = managerObject.GetComponent<NetworkManager>();
            manager.NetworkConfig.NetworkTransport = managerObject.GetComponent<UnityTransport>();

            // 플레이어 프리팹을 스폰하려면 프리팹 목록에 등록되어 있어야 한다.
            NetworkPrefabsList prefabList =
                AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>("Assets/DefaultNetworkPrefabs.asset");
            if (prefabList != null)
                manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(prefabList);
            EditorUtility.SetDirty(manager);
        }
    }
}
