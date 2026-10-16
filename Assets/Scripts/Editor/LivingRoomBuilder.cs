using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

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

            BuildLivingRoom(root);
            BuildStudyCorner(root);
            BuildDiningRoom(root);
            BuildCorridor(root);

            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            Selection.activeGameObject = root.gameObject;
            Debug.Log("거실 가구를 만들었습니다. 씬을 저장하세요.");
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

        /// <summary>복도. 스폰 자리와 청소기, 쓰레기통을 피해 동쪽 끝에 콘솔만 둔다.</summary>
        private static void BuildCorridor(Transform root)
        {
            Material oak = GetMaterial("Oak", new Color(0.54f, 0.42f, 0.3f));
            Box(root, "Console Table", new Vector3(10.1f, 0.35f, -4f), new Vector3(0.55f, 0.7f, 1.6f), oak);
            Plant(root, "Corridor Plant", new Vector3(10.1f, 0f, -5.7f));
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
