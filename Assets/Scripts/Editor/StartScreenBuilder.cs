using OverCleaning.Network;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
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

        private static readonly Color PanelBackground = new Color(0f, 0f, 0f, 0.65f);
        private static readonly Color DialogBackground = new Color(0.16f, 0.17f, 0.20f, 1f);

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

            RectTransform panel = CreatePanel(canvas.transform, "StartPanel", 760f, PanelBackground);
            CreateLabel(panel, "Title", "OverCleaning", 72f, 96f);
            TMP_Text message = CreateLabel(panel, "Message", "서버에 연결하는 중...", 32f, 48f);
            TMP_InputField nicknameField = CreateInputField(panel, "NicknameField", "닉네임을 입력하세요", 12);
            Button createButton = CreateButton(panel, "CreateRoomButton", "방 만들기");
            Button joinButton = CreateButton(panel, "JoinRoomButton", "방 참가하기");
            Button retryButton = CreateButton(panel, "RetryButton", "다시 시도");
            retryButton.gameObject.SetActive(false);

            // 방 코드 입력은 참가를 누른 뒤에만 보여준다.
            GameObject joinPanel = CreateJoinPanel(canvas.transform,
                out TMP_InputField roomCodeField, out Button confirmButton, out Button cancelButton);
            joinPanel.SetActive(false);

            CreateEventSystem();
            CreateNetworkManager();
            AssignReferences(startScreen, nicknameField, roomCodeField, message,
                createButton, joinButton, retryButton, joinPanel, confirmButton, cancelButton);

            Undo.RegisterCreatedObjectUndo(canvas.gameObject, "Build Start Screen UI");
            EditorSceneManager.MarkSceneDirty(startScreen.gameObject.scene);
            Selection.activeGameObject = canvas.gameObject;
            Debug.Log("시작 화면 UI를 생성했습니다. 씬을 저장하세요.");
        }

        /// <summary>
        /// Relay 연결은 NetworkManager.Singleton을 통해 시작되므로 방을 만들기 전에 씬에 있어야 한다.
        /// 씬을 넘어가도 살아남도록 Netcode가 스스로 DontDestroyOnLoad 처리한다.
        /// </summary>
        private static void CreateNetworkManager()
        {
            if (Object.FindFirstObjectByType<NetworkManager>() != null)
                return;

            GameObject managerObject = new GameObject("NetworkManager",
                typeof(NetworkManager), typeof(UnityTransport));
            NetworkManager manager = managerObject.GetComponent<NetworkManager>();

            SerializedObject serialized = new SerializedObject(manager);
            // 씬 전환은 세션 상태에 맞춰 직접 처리한다.
            serialized.FindProperty("NetworkConfig.EnableSceneManagement").boolValue = false;
            serialized.FindProperty("NetworkConfig.NetworkTransport").objectReferenceValue =
                managerObject.GetComponent<UnityTransport>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
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
            TMP_InputField roomCodeField, TMP_Text message, Button createButton, Button joinButton,
            Button retryButton, GameObject joinPanel, Button confirmButton, Button cancelButton)
        {
            SerializedObject serialized = new SerializedObject(startScreen);
            serialized.FindProperty("_nicknameField").objectReferenceValue = nicknameField;
            serialized.FindProperty("_roomCodeField").objectReferenceValue = roomCodeField;
            serialized.FindProperty("_messageText").objectReferenceValue = message;
            serialized.FindProperty("_createRoomButton").objectReferenceValue = createButton;
            serialized.FindProperty("_joinRoomButton").objectReferenceValue = joinButton;
            serialized.FindProperty("_retryButton").objectReferenceValue = retryButton;
            serialized.FindProperty("_joinRoomPanel").objectReferenceValue = joinPanel;
            serialized.FindProperty("_confirmJoinButton").objectReferenceValue = confirmButton;
            serialized.FindProperty("_cancelJoinButton").objectReferenceValue = cancelButton;
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
        private static RectTransform CreatePanel(Transform parent, string name, float width, Color background)
        {
            GameObject panelObject = new GameObject(name,
                typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panelObject.transform.SetParent(parent, false);
            panelObject.GetComponent<Image>().color = background;

            RectTransform rect = panelObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, 0f);
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

        /// <summary>
        /// 방 코드를 입력받는 겹침 화면. 뒤쪽 버튼이 눌리지 않도록 화면 전체를 덮는다.
        /// </summary>
        private static GameObject CreateJoinPanel(Transform parent, out TMP_InputField roomCodeField,
            out Button confirmButton, out Button cancelButton)
        {
            GameObject overlay = new GameObject("JoinRoomOverlay", typeof(Image));
            overlay.transform.SetParent(parent, false);
            overlay.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);
            Stretch(overlay.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero);

            // 어두운 겹침 배경 위에 올라가므로 불투명한 배경을 줘야 창처럼 보인다.
            RectTransform panel = CreatePanel(overlay.transform, "JoinRoomPanel", 700f, DialogBackground);
            CreateLabel(panel, "JoinTitle", "방 코드 입력", 48f, 68f);
            roomCodeField = CreateInputField(panel, "RoomCodeField", "예: ABC123", 8);
            // 방 코드는 영문과 숫자로만 이루어진다. 대소문자는 참가할 때 맞춘다.
            roomCodeField.characterValidation = TMP_InputField.CharacterValidation.Alphanumeric;
            confirmButton = CreateButton(panel, "ConfirmJoinButton", "참가");
            cancelButton = CreateButton(panel, "CancelJoinButton", "취소");
            return overlay;
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

        private static TMP_InputField CreateInputField(RectTransform parent, string name,
            string placeholderText, int characterLimit)
        {
            GameObject fieldObject = new GameObject(name, typeof(Image), typeof(TMP_InputField));
            fieldObject.transform.SetParent(parent, false);
            fieldObject.GetComponent<Image>().color = Color.white;
            AddLayoutHeight(fieldObject, 72f);

            // TMP 입력 필드는 텍스트가 영역 밖으로 나가지 않도록 뷰포트를 따로 둔다.
            GameObject viewport = new GameObject("TextArea", typeof(RectMask2D));
            viewport.transform.SetParent(fieldObject.transform, false);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            Stretch(viewportRect, new Vector2(20f, 10f), new Vector2(-20f, -10f));

            TextMeshProUGUI placeholder = CreateFieldText(viewport.transform, "Placeholder", placeholderText);
            placeholder.fontStyle = FontStyles.Italic;
            placeholder.color = new Color(0.45f, 0.45f, 0.45f, 1f);
            TextMeshProUGUI input = CreateFieldText(viewport.transform, "Text", string.Empty);

            TMP_InputField field = fieldObject.GetComponent<TMP_InputField>();
            field.textViewport = viewportRect;
            field.textComponent = input;
            field.placeholder = placeholder;
            field.characterLimit = characterLimit;
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
