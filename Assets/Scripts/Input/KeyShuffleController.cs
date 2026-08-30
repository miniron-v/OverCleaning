using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OverCleaning.Input
{
    /// <summary>
    /// 스테이지 시작 시 이동/상호작용 키를 랜덤 셔플하고, 필요 시 기본 배치로 복원한다.
    /// 실제 셔플 계산과 바인딩 적용은 KeyShuffle이 맡고, 이 컴포넌트는 Unity 관련만 담당한다:
    /// 인스펙터 설정 읽기, 액션 조회, 셔플 트리거.
    ///
    /// 자리(slot)는 자리 카탈로그 맵(_slotMapName)의 액션들이 정의한다. 그 액션 하나가 자리 하나이며,
    /// 거기 묶인 바인딩 전부가 그 자리의 물리 키다(키보드 키 + 게임패드 버튼 등).
    /// 자리 카탈로그 맵은 절대 Enable하지 않는다 — 바인딩의 원본 정보만 담는다.
    /// </summary>
    public sealed class KeyShuffleController : MonoBehaviour
    {
        /// <summary>
        /// 금지 규칙 한 묶음. 여기 적힌 액션들이 '모두' 기본과 같은 배정은 버려진다.
        /// 묶음 중 일부만 제자리인 것은 허용된다. Unity가 중첩 배열을 직렬화하지 못해 클래스로 감싼다.
        /// </summary>
        [Serializable]
        private sealed class ForbiddenGroup
        {
            [Tooltip("이 액션들이 전부 기본 값과 같으면 그 배정을 버린다. 개수 제한은 없다.")]
            public string[] ActionNames;
        }

        [Tooltip("셔플 대상 InputActionAsset을 참조하는 PlayerInput. 비워두면 같은 오브젝트에서 찾는다.")]
        [SerializeField] private PlayerInput _playerInput;

        [Tooltip("실제로 플레이 중 사용하는 액션 맵의 이름.")]
        [SerializeField] private string _actionMapName = "Player";

        [Tooltip("자리(물리 키)를 정의하는 자리 카탈로그 맵의 이름. 이 맵은 실행 중 활성화되지 않는다.")]
        [SerializeField] private string _slotMapName = "KeySlots";

        [Tooltip("셔플 대상 액션 이름. 이 맵의 다른 액션(예: Pause)은 셔플되지 않는다. " +
                 "각 액션의 기본 자리는 바인딩을 자리 카탈로그와 대조해 자동으로 찾으므로 나열 순서는 무관하다.")]
        [SerializeField]
        private string[] _actionNames =
        {
            "MoveUp", "MoveDown", "MoveLeft", "MoveRight", "Interact",
        };

        [Tooltip("기본 배치가 그대로 남는 것을 막는 규칙. " +
                 "기본값은 상하 한 쌍과 좌우 한 쌍으로, 원래 십자 배치의 축이 통째로 보존되는 것을 막는다.")]
        [SerializeField]
        private ForbiddenGroup[] _forbiddenGroups =
        {
            new ForbiddenGroup { ActionNames = new[] { "MoveUp", "MoveDown" } },
            new ForbiddenGroup { ActionNames = new[] { "MoveLeft", "MoveRight" } },
        };

        [Tooltip("셔플 후 입력 상태를 초기화할 대상. 비워두면 같은 오브젝트에서 찾는다.")]
        [SerializeField] private PlayerMovement _playerMovement;

        [Tooltip("에디터/개발 빌드 전용. 1키로 셔플, 2키로 복원.")]
        [SerializeField] private bool _enableDebugKeys = true;

        private InputActionMap _actionMap;
        private InputAction[] _actions;
        private InputAction[] _slots;

        /// <summary>defaultAssignment[액션] = 그 액션의 기본 자리 인덱스. 원복과 금지 규칙 판정의 기준.</summary>
        private int[] _defaultAssignment;

        /// <summary>_forbiddenGroups의 이름을 액션 인덱스로 해석해 둔 것. 게임 중 변하지 않으므로 1회만 만든다.</summary>
        private int[][] _resolvedForbiddenGroups;

        private readonly System.Random _rng = new System.Random();

        private void Awake()
        {
            if (_playerInput == null)
                _playerInput = GetComponent<PlayerInput>();
            if (_playerInput == null)
                throw new InvalidOperationException(
                    $"{nameof(PlayerInput)}가 지정되지 않았고 이 오브젝트에서도 찾을 수 없습니다.");

            if (_playerMovement == null)
                _playerMovement = GetComponent<PlayerMovement>();

            InputActionAsset asset = _playerInput.actions;
            _actionMap = asset.FindActionMap(_actionMapName, throwIfNotFound: true);
            InputActionMap slotMap = asset.FindActionMap(_slotMapName, throwIfNotFound: true);

            // 카탈로그는 바인딩 원본일 뿐이다. 실수로 켜져 입력이 두 번 들어오는 일이 없도록 막는다.
            slotMap.Disable();

            // 자리 인덱스는 카탈로그의 액션 정의 순서다. 이름은 상관없다.
            _slots = new InputAction[slotMap.actions.Count];
            for (int i = 0; i < _slots.Length; i++)
                _slots[i] = slotMap.actions[i];

            if (_slots.Length < _actionNames.Length)
                throw new InvalidOperationException(
                    $"자리 카탈로그 맵 '{_slotMapName}'에 정의된 자리는 {_slots.Length}개인데, " +
                    $"배정할 액션은 {_actionNames.Length}개입니다. 자리가 더 필요합니다.");

            _actions = new InputAction[_actionNames.Length];
            for (int i = 0; i < _actionNames.Length; i++)
                _actions[i] = GetRequiredAction(_actionMap, _actionNames[i]);

            _defaultAssignment = ResolveDefaultAssignment();
            _resolvedForbiddenGroups = ResolveForbiddenGroups();
        }

        /// <summary>스테이지 매니저가 호출: 새 스테이지용 키 셔플 적용.</summary>
        public void ShuffleKeys()
        {
            int[] assignment = KeyShuffle.Shuffle(_rng, _slots.Length, _defaultAssignment, _resolvedForbiddenGroups);
            ApplyAssignment(assignment);
        }

        /// <summary>스테이지 매니저가 호출: 개발자가 정의한 기본 배치로 원복.</summary>
        public void ResetToDefault()
        {
            ApplyAssignment(_defaultAssignment);
        }

        private void ApplyAssignment(int[] assignment)
        {
            KeyShuffle.Apply(_actionMap, _actions, _slots, assignment);

            // 바인딩이 바뀌는 순간 눌려 있던 키의 상태가 남아 플레이어가 계속 미끄러지는 것을 막는다.
            if (_playerMovement != null)
                _playerMovement.ResetInputState();
        }

        /// <summary>
        /// 각 액션의 기본 자리를 에셋에 적힌 바인딩에서 알아낸다. 기본 배치가 이미 에셋에 있으므로
        /// 인스펙터에 다시 적게 하지 않는다. 셔플이 바인딩을 덮어쓰기 전에 호출해야 한다.
        ///
        /// 첫 바인딩 하나만 보면 된다. 한 자리의 키들은 늘 함께 움직이므로
        /// MoveUp의 <Keyboard>/w로 찾든 <Keyboard>/upArrow로 찾든 같은 자리가 나온다.
        /// </summary>
        private int[] ResolveDefaultAssignment()
        {
            var assignment = new int[_actions.Length];
            for (int i = 0; i < _actions.Length; i++)
            {
                InputAction action = _actions[i];
                if (action.bindings.Count == 0)
                    throw new InvalidOperationException(
                        $"액션 '{action.name}'에 바인딩이 없어 기본 자리를 알아낼 수 없습니다. " +
                        $"'{_actionMapName}' 맵에서 이 액션에 기본 키를 지정하세요.");

                string path = action.bindings[0].path;
                int index = FindSlotByPath(path);
                if (index < 0)
                    throw new InvalidOperationException(
                        $"액션 '{action.name}'의 기본 키가 '{path}'인데, 자리 카탈로그 맵 '{_slotMapName}'의 " +
                        "어느 자리에도 이 키가 없습니다. 모든 기본 키는 카탈로그에도 있어야 합니다.");

                // 두 액션이 같은 기본 자리를 쓰면 원복이 한 자리에 두 액션을 얹게 된다.
                int duplicate = Array.IndexOf(assignment, index, 0, i);
                if (duplicate >= 0)
                    throw new InvalidOperationException(
                        $"액션 '{_actions[duplicate].name}'과 '{action.name}'의 기본 자리가 " +
                        $"'{_slots[index].name}'으로 겹칩니다. 각 액션의 기본 자리는 서로 달라야 합니다.");

                assignment[i] = index;
            }

            return assignment;
        }

        /// <summary>
        /// 인스펙터에 이름으로 적힌 금지 규칙을 액션 인덱스로 해석한다.
        /// 이름이 셔플 대상에 없으면 예외를 던진다 — 규칙이 조용히 사라지면
        /// 셔플은 계속 돌지만 기본 배치가 그대로 나오는 일이 생긴다.
        /// </summary>
        private int[][] ResolveForbiddenGroups()
        {
            var resolved = new int[_forbiddenGroups.Length][];
            for (int g = 0; g < _forbiddenGroups.Length; g++)
            {
                string[] names = _forbiddenGroups[g].ActionNames;
                if (names == null || names.Length == 0)
                    throw new InvalidOperationException(
                        $"금지 규칙 {g}번이 비어 있습니다. 액션 이름을 채우거나 이 묶음을 삭제하세요.");

                var indices = new int[names.Length];
                for (int i = 0; i < names.Length; i++)
                {
                    indices[i] = Array.FindIndex(_actions, action => action.name == names[i]);
                    if (indices[i] < 0)
                        throw new InvalidOperationException(
                            $"금지 규칙 {g}번의 액션 '{names[i]}'이 셔플 대상에 없습니다. " +
                            $"{nameof(_actionNames)}에 있는 이름만 쓸 수 있습니다.");
                }

                resolved[g] = indices;
            }

            return resolved;
        }

        /// <summary>주어진 바인딩 path를 담고 있는 자리를 찾는다. 없으면 -1.</summary>
        private int FindSlotByPath(string path)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                var bindings = _slots[i].bindings;
                for (int b = 0; b < bindings.Count; b++)
                {
                    if (bindings[b].path == path)
                        return i;
                }
            }

            return -1;
        }

        private static InputAction GetRequiredAction(InputActionMap map, string actionName)
        {
            InputAction action = map.FindAction(actionName);
            if (action == null)
                throw new InvalidOperationException($"맵 '{map.name}'에서 액션 '{actionName}'을 찾을 수 없습니다.");
            return action;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void Update()
        {
            if (!_enableDebugKeys || Keyboard.current == null)
                return;

            if (Keyboard.current.digit1Key.wasPressedThisFrame)
                ShuffleKeys();
            else if (Keyboard.current.digit2Key.wasPressedThisFrame)
                ResetToDefault();
        }
#endif
    }
}
