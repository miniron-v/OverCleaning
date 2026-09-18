using OverCleaning.Network;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace OverCleaning.EditorTools
{
    /// <summary>
    /// 대기방 UI를 현재 열린 씬에 오브젝트로 생성한다.
    /// </summary>
    public static class RoomScreenBuilder
    {
        [MenuItem("OverCleaning/Build Room Screen UI")]
        public static void Build()
        {
            if (!UIBuilder.TryRemoveExistingUI())
                return;

            // 시작 화면과 달리 룸은 씬에 미리 둘 것이 없어 진입점까지 여기서 만든다.
            RoomScreen roomScreen = Object.FindFirstObjectByType<RoomScreen>();
            if (roomScreen == null)
                roomScreen = new GameObject("RoomScreen", typeof(RoomScreen)).GetComponent<RoomScreen>();

            Canvas canvas = UIBuilder.CreateCanvas();
            RectTransform panel = UIBuilder.CreatePanel(canvas.transform, "RoomPanel", 760f,
                UIBuilder.PanelBackground);

            // 코드와 복사 버튼을 한 줄에 나란히 둔다.
            RectTransform codeRow = CreateRow(panel, "RoomCodeRow", 72f);
            TMP_Text roomCode = UIBuilder.CreateLabel(codeRow, "RoomCode", "방 코드: ------", 52f, 72f);
            Button copyButton = UIBuilder.CreateButton(codeRow, "CopyCodeButton", "복사");
            // 라벨이 남는 폭을 모두 쓰고, 버튼은 제 크기만 차지하게 한다.
            roomCode.GetComponent<LayoutElement>().flexibleWidth = 1f;
            LayoutElement copyLayout = copyButton.GetComponent<LayoutElement>();
            copyLayout.flexibleWidth = 0f;
            copyLayout.preferredWidth = 140f;

            TMP_Text playerList = UIBuilder.CreateLabel(panel, "PlayerList", "참가자를 불러오는 중...", 32f, 220f);
            playerList.alignment = TextAlignmentOptions.TopLeft;

            Button startButton = UIBuilder.CreateButton(panel, "StartGameButton", "게임 시작");
            Button leaveButton = UIBuilder.CreateButton(panel, "LeaveButton", "나가기");

            UIBuilder.CreateEventSystem();
            CreatePlayerSpawner();
            AssignReferences(roomScreen, roomCode, copyButton, playerList, startButton, leaveButton);

            Undo.RegisterCreatedObjectUndo(canvas.gameObject, "Build Room Screen UI");
            EditorSceneManager.MarkSceneDirty(roomScreen.gameObject.scene);
            Selection.activeGameObject = canvas.gameObject;
            Debug.Log("룸 UI를 생성했습니다. 씬을 저장하세요.");
        }

        /// <summary>
        /// 룸에서 플레이어를 만들 스포너와 스폰 지점을 둔다.
        /// 지점을 나눠두지 않으면 참가자가 모두 한 자리에 겹친다.
        /// </summary>
        private static void CreatePlayerSpawner()
        {
            if (Object.FindFirstObjectByType<PlayerSpawner>() != null)
                return;

            GameObject spawnerObject = new GameObject("PlayerSpawner", typeof(PlayerSpawner));
            Transform[] points = new Transform[GameSession.MaxPlayers];
            for (int index = 0; index < points.Length; index++)
            {
                GameObject point = new GameObject($"SpawnPoint {index + 1}");
                point.transform.SetParent(spawnerObject.transform, false);
                // 바닥 가운데를 기준으로 가로로 늘어세운다.
                float offset = (index - (points.Length - 1) * 0.5f) * 2f;
                point.transform.position = new Vector3(offset, 0f, 0f);
                points[index] = point.transform;
            }

            SerializedObject serialized =
                new SerializedObject(spawnerObject.GetComponent<PlayerSpawner>());
            SerializedProperty spawnPoints = serialized.FindProperty("_spawnPoints");
            spawnPoints.arraySize = points.Length;
            for (int index = 0; index < points.Length; index++)
                spawnPoints.GetArrayElementAtIndex(index).objectReferenceValue = points[index];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 자식을 가로로 늘어놓는 한 줄. 세로 레이아웃 안에 가로 배치를 넣을 때 쓴다.
        /// </summary>
        private static RectTransform CreateRow(RectTransform parent, string name, float height)
        {
            GameObject rowObject = new GameObject(name, typeof(HorizontalLayoutGroup));
            rowObject.transform.SetParent(parent, false);

            HorizontalLayoutGroup layout = rowObject.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 16f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            UIBuilder.AddLayoutHeight(rowObject, height);
            return rowObject.GetComponent<RectTransform>();
        }

        private static void AssignReferences(RoomScreen roomScreen, TMP_Text roomCode, Button copyButton,
            TMP_Text playerList, Button startButton, Button leaveButton)
        {
            SerializedObject serialized = new SerializedObject(roomScreen);
            serialized.FindProperty("_roomCodeText").objectReferenceValue = roomCode;
            serialized.FindProperty("_copyCodeButton").objectReferenceValue = copyButton;
            serialized.FindProperty("_playerListText").objectReferenceValue = playerList;
            serialized.FindProperty("_startGameButton").objectReferenceValue = startButton;
            serialized.FindProperty("_leaveButton").objectReferenceValue = leaveButton;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
