using UnityEngine;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 인게임 IMGUI 공용 스타일. 어두운 둥근 판에 호텔 금색 포인트로 통일한다.
    /// 그림(둥근 모서리)과 스타일은 처음 쓸 때 한 번 만들어 계속 쓴다.
    /// 판은 색을 구워 두고, 막대 채움은 흰 그림을 GUI.color로 물들여 쓴다.
    /// </summary>
    public static class HudStyle
    {
        /// <summary>호텔 금색 포인트. 리치 텍스트에서 키 이름 등을 칠할 때 쓴다.</summary>
        public const string AccentHex = "FFCC6B";

        public static readonly Color Accent = new Color(1f, 0.8f, 0.42f);
        public static readonly Color Good = new Color(0.47f, 0.87f, 0.55f);
        public static readonly Color Bad = new Color(1f, 0.4f, 0.35f);

        private static Texture2D _panelTexture;
        private static Texture2D _fillTexture;
        private static GUIStyle _panel;
        private static GUIStyle _title;
        private static GUIStyle _body;
        private static GUIStyle _hint;
        private static GUIStyle _pill;
        private static GUIStyle _titleCenter;
        private static GUIStyle _bodyCenter;

        public static GUIStyle TitleStyle { get { Ensure(); return _title; } }
        public static GUIStyle BodyStyle { get { Ensure(); return _body; } }
        public static GUIStyle HintStyle { get { Ensure(); return _hint; } }
        public static GUIStyle TitleCenterStyle { get { Ensure(); return _titleCenter; } }
        public static GUIStyle BodyCenterStyle { get { Ensure(); return _bodyCenter; } }

        /// <summary>어두운 둥근 판을 그린다. 내용은 위에 따로 얹는다.</summary>
        public static void Panel(Rect rect)
        {
            Ensure();
            GUI.Label(rect, GUIContent.none, _panel);
        }

        /// <summary>한 줄짜리 알약 판. 안내 문구처럼 가운데 정렬 한 줄에 쓴다.</summary>
        public static void Pill(Rect rect, string text)
        {
            Ensure();
            GUI.Label(rect, text, _pill);
        }

        /// <summary>둥근 진행 막대. 어두운 홈 위에 채움이 차오른다.</summary>
        public static void Bar(Rect rect, float ratio, Color fillColor)
        {
            Ensure();
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(rect, _fillTexture, ScaleMode.StretchToFill, true,
                0f, GUI.color, Vector4.zero, rect.height * 0.5f);
            GUI.color = fillColor;
            float width = Mathf.Max(rect.height, rect.width * Mathf.Clamp01(ratio));
            if (ratio > 0.001f)
                GUI.DrawTexture(new Rect(rect.x, rect.y, width, rect.height), _fillTexture,
                    ScaleMode.StretchToFill, true, 0f, GUI.color, Vector4.zero, rect.height * 0.5f);
            GUI.color = previous;
        }

        private static void Ensure()
        {
            if (_panelTexture != null)
                return;

            _panelTexture = CreateRounded(48, 14, new Color(0.09f, 0.09f, 0.13f, 0.88f));
            _fillTexture = Texture2D.whiteTexture;

            _panel = new GUIStyle
            {
                normal = { background = _panelTexture },
                border = new RectOffset(16, 16, 16, 16),
            };

            _title = new GUIStyle
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                richText = true,
                normal = { textColor = Accent },
            };
            _body = new GUIStyle(_title)
            {
                fontSize = 13,
                fontStyle = FontStyle.Normal,
                normal = { textColor = new Color(1f, 1f, 1f, 0.93f) },
            };
            _hint = new GUIStyle(_body)
            {
                fontSize = 12,
                normal = { textColor = new Color(1f, 1f, 1f, 0.55f) },
            };
            _titleCenter = new GUIStyle(_title) { alignment = TextAnchor.MiddleCenter, fontSize = 20 };
            _bodyCenter = new GUIStyle(_body) { alignment = TextAnchor.MiddleCenter };
            _pill = new GUIStyle(_panel)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                richText = true,
                alignment = TextAnchor.MiddleCenter,
                normal = { background = _panelTexture, textColor = new Color(1f, 1f, 1f, 0.95f) },
            };
        }

        /// <summary>모서리가 둥근 단색 그림. 9분할 테두리로 늘려 쓴다.</summary>
        private static Texture2D CreateRounded(int size, int radius, Color color)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Hud Rounded",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // 네 모퉁이에서 반지름 밖이면 투명, 가장자리 한 픽셀은 부드럽게.
                    float dx = Mathf.Max(0, Mathf.Max(radius - x, x - (size - 1 - radius)));
                    float dy = Mathf.Max(0, Mathf.Max(radius - y, y - (size - 1 - radius)));
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(radius + 0.5f - distance);
                    Color pixel = color;
                    pixel.a *= alpha;
                    pixels[y * size + x] = pixel;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
