using OverCleaning.Network;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace OverCleaning.EditorTools
{
    /// <summary>
    /// 시작 화면 UI를 현재 열린 씬에 오브젝트로 생성한다.
    /// 런타임 생성이 아니라 에디터에서 한 번 실행해 하이어라키에 남기는 용도다.
    /// </summary>
    public static class StartScreenBuilder
    {
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

            if (!UIBuilder.TryRemoveExistingUI())
                return;

            Canvas canvas = UIBuilder.CreateCanvas();

            RectTransform panel = UIBuilder.CreatePanel(canvas.transform, "StartPanel", 760f,
                UIBuilder.PanelBackground);
            UIBuilder.CreateLabel(panel, "Title", "OverCleaning", 72f, 96f);
            TMP_Text message = UIBuilder.CreateLabel(panel, "Message", "서버에 연결하는 중...", 32f, 48f);
            TMP_InputField nicknameField = UIBuilder.CreateInputField(panel, "NicknameField",
                "닉네임을 입력하세요", 12);
            Button createButton = UIBuilder.CreateButton(panel, "CreateRoomButton", "방 만들기");
            Button joinButton = UIBuilder.CreateButton(panel, "JoinRoomButton", "방 참가하기");
            Button retryButton = UIBuilder.CreateButton(panel, "RetryButton", "다시 시도");
            retryButton.gameObject.SetActive(false);

            // 방 코드 입력은 참가를 누른 뒤에만 보여준다.
            GameObject joinPanel = CreateJoinPanel(canvas.transform,
                out TMP_InputField roomCodeField, out Button confirmButton, out Button cancelButton);
            joinPanel.SetActive(false);

            UIBuilder.CreateEventSystem();
            CreateNetworkManager();
            AssignReferences(startScreen, nicknameField, roomCodeField, message,
                createButton, joinButton, retryButton, joinPanel, confirmButton, cancelButton);

            Undo.RegisterCreatedObjectUndo(canvas.gameObject, "Build Start Screen UI");
            EditorSceneManager.MarkSceneDirty(startScreen.gameObject.scene);
            Selection.activeGameObject = canvas.gameObject;
            Debug.Log("시작 화면 UI를 생성했습니다. 씬을 저장하세요.");
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
            UIBuilder.Stretch(overlay.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero);

            // 어두운 겹침 배경 위에 올라가므로 불투명한 배경을 줘야 창처럼 보인다.
            RectTransform panel = UIBuilder.CreatePanel(overlay.transform, "JoinRoomPanel", 700f,
                UIBuilder.DialogBackground);
            UIBuilder.CreateLabel(panel, "JoinTitle", "방 코드 입력", 48f, 68f);
            roomCodeField = UIBuilder.CreateInputField(panel, "RoomCodeField", "예: ABC123", 8);
            // 방 코드는 영문과 숫자로만 이루어진다. 대소문자는 참가할 때 맞춘다.
            roomCodeField.characterValidation = TMP_InputField.CharacterValidation.Alphanumeric;
            confirmButton = UIBuilder.CreateButton(panel, "ConfirmJoinButton", "참가");
            cancelButton = UIBuilder.CreateButton(panel, "CancelJoinButton", "취소");
            return overlay;
        }

        /// <summary>
        /// Relay 연결은 NetworkManager.Singleton을 통해 시작되므로 방을 만들기 전에 씬에 있어야 한다.
        /// 씬을 넘어가도 살아남도록 Netcode가 스스로 DontDestroyOnLoad 처리한다.
        /// </summary>
        private static void CreateNetworkManager()
        {
            // 이미 있으면 그대로 두되 설정은 다시 맞춘다. 건너뛰면 예전 값이 그대로 남는다.
            NetworkManager manager = Object.FindFirstObjectByType<NetworkManager>();
            if (manager == null)
            {
                GameObject managerObject = new GameObject("NetworkManager",
                    typeof(NetworkManager), typeof(UnityTransport));
                manager = managerObject.GetComponent<NetworkManager>();
            }

            if (manager.GetComponent<UnityTransport>() == null)
                manager.gameObject.AddComponent<UnityTransport>();

            SerializedObject serialized = new SerializedObject(manager);
            // 호스트가 씬을 바꾸면 참가자도 같은 씬으로 따라오게 한다.
            serialized.FindProperty("NetworkConfig.EnableSceneManagement").boolValue = true;
            // 접속하자마자 시작 화면에 플레이어가 생기지 않도록, 스폰은 룸에서 직접 한다.
            serialized.FindProperty("NetworkConfig.AutoSpawnPlayerPrefabClientSide").boolValue = false;
            serialized.FindProperty("NetworkConfig.NetworkTransport").objectReferenceValue =
                manager.GetComponent<UnityTransport>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(manager);
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
    }
}
