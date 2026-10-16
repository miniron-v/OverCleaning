using OverCleaning.InGame;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace OverCleaning.EditorTools
{
    /// <summary>
    /// 인게임 맵을 거실처럼 꾸미는 가구를 만든다. 가운데 방은 소파와 TV가 있는 거실,
    /// 왼쪽 방은 책장과 안락의자가 있는 서재 코너, 오른쪽 방은 식탁이 있는 다이닝이다.
    ///
    /// 가구는 색을 입힌 상자라서 따로 모델이 없어도 분위기를 잡고, Collider가 있어
    /// 먼지 생성과 청소기 배치 검사에 저절로 장애물로 잡힌다. 러그나 쿠션처럼
    /// 밟고 지나가야 자연스러운 장식에는 Collider를 두지 않는다.
    ///
    /// 다시 실행하면 기존 가구를 지우고 새로 만든다. 배치를 고친 뒤 재실행해도 된다.
    /// </summary>
    public static class LivingRoomBuilder
    {
        private const string FurnitureRootName = "Furniture";
        private const string MaterialFolder = "Assets/Materials/Furniture";

        [MenuItem("OverCleaning/Build Living Room Furniture")]
        public static void Build()
        {
            GameObject map = GameObject.Find("Map");
            if (map == null)
            {
                Debug.LogError("Map 오브젝트가 없습니다. 인게임 씬에서 실행하세요.");
                return;
            }

            Transform previous = map.transform.Find(FurnitureRootName);
            if (previous != null)
                Object.DestroyImmediate(previous.gameObject);

            Transform root = new GameObject(FurnitureRootName).transform;
            root.SetParent(map.transform, false);

            RefineShell();
            TuneLighting();
            BuildFinishes();
            BuildYard(root);
            BuildLivingRoom(root);
            BuildStudyCorner(root);
            BuildDiningRoom(root);
            BuildCorridor(root);
            RestyleVacuum();
            RestyleTrashCan();

            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            Selection.activeGameObject = root.gameObject;
            Debug.Log("거실 가구를 만들었습니다. 씬을 저장하세요.");
        }

        /// <summary>
        /// 바깥벽을 얇게 깎고 벽 밑으로 삐져나온 바닥 가장자리를 다듬는다.
        /// 벽의 안쪽 면 자리는 그대로 두므로 플레이 영역과 판정은 변하지 않는다.
        /// </summary>
        private static void RefineShell()
        {
            const float thickness = 0.35f;

            // 바깥벽: 안쪽 면(±10.5, ±6)을 고정한 채 바깥쪽으로만 얇아진다.
            SetTransform("Wall (2)", new Vector3(0f, 1f, -6f - thickness * 0.5f), new Vector3(21.7f, 2f, thickness));
            SetTransform("Wall (3)", new Vector3(0f, 1f, 6f + thickness * 0.5f), new Vector3(21.7f, 2f, thickness));
            SetTransform("Wall", new Vector3(-10.5f - thickness * 0.5f, 1f, 0f), new Vector3(thickness, 2f, 12.7f));
            SetTransform("Wall (1)", new Vector3(10.5f + thickness * 0.5f, 1f, 0f), new Vector3(thickness, 2f, 12.7f));

            // 바닥: 벽 바깥면까지만 깔리게 다듬는다. 두꺼운 벽 밑까지 뻗어 있던 가장자리가
            // 밖에서 흰 테두리처럼 보였다.
            SetTransform("Floor_Corridor", new Vector3(0f, 0f, -3.675f), new Vector3(21.7f, 5.35f, 1f));
            SetTransform("DustFloor_Room1", new Vector3(-7.175f, 0f, 2.675f), new Vector3(7.35f, 7.35f, 1f));
            SetTransform("DustFloor_Room2", new Vector3(0f, 0f, 2.675f), new Vector3(7f, 7.35f, 1f));
            SetTransform("DustFloor_Room3", new Vector3(7.175f, 0f, 2.675f), new Vector3(7.35f, 7.35f, 1f));
        }

        /// <summary>
        /// 오후 햇살 느낌으로 빛을 맞춘다. 해를 비스듬히 기울여 가구 그림자가 지게 하고,
        /// 순백 과노출 대신 따뜻한 색과 낮춘 세기를 쓴다. 바닥에 깔린 먼지가 잘 보이도록
        /// 주변광은 밝은 중간톤으로 둔다.
        /// </summary>
        private static void TuneLighting()
        {
            GameObject lightObject = GameObject.Find("Directional Light");
            if (lightObject != null && lightObject.TryGetComponent(out Light sun))
            {
                lightObject.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
                sun.color = new Color(1f, 0.95f, 0.87f);
                sun.intensity = 1.2f;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.72f;
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.78f, 0.77f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.62f, 0.59f, 0.54f);
            RenderSettings.ambientGroundColor = new Color(0.45f, 0.41f, 0.37f);

            // 잔디 너머 지평선. 검은 허공 대신 옅은 하늘빛을 보여 준다.
            Camera camera = Camera.main;
            if (camera != null)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.66f, 0.74f, 0.78f);
            }
        }

        /// <summary>집 둘레의 마당. 잔디와 나무, 덤불로 바깥 허공을 채운다. 전부 장식이다.</summary>
        private static void BuildYard(Transform root)
        {
            Transform yard = new GameObject("Yard").transform;
            yard.SetParent(root, false);

            Material grass = GetMaterial("Yard Grass", new Color(0.42f, 0.55f, 0.36f));
            Material trunk = GetMaterial("Tree Trunk", new Color(0.4f, 0.3f, 0.22f));
            Material leaf = GetMaterial("Tree Leaf", new Color(0.27f, 0.42f, 0.24f));
            Material stone = GetMaterial("Yard Stone", new Color(0.62f, 0.62f, 0.58f));

            Box(yard, "Grass", new Vector3(0f, -0.06f, 0f), new Vector3(90f, 0.1f, 60f), grass, false);

            foreach (Vector3 position in new[]
            {
                new Vector3(-15.5f, 0f, 8.5f), new Vector3(-17.5f, 0f, -3f), new Vector3(-13.5f, 0f, -9.5f),
                new Vector3(15.5f, 0f, 9f), new Vector3(18f, 0f, -1.5f), new Vector3(14f, 0f, -10f),
                new Vector3(-5f, 0f, 11f), new Vector3(7.5f, 0f, 10.5f),
            })
                Tree(yard, position, trunk, leaf);

            foreach (Vector3 position in new[]
            {
                new Vector3(-12.5f, 0f, 3f), new Vector3(-12.8f, 0f, -6.5f), new Vector3(12.6f, 0f, 4.5f),
                new Vector3(12.4f, 0f, -7f), new Vector3(-2f, 0f, 8.5f), new Vector3(3.5f, 0f, 8.8f),
                new Vector3(-16f, 0f, 2f), new Vector3(16.5f, 0f, 6f),
            })
            {
                GameObject bush = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                bush.name = "Bush";
                bush.transform.SetParent(yard, false);
                bush.transform.localPosition = position + Vector3.up * 0.3f;
                bush.transform.localScale = new Vector3(0.9f, 0.6f, 0.9f);
                bush.GetComponent<Renderer>().sharedMaterial = leaf;
                Object.DestroyImmediate(bush.GetComponent<Collider>());
            }

            foreach (Vector3 position in new[]
            {
                new Vector3(-13.2f, 0f, 0.5f), new Vector3(13.4f, 0f, 1.2f), new Vector3(0.5f, 0f, 9.3f),
            })
            {
                GameObject rock = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                rock.name = "Stone";
                rock.transform.SetParent(yard, false);
                rock.transform.localPosition = position + Vector3.up * 0.03f;
                rock.transform.localScale = new Vector3(0.7f, 0.04f, 0.7f);
                rock.GetComponent<Renderer>().sharedMaterial = stone;
                Object.DestroyImmediate(rock.GetComponent<Collider>());
            }
        }

        /// <summary>마당 나무 한 그루. 기둥과 수풀 공이다.</summary>
        private static void Tree(Transform yard, Vector3 floorPosition, Material trunk, Material leaf)
        {
            Shape(yard, "Tree Trunk", PrimitiveType.Cylinder, floorPosition + Vector3.up * 0.8f,
                new Vector3(0.35f, 0.8f, 0.35f), Quaternion.identity, trunk);
            Shape(yard, "Tree Leaves", PrimitiveType.Sphere, floorPosition + Vector3.up * 2f,
                new Vector3(2.2f, 1.9f, 2.2f), Quaternion.identity, leaf);
        }

        private static void SetTransform(string objectName, Vector3 position, Vector3 scale)
        {
            GameObject target = GameObject.Find(objectName);
            if (target == null)
            {
                Debug.LogWarning($"'{objectName}'을 찾지 못해 크기를 고치지 못했습니다.");
                return;
            }
            target.transform.localPosition = position;
            target.transform.localScale = scale;
        }

        /// <summary>
        /// 바닥을 원목 마루로, 벽을 크림 벽지 톤으로 바꾼다. 파티션은 세이지로 투톤을 준다.
        /// 벽은 단색만 쓴다. 플레이 중 WallFader가 반투명 셰이더로 갈아 끼우며 색만 가져가서,
        /// 무늬를 입혀도 그때 사라진다. 벽 전용 머티리얼이 생기므로 다른 물건과
        /// 머티리얼을 나눠 쓰던 문제도 함께 풀린다.
        /// </summary>
        private static void BuildFinishes()
        {
            Material roomFloor = GetWoodMaterial("Wood Floor Room", new Vector2(3.75f, 4f), Color.white);
            Material corridorFloor = GetWoodMaterial("Wood Floor Corridor", new Vector2(11.5f, 3f),
                new Color(0.86f, 0.82f, 0.78f));
            AssignMaterial("DustFloor_Room1", roomFloor);
            AssignMaterial("DustFloor_Room2", roomFloor);
            AssignMaterial("DustFloor_Room3", roomFloor);
            AssignMaterial("Floor_Corridor", corridorFloor);

            Material cream = GetMaterial("Wall Cream", new Color(0.91f, 0.88f, 0.81f));
            Material sage = GetMaterial("Wall Sage", new Color(0.72f, 0.76f, 0.68f));
            foreach (string wall in new[]
            {
                "Wall", "Wall (1)", "Wall (2)", "Wall (3)",
                "Room1 Door Left", "Room1 Door Right", "Room2 Door Left", "Room2 Door Right",
                "Room3 Door Left", "Room3 Door Right",
            })
                AssignMaterial(wall, cream);
            AssignMaterial("Partition Left", sage);
            AssignMaterial("Partition Right", sage);
        }

        /// <summary>
        /// 청소기를 캐니스터형으로 다시 그린다. Collider와 흡입구는 그대로 두고
        /// 겉모습만 바꾸며, 상태색을 보여 주는 노즐 Renderer를 새 노즐로 이어 준다.
        /// </summary>
        private static void RestyleVacuum()
        {
            GameObject vacuumObject = GameObject.Find("Vacuum");
            if (vacuumObject == null)
                return;
            ClearVisualChildren(vacuumObject.transform, "Suction Point");

            Material red = GetMaterial("Vacuum Red", new Color(0.66f, 0.26f, 0.24f));
            Material dark = GetMaterial("Appliance Dark", new Color(0.2f, 0.21f, 0.23f));
            Material light = GetMaterial("Nozzle Light", new Color(0.86f, 0.86f, 0.86f));
            Transform parent = vacuumObject.transform;

            // 눕힌 캡슐 몸통과 바퀴. 끄는 캐니스터 청소기 모양이다.
            Shape(parent, "Vacuum Body", PrimitiveType.Capsule, new Vector3(0f, 0.42f, 0.8f),
                new Vector3(0.6f, 0.5f, 0.62f), Quaternion.Euler(90f, 0f, 0f), red);
            Shape(parent, "Vacuum Body Stripe", PrimitiveType.Cube, new Vector3(0f, 0.63f, 0.8f),
                new Vector3(0.5f, 0.05f, 0.72f), Quaternion.identity, dark);
            Shape(parent, "Vacuum Wheel Left", PrimitiveType.Cylinder, new Vector3(-0.34f, 0.2f, 0.55f),
                new Vector3(0.4f, 0.05f, 0.4f), Quaternion.Euler(0f, 0f, 90f), dark);
            Shape(parent, "Vacuum Wheel Right", PrimitiveType.Cylinder, new Vector3(0.34f, 0.2f, 0.55f),
                new Vector3(0.4f, 0.05f, 0.4f), Quaternion.Euler(0f, 0f, 90f), dark);
            Shape(parent, "Vacuum Caster", PrimitiveType.Sphere, new Vector3(0f, 0.12f, 0.35f),
                new Vector3(0.24f, 0.24f, 0.24f), Quaternion.identity, dark);
            Shape(parent, "Vacuum Handle", PrimitiveType.Cube, new Vector3(0f, 0.8f, 0.42f),
                new Vector3(0.26f, 0.05f, 0.34f), Quaternion.Euler(-35f, 0f, 0f), dark);
            Shape(parent, "Vacuum Pipe", PrimitiveType.Cube, new Vector3(0f, 0.3f, 1.18f),
                new Vector3(0.14f, 0.09f, 0.52f), Quaternion.Euler(40f, 0f, 0f), dark);
            GameObject nozzle = Shape(parent, "Vacuum Nozzle", PrimitiveType.Cube,
                new Vector3(0f, 0.1f, 1.38f), new Vector3(0.62f, 0.16f, 0.28f), Quaternion.identity, light);

            Vacuum vacuum = vacuumObject.GetComponent<Vacuum>();
            if (vacuum != null)
            {
                SerializedObject serialized = new SerializedObject(vacuum);
                serialized.FindProperty("_nozzleRenderer").objectReferenceValue =
                    nozzle.GetComponent<Renderer>();
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>쓰레기통을 페달 달린 원통으로 다시 그린다. Collider는 그대로 둔다.</summary>
        private static void RestyleTrashCan()
        {
            GameObject binObject = GameObject.Find("Trash Can");
            if (binObject == null)
                return;
            ClearVisualChildren(binObject.transform);

            Material green = GetMaterial("Bin Green", new Color(0.37f, 0.45f, 0.4f));
            Material rim = GetMaterial("Bin Rim", new Color(0.26f, 0.32f, 0.28f));
            Material dark = GetMaterial("Appliance Dark", new Color(0.2f, 0.21f, 0.23f));
            Transform parent = binObject.transform;

            Shape(parent, "Bin Body", PrimitiveType.Cylinder, new Vector3(0f, 0.6f, 0f),
                new Vector3(1f, 0.6f, 1f), Quaternion.identity, green);
            Shape(parent, "Bin Rim", PrimitiveType.Cylinder, new Vector3(0f, 1.22f, 0f),
                new Vector3(1.08f, 0.05f, 1.08f), Quaternion.identity, rim);
            Shape(parent, "Bin Opening", PrimitiveType.Cylinder, new Vector3(0f, 1.27f, 0f),
                new Vector3(0.85f, 0.012f, 0.85f), Quaternion.identity, dark);
            Shape(parent, "Bin Pedal", PrimitiveType.Cube, new Vector3(0f, 0.09f, 0.56f),
                new Vector3(0.3f, 0.16f, 0.16f), Quaternion.identity, dark);
            Shape(parent, "Bin Handle Left", PrimitiveType.Cube, new Vector3(-0.54f, 0.95f, 0f),
                new Vector3(0.08f, 0.22f, 0.2f), Quaternion.identity, rim);
            Shape(parent, "Bin Handle Right", PrimitiveType.Cube, new Vector3(0.54f, 0.95f, 0f),
                new Vector3(0.08f, 0.22f, 0.2f), Quaternion.identity, rim);
        }

        /// <summary>남길 이름만 빼고 자식을 지운다. 겉모습을 새로 그리기 전에 부른다.</summary>
        private static void ClearVisualChildren(Transform parent, string keepName = null)
        {
            for (int index = parent.childCount - 1; index >= 0; index--)
            {
                Transform child = parent.GetChild(index);
                if (child.name != keepName)
                    Object.DestroyImmediate(child.gameObject);
            }
        }

        /// <summary>장식 도형. 겉모습일 뿐이므로 Collider는 두지 않는다.</summary>
        private static GameObject Shape(Transform parent, string name, PrimitiveType type,
            Vector3 position, Vector3 scale, Quaternion rotation, Material material)
        {
            GameObject shape = GameObject.CreatePrimitive(type);
            shape.name = name;
            shape.transform.SetParent(parent, false);
            shape.transform.localPosition = position;
            shape.transform.localRotation = rotation;
            shape.transform.localScale = scale;
            shape.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(shape.GetComponent<Collider>());
            return shape;
        }

        private static void AssignMaterial(string objectName, Material material)
        {
            GameObject target = GameObject.Find(objectName);
            if (target == null)
            {
                Debug.LogWarning($"'{objectName}'을 찾지 못해 머티리얼을 입히지 못했습니다.");
                return;
            }
            target.GetComponent<Renderer>().sharedMaterial = material;
        }

        /// <summary>원목 마루 텍스처를 입힌 바닥 머티리얼. 바닥 크기에 맞게 타일링한다.</summary>
        private static Material GetWoodMaterial(string name, Vector2 tiling, Color tint)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;

            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", tint);
            material.SetTexture("_BaseMap",
                AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Furniture/WoodFloor.png"));
            material.SetTextureScale("_BaseMap", tiling);
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder("Assets/Materials", "Furniture");
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>가운데 방. 북쪽 벽에 소파를 붙이고 남쪽 문가 벽에 TV를 마주 보게 둔다.</summary>
        private static void BuildLivingRoom(Transform root)
        {
            Material navy = GetMaterial("Sofa Navy", new Color(0.25f, 0.33f, 0.47f));
            Material walnut = GetMaterial("Walnut", new Color(0.42f, 0.29f, 0.2f));
            Material beige = GetMaterial("Rug Beige", new Color(0.85f, 0.8f, 0.72f));
            Material yellow = GetMaterial("Cushion Yellow", new Color(0.91f, 0.76f, 0.35f));
            Material coral = GetMaterial("Cushion Coral", new Color(0.85f, 0.48f, 0.42f));
            Material screen = GetMaterial("TV Screen", new Color(0.13f, 0.13f, 0.14f));

            Box(root, "Rug", new Vector3(0f, 0.005f, 3.2f), new Vector3(4.6f, 0.01f, 3.4f), beige, false);

            Box(root, "Sofa Seat", new Vector3(0f, 0.25f, 5.3f), new Vector3(3f, 0.5f, 1.1f), navy);
            Box(root, "Sofa Back", new Vector3(0f, 0.75f, 5.78f), new Vector3(3f, 0.7f, 0.25f), navy);
            Box(root, "Sofa Arm Left", new Vector3(-1.65f, 0.55f, 5.3f), new Vector3(0.3f, 0.6f, 1.1f), navy);
            Box(root, "Sofa Arm Right", new Vector3(1.65f, 0.55f, 5.3f), new Vector3(0.3f, 0.6f, 1.1f), navy);
            Box(root, "Cushion Left", new Vector3(-0.7f, 0.59f, 5.45f), new Vector3(0.55f, 0.18f, 0.5f), yellow, false);
            Box(root, "Cushion Right", new Vector3(0.7f, 0.59f, 5.45f), new Vector3(0.55f, 0.18f, 0.5f), coral, false);

            Box(root, "Coffee Table", new Vector3(0f, 0.2f, 3.2f), new Vector3(1.6f, 0.4f, 0.9f), walnut);

            // 남쪽 문가 벽(문 왼쪽 구간) 앞. 소파에서 마주 보인다.
            Box(root, "TV Stand", new Vector3(-2.2f, 0.3f, -0.45f), new Vector3(2f, 0.6f, 0.5f), walnut);
            Box(root, "TV", new Vector3(-2.2f, 1.1f, -0.55f), new Vector3(1.6f, 0.9f, 0.08f), screen, false);

            Plant(root, "Living Plant", new Vector3(3f, 0f, 5.5f));
        }

        /// <summary>왼쪽 방. 책장과 안락의자, 스탠드 조명으로 서재 코너를 만든다.</summary>
        private static void BuildStudyCorner(Transform root)
        {
            Material oak = GetMaterial("Oak", new Color(0.54f, 0.42f, 0.3f));
            Material walnut = GetMaterial("Walnut", new Color(0.42f, 0.29f, 0.2f));
            Material green = GetMaterial("Chair Green", new Color(0.29f, 0.42f, 0.31f));
            Material dark = GetMaterial("Lamp Dark", new Color(0.23f, 0.23f, 0.24f));
            Material shade = GetMaterial("Lamp Shade", new Color(0.95f, 0.87f, 0.65f));

            Box(root, "Bookshelf Left", new Vector3(-9f, 0.9f, 5.65f), new Vector3(1.3f, 1.8f, 0.55f), oak);
            Box(root, "Bookshelf Right", new Vector3(-7.6f, 0.9f, 5.65f), new Vector3(1.3f, 1.8f, 0.55f), oak);

            Box(root, "Armchair Seat", new Vector3(-9.6f, 0.25f, 2.5f), new Vector3(0.9f, 0.5f, 0.9f), green);
            Box(root, "Armchair Back", new Vector3(-9.98f, 0.7f, 2.5f), new Vector3(0.22f, 0.6f, 0.9f), green);
            Box(root, "Side Table", new Vector3(-8.5f, 0.2f, 2.5f), new Vector3(0.6f, 0.4f, 0.6f), walnut);

            // 넘어다닐 수 있게 받침에만 Collider를 두고 기둥과 갓은 장식으로 둔다.
            Box(root, "Lamp Base", new Vector3(-9.7f, 0.03f, 4.2f), new Vector3(0.35f, 0.06f, 0.35f), dark);
            Box(root, "Lamp Pole", new Vector3(-9.7f, 0.85f, 4.2f), new Vector3(0.08f, 1.6f, 0.08f), dark, false);
            Box(root, "Lamp Shade", new Vector3(-9.7f, 1.75f, 4.2f), new Vector3(0.45f, 0.35f, 0.45f), shade, false);

            Plant(root, "Study Plant", new Vector3(-4.2f, 0f, 5.6f));
        }

        /// <summary>오른쪽 방. 식탁과 의자, 수납장으로 다이닝을 만든다.</summary>
        private static void BuildDiningRoom(Transform root)
        {
            Material walnut = GetMaterial("Walnut", new Color(0.42f, 0.29f, 0.2f));
            Material oak = GetMaterial("Oak", new Color(0.54f, 0.42f, 0.3f));
            Material coral = GetMaterial("Cushion Coral", new Color(0.85f, 0.48f, 0.42f));
            Material teal = GetMaterial("Chair Teal", new Color(0.37f, 0.63f, 0.6f));

            Box(root, "Dining Table", new Vector3(7.1f, 0.38f, 3.4f), new Vector3(2.2f, 0.76f, 1.3f), walnut);

            Box(root, "Dining Chair 1", new Vector3(6.4f, 0.25f, 2.45f), new Vector3(0.5f, 0.5f, 0.5f), coral);
            Box(root, "Dining Chair 2", new Vector3(7.8f, 0.25f, 2.45f), new Vector3(0.5f, 0.5f, 0.5f), teal);
            Box(root, "Dining Chair 3", new Vector3(6.4f, 0.25f, 4.35f), new Vector3(0.5f, 0.5f, 0.5f), teal);
            Box(root, "Dining Chair 4", new Vector3(7.8f, 0.25f, 4.35f), new Vector3(0.5f, 0.5f, 0.5f), coral);

            Box(root, "Sideboard", new Vector3(10.15f, 0.45f, 4.5f), new Vector3(0.55f, 0.9f, 2.2f), oak);

            Plant(root, "Dining Plant", new Vector3(4.2f, 0f, 5.5f));
        }

        /// <summary>
        /// 복도. 스폰 자리와 청소기, 쓰레기통을 피해 동쪽 끝에 콘솔만 두고,
        /// 긴 러너 러그와 방마다 도어매트를 깔아 집 복도 느낌을 준다.
        /// </summary>
        private static void BuildCorridor(Transform root)
        {
            Material oak = GetMaterial("Oak", new Color(0.54f, 0.42f, 0.3f));
            Box(root, "Console Table", new Vector3(10.1f, 0.35f, -4f), new Vector3(0.55f, 0.7f, 1.6f), oak);
            Plant(root, "Corridor Plant", new Vector3(10.1f, 0f, -5.7f));

            Material runner = GetMaterial("Runner Red", new Color(0.59f, 0.34f, 0.31f));
            Box(root, "Runner Rug", new Vector3(1f, 0.004f, -4f), new Vector3(14f, 0.008f, 1.5f), runner, false);

            Material teal = GetMaterial("Chair Teal", new Color(0.37f, 0.63f, 0.6f));
            Material yellow = GetMaterial("Cushion Yellow", new Color(0.91f, 0.76f, 0.35f));
            Material coral = GetMaterial("Cushion Coral", new Color(0.85f, 0.48f, 0.42f));
            Box(root, "Doormat Room1", new Vector3(-7f, 0.006f, -1.45f), new Vector3(1.6f, 0.012f, 0.7f), teal, false);
            Box(root, "Doormat Room2", new Vector3(0f, 0.006f, -1.45f), new Vector3(1.6f, 0.012f, 0.7f), yellow, false);
            Box(root, "Doormat Room3", new Vector3(7f, 0.006f, -1.45f), new Vector3(1.6f, 0.012f, 0.7f), coral, false);
        }

        /// <summary>화분. 통에만 Collider를 두고 수풀은 장식이다.</summary>
        private static void Plant(Transform root, string name, Vector3 floorPosition)
        {
            Material terracotta = GetMaterial("Pot Terracotta", new Color(0.69f, 0.42f, 0.29f));
            Material leaf = GetMaterial("Plant Green", new Color(0.31f, 0.49f, 0.26f));

            Box(root, $"{name} Pot", floorPosition + Vector3.up * 0.3f, new Vector3(0.45f, 0.6f, 0.45f), terracotta);
            GameObject foliage = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            foliage.name = $"{name} Leaves";
            foliage.transform.SetParent(root, false);
            foliage.transform.localPosition = floorPosition + Vector3.up * 0.95f;
            foliage.transform.localScale = new Vector3(0.8f, 0.7f, 0.8f);
            foliage.GetComponent<Renderer>().sharedMaterial = leaf;
            Object.DestroyImmediate(foliage.GetComponent<Collider>());
        }

        private static GameObject Box(Transform root, string name, Vector3 center, Vector3 size,
            Material material, bool withCollider = true)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(root, false);
            box.transform.localPosition = center;
            box.transform.localScale = size;
            box.GetComponent<Renderer>().sharedMaterial = material;
            if (!withCollider)
                Object.DestroyImmediate(box.GetComponent<Collider>());
            return box;
        }

        /// <summary>색만 다른 단색 머티리얼. 같은 이름이 이미 있으면 그대로 쓴다.</summary>
        private static Material GetMaterial(string name, Color color)
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder("Assets/Materials", "Furniture");

            string path = $"{MaterialFolder}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;

            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
