using UnityEngine;
using UnityEngine.InputSystem;

namespace OverCleaning.Interaction
{
    /// <summary>
    /// 주변에서 가장 가까운 상호작용 대상을 찾아 안내하고, 상호작용 키를 누르면 실행한다.
    /// 자기 캐릭터에서만 켜둔다. 남의 캐릭터에서 켜지면 안내가 겹쳐 보인다.
    /// </summary>
    public sealed class PlayerInteractor : MonoBehaviour
    {
        private const string InteractActionName = "Interact";

        [Tooltip("키 안내에 쓸 PlayerInput. 비워두면 같은 오브젝트에서 찾는다.")]
        [SerializeField] private PlayerInput _playerInput;

        [Min(0.1f)] [SerializeField] private float _interactionRadius = 1.5f;
        [SerializeField] private LayerMask _interactableLayers = ~0;

        private readonly Collider[] _overlapBuffer = new Collider[32];
        private IInteractable _target;

        private void Awake()
        {
            if (_playerInput == null)
                _playerInput = GetComponent<PlayerInput>();
        }

        private void OnDisable()
        {
            _target = null;
        }

        private void Update()
        {
            _target = FindClosestTarget();
        }

        // PlayerInput "Send Messages" 방식: Interact 액션이 수행되면 호출된다.
        private void OnInteract(InputValue value)
        {
            // Send Messages는 꺼진 컴포넌트에도 전달되므로 직접 걸러낸다.
            if (!isActiveAndEnabled || !value.isPressed)
                return;

            IInteractable target = FindClosestTarget();
            if (target != null)
                target.Interact();
        }

        private IInteractable FindClosestTarget()
        {
            Vector3 center = transform.position + Vector3.up * 0.5f;
            int count = Physics.OverlapSphereNonAlloc(center, _interactionRadius, _overlapBuffer,
                _interactableLayers, QueryTriggerInteraction.Collide);

            IInteractable closest = null;
            float closestDistance = float.MaxValue;
            for (int index = 0; index < count; index++)
            {
                Collider candidate = _overlapBuffer[index];
                IInteractable interactable = candidate.GetComponentInParent<IInteractable>();
                if (interactable == null || !interactable.CanInteract)
                    continue;

                float distance = (candidate.ClosestPoint(center) - center).sqrMagnitude;
                if (distance >= closestDistance)
                    continue;
                closest = interactable;
                closestDistance = distance;
            }

            return closest;
        }

        private void OnGUI()
        {
            if (_target == null)
                return;

            // 키가 셔플되어도 맞는 안내가 나오도록 실제 바인딩에서 이름을 읽는다.
            string keyName = GetInteractKeyName();
            string message = string.IsNullOrEmpty(keyName) ? _target.Prompt : $"{keyName}: {_target.Prompt}";
            const float width = 320f;
            const float height = 32f;
            GUI.Box(new Rect((Screen.width - width) * 0.5f, Screen.height - height - 48f, width, height), message);
        }

        private string GetInteractKeyName()
        {
            if (_playerInput == null || _playerInput.actions == null)
                return string.Empty;
            InputAction action = _playerInput.actions.FindAction(InteractActionName);
            return action != null ? action.GetBindingDisplayString() : string.Empty;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.5f, _interactionRadius);
        }
    }
}
