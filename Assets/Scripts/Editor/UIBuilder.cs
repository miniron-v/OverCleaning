using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace OverCleaning.EditorTools
{
    /// <summary>
    /// 화면을 씬에 오브젝트로 만들 때 쓰는 공용 조각들.
    /// 시작 화면과 룸이 같은 생김새를 쓰도록 한곳에 모아둔다.
    /// </summary>
    internal static class UIBuilder
    {
        private const string FontAssetPath = "Assets/Fonts/NanumGothic SDF.asset";

        internal static readonly Color PanelBackground = new Color(0f, 0f, 0f, 0.65f);
        internal static readonly Color DialogBackground = new Color(0.16f, 0.17f, 0.20f, 1f);

        internal static Canvas CreateCanvas()
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

        internal static void CreateEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null)
                return;
            // 이 프로젝트는 Input System 전용이라 StandaloneInputModule로는 클릭이 동작하지 않는다.
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        /// <summary>
        /// 자식 높이의 합에 맞춰 세로로 늘어나는 패널. 자식 위치는 레이아웃 그룹이 정한다.
        /// </summary>
        internal static RectTransform CreatePanel(Transform parent, string name, float width, Color background)
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

            ContentSizeFitter fitter = panelObject.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rect;
        }

        internal static TMP_Text CreateLabel(RectTransform parent, string name, string content,
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

        internal static TMP_InputField CreateInputField(RectTransform parent, string name,
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

        internal static Button CreateButton(RectTransform parent, string name, string label)
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
        internal static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
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
        internal static void AddLayoutHeight(GameObject target, float height)
        {
            LayoutElement element = target.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
        }

        /// <summary>
        /// 씬에 남아 있는 Canvas와 EventSystem을 지운다.
        /// 지우고 다시 만들어야 코드 변경이 기존 씬에도 반영된다.
        /// </summary>
        internal static bool TryRemoveExistingUI()
        {
            Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            EventSystem[] eventSystems = Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
            if (canvases.Length == 0 && eventSystems.Length == 0)
                return true;

            if (!EditorUtility.DisplayDialog("화면 만들기",
                    "기존 Canvas와 EventSystem을 지우고 새로 만듭니다. Inspector에서 직접 수정한 값은 사라집니다.",
                    "다시 만들기", "취소"))
                return false;

            foreach (Canvas canvas in canvases)
                Undo.DestroyObjectImmediate(canvas.gameObject);
            foreach (EventSystem eventSystem in eventSystems)
                Undo.DestroyObjectImmediate(eventSystem.gameObject);
            return true;
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

        private static TMP_FontAsset LoadFont()
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (font == null)
                Debug.LogWarning($"폰트를 찾지 못했습니다: {FontAssetPath}");
            return font;
        }
    }
}
