using UnityEngine;
using UnityEngine.InputSystem;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 물건을 든 사람 쪽 처리. 물건 로직은 CarriableItem 쪽이 맡고 여기서는 사람이 하는
    /// 일만 한다: 든 물건에 보는 쪽을 알려주고, 들기 키의 짧게/길게를 가려 내려놓기와
    /// 물건마다의 꾹 누르기(청소기 비우기, 빗자루 쓸어 담기)를 시킨다.
    ///
    /// 들기 키 하나로 조작한다. 줍기는 PlayerInteractor가 누르는 순간 처리하고,
    /// 든 뒤에는 짧게 눌렀다 떼면 내려놓는다. 꾹 누르면 물건의 꾹 누르기가 가능한 동안
    /// 이어지고, 중간에 떼면 거기까지만 된다. 무엇을 할지는 물건(CanHoldUse)이 정한다.
    ///
    /// 키 상태는 콜백 대신 매 프레임 읽는다. 키 셔플이 맵을 껐다 켜는 순간
    /// 뗌 콜백이 사라져도 눌림이 남지 않는다.
    /// </summary>
    [RequireComponent(typeof(PlayerMovement))]
    [DefaultExecutionOrder(100)]
    public sealed class ItemCarrier : MonoBehaviour
    {
        private const string InteractActionName = "Interact";

        [Tooltip("이보다 짧게 누르면 내려놓기, 길게 누르고 있으면 물건의 꾹 누르기다(초).")]
        [Min(0.05f)] [SerializeField] private float _holdThreshold = 0.3f;

        private PlayerMovement _playerMovement;
        private PlayerInput _playerInput;
        private Camera _statusCamera;
        private CarriableItem _heldItem;
        private bool _interactWasPressed;
        private bool _isTrackingPress;
        private float _pressElapsedTime;
        private bool _isHoldUsing;
        private int _emptyStartCount;

        /// <summary>든 물건이 청소기일 때만 값이 있다. 비우기와 HUD는 청소기만의 것이다.</summary>
        private Vacuum HeldVacuum => _heldItem as Vacuum;

        private void Awake()
        {
            _playerMovement = GetComponent<PlayerMovement>();
            _playerInput = GetComponent<PlayerInput>();
        }

        private void OnDisable()
        {
            StopHoldUse();
            _isTrackingPress = false;
            _interactWasPressed = false;
        }

        private void Update()
        {
            // 누름 시작은 지난 프레임의 들림 상태로 가린다. 줍기도 같은 키라서,
            // 호스트에서는 줍는 탭과 같은 프레임에 물건이 이미 붙어 버리는데,
            // 그 탭을 여기서 또 받으면 떼는 순간 도로 내려놓는다.
            bool isPressed = IsInteractPressed();
            if (isPressed && !_interactWasPressed)
                BeginPress();
            _interactWasPressed = isPressed;

            // 서버가 들린 물건을 내 자식으로 붙여 준다. 남이 든 물건은 그 사람 밑에 있으므로
            // 여기서 찾히지 않는다.
            _heldItem = GetComponentInChildren<CarriableItem>();

            // 무거운 물건을 들면 느려진다. 내려놓으면 돌아온다.
            _playerMovement.SpeedMultiplier = _heldItem != null ? _heldItem.CarrySpeedMultiplier : 1f;

            if (_isTrackingPress)
                UpdatePress(isPressed);
        }

        private void FixedUpdate()
        {
            if (_heldItem != null)
                _heldItem.AimAt(_playerMovement.FacingDirection);
        }

        /// <summary>들고 있지 않을 때의 누름은 줍기라서 PlayerInteractor의 몫이다.</summary>
        private void BeginPress()
        {
            if (_heldItem == null)
                return;
            _isTrackingPress = true;
            _pressElapsedTime = 0f;
        }

        private void UpdatePress(bool isPressed)
        {
            if (_heldItem == null)
            {
                StopHoldUse();
                _isTrackingPress = false;
                return;
            }

            if (!isPressed)
            {
                // 뗐다. 꾹 누르기를 하던 중이면 거기서 멈출 뿐 내려놓지 않고,
                // 짧게 눌렀다 뗀 것이면 내려놓는다.
                bool wasHoldUsing = _isHoldUsing;
                StopHoldUse();
                _isTrackingPress = false;
                if (!wasHoldUsing && _pressElapsedTime < _holdThreshold)
                    _heldItem.RequestDrop();
                return;
            }

            _pressElapsedTime += Time.deltaTime;
            if (!_isHoldUsing && _pressElapsedTime >= _holdThreshold && _heldItem.CanHoldUse)
                BeginHoldUse();

            // 다 했거나(비우기 끝, 쓰레기 없음) 자리가 어긋나면 멈춘다. 키를 계속 누르고 있어도 된다.
            if (_isHoldUsing && !_heldItem.CanHoldUse)
                StopHoldUse();
        }

        private void BeginHoldUse()
        {
            _isHoldUsing = true;
            // 비우기 진행 표시는 청소기만의 것이라 시작 수량도 청소기일 때만 받아 둔다.
            _emptyStartCount = HeldVacuum != null ? HeldVacuum.StoredDustCount : 0;
            _heldItem.BeginHoldUse();
        }

        private void StopHoldUse()
        {
            if (!_isHoldUsing)
                return;
            _isHoldUsing = false;
            if (_heldItem != null)
                _heldItem.EndHoldUse();
        }

        /// <summary>
        /// 들기 키의 눌림을 지금 쓰는 액션 맵에서 읽는다. 셔플로 자리가 바뀌어도 그대로 따라간다.
        /// </summary>
        private bool IsInteractPressed()
        {
            InputActionMap map = _playerInput != null ? _playerInput.currentActionMap : null;
            if (map == null)
                return false;
            InputAction action = map.FindAction(InteractActionName);
            return action != null && action.IsPressed();
        }

        private void OnGUI()
        {
            Vacuum vacuum = HeldVacuum;
            if (vacuum == null)
                return;

            string status = _isHoldUsing ? "먼지통 비우는 중" :
                vacuum.IsFull ? "먼지통 가득 참 — 비워주세요" :
                vacuum.IsRunning ? "청소 중" : "작동 정지";
            string hint = vacuum.StoredDustCount == 0 ? "먼지통이 비어 있습니다 (짧게 눌러 내려놓기)" :
                vacuum.CanEmptyDustBin() ? "들기 키 꾹: 먼지통 비우기 / 짧게: 내려놓기" :
                "먼지를 버리려면 쓰레기통 가까이 가세요";
            GUI.Box(new Rect(16f, 16f, 360f, 72f),
                $"먼지통 {vacuum.StoredDustCount}/{vacuum.DustCapacity}" +
                $" (흡입 중: {vacuum.ReservedDustCount})\n{status}\n{hint}");
            DrawEmptyingProgress();
        }

        private void DrawEmptyingProgress()
        {
            if (!_isHoldUsing)
                return;
            if (_statusCamera == null)
                _statusCamera = Camera.main;
            if (_statusCamera == null)
                return;

            Vector3 screenPosition = _statusCamera.WorldToScreenPoint(transform.position + Vector3.up * 2.3f);
            if (screenPosition.z <= 0f)
                return;

            // 시작 수량 대비 얼마나 빠져나갔는가. 수량은 서버가 깎아 내려주는 값이다.
            float progress = _emptyStartCount > 0
                ? 1f - (float)HeldVacuum.StoredDustCount / _emptyStartCount
                : 1f;
            float left = screenPosition.x - 75f;
            float top = Screen.height - screenPosition.y - 40f;
            GUI.Box(new Rect(left, top, 150f, 40f), "먼지통 비우는 중...");
            Color previousColor = GUI.color;
            GUI.color = Color.gray;
            GUI.DrawTexture(new Rect(left + 8f, top + 25f, 134f, 8f), Texture2D.whiteTexture);
            GUI.color = Color.green;
            GUI.DrawTexture(new Rect(left + 8f, top + 25f, 134f * progress, 8f), Texture2D.whiteTexture);
            GUI.color = previousColor;
        }
    }
}
