using OverCleaning.Lobby;
using OverCleaning.Stages;
using OverCleaning.UI;
using TMPro;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace OverCleaning.EditorTools
{
    /// <summary>
    /// 대기방의 단계 선택에 필요한 것을 만든다: 상태를 맞추는 네트워크 오브젝트,
    /// 상호작용할 맵 선택 오브젝트, 우측 모달 화면.
    /// 화면은 룸 UI와 같은 Canvas에 들어가므로 룸 UI를 만들 때 함께 만든다.
    /// </summary>
    internal static class StageSelectBuilder
    {
        private const string CatalogPath = "Assets/Stages/StageCatalog.asset";

        /// <summary>화면 위, 아래, 오른쪽의 같은 여백.</summary>
        private const float ScreenMargin = 40f;
        private const float PanelWidth = 760f;
        private const float ArrowWidth = 64f;

        private static readonly Vector3 SelectObjectPosition = new Vector3(5.5f, 0f, 3f);
        private static readonly Color ModalBackground = new Color(0f, 0f, 0f, 0.35f);
        private static readonly Color SubTextColor = new Color(0.75f, 0.77f, 0.8f, 1f);

        internal static void Build(Canvas canvas)
        {
            StageSelection selection = CreateSelectionIfMissing();
            CreateSelectObjectIfMissing(selection);
            CreateScreen(canvas.transform, selection);
        }

        /// <summary>
        /// 씬에 미리 놓인 NetworkObject는 호스트가 씬을 불러올 때 함께 스폰된다.
        /// 이미 있으면 새로 만들지 않고 스테이지 목록만 다시 맞춘다.
        /// </summary>
        private static StageSelection CreateSelectionIfMissing()
        {
            StageSelection selection = Object.FindAnyObjectByType<StageSelection>();
            if (selection == null)
            {
                GameObject selectionObject = new GameObject("StageSelection",
                    typeof(NetworkObject), typeof(StageSelection));
                Undo.RegisterCreatedObjectUndo(selectionObject, "Build Stage Select");
                selection = selectionObject.GetComponent<StageSelection>();
            }

            StageCatalog catalog = AssetDatabase.LoadAssetAtPath<StageCatalog>(CatalogPath);
            if (catalog == null)
                Debug.LogWarning($"스테이지 목록을 찾지 못했습니다: {CatalogPath}");

            SerializedObject serialized = new SerializedObject(selection);
            serialized.FindProperty("_catalog").objectReferenceValue = catalog;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return selection;
        }

        /// <summary>
        /// 받침대 위에 구슬을 얹은 모양. 구슬 색으로 누가 고르는 중인지 알린다.
        /// 이미 있으면 자리는 그대로 두고 참조만 다시 맞춘다.
        /// </summary>
        private static void CreateSelectObjectIfMissing(StageSelection selection)
        {
            StageSelectObject selectObject = Object.FindAnyObjectByType<StageSelectObject>();
            if (selectObject == null)
            {
                GameObject root = new GameObject("StageSelectObject", typeof(StageSelectObject));
                root.transform.position = SelectObjectPosition;
                Undo.RegisterCreatedObjectUndo(root, "Build Stage Select");

                // 기본 원기둥은 높이가 2라서 세로 0.3배면 높이 0.6이 된다.
                CreatePrimitive(PrimitiveType.Cylinder, "Base", root.transform,
                    new Vector3(0f, 0.3f, 0f), new Vector3(1.2f, 0.3f, 1.2f));
                CreatePrimitive(PrimitiveType.Sphere, "Indicator", root.transform,
                    new Vector3(0f, 0.9f, 0f), Vector3.one * 0.6f);
                selectObject = root.GetComponent<StageSelectObject>();
            }

            Transform indicator = selectObject.transform.Find("Indicator");
            SerializedObject serialized = new SerializedObject(selectObject);
            serialized.FindProperty("_selection").objectReferenceValue = selection;
            serialized.FindProperty("_indicatorRenderer").objectReferenceValue =
                indicator != null ? indicator.GetComponent<Renderer>() : null;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void CreatePrimitive(PrimitiveType type, string name, Transform parent,
            Vector3 localPosition, Vector3 localScale)
        {
            GameObject primitive = GameObject.CreatePrimitive(type);
            primitive.name = name;
            primitive.transform.SetParent(parent, false);
            primitive.transform.localPosition = localPosition;
            primitive.transform.localScale = localScale;
        }

        private static void CreateScreen(Transform canvas, StageSelection selection)
        {
            // 모달이 꺼져 있어도 상태 알림을 받아야 하므로, 켜고 끄는 부분과 컴포넌트를 나눈다.
            GameObject screenObject = new GameObject("StageSelectScreen", typeof(RectTransform),
                typeof(StageSelectPanel));
            screenObject.transform.SetParent(canvas, false);
            UIBuilder.Stretch(screenObject.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero);

            // 화면 전체를 덮어 뒤의 룸 UI를 누르지 못하게 한다.
            GameObject modal = new GameObject("Modal", typeof(Image));
            modal.transform.SetParent(screenObject.transform, false);
            modal.GetComponent<Image>().color = ModalBackground;
            UIBuilder.Stretch(modal.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero);

            RectTransform panel = CreatePanel(modal.transform);

            RectTransform header = UIBuilder.CreateRow(panel, "Header", 64f);
            TMP_Text title = UIBuilder.CreateLabel(header, "Title", "단계 선택", 44f, 64f);
            title.alignment = TextAlignmentOptions.Left;
            title.GetComponent<LayoutElement>().flexibleWidth = 1f;
            Button closeButton = UIBuilder.CreateButton(header, "CloseButton", "닫기");
            SetFixedWidth(closeButton.gameObject, 140f);

            TMP_Text controller = UIBuilder.CreateLabel(panel, "Controller", "OO 님이 고르는 중", 28f, 40f);
            controller.alignment = TextAlignmentOptions.Left;
            controller.color = SubTextColor;

            StagePage page = CreateStagePage(panel);
            Button startButton = UIBuilder.CreateButton(panel, "StartStageButton", "시작하기");

            // 화살표는 레이아웃과 상관없이 패널 세로 가운데의 양 끝에 붙인다.
            Button previousButton = CreateArrow(panel, "PreviousButton", "<", 0f);
            Button nextButton = CreateArrow(panel, "NextButton", ">", 1f);

            SerializedObject serialized = new SerializedObject(screenObject.GetComponent<StageSelectPanel>());
            serialized.FindProperty("_selection").objectReferenceValue = selection;
            serialized.FindProperty("_modalRoot").objectReferenceValue = modal;
            serialized.FindProperty("_controllerText").objectReferenceValue = controller;
            serialized.FindProperty("_closeButton").objectReferenceValue = closeButton;
            serialized.FindProperty("_stageNumberText").objectReferenceValue = page.StageNumber;
            serialized.FindProperty("_stageNameText").objectReferenceValue = page.StageName;
            serialized.FindProperty("_thumbnailImage").objectReferenceValue = page.Thumbnail;
            serialized.FindProperty("_thumbnailPlaceholder").objectReferenceValue = page.ThumbnailPlaceholder;
            serialized.FindProperty("_goalText").objectReferenceValue = page.Goal;
            serialized.FindProperty("_pageText").objectReferenceValue = page.PageNumber;
            serialized.FindProperty("_previousButton").objectReferenceValue = previousButton;
            serialized.FindProperty("_nextButton").objectReferenceValue = nextButton;
            serialized.FindProperty("_startButton").objectReferenceValue = startButton;

            SerializedProperty stars = serialized.FindProperty("_starTexts");
            stars.arraySize = page.Stars.Length;
            for (int index = 0; index < page.Stars.Length; index++)
                stars.GetArrayElementAtIndex(index).objectReferenceValue = page.Stars[index];
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // 편집 중에는 모양을 보며 고칠 수 있게 켜 두고, 실행하면 StageSelectPanel이 끈다.
            modal.SetActive(true);
        }

        /// <summary>
        /// 오른쪽 가운데에 붙어 위, 아래, 오른쪽 여백이 같은 패널.
        /// 세로는 화면을 따라 늘어나고, 가로는 화면의 절반을 넘지 않는다.
        /// </summary>
        private static RectTransform CreatePanel(Transform parent)
        {
            GameObject panelObject = new GameObject("StageSelectPanel",
                typeof(Image), typeof(VerticalLayoutGroup), typeof(ParentWidthLimiter));
            panelObject.transform.SetParent(parent, false);
            panelObject.GetComponent<Image>().color = UIBuilder.DialogBackground;

            RectTransform rect = panelObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(PanelWidth, -ScreenMargin * 2f);
            rect.anchoredPosition = new Vector2(-ScreenMargin, 0f);

            VerticalLayoutGroup layout = panelObject.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(40, 40, 40, 40);
            layout.spacing = 16f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            SerializedObject limiter = new SerializedObject(panelObject.GetComponent<ParentWidthLimiter>());
            limiter.FindProperty("_preferredWidth").floatValue = PanelWidth;
            limiter.FindProperty("_maxParentRatio").floatValue = 0.5f;
            limiter.ApplyModifiedPropertiesWithoutUndo();
            return rect;
        }

        private readonly struct StagePage
        {
            public readonly TMP_Text StageNumber;
            public readonly TMP_Text StageName;
            public readonly Image Thumbnail;
            public readonly GameObject ThumbnailPlaceholder;
            public readonly TMP_Text[] Stars;
            public readonly TMP_Text Goal;
            public readonly TMP_Text PageNumber;

            public StagePage(TMP_Text stageNumber, TMP_Text stageName, Image thumbnail,
                GameObject thumbnailPlaceholder, TMP_Text[] stars, TMP_Text goal, TMP_Text pageNumber)
            {
                StageNumber = stageNumber;
                StageName = stageName;
                Thumbnail = thumbnail;
                ThumbnailPlaceholder = thumbnailPlaceholder;
                Stars = stars;
                Goal = goal;
                PageNumber = pageNumber;
            }
        }

        /// <summary>
        /// 스테이지 한 장. 남는 세로 공간은 썸네일이 차지한다.
        /// 양옆은 화살표 자리만큼 비워 둔다.
        /// </summary>
        private static StagePage CreateStagePage(RectTransform panel)
        {
            GameObject pageObject = new GameObject("StagePage", typeof(VerticalLayoutGroup), typeof(LayoutElement));
            pageObject.transform.SetParent(panel, false);
            pageObject.GetComponent<LayoutElement>().flexibleHeight = 1f;

            VerticalLayoutGroup layout = pageObject.GetComponent<VerticalLayoutGroup>();
            int sidePadding = Mathf.RoundToInt(ArrowWidth);
            layout.padding = new RectOffset(sidePadding, sidePadding, 8, 8);
            layout.spacing = 12f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            RectTransform page = pageObject.GetComponent<RectTransform>();

            TMP_Text stageNumber = UIBuilder.CreateLabel(page, "StageNumber", "STAGE 1", 30f, 40f);
            stageNumber.color = SubTextColor;
            TMP_Text stageName = UIBuilder.CreateLabel(page, "StageName", "스테이지 이름", 48f, 64f);

            GameObject thumbnailObject = new GameObject("Thumbnail", typeof(Image));
            thumbnailObject.transform.SetParent(page, false);
            Image thumbnail = thumbnailObject.GetComponent<Image>();
            thumbnail.preserveAspect = true;
            LayoutElement thumbnailLayout = thumbnailObject.AddComponent<LayoutElement>();
            thumbnailLayout.minHeight = 120f;
            thumbnailLayout.flexibleHeight = 1f;

            TMP_Text placeholder = UIBuilder.CreateLabel(thumbnailObject.GetComponent<RectTransform>(),
                "Placeholder", "썸네일 준비 중", 28f, 40f);
            placeholder.color = SubTextColor;
            UIBuilder.Stretch(placeholder.rectTransform, Vector2.zero, Vector2.zero);

            RectTransform starRow = UIBuilder.CreateRow(page, "Stars", 72f);
            starRow.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            TMP_Text[] stars = new TMP_Text[StageDefinition.MaxStars];
            for (int index = 0; index < stars.Length; index++)
            {
                stars[index] = UIBuilder.CreateLabel(starRow, $"Star {index + 1}", "★", 64f, 72f);
                SetFixedWidth(stars[index].gameObject, 72f);
            }

            TMP_Text goal = UIBuilder.CreateLabel(page, "Goal", "목표: 별 3개를 얻기 위한 목표", 30f, 96f);
            TMP_Text pageNumber = UIBuilder.CreateLabel(page, "PageNumber", "1 / 3", 28f, 40f);
            pageNumber.color = SubTextColor;

            return new StagePage(stageNumber, stageName, thumbnail, placeholder.gameObject, stars, goal,
                pageNumber);
        }

        private static Button CreateArrow(RectTransform panel, string name, string label, float anchorX)
        {
            Button button = UIBuilder.CreateButton(panel, name, label);
            button.GetComponent<LayoutElement>().ignoreLayout = true;

            RectTransform rect = button.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(anchorX, 0.5f);
            rect.anchorMax = new Vector2(anchorX, 0.5f);
            rect.pivot = new Vector2(anchorX, 0.5f);
            rect.sizeDelta = new Vector2(ArrowWidth, 120f);
            // 패널 가장자리에서 조금 안쪽으로 들인다.
            rect.anchoredPosition = new Vector2(anchorX < 0.5f ? 12f : -12f, 0f);
            return button;
        }

        private static void SetFixedWidth(GameObject target, float width)
        {
            LayoutElement element = target.GetComponent<LayoutElement>();
            element.flexibleWidth = 0f;
            element.minWidth = width;
            element.preferredWidth = width;
        }
    }
}
