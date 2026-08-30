using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace OverCleaning.Input
{
    /// <summary>
    /// 키 셔플과 적용. 상태를 갖지 않는 순수 함수 모음.
    ///
    /// 용어:
    ///   자리(slot)   — 실제 키 하나를 가리키는 인덱스. 자리 카탈로그 맵의 액션 정의 순서로 매겨진다.
    ///   액션(action) — 게임에 사용되는 논리적 입력(위/아래/왼/오른/상호작용).
    ///   배정(assignment) — assignment[액션] = 그 액션이 놓인 자리. 액션 수 <= 자리 수이며,
    ///                      배정받지 못한 자리는 비어 있어서(어떤 액션에도 실리지 않아) 눌러도 아무 일이 없다.
    /// </summary>
    public static class KeyShuffle
    {
        /// <summary>
        /// 금지 규칙이 대부분의 조합을 걸러내는 설정이 들어와도 에디터가 멈추지 않도록 하는 상한.
        /// 정상 상황에서는 첫 시도가 거의 항상 통과한다.
        /// </summary>
        private const int MaxAttempts = 1000;

        /// <summary>
        /// 액션들을 서로 다른 자리에 무작위 배정한다.
        /// <paramref name="defaultAssignment"/>은 "원래 자리" 정보로 금지 규칙 판정에만 쓰인다.
        /// </summary>
        /// <param name="rng">난수 생성기. 호출자가 소유하며, 고정 시드를 넣으면 결과가 재현된다.</param>
        /// <param name="slotCount">전체 자리 수. 액션 수보다 크면 그만큼 빈 자리가 생긴다.</param>
        /// <param name="defaultAssignment">기본 배치. 길이가 곧 배정할 액션 수.</param>
        /// <param name="forbiddenGroups">
        /// 금지 규칙. 한 묶음의 액션이 '모두' 기본 자리에 남는 배정은 버린다.
        /// 값은 액션 인덱스이며, 호출자가 미리 가공한 정보. 묶음 크기에는 제한이 없다.
        /// </param>
        /// <returns>assignment[액션] = 자리 인덱스. 어떤 액션이 어떤 자리인가.</returns>
        public static int[] Shuffle(Random rng, int slotCount, int[] defaultAssignment, int[][] forbiddenGroups)
        {
            if (rng == null)
                throw new ArgumentNullException(nameof(rng));
            if (defaultAssignment == null)
                throw new ArgumentNullException(nameof(defaultAssignment));
            if (forbiddenGroups == null)
                throw new ArgumentNullException(nameof(forbiddenGroups));
            if (defaultAssignment.Length > slotCount)
                throw new ArgumentException(
                    $"액션 수({defaultAssignment.Length})가 자리 수({slotCount})보다 많습니다. " +
                    "자리 카탈로그 맵에 자리를 더 정의하거나 셔플 대상 액션을 줄이세요.");

            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                int[] assignment = RandomAssignment(rng, slotCount, defaultAssignment.Length);
                if (!IsForbidden(assignment, defaultAssignment, forbiddenGroups))
                    return assignment;
            }

            throw new InvalidOperationException(
                $"{MaxAttempts}번 시도했지만 유효한 키 배정을 찾지 못했습니다. " +
                "현재 자리 수에서는 금지 규칙이 거의 모든 배치를 걸러내고 있을 수 있습니다.");
        }

        /// <summary>
        /// 계산된 배정을 실제 액션에 반영한다.
        /// 각 액션의 바인딩을 지우고, 배정된 자리의 카탈로그 바인딩을 복사해 채운다.
        /// 카탈로그는 Enable되지 않으므로 언제나 원본이며, 별도 캐시가 필요 없다.
        /// </summary>
        /// <param name="map">액션들이 속한 맵. 바인딩 변경 동안 안전을 위해 통째로 비활성화된다.</param>
        /// <param name="actions">셔플 대상 액션. 순서가 assignment의 인덱스와 대응한다.</param>
        /// <param name="slots">자리별 카탈로그 액션. 순서가 자리 인덱스다.</param>
        /// <param name="assignment">assignment[액션] = 자리 인덱스.</param>
        public static void Apply(InputActionMap map, IReadOnlyList<InputAction> actions, IReadOnlyList<InputAction> slots, int[] assignment)
        {
            if (map == null)
                throw new ArgumentNullException(nameof(map));
            if (actions == null)
                throw new ArgumentNullException(nameof(actions));
            if (slots == null)
                throw new ArgumentNullException(nameof(slots));
            if (assignment == null)
                throw new ArgumentNullException(nameof(assignment));
            if (assignment.Length != actions.Count)
                throw new ArgumentException(
                    $"배정 길이({assignment.Length})와 액션 수({actions.Count})가 다릅니다.");

            // 바인딩 세트 변경은 액션이 비활성인 동안에만 허용된다.
            bool wasEnabled = map.enabled;
            map.Disable();

            for (int i = 0; i < actions.Count; i++)
            {
                InputAction action = actions[i];
                ClearBindings(action);
                CopyBindings(from: slots[assignment[i]], to: action);
            }

            if (wasEnabled)
                map.Enable();
        }

        /// <summary>
        /// 0..slotCount-1 중에서 서로 다른 자리 actionCount개를 뽑아 각 액션에 하나씩 준다.
        /// 뽑히지 않은 자리는 빈 자리가 된다.
        /// </summary>
        private static int[] RandomAssignment(Random rng, int slotCount, int actionCount)
        {
            var slots = new int[slotCount];
            for (int i = 0; i < slotCount; i++)
                slots[i] = i;

            // Fisher-Yates를 앞 actionCount번만 돌린다. 매 회차에서 남은 후보 중 하나를 뽑아
            // 앞으로 보내므로, 끝까지 섞지 않아도 앞 actionCount개는 고르게 뽑힌 결과가 된다.
            for (int i = 0; i < actionCount; i++)
            {
                int j = rng.Next(i, slotCount);
                (slots[i], slots[j]) = (slots[j], slots[i]);
            }

            var assignment = new int[actionCount];
            Array.Copy(slots, assignment, actionCount);
            return assignment;
        }

        /// <summary>금지 묶음 중 단 하나라도 '묶음 내 모든 액션이 기본 자리'이면 true.</summary>
        private static bool IsForbidden(int[] assignment, int[] defaultAssignment, int[][] forbiddenGroups)
        {
            foreach (int[] group in forbiddenGroups)
            {
                bool allAtDefault = true;
                foreach (int action in group)
                {
                    if (assignment[action] != defaultAssignment[action])
                    {
                        allAtDefault = false;
                        break;
                    }
                }

                if (allAtDefault)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 자리 카탈로그의 바인딩을 전부 실제 액션으로 복사한다.
        /// 한 자리의 모든 물리 바인딩(키보드 키, 화살표, 게임패드 버튼 등)이 함께 움직이므로
        /// 플랫폼이 달라도 같은 자리는 같은 액션이 된다.
        /// InputBinding은 struct여서 source[i]를 읽는 순간 값 복사본이 만들어진다.
        /// 아래에서 id·action을 고쳐도 카탈로그 원본에는 닿지 않는다.
        /// id를 새로 부여하는 것은 여러 액션에 같은 바인딩 id가 생기는 것을 막기 위해서다.
        /// </summary>
        private static void CopyBindings(InputAction from, InputAction to)
        {
            var source = from.bindings;
            for (int i = 0; i < source.Count; i++)
            {
                InputBinding copy = source[i];
                copy.id = Guid.NewGuid();
                copy.action = to.name;
                to.AddBinding(copy);
            }
        }

        /// <summary>액션의 모든 바인딩을 제거한다. 인덱스가 밀리지 않도록 뒤에서부터 지운다.</summary>
        private static void ClearBindings(InputAction action)
        {
            for (int i = action.bindings.Count - 1; i >= 0; i--)
                action.ChangeBinding(i).Erase();
        }
    }
}
