using System.Collections.Generic;
using System.Reflection;
using OverCleaning.InGame;
using TMPro;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OverCleaning.EditorTools
{
    /// <summary>
    /// 아이템 테스트 씬(ItemTest)을 만든다. ObjectTest를 복사해 자동 호스트·스포너·
    /// 청소기 같은 뼈대를 물려받고, 구역 다섯 칸(청소기/물걸레/창문닦이/빗자루/소파)을
    /// 깔아 도구와 청소 대상을 구역마다 놓는다. 도구와 소파는 스테이지 씬에서 복사해
    /// 실제 게임과 같은 설정을 쓴다. 키 셔플이 없는 씬이라 기본 WASD/E로 움직인다.
    /// 마지막에 빌드 목록에서 ObjectTest를 ItemTest로 바꾸고 ObjectTest를 지운다.
    /// </summary>
    public static class ItemTestSceneSetup
    {
        private const string SourceScenePath = "Assets/Scenes/ObjectTest.unity";
        private const string StageScenePath = "Assets/Scenes/DustCleaningTest.unity";
        private const string ScenePath = "Assets/Scenes/ItemTest.unity";
        private const string MaterialFolder = "Assets/Materials/ItemTest";
        private const string FontPath = "Assets/Fonts/NanumGothic SDF.asset";

        private const float ZoneWidth = 8f;
        private const float ZoneDepth = 12f;
        private const int ZoneCount = 5;

        private static readonly Collider[] OverlapBuffer = new Collider[16];

        [MenuItem("Tools/OverCleaning/ItemTest 씬 만들기")]
        public static void Run()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                if (!AssetDatabase.CopyAsset(SourceScenePath, ScenePath))
                {
                    Debug.LogError("ObjectTest 씬을 복사하지 못했습니다.");
                    return;
                }
            }

            Scene scene = EditorSceneManager.OpenScene(ScenePath);
            SceneManager.SetActiveScene(scene);

            RemoveOldLayout();
            BoxCollider[] zoneFloors = BuildZones(out Transform northWall);
            PlaceInfrastructure(zoneFloors);

            var spots = new List<GrimeSpot>();
            CreateWindowsWithStains(northWall, spots);
            CreateFloorStains(zoneFloors[1], spots);
            GrimeField grimeField = CreateField<GrimeField>("Grime Field", "_spots", spots.ToArray());
            GameObject[] litters = CreateLitters(zoneFloors[3]);
            LitterField litterField = CreateField<LitterField>("Litter Field", "_litters", litters);

            CopyStageObjects(scene, grimeField, litterField);

            RefreshNetworkObjectHashes();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            ReplaceInBuildSettings();
            AssetDatabase.DeleteAsset(SourceScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("ItemTest 씬 세팅 완료.");
        }

        // ---------- 옛 배치 정리 ----------

        private static void RemoveOldLayout()
        {
            string[] names =
            {
                "Test Floor", "Test Obstacle",
                "Test Wall North", "Test Wall South", "Test Wall East", "Test Wall West",
            };
            foreach (string name in names)
            {
                GameObject target = GameObject.Find(name);
                if (target != null)
                    Object.DestroyImmediate(target);
            }
        }

        // ---------- 구역 바닥과 벽 ----------

        private static BoxCollider[] BuildZones(out Transform northWall)
        {
            Color[] zoneColors =
            {
                new Color(0.72f, 0.76f, 0.8f),  // 청소기
                new Color(0.68f, 0.76f, 0.88f), // 물걸레
                new Color(0.72f, 0.84f, 0.88f), // 창문닦이
                new Color(0.86f, 0.8f, 0.64f),  // 빗자루
                new Color(0.86f, 0.72f, 0.72f), // 소파
            };
            string[] zoneLabels = { "청소기", "물걸레", "창문닦이", "빗자루", "소파 (2인)" };

            var ground = new GameObject("Zones");
            var floors = new BoxCollider[ZoneCount];
            for (int index = 0; index < ZoneCount; index++)
            {
                Material material = EnsureMaterial($"Zone{index + 1}", zoneColors[index]);
                GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.name = $"Zone Floor {index + 1}";
                floor.transform.SetParent(ground.transform);
                floor.transform.position = new Vector3(ZoneWidth * (index + 0.5f), -0.1f, ZoneDepth * 0.5f);
                floor.transform.localScale = new Vector3(ZoneWidth, 0.2f, ZoneDepth);
                floor.GetComponent<Renderer>().sharedMaterial = material;
                floors[index] = floor.GetComponent<BoxCollider>();

                CreateLabel(zoneLabels[index],
                    new Vector3(ZoneWidth * (index + 0.5f), 2.4f, ZoneDepth - 2f));
            }

            Material wallMaterial = EnsureMaterial("TestWall", new Color(0.55f, 0.55f, 0.58f));
            float width = ZoneWidth * ZoneCount;
            // 북쪽 벽만 높여 창문을 붙일 자리를 준다. 나머지는 낮아 화면을 가리지 않는다.
            northWall = CreateWall("Wall North", new Vector3(width * 0.5f, 1.1f, ZoneDepth + 0.15f),
                new Vector3(width + 0.6f, 2.2f, 0.3f), wallMaterial);
            CreateWall("Wall South", new Vector3(width * 0.5f, 0.6f, -0.15f),
                new Vector3(width + 0.6f, 1.2f, 0.3f), wallMaterial);
            CreateWall("Wall West", new Vector3(-0.15f, 0.6f, ZoneDepth * 0.5f),
                new Vector3(0.3f, 1.2f, ZoneDepth), wallMaterial);
            CreateWall("Wall East", new Vector3(width + 0.15f, 0.6f, ZoneDepth * 0.5f),
                new Vector3(0.3f, 1.2f, ZoneDepth), wallMaterial);

            // 구역 사이 칸막이. 남쪽에 통로를 남겨 걸어서 오갈 수 있다.
            Material dividerMaterial = EnsureMaterial("TestDivider", new Color(0.8f, 0.8f, 0.82f));
            for (int index = 1; index < ZoneCount; index++)
            {
                CreateWall($"Divider {index}", new Vector3(ZoneWidth * index, 0.5f, 8f),
                    new Vector3(0.3f, 1f, 8f), dividerMaterial);
            }

            return floors;
        }

        private static Transform CreateWall(string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.position = position;
            wall.transform.localScale = scale;
            wall.GetComponent<Renderer>().sharedMaterial = material;
            return wall.transform;
        }

        private static void CreateLabel(string text, Vector3 position)
        {
            var label = new GameObject($"Label {text}");
            label.transform.position = position;
            label.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
            TextMeshPro tmp = label.AddComponent<TextMeshPro>();
            tmp.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            tmp.text = text;
            tmp.fontSize = 8f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.rectTransform.sizeDelta = new Vector2(8f, 2f);
        }

        // ---------- 물려받은 뼈대 재배치 ----------

        private static void PlaceInfrastructure(BoxCollider[] zoneFloors)
        {
            // 스폰은 가운데 통로에서 한다. 어느 구역으로든 바로 걸어갈 수 있다.
            for (int index = 0; index < 4; index++)
            {
                GameObject point = GameObject.Find($"SpawnPoint {index + 1}");
                if (point != null)
                    point.transform.position = new Vector3(17f + index * 2f, 0f, 2f);
            }

            // 1구역: 청소기와 쓰레기통은 ObjectTest 것을 자리만 옮겨 쓴다.
            Vacuum vacuum = Object.FindFirstObjectByType<Vacuum>();
            if (vacuum != null)
                vacuum.transform.SetPositionAndRotation(new Vector3(3f, 0f, 6f), Quaternion.identity);
            TrashCan trashCan = Object.FindFirstObjectByType<TrashCan>();
            if (trashCan != null)
                trashCan.transform.position = new Vector3(6f, trashCan.transform.position.y, 9f);

            // 먼지는 1구역 바닥에만 깔리게 바꾼다. 테스트니 양은 적게.
            DustField dustField = Object.FindFirstObjectByType<DustField>();
            if (dustField != null)
            {
                var serialized = new SerializedObject(dustField);
                SerializedProperty floors = serialized.FindProperty("_floors");
                floors.arraySize = 1;
                floors.GetArrayElementAtIndex(0).objectReferenceValue = zoneFloors[0];
                serialized.FindProperty("_dustCount").intValue = 50;
                serialized.FindProperty("_clustersPerFloor").intValue = 2;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            // 넓은 맵이라 카메라가 따라다녀야 한다. 대상은 스폰 때 NetworkPlayer가 걸어 준다.
            Camera mainCamera = Camera.main;
            if (mainCamera != null && mainCamera.GetComponent<CameraFollow>() == null)
                mainCamera.gameObject.AddComponent<CameraFollow>();
        }

        // ---------- 스테이지에서 도구와 소파 복사 ----------

        private static void CopyStageObjects(Scene scene, GrimeField grimeField, LitterField litterField)
        {
            Scene stage = EditorSceneManager.OpenScene(StageScenePath, OpenSceneMode.Additive);

            CopyFromStage(stage, scene, "Mop", new Vector3(12f, 0f, 6f),
                copy => Rewire(copy.GetComponent<Wiper>(), "_grimeField", grimeField));
            CopyFromStage(stage, scene, "Window Wiper", new Vector3(20f, 0f, 6f),
                copy => Rewire(copy.GetComponent<Wiper>(), "_grimeField", grimeField));
            CopyFromStage(stage, scene, "Broom", new Vector3(28f, 0f, 6f),
                copy => Rewire(copy.GetComponent<Broom>(), "_litterField", litterField));
            CopyFromStage(stage, scene, "Sofa", new Vector3(36f, 0f, 7f), null);

            EditorSceneManager.CloseScene(stage, true);
        }

        private static void CopyFromStage(Scene stage, Scene destination, string name,
            Vector3 position, System.Action<GameObject> configure)
        {
            // 이미 있는지는 이 씬의 루트만 본다. GameObject.Find는 추가로 열어 둔
            // 스테이지 씬의 원본까지 찾아 "이미 있다"고 오판한다.
            foreach (GameObject root in destination.GetRootGameObjects())
            {
                if (root.name == name)
                    return;
            }

            GameObject source = null;
            foreach (GameObject root in stage.GetRootGameObjects())
            {
                if (root.name == name)
                {
                    source = root;
                    break;
                }
            }
            if (source == null)
            {
                Debug.LogWarning($"스테이지 씬에서 '{name}'을 찾지 못했습니다.");
                return;
            }

            GameObject copy = Object.Instantiate(source);
            copy.name = name;
            SceneManager.MoveGameObjectToScene(copy, destination);
            float height = source.transform.position.y;
            copy.transform.position = new Vector3(position.x, height, position.z);
            copy.transform.rotation = Quaternion.identity;
            configure?.Invoke(copy);
        }

        private static void Rewire(Component component, string property, Object value)
        {
            if (component == null)
                return;
            var serialized = new SerializedObject(component);
            serialized.FindProperty(property).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------- 얼룩과 쓰레기 ----------

        private static void CreateWindowsWithStains(Transform northWall, List<GrimeSpot> spots)
        {
            Material pane = LoadCleaningMaterial("WindowPane");
            Material stainMaterial = LoadCleaningMaterial("WindowStain");

            // 3구역(창문닦이) 북쪽 벽에 창문 두 개를 붙인다.
            float[] windowCenters = { 18.5f, 21.5f };
            for (int index = 0; index < windowCenters.Length; index++)
            {
                var window = new GameObject($"Window {index + 1}");
                window.transform.position = new Vector3(
                    windowCenters[index], 1.3f, northWall.position.z - 0.17f);
                window.transform.rotation = Quaternion.identity;

                GameObject paneQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                paneQuad.name = "Pane";
                Object.DestroyImmediate(paneQuad.GetComponent<Collider>());
                paneQuad.transform.SetParent(window.transform, false);
                paneQuad.transform.localScale = new Vector3(1.8f, 1.2f, 1f);
                paneQuad.GetComponent<Renderer>().sharedMaterial = pane;

                CreateStainQuad(window.transform, new Vector3(-0.45f, 0.2f, -0.03f), 0.5f,
                    GrimeKind.Window, stainMaterial, spots);
                CreateStainQuad(window.transform, new Vector3(0.4f, -0.25f, -0.03f), 0.42f,
                    GrimeKind.Window, stainMaterial, spots);
            }
        }

        private static void CreateFloorStains(BoxCollider floor, List<GrimeSpot> spots)
        {
            Material stainMaterial = LoadCleaningMaterial("FloorStain");
            var parent = new GameObject("Floor Stains");
            Vector3 center = floor.bounds.center;
            Vector2[] offsets = { new Vector2(-2f, 2f), new Vector2(1.5f, 3f), new Vector2(-1f, -2f), new Vector2(2f, -0.5f) };
            for (int index = 0; index < offsets.Length; index++)
            {
                var stain = new GameObject($"Floor Stain {index + 1}");
                stain.transform.SetParent(parent.transform);
                stain.transform.position = new Vector3(
                    center.x + offsets[index].x, floor.bounds.max.y + 0.03f, center.z + offsets[index].y);
                stain.transform.rotation = Quaternion.Euler(90f, index * 70f, 0f);
                CreateStainQuad(stain.transform, Vector3.zero, 0.7f, GrimeKind.Floor, stainMaterial, spots);
            }
        }

        private static void CreateStainQuad(Transform parent, Vector3 localPosition, float size,
            GrimeKind kind, Material material, List<GrimeSpot> spots)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = kind == GrimeKind.Window ? "Window Stain" : "Stain";
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = localPosition;
            quad.transform.localScale = new Vector3(size, size, 1f);
            quad.GetComponent<Renderer>().sharedMaterial = material;

            var spot = quad.AddComponent<GrimeSpot>();
            var serialized = new SerializedObject(spot);
            serialized.FindProperty("_kind").enumValueIndex = (int)kind;
            serialized.FindProperty("_renderer").objectReferenceValue = quad.GetComponent<Renderer>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            spots.Add(spot);
        }

        private static GameObject[] CreateLitters(BoxCollider floor)
        {
            Material paper = LoadCleaningMaterial("LitterPaper");
            Material can = LoadCleaningMaterial("LitterCan");
            var parent = new GameObject("Litters");
            var litters = new List<GameObject>();
            Vector3 center = floor.bounds.center;
            Vector2[] offsets =
            {
                new Vector2(-2.2f, 2.5f), new Vector2(0.5f, 3.2f), new Vector2(2.2f, 1.8f),
                new Vector2(-1.5f, -0.8f), new Vector2(1f, -2f), new Vector2(-0.3f, 0.9f),
            };
            for (int index = 0; index < offsets.Length; index++)
            {
                bool isPaper = index % 2 == 0;
                GameObject litter = GameObject.CreatePrimitive(
                    isPaper ? PrimitiveType.Sphere : PrimitiveType.Cylinder);
                litter.name = isPaper ? $"Litter Paper {index + 1}" : $"Litter Can {index + 1}";
                Object.DestroyImmediate(litter.GetComponent<Collider>());
                litter.transform.SetParent(parent.transform);
                litter.transform.localScale = isPaper
                    ? new Vector3(0.22f, 0.18f, 0.22f)
                    : new Vector3(0.14f, 0.11f, 0.14f);
                litter.transform.position = new Vector3(
                    center.x + offsets[index].x,
                    floor.bounds.max.y + (isPaper ? 0.09f : 0.08f),
                    center.z + offsets[index].y);
                litter.transform.rotation = isPaper
                    ? Quaternion.Euler(0f, index * 55f, 0f)
                    : Quaternion.Euler(90f, index * 55f, 0f);
                litter.GetComponent<Renderer>().sharedMaterial = isPaper ? paper : can;
                litters.Add(litter);
            }

            return litters.ToArray();
        }

        // ---------- 공통 도우미 ----------

        private static T CreateField<T>(string name, string arrayProperty, Object[] entries)
            where T : NetworkBehaviour
        {
            var root = new GameObject(name);
            root.AddComponent<NetworkObject>();
            T field = root.AddComponent<T>();
            var serialized = new SerializedObject(field);
            SerializedProperty array = serialized.FindProperty(arrayProperty);
            array.arraySize = entries.Length;
            for (int index = 0; index < entries.Length; index++)
                array.GetArrayElementAtIndex(index).objectReferenceValue = entries[index];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return field;
        }

        private static Material LoadCleaningMaterial(string name)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Materials/Cleaning/{name}.mat");
            if (material == null)
                Debug.LogError($"Assets/Materials/Cleaning/{name}.mat 이 없습니다.");
            return material;
        }

        private static Material EnsureMaterial(string name, Color color)
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder("Assets/Materials", "ItemTest");
            string path = $"{MaterialFolder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;

            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>다른 씬에서 복사해 온 NetworkObject가 원본의 해시를 들고 저장되지 않게 갱신한다.</summary>
        private static void RefreshNetworkObjectHashes()
        {
            MethodInfo onValidate = typeof(NetworkObject).GetMethod(
                "OnValidate", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (NetworkObject networkObject in
                Object.FindObjectsByType<NetworkObject>(FindObjectsSortMode.None))
            {
                onValidate?.Invoke(networkObject, null);
                EditorUtility.SetDirty(networkObject);
            }
        }

        private static void ReplaceInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(entry => entry.path == SourceScenePath || entry.path == ScenePath);
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
