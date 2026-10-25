using System.Collections.Generic;
using System.Reflection;
using OverCleaning.InGame;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OverCleaning.EditorTools
{
    /// <summary>
    /// 청소 업데이트의 씬 배치를 한 번에 한다. 에디터 없이도 batchmode로 돌릴 수 있다.
    /// 청소기 한 대 복제, 새 도구(물걸레·창문닦이·빗자루) 배치, 벽에 창문과 얼룩,
    /// 바닥에 얼룩과 쓰레기, GrimeField/LitterField 생성과 GameRound 연결까지.
    /// 씬을 저장하므로 NetworkObject의 GlobalObjectIdHash는 Unity가 정식으로 만든다.
    /// 다시 돌려도 이미 만든 것은 건너뛴다.
    /// </summary>
    public static class CleaningUpdateSceneSetup
    {
        private const string ScenePath = "Assets/Scenes/DustCleaningTest.unity";
        private const string MaterialFolder = "Assets/Materials/Cleaning";

        private static readonly Collider[] OverlapBuffer = new Collider[16];

        [MenuItem("Tools/OverCleaning/Cleaning Update 씬 세팅")]
        public static void Run()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath);
            Physics.SyncTransforms();

            Vacuum vacuum = Object.FindFirstObjectByType<Vacuum>();
            GameRound gameRound = Object.FindFirstObjectByType<GameRound>();
            DustField dustField = Object.FindFirstObjectByType<DustField>();
            if (vacuum == null || gameRound == null || dustField == null)
            {
                Debug.LogError("씬에서 Vacuum/GameRound/DustField를 찾지 못했습니다.");
                return;
            }

            Collider[] floors = ReadFloors(dustField);
            Bounds mapBounds = CombineBounds(floors);

            DuplicateVacuum(vacuum);

            GrimeField grimeField = Object.FindFirstObjectByType<GrimeField>();
            LitterField litterField = Object.FindFirstObjectByType<LitterField>();
            var spots = new List<GrimeSpot>();
            if (grimeField == null)
            {
                CreateWindowsWithStains(mapBounds, spots);
                CreateFloorStains(floors, spots);
                grimeField = CreateField<GrimeField>("Grime Field", "_spots", spots.ToArray());
            }

            if (litterField == null)
            {
                GameObject[] litters = CreateLitters(floors);
                litterField = CreateField<LitterField>("Litter Field", "_litters", litters);
            }

            CreateTools(vacuum, grimeField, litterField);

            var round = new SerializedObject(gameRound);
            round.FindProperty("_grimeField").objectReferenceValue = grimeField;
            round.FindProperty("_litterField").objectReferenceValue = litterField;
            round.ApplyModifiedPropertiesWithoutUndo();

            RefreshNetworkObjectHashes();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Cleaning Update 씬 세팅 완료.");
        }

        // ---------- 청소기 복제 ----------

        private static void DuplicateVacuum(Vacuum vacuum)
        {
            if (GameObject.Find("Vacuum 2") != null)
                return;

            GameObject clone = Object.Instantiate(vacuum.gameObject, vacuum.transform.parent);
            clone.name = "Vacuum 2";
            BoxCollider collider = clone.GetComponent<BoxCollider>();
            clone.transform.position = FindFreeSpot(vacuum.transform.position,
                Vector3.Scale(collider.size, clone.transform.lossyScale) * 0.5f,
                collider, 1.2f);
        }

        // ---------- 새 도구 ----------

        private static void CreateTools(Vacuum vacuum, GrimeField grimeField, LitterField litterField)
        {
            Material handle = EnsureOpaqueMaterial("ToolHandle", new Color(0.55f, 0.38f, 0.2f));
            Material mopPad = EnsureOpaqueMaterial("MopPad", new Color(0.25f, 0.45f, 0.8f));
            Material blade = EnsureOpaqueMaterial("WiperBlade", new Color(0.2f, 0.2f, 0.22f));
            Material bristles = EnsureOpaqueMaterial("BroomBristles", new Color(0.85f, 0.7f, 0.3f));

            Vector3 anchor = vacuum.transform.position;

            if (GameObject.Find("Mop") == null)
            {
                GameObject mop = CreateToolRoot("Mop", vacuum,
                    new Vector3(0.45f, 1.1f, 0.8f), new Vector3(0f, 0.55f, 0.2f));
                AddPart(mop, PrimitiveType.Cube, "Mop Pad",
                    new Vector3(0f, 0.06f, 0.45f), Vector3.zero, new Vector3(0.4f, 0.12f, 0.5f), mopPad);
                AddPart(mop, PrimitiveType.Cylinder, "Mop Handle",
                    new Vector3(0f, 0.6f, 0.12f), new Vector3(-20f, 0f, 0f), new Vector3(0.06f, 0.55f, 0.06f), handle);
                Transform wipePoint = AddPoint(mop, "Wipe Point", new Vector3(0f, 0.1f, 0.45f));
                var wiper = mop.AddComponent<Wiper>();
                var serialized = new SerializedObject(wiper);
                serialized.FindProperty("_itemName").stringValue = "물걸레";
                serialized.FindProperty("_targetKind").enumValueIndex = (int)GrimeKind.Floor;
                serialized.FindProperty("_grimeField").objectReferenceValue = grimeField;
                serialized.FindProperty("_wipePoint").objectReferenceValue = wipePoint;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PlaceTool(mop, anchor + new Vector3(-1.5f, 0f, 0f));
            }

            if (GameObject.Find("Window Wiper") == null)
            {
                GameObject squeegee = CreateToolRoot("Window Wiper", vacuum,
                    new Vector3(0.55f, 1.15f, 0.35f), new Vector3(0f, 0.58f, 0.3f));
                AddPart(squeegee, PrimitiveType.Cube, "Wiper Blade",
                    new Vector3(0f, 1.05f, 0.35f), Vector3.zero, new Vector3(0.5f, 0.08f, 0.07f), blade);
                AddPart(squeegee, PrimitiveType.Cylinder, "Wiper Handle",
                    new Vector3(0f, 0.55f, 0.32f), Vector3.zero, new Vector3(0.05f, 0.48f, 0.05f), handle);
                Transform wipePoint = AddPoint(squeegee, "Wipe Point", new Vector3(0f, 1.05f, 0.4f));
                var wiper = squeegee.AddComponent<Wiper>();
                var serialized = new SerializedObject(wiper);
                serialized.FindProperty("_itemName").stringValue = "창문닦이";
                serialized.FindProperty("_targetKind").enumValueIndex = (int)GrimeKind.Window;
                serialized.FindProperty("_grimeField").objectReferenceValue = grimeField;
                serialized.FindProperty("_wipePoint").objectReferenceValue = wipePoint;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PlaceTool(squeegee, anchor + new Vector3(-3f, 0f, 0f));
            }

            if (GameObject.Find("Broom") == null)
            {
                GameObject broom = CreateToolRoot("Broom", vacuum,
                    new Vector3(0.5f, 1.3f, 0.55f), new Vector3(0f, 0.65f, 0.25f));
                AddPart(broom, PrimitiveType.Cube, "Broom Head",
                    new Vector3(0f, 0.15f, 0.42f), Vector3.zero, new Vector3(0.45f, 0.3f, 0.12f), bristles);
                AddPart(broom, PrimitiveType.Cylinder, "Broom Handle",
                    new Vector3(0f, 0.75f, 0.22f), new Vector3(-18f, 0f, 0f), new Vector3(0.05f, 0.55f, 0.05f), handle);
                Transform sweepPoint = AddPoint(broom, "Sweep Point", new Vector3(0f, 0.1f, 0.5f));
                var broomItem = broom.AddComponent<Broom>();
                var serialized = new SerializedObject(broomItem);
                serialized.FindProperty("_litterField").objectReferenceValue = litterField;
                serialized.FindProperty("_sweepPoint").objectReferenceValue = sweepPoint;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PlaceTool(broom, anchor + new Vector3(-4.5f, 0f, 0f));
            }
        }

        /// <summary>청소기와 같은 구성(BoxCollider + NetworkObject + NetworkTransform)의 빈 도구를 만든다.</summary>
        private static GameObject CreateToolRoot(string name, Vacuum vacuum,
            Vector3 colliderSize, Vector3 colliderCenter)
        {
            var root = new GameObject(name);
            root.layer = vacuum.gameObject.layer;

            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.size = colliderSize;
            collider.center = colliderCenter;

            NetworkObject networkObject = root.AddComponent<NetworkObject>();
            EditorUtility.CopySerialized(vacuum.GetComponent<NetworkObject>(), networkObject);

            NetworkTransform networkTransform = root.AddComponent<NetworkTransform>();
            EditorUtility.CopySerialized(vacuum.GetComponent<NetworkTransform>(), networkTransform);
            return root;
        }

        private static void AddPart(GameObject root, PrimitiveType type, string name,
            Vector3 position, Vector3 rotation, Vector3 scale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = position;
            part.transform.localEulerAngles = rotation;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static Transform AddPoint(GameObject root, string name, Vector3 position)
        {
            var point = new GameObject(name);
            point.transform.SetParent(root.transform, false);
            point.transform.localPosition = position;
            return point.transform;
        }

        private static void PlaceTool(GameObject tool, Vector3 preferredPosition)
        {
            BoxCollider collider = tool.GetComponent<BoxCollider>();
            tool.transform.position = preferredPosition;
            Physics.SyncTransforms();
            tool.transform.position = FindFreeSpot(preferredPosition,
                Vector3.Scale(collider.size, tool.transform.lossyScale) * 0.5f, collider, 0.8f);
        }

        // ---------- 창문과 얼룩 ----------

        private static void CreateWindowsWithStains(Bounds mapBounds, List<GrimeSpot> spots)
        {
            Material pane = EnsureTransparentMaterial("WindowPane", new Color(0.6f, 0.8f, 0.95f, 0.35f));
            Material windowStain = EnsureTransparentMaterial("WindowStain", new Color(0.35f, 0.3f, 0.2f, 0.85f));

            List<BoxCollider> walls = FindLargeWalls(2);
            for (int index = 0; index < walls.Count; index++)
            {
                BoxCollider wall = walls[index];
                Bounds bounds = wall.bounds;
                // 벽의 얇은 축이 법선이다. 안쪽(맵 중심 쪽) 면에 창문을 붙인다.
                Vector3 normal = bounds.size.x < bounds.size.z ? Vector3.right : Vector3.forward;
                float side = Vector3.Dot(mapBounds.center - bounds.center, normal) >= 0f ? 1f : -1f;
                normal *= side;
                Vector3 surface = bounds.center + normal * (Vector3.Scale(bounds.extents, normal).magnitude + 0.02f);
                surface.y = 1.4f;

                var window = new GameObject($"Window {index + 1}");
                window.transform.position = surface;
                window.transform.rotation = Quaternion.LookRotation(-normal, Vector3.up);

                GameObject paneQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                paneQuad.name = "Pane";
                Object.DestroyImmediate(paneQuad.GetComponent<Collider>());
                paneQuad.transform.SetParent(window.transform, false);
                paneQuad.transform.localScale = new Vector3(1.6f, 1.1f, 1f);
                paneQuad.GetComponent<Renderer>().sharedMaterial = pane;

                // 창문마다 얼룩 두 개. 창문보다 살짝 앞에 띄워 겹쳐 그리지 않는다.
                CreateStainQuad(window.transform, new Vector3(-0.4f, 0.15f, -0.03f), 0.5f,
                    GrimeKind.Window, windowStain, spots);
                CreateStainQuad(window.transform, new Vector3(0.35f, -0.2f, -0.03f), 0.42f,
                    GrimeKind.Window, windowStain, spots);
            }
        }

        private static void CreateFloorStains(Collider[] floors, List<GrimeSpot> spots)
        {
            Material floorStain = EnsureTransparentMaterial("FloorStain", new Color(0.3f, 0.22f, 0.12f, 0.85f));
            var random = new System.Random(20261018);

            var parent = new GameObject("Floor Stains");
            int created = 0;
            for (int attempt = 0; attempt < 120 && created < 6; attempt++)
            {
                Collider floor = floors[created % floors.Length];
                Bounds bounds = floor.bounds;
                var candidate = new Vector3(
                    Mathf.Lerp(bounds.min.x + 0.6f, bounds.max.x - 0.6f, (float)random.NextDouble()),
                    bounds.max.y + 1f,
                    Mathf.Lerp(bounds.min.z + 0.6f, bounds.max.z - 0.6f, (float)random.NextDouble()));
                if (!floor.Raycast(new Ray(candidate, Vector3.down), out RaycastHit hit, 3f))
                    continue;
                if (IsBlocked(hit.point + Vector3.up * 0.3f, new Vector3(0.45f, 0.28f, 0.45f), floor))
                    continue;

                var stain = new GameObject($"Floor Stain {created + 1}");
                stain.transform.SetParent(parent.transform);
                stain.transform.position = hit.point + Vector3.up * 0.03f;
                stain.transform.rotation = Quaternion.Euler(90f, (float)random.NextDouble() * 360f, 0f);
                CreateStainQuad(stain.transform, Vector3.zero, 0.7f, GrimeKind.Floor, floorStain, spots);
                created++;
            }

            if (created < 6)
                Debug.LogWarning($"바닥 얼룩을 {created}개만 놓았습니다. 자리가 부족합니다.");
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

        // ---------- 쓰레기 ----------

        private static GameObject[] CreateLitters(Collider[] floors)
        {
            Material paper = EnsureOpaqueMaterial("LitterPaper", new Color(0.93f, 0.92f, 0.88f));
            Material can = EnsureOpaqueMaterial("LitterCan", new Color(0.75f, 0.3f, 0.25f));
            var random = new System.Random(20261019);

            var parent = new GameObject("Litters");
            var litters = new List<GameObject>();
            for (int attempt = 0; attempt < 160 && litters.Count < 8; attempt++)
            {
                Collider floor = floors[litters.Count % floors.Length];
                Bounds bounds = floor.bounds;
                var candidate = new Vector3(
                    Mathf.Lerp(bounds.min.x + 0.5f, bounds.max.x - 0.5f, (float)random.NextDouble()),
                    bounds.max.y + 1f,
                    Mathf.Lerp(bounds.min.z + 0.5f, bounds.max.z - 0.5f, (float)random.NextDouble()));
                if (!floor.Raycast(new Ray(candidate, Vector3.down), out RaycastHit hit, 3f))
                    continue;
                if (IsBlocked(hit.point + Vector3.up * 0.2f, new Vector3(0.2f, 0.18f, 0.2f), floor))
                    continue;

                bool isPaper = litters.Count % 2 == 0;
                GameObject litter = GameObject.CreatePrimitive(
                    isPaper ? PrimitiveType.Sphere : PrimitiveType.Cylinder);
                litter.name = isPaper ? $"Litter Paper {litters.Count + 1}" : $"Litter Can {litters.Count + 1}";
                Object.DestroyImmediate(litter.GetComponent<Collider>());
                litter.transform.SetParent(parent.transform);
                litter.transform.localScale = isPaper
                    ? new Vector3(0.22f, 0.18f, 0.22f)
                    : new Vector3(0.14f, 0.11f, 0.14f);
                litter.transform.position = hit.point + Vector3.up * (isPaper ? 0.09f : 0.08f);
                litter.transform.rotation = isPaper
                    ? Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f)
                    : Quaternion.Euler(90f, (float)random.NextDouble() * 360f, 0f);
                litter.GetComponent<Renderer>().sharedMaterial = isPaper ? paper : can;
                litters.Add(litter);
            }

            if (litters.Count < 8)
                Debug.LogWarning($"쓰레기를 {litters.Count}개만 놓았습니다. 자리가 부족합니다.");
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

        private static Collider[] ReadFloors(DustField dustField)
        {
            var serialized = new SerializedObject(dustField);
            SerializedProperty array = serialized.FindProperty("_floors");
            var floors = new List<Collider>();
            for (int index = 0; index < array.arraySize; index++)
            {
                var floor = array.GetArrayElementAtIndex(index).objectReferenceValue as Collider;
                if (floor != null)
                    floors.Add(floor);
            }

            if (floors.Count == 0)
                throw new System.InvalidOperationException("DustField에 등록된 바닥이 없습니다.");
            return floors.ToArray();
        }

        private static Bounds CombineBounds(Collider[] colliders)
        {
            Bounds bounds = colliders[0].bounds;
            foreach (Collider collider in colliders)
                bounds.Encapsulate(collider.bounds);
            return bounds;
        }

        /// <summary>이름이 Wall로 시작하는 Collider 중 면적이 큰 것부터 돌려준다.</summary>
        private static List<BoxCollider> FindLargeWalls(int count)
        {
            var walls = new List<BoxCollider>();
            foreach (BoxCollider collider in Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None))
            {
                if (collider.name.StartsWith("Wall") && collider.bounds.size.y > 1f)
                    walls.Add(collider);
            }

            walls.Sort((left, right) =>
                (right.bounds.size.x + right.bounds.size.z).CompareTo(left.bounds.size.x + left.bounds.size.z));
            if (walls.Count > count)
                walls.RemoveRange(count, walls.Count - count);
            if (walls.Count == 0)
                Debug.LogWarning("창문을 붙일 벽을 찾지 못했습니다.");
            return walls;
        }

        /// <summary>그 자리가 다른 Collider와 겹치는가. 바닥과 자기 자신은 빼고 본다.</summary>
        private static bool IsBlocked(Vector3 center, Vector3 halfExtents, Collider ignored)
        {
            int count = Physics.OverlapBoxNonAlloc(center, halfExtents, OverlapBuffer,
                Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            for (int index = 0; index < count; index++)
            {
                if (OverlapBuffer[index] != ignored && !OverlapBuffer[index].name.Contains("Floor"))
                    return true;
            }

            return false;
        }

        /// <summary>처음 자리부터 주변을 돌며 아무것과도 겹치지 않는 자리를 찾는다.</summary>
        private static Vector3 FindFreeSpot(Vector3 origin, Vector3 halfExtents, BoxCollider self, float step)
        {
            for (int ring = 1; ring <= 6; ring++)
            {
                for (int direction = 0; direction < 8; direction++)
                {
                    Vector3 offset = Quaternion.Euler(0f, direction * 45f, 0f) *
                        Vector3.forward * (ring * step);
                    Vector3 candidate = origin + offset;
                    Vector3 center = candidate + self.center;
                    if (!IsBlocked(center, halfExtents, self))
                        return candidate;
                }
            }

            Debug.LogWarning($"{self.name}의 빈자리를 찾지 못해 원래 자리 옆에 둡니다.");
            return origin + Vector3.forward * step;
        }

        private static Material EnsureOpaqueMaterial(string name, Color color)
        {
            return EnsureMaterial(name, color, false);
        }

        private static Material EnsureTransparentMaterial(string name, Color color)
        {
            return EnsureMaterial(name, color, true);
        }

        private static Material EnsureMaterial(string name, Color color, bool transparent)
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder("Assets/Materials", "Cleaning");
            string path = $"{MaterialFolder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;

            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color);
            if (transparent)
            {
                material.SetFloat("_Surface", 1f);
                material.SetOverrideTag("RenderType", "Transparent");
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                // 얼룩 Quad는 뒤집혀 보여도 되도록 양면으로 그린다.
                material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            }

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>
        /// 복제·새로 만든 NetworkObject의 GlobalObjectIdHash를 저장 전에 갱신해 둔다.
        /// OnValidate가 하는 일과 같다. 복제본이 원본의 해시를 그대로 들고 저장되는 것을 막는다.
        /// </summary>
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
    }
}
