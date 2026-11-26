using OverCleaning.InGame;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OverCleaning.EditorTools
{
    /// <summary>
    /// 스테이지를 호텔답게 꾸미는 소품을 배치한다. 객실마다 침대·협탁·램프,
    /// 복도에 리셉션 데스크·짐수레·야자수 화분. 전부 프리미티브 조립이고
    /// 네트워크와 무관한 붙박이라 씬에 저장만 하면 된다.
    /// 소파가 있는 거실은 손대지 않고, 자리는 바닥 Collider 경계에서 재며
    /// 기존 가구와 겹치면 옆으로 비켜 놓는다.
    /// 다시 돌리면 이미 놓인 소품을 걷어내고 처음부터 다시 놓는다.
    /// </summary>
    public static class HotelDecorSetup
    {
        private const string ScenePath = "Assets/Scenes/DustCleaningTest.unity";
        private const string MaterialFolder = "Assets/Materials/Hotel";

        private static readonly Collider[] OverlapBuffer = new Collider[16];

        private static Material _darkWood;
        private static Material _linen;
        private static Material _brass;
        private static Material _burgundy;
        private static Material _palm;
        private static Material _terracotta;

        [MenuItem("Tools/OverCleaning/호텔 소품 배치 (다시 놓기)")]
        public static void Run()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath);
            RemoveExisting();
            Physics.SyncTransforms();
            _darkWood = EnsureMaterial("Hotel Dark Wood", new Color(0.32f, 0.22f, 0.15f));
            _linen = EnsureMaterial("Hotel Linen", new Color(0.95f, 0.93f, 0.87f));
            _palm = EnsureMaterial("Hotel Palm", new Color(0.24f, 0.44f, 0.26f));
            _terracotta = EnsureMaterial("Hotel Terracotta", new Color(0.62f, 0.37f, 0.26f));
            _brass = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/Hotel Gold Runner.mat");
            _burgundy = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/Hotel Rug.mat");

            var root = new GameObject("Hotel Decor");

            // 객실 세 칸: 북쪽 벽에 머리를 붙인 침대와 곁의 협탁.
            // 창문 앞을 막으면 창문닦이가 설 자리가 없으므로 창문 x를 피해서 놓는다.
            GameObject northWall = GameObject.Find("Wall (3)");
            float northInnerZ = northWall != null
                ? northWall.transform.position.z - northWall.transform.localScale.z * 0.5f
                : 6f;
            var windowXs = new System.Collections.Generic.List<float>();
            for (int windowIndex = 1; windowIndex <= 3; windowIndex++)
            {
                GameObject window = GameObject.Find($"Window {windowIndex}");
                if (window != null)
                    windowXs.Add(window.transform.position.x);
            }
            // 소파가 놓인 거실은 그대로 둔다. 침대는 나머지 객실에만 놓는다.
            CoopCarriable sofa = Object.FindAnyObjectByType<CoopCarriable>();
            for (int roomIndex = 1; roomIndex <= 3; roomIndex++)
            {
                GameObject floor = GameObject.Find($"DustFloor_Room{roomIndex}");
                if (floor == null || !floor.TryGetComponent(out Collider floorCollider))
                    continue;
                Bounds bounds = floorCollider.bounds;
                if (sofa != null)
                {
                    Vector3 sofaPosition = sofa.transform.position;
                    if (sofaPosition.x >= bounds.min.x && sofaPosition.x <= bounds.max.x &&
                        sofaPosition.z >= bounds.min.z - 1f)
                        continue;
                }

                // 이 방의 창문에서 비켜선 쪽을 먼저 노린다. 창문 앞은 창문닦이의 자리다.
                float preferredX = bounds.center.x;
                foreach (float windowX in windowXs)
                {
                    if (windowX < bounds.min.x || windowX > bounds.max.x)
                        continue;
                    bool roomOnRight = bounds.max.x - windowX > windowX - bounds.min.x;
                    preferredX = windowX + (roomOnRight ? 2.6f : -2.6f);
                }

                Vector3 bedSpot = FindFreeSpot(
                    new Vector3(preferredX, 0f, northInnerZ - 1.35f),
                    new Vector3(0.85f, 0.45f, 1.15f), bounds, windowXs, 2.1f);
                BuildBed(root.transform, bedSpot);

                Vector3 standSpot = FindFreeSpot(
                    bedSpot + new Vector3(1.35f, 0f, 0.75f),
                    new Vector3(0.25f, 0.4f, 0.23f), bounds, windowXs, 1.3f);
                BuildNightstand(root.transform, standSpot);
            }

            // 복도: 리셉션 데스크, 짐수레, 양 끝 야자수.
            GameObject corridor = GameObject.Find("Floor_Corridor");
            if (corridor != null && corridor.TryGetComponent(out Collider corridorCollider))
            {
                Bounds bounds = corridorCollider.bounds;
                float southZ = bounds.min.z + 1.0f;

                BuildReception(root.transform, FindFreeSpot(
                    new Vector3(bounds.center.x + 6.5f, 0f, southZ),
                    new Vector3(1.2f, 0.55f, 0.4f), bounds));
                BuildLuggageCart(root.transform, FindFreeSpot(
                    new Vector3(bounds.center.x + 3.6f, 0f, southZ),
                    new Vector3(0.65f, 0.75f, 0.35f), bounds));
                BuildPalm(root.transform, FindFreeSpot(
                    new Vector3(bounds.min.x + 1.1f, 0f, bounds.center.z),
                    new Vector3(0.26f, 0.65f, 0.26f), bounds));
                BuildPalm(root.transform, FindFreeSpot(
                    new Vector3(bounds.max.x - 1.1f, 0f, southZ),
                    new Vector3(0.26f, 0.65f, 0.26f), bounds));
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("호텔 소품 배치 완료.");
        }

        [MenuItem("Tools/OverCleaning/호텔 소품 제거")]
        public static void Remove()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath);
            if (!RemoveExisting())
            {
                Debug.Log("치울 호텔 소품이 없습니다.");
                return;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("호텔 소품을 치웠습니다.");
        }

        private static bool RemoveExisting()
        {
            GameObject existing = GameObject.Find("Hotel Decor");
            if (existing == null)
                return false;
            Object.DestroyImmediate(existing);
            return true;
        }

        // ---------- 소품 ----------

        private static void BuildBed(Transform parent, Vector3 position)
        {
            GameObject bed = NewProp(parent, "Hotel Bed", position,
                new Vector3(1.7f, 0.95f, 2.3f), new Vector3(0f, 0.48f, 0f));
            AddPart(bed, PrimitiveType.Cube, "Frame",
                new Vector3(0f, 0.17f, 0f), Vector3.zero, new Vector3(1.7f, 0.34f, 2.3f), _darkWood);
            AddPart(bed, PrimitiveType.Cube, "Headboard",
                new Vector3(0f, 0.62f, 1.09f), Vector3.zero, new Vector3(1.7f, 0.9f, 0.12f), _darkWood);
            AddPart(bed, PrimitiveType.Cube, "Mattress",
                new Vector3(0f, 0.45f, -0.04f), Vector3.zero, new Vector3(1.55f, 0.24f, 2.08f), _linen);
            AddPart(bed, PrimitiveType.Cube, "Blanket",
                new Vector3(0f, 0.58f, -0.52f), Vector3.zero, new Vector3(1.6f, 0.08f, 1.1f), _burgundy);
            AddPart(bed, PrimitiveType.Cube, "Pillow Left",
                new Vector3(-0.4f, 0.62f, 0.75f), new Vector3(0f, 0f, 4f), new Vector3(0.62f, 0.15f, 0.42f), _linen);
            AddPart(bed, PrimitiveType.Cube, "Pillow Right",
                new Vector3(0.4f, 0.62f, 0.75f), new Vector3(0f, 0f, -4f), new Vector3(0.62f, 0.15f, 0.42f), _linen);
        }

        private static void BuildNightstand(Transform parent, Vector3 position)
        {
            GameObject stand = NewProp(parent, "Hotel Nightstand", position,
                new Vector3(0.5f, 0.95f, 0.46f), new Vector3(0f, 0.48f, 0f));
            AddPart(stand, PrimitiveType.Cube, "Body",
                new Vector3(0f, 0.26f, 0f), Vector3.zero, new Vector3(0.5f, 0.52f, 0.46f), _darkWood);
            AddPart(stand, PrimitiveType.Cylinder, "Lamp Pole",
                new Vector3(0f, 0.62f, 0f), Vector3.zero, new Vector3(0.05f, 0.09f, 0.05f), _brass);
            AddPart(stand, PrimitiveType.Sphere, "Lamp Shade",
                new Vector3(0f, 0.8f, 0f), Vector3.zero, new Vector3(0.3f, 0.22f, 0.3f), _linen);
        }

        private static void BuildReception(Transform parent, Vector3 position)
        {
            GameObject desk = NewProp(parent, "Hotel Reception", position,
                new Vector3(2.4f, 1.1f, 0.8f), new Vector3(0f, 0.55f, 0f));
            AddPart(desk, PrimitiveType.Cube, "Counter",
                new Vector3(0f, 0.48f, 0f), Vector3.zero, new Vector3(2.2f, 0.96f, 0.68f), _darkWood);
            AddPart(desk, PrimitiveType.Cube, "Top",
                new Vector3(0f, 0.99f, 0f), Vector3.zero, new Vector3(2.4f, 0.07f, 0.8f), _brass);
            AddPart(desk, PrimitiveType.Sphere, "Bell",
                new Vector3(0.75f, 1.1f, 0f), Vector3.zero, new Vector3(0.16f, 0.13f, 0.16f), _brass);
        }

        private static void BuildLuggageCart(Transform parent, Vector3 position)
        {
            GameObject cart = NewProp(parent, "Hotel Luggage Cart", position,
                new Vector3(1.3f, 1.55f, 0.7f), new Vector3(0f, 0.78f, 0f));
            AddPart(cart, PrimitiveType.Cube, "Base",
                new Vector3(0f, 0.1f, 0f), Vector3.zero, new Vector3(1.25f, 0.1f, 0.68f), _darkWood);
            for (int x = -1; x <= 1; x += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    AddPart(cart, PrimitiveType.Cylinder, "Post",
                        new Vector3(x * 0.58f, 0.78f, z * 0.3f), Vector3.zero,
                        new Vector3(0.05f, 0.68f, 0.05f), _brass);
                }
            }
            AddPart(cart, PrimitiveType.Cylinder, "Top Bar Left",
                new Vector3(-0.58f, 1.46f, 0f), new Vector3(90f, 0f, 0f), new Vector3(0.05f, 0.3f, 0.05f), _brass);
            AddPart(cart, PrimitiveType.Cylinder, "Top Bar Right",
                new Vector3(0.58f, 1.46f, 0f), new Vector3(90f, 0f, 0f), new Vector3(0.05f, 0.3f, 0.05f), _brass);
            AddPart(cart, PrimitiveType.Cube, "Bag Big",
                new Vector3(-0.15f, 0.38f, 0f), new Vector3(0f, 8f, 0f), new Vector3(0.6f, 0.44f, 0.34f), _burgundy);
            AddPart(cart, PrimitiveType.Cube, "Bag Small",
                new Vector3(0.28f, 0.33f, 0.05f), new Vector3(0f, -12f, 0f), new Vector3(0.42f, 0.32f, 0.3f),
                AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/Hotel Room Carpet.mat"));
        }

        private static void BuildPalm(Transform parent, Vector3 position)
        {
            GameObject palm = NewProp(parent, "Hotel Palm", position,
                new Vector3(0.55f, 1.35f, 0.55f), new Vector3(0f, 0.68f, 0f));
            AddPart(palm, PrimitiveType.Cylinder, "Pot",
                new Vector3(0f, 0.17f, 0f), Vector3.zero, new Vector3(0.52f, 0.17f, 0.52f), _terracotta);
            AddPart(palm, PrimitiveType.Cylinder, "Trunk",
                new Vector3(0f, 0.62f, 0f), Vector3.zero, new Vector3(0.12f, 0.32f, 0.12f), _darkWood);
            for (int index = 0; index < 4; index++)
            {
                float angle = index * 90f + 45f;
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                AddPart(palm, PrimitiveType.Sphere, "Leaf",
                    new Vector3(direction.x * 0.22f, 1.02f, direction.z * 0.22f),
                    new Vector3(18f * direction.z, angle, -18f * direction.x),
                    new Vector3(0.2f, 0.1f, 0.55f), _palm);
            }
            AddPart(palm, PrimitiveType.Sphere, "Crown",
                new Vector3(0f, 1.08f, 0f), Vector3.zero, new Vector3(0.28f, 0.18f, 0.28f), _palm);
        }

        // ---------- 도우미 ----------

        private static GameObject NewProp(Transform parent, string name, Vector3 position,
            Vector3 colliderSize, Vector3 colliderCenter)
        {
            var prop = new GameObject(name);
            prop.transform.SetParent(parent);
            prop.transform.position = position;
            BoxCollider collider = prop.AddComponent<BoxCollider>();
            collider.size = colliderSize;
            collider.center = colliderCenter;
            return prop;
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
            if (material != null)
                part.GetComponent<Renderer>().sharedMaterial = material;
        }

        /// <summary>
        /// 그 자리가 기존 가구와 겹치면 좌우로 비켜 가며 빈자리를 찾는다.
        /// 바닥·러그·매트는 깔개라 장애물로 치지 않고, 피할 x(창문 앞)도 받는다.
        /// </summary>
        private static Vector3 FindFreeSpot(Vector3 preferred, Vector3 halfExtents, Bounds floorBounds,
            System.Collections.Generic.List<float> avoidXs = null, float avoidDistance = 0f)
        {
            float[] offsets = { 0f, 0.9f, -0.9f, 1.8f, -1.8f, 2.7f, -2.7f };
            foreach (float offset in offsets)
            {
                Vector3 candidate = preferred + new Vector3(offset, 0f, 0f);
                if (candidate.x - halfExtents.x < floorBounds.min.x + 0.2f ||
                    candidate.x + halfExtents.x > floorBounds.max.x - 0.2f)
                    continue;
                if (avoidXs != null && IsNearAny(candidate.x, avoidXs, avoidDistance))
                    continue;
                Vector3 center = candidate + Vector3.up * (halfExtents.y + 0.05f);
                if (!OverlapsAnything(center, halfExtents))
                    return candidate;
            }

            return preferred;
        }

        /// <summary>
        /// 그 자리에 무엇이든 서 있는지 본다. 장식 가구(장롱·사이드보드)는 Collider가
        /// 없어 물리 검사에 안 걸리므로, Renderer 경계로도 함께 본다.
        /// 바닥·깔개·얼룩·먼지 같은 깔린 것들은 장애물로 치지 않는다.
        /// </summary>
        private static bool OverlapsAnything(Vector3 center, Vector3 halfExtents)
        {
            int count = Physics.OverlapBoxNonAlloc(center, halfExtents, OverlapBuffer,
                Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            for (int index = 0; index < count; index++)
            {
                if (!IsIgnorableName(OverlapBuffer[index].name))
                    return true;
            }

            var box = new Bounds(center, halfExtents * 2f);
            foreach (Renderer blocker in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (IsIgnorableName(blocker.gameObject.name) ||
                    IsIgnorableName(blocker.transform.root.gameObject.name))
                    continue;
                if (box.Intersects(blocker.bounds))
                    return true;
            }

            return false;
        }

        private static readonly string[] IgnorableNames =
        {
            "Floor", "Rug", "Doormat", "Grass", "Yard", "Stain", "Pane",
            "Dust", "Sparkle", "Puff", "Label", "Tree", "Bush", "Stone",
        };

        private static bool IsIgnorableName(string name)
        {
            foreach (string keyword in IgnorableNames)
            {
                if (name.Contains(keyword))
                    return true;
            }

            return false;
        }

        private static bool IsNearAny(float x, System.Collections.Generic.List<float> targets, float distance)
        {
            foreach (float target in targets)
            {
                if (Mathf.Abs(x - target) < distance)
                    return true;
            }

            return false;
        }

        private static Material EnsureMaterial(string name, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;

            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
