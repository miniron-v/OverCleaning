using OverCleaning.Network;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace OverCleaning.EditorTools
{
    /// <summary>
    /// 시작 화면 UI를 현재 열린 씬에 오브젝트로 생성한다.
    /// 런타임 생성이 아니라 에디터에서 한 번 실행해 하이어라키에 남기는 용도다.
    /// </summary>
    public static class StartScreenBuilder
    {
        private const string FontAssetPath = "Assets/Fonts/NanumGothic SDF.asset";

        [MenuItem("OverCleaning/Build Start Screen UI")]
        public static void Build()
        {
            StartScreen startScreen = Object.FindFirstObjectByType<StartScreen>();
            if (startScreen == null)
            {
                EditorUtility.DisplayDialog("시작 화면 UI",
                    "씬에서 StartScreen 컴포넌트를 찾지 못했습니다. Start 씬을 열고 다시 실행하세요.", "확인");
                return;
            }

            // 여러 번 실행해도 중복되지 않도록 이전에 만든 UI를 먼저 지운다.
            if (!TryRemoveExistingUI())
                return;

            Canvas canvas = CreateCanvas();
            RectTransform panel = CreatePanel(canvas.transform);

            CreateLabel(panel, "Title", "OverCleaning", 72f, 96f);
            TMP_Text message = CreateLabel(panel, "Message", "서버에 연결하는 중...", 32f, 48f);
            CreateLabel(panel, "NicknameLabel", "닉네임", 32f, 44f);
            TMP_InputField nicknameField = CreateInputField(panel);
            Button createButton = CreateButton(panel, "CreateRoomButton", "방 만들기");
            Button joinButton = CreateButton(panel, "JoinRoomButton", "방 참가하기");
            Button retryButton = CreateButton(panel, "RetryButton", "다시 시도");
            retryButton.gameObject.SetActive(false);

            CreateEventSystem();
            AssignReferences(startScreen, nicknameField, message, createButton, joinButton, retryButton);

            Undo.RegisterCreatedObjectUndo(canvas.gameObject, "Build Start Screen UI");
            EditorSceneManager.MarkSceneDirty(startScreen.gameObject.scene);
            Selection.activeGameObject = canvas.gameObject;
            Debug.Log("시작 화면 UI를 생성했습니다. 씬을 저장하세요.");
        }

        /// <summary>
        /// 씬에 남아 있는 Canvas와 EventSystem을 지운다.
        /// 지우고 다시 만들어야 입력 모듈 교체 같은 변경이 기존 씬에도 반영된다.
        /// </summary>
        private static bool TryRemoveExistingUI()
        {
            Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            EventSystem[] eventSystems = Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
            if (canvases.Length == 0 && eventSystems.Length == 0)
                return true;

            if (!EditorUtility.DisplayDialog("시작 화면 UI",
                    "기존 Canvas와 EventSystem을 지우고 새로 만듭니다. Inspector에서 직접 수정한 값은 사라집니다.",
                    "다시 만들기", "취소"))
                return false;

            foreach (Canvas canvas in canvases)
                Undo.DestroyObjectImmediate(canvas.gameObject);
            foreach (EventSystem eventSystem in eventSystems)
                Undo.DestroyObjectImmediate(eventSystem.gameObject);
            return true;
        }

        private static void AssignReferences(StartScreen startScreen, TMP_InputField nicknameField,
            TMP_Text message, Button createButton, Button joinButton, Button retryButton)
        {
            SerializedObject serialized = new SerializedObject(startScreen);
            serialized.FindProperty("_nicknameField").objectReferenceValue = nicknameField;
            serialized.FindProperty("_messageText").objectReferenceValue = message;
            serialized.FindProperty("_createRoomButton").objectReferenceValue = createButton;
            serialized.FindProperty("_joinRoomButton").objectReferenceValue = joinButton;
            serialized.FindProperty("_retryButton").objectReferenceValue = retryButton;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Canvas CreateCanvas()
        {
            GameObject canvasObject = new GameObject("Canvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            // 가로세로 어느 쪽이 늘어나도 같은 비중으로 스케일한다.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        private static void CreateEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null)
                return;
            // 이 프로젝트는 Input System 전용이라 StandaloneInputModule로는 클릭이 동작하지 않는다.
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        /// <summary>
        /// 화면 중앙에서 세로로 늘어나는 패널. 자식은 레이아웃 그룹이 배치하므로
        /// 해상도가 바뀌어도 위치를 따로 계산하지 않는다.
        /// </summary>
        private static RectTransform CreatePanel(Transform parent)
        {
            GameObject panelObject = new GameObject("StartPanel",
                typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panelObject.transform.SetParent(parent, false);
            panelObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);

            RectTransform rect = panelObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(760f, 0f);
            rect.anchoredPosition = Vector2.zero;

            VerticalLayoutGroup layout = panelObject.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(48, 48, 48, 48);
            layout.spacing = 20f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            // 자식 높이의 합에 맞춰 패널이 세로로 늘어난다.
            ContentSizeFitter fitter = panelObject.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rect;
        }

        private static TMP_Text CreateLabel(RectTransform parent, string name, string content,
            float fontSize, float height)
        {
            GameObject labelObject = new GameObject(name, typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(parent, false);

            TextMeshProUGUI text = labelObject.GetComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.font = LoadFont();
            AddLayoutHeight(labelObject, height);
            return text;
        }

        private static TMP_InputField CreateInputField(RectTransform parent)
        {
            GameObject fieldObject = new GameObject("NicknameField", typeof(Image), typeof(TMP_InputField));
            fieldObject.transform.SetParent(parent, false);
            fieldObject.GetComponent<Image>().color = Color.white;
            AddLayoutHeight(fieldObject, 72f);

            // TMP 입력 필드는 텍스트가 영역 밖으로 나가지 않도록 뷰포트를 따로 둔다.
            GameObject viewport = new GameObject("TextArea", typeof(RectMask2D));
            viewport.transform.SetParent(fieldObject.transform, false);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            Stretch(viewportRect, new Vector2(20f, 10f), new Vector2(-20f, -10f));

            TextMeshProUGUI placeholder = CreateFieldText(viewport.transform, "Placeholder", "닉네임을 입력하세요");
            placeholder.fontStyle = FontStyles.Italic;
            placeholder.color = new Color(0.45f, 0.45f, 0.45f, 1f);
            TextMeshProUGUI input = CreateFieldText(viewport.transform, "Text", string.Empty);

            TMP_InputField field = fieldObject.GetComponent<TMP_InputField>();
            field.textViewport = viewportRect;
            field.textComponent = input;
            field.placeholder = placeholder;
            field.characterLimit = 12;
            field.lineType = TMP_InputField.LineType.SingleLine;
            return field;
        }

        private static TextMeshProUGUI CreateFieldText(Transform parent, string name, string content)
        {
            GameObject textObject = new GameObject(name, typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);

            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = 34f;
            text.alignment = TextAlignmentOptions.Left;
            text.color = Color.black;
            text.richText = false;
            text.font = LoadFont();
            Stretch(text.rectTransform, Vector2.zero, Vector2.zero);
            return text;
        }

        private static Button CreateButton(RectTransform parent, string name, string label)
        {
            GameObject buttonObject = new GameObject(name, typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            buttonObject.GetComponent<Image>().color = new Color(0.88f, 0.88f, 0.88f, 1f);
            AddLayoutHeight(buttonObject, 84f);

            GameObject labelObject = new GameObject("Text", typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(buttonObject.transform, false);
            TextMeshProUGUI text = labelObject.GetComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = 36f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.black;
            text.font = LoadFont();
            Stretch(text.rectTransform, Vector2.zero, Vector2.zero);

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = buttonObject.GetComponent<Image>();
            return button;
        }

        /// <summary>
        /// 부모 영역을 가득 채우게 한다. 해상도가 바뀌어도 여백만 유지된다.
        /// </summary>
        private static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        /// <summary>
        /// 가로는 레이아웃 그룹이 늘려주고, 세로만 고정 높이를 지정한다.
        /// </summary>
        private static void AddLayoutHeight(GameObject target, float height)
        {
            LayoutElement element = target.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
        }

        private static TMP_FontAsset LoadFont()
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (font == null)
                Debug.LogWarning($"폰트를 찾지 못했습니다: {FontAssetPath}");
            return font;
        }
    }
}
