using UnityEngine;
using UnityEngine.InputSystem;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 청소기를 든 사람 쪽 처리. 기기 로직은 Vacuum이 맡고 여기서는 사람이 하는 일만 한다:
    /// 든 청소기를 보는 쪽으로 돌리고, 들기 키의 짧게/길게를 가려 내려놓기와 비우기를 시킨다.
    ///
    /// 들기 키 하나로 조작한다. 줍기는 PlayerInteractor가 누르는 순간 처리하고,
    /// 든 뒤에는 짧게 눌렀다 떼면 내려놓는다. 쓰레기통 옆에서 꾹 누르고 있으면
    /// 누르는 동안 먼지가 쓰레기통으로 빠져나가고, 중간에 떼면 그때까지 빠진 만큼만 비워진다.
    ///
    /// 키 상태는 콜백 대신 매 프레임 읽는다. 키 셔플이 맵을 껐다 켜는 순간
    /// 뗌 콜백이 사라져도 눌림이 남지 않는다.
    /// </summary>
    [RequireComponent(typeof(PlayerMovement))]
    [DefaultExecutionOrder(100)]
    public sealed class VacuumCarrier : MonoBehaviour
    {
        private const string InteractActionName = "Interact";

        [Tooltip("이보다 짧게 누르면 내려놓기, 길게 누르고 있으면 먼지통 비우기다(초).")]
        [Min(0.05f)] [SerializeField] private float _holdThreshold = 0.3f;

        private PlayerMovement _playerMovement;
        private PlayerInput _playerInput;
        private Camera _statusCamera;
        private Vacuum _heldVacuum;
        private bool _interactWasPressed;
        private bool _isTrackingPress;
        private float _pressElapsedTime;
        private bool _isEmptying;
        private int _emptyStartCount;

        private void Awake()
        {
            _playerMovement = GetComponent<PlayerMovement>();
            _playerInput = GetComponent<PlayerInput>();
        }

        private void OnDisable()
        {
            StopEmptying();
            _isTrackingPress = false;
            _interactWasPressed = false;
        }

        private void Update()
        {
            // 서버가 들린 청소기를 내 자식으로 붙여 준다. 남이 든 청소기는 그 사람 밑에 있으므로
            // 여기서 찾히지 않는다.
            _heldVacuum = GetComponentInChildren<Vacuum>();

            bool isPressed = IsInteractPressed();
            if (isPressed && !_interactWasPressed)
                BeginPress();
            if (_isTrackingPress)
                UpdatePress(isPressed);
            _interactWasPressed = isPressed;
        }

        private void FixedUpdate()
        {
            if (_heldVacuum != null)
                _heldVacuum.AimAt(_playerMovement.FacingDirection);
        }

        /// <summary>들고 있지 않을 때의 누름은 줍기라서 PlayerInteractor의 몫이다.</summary>
        private void BeginPress()
        {
            if (_heldVacuum == null)
                return;
            _isTrackingPress = true;
            _pressElapsedTime = 0f;
        }

        private void UpdatePress(bool isPressed)
        {
            if (_heldVacuum == null)
            {
                StopEmptying();
                _isTrackingPress = false;
                return;
            }

            if (!isPressed)
            {
                // 뗐다. 비우던 중이었으면 거기서 멈출 뿐 내려놓지 않고,
                // 짧게 눌렀다 뗀 것이면 내려놓는다.
                bool wasEmptying = _isEmptying;
                StopEmptying();
                _isTrackingPress = false;
                if (!wasEmptying && _pressElapsedTime < _holdThreshold)
                    _heldVacuum.RequestDrop();
                return;
            }

            _pressElapsedTime += Time.deltaTime;
            if (!_isEmptying && _pressElapsedTime >= _holdThreshold && _heldVacuum.CanEmptyDustBin())
                BeginEmptying();

            // 다 비웠거나 쓰레기통에서 멀어지면 멈춘다. 키를 계속 누르고 있어도 된다.
            if (_isEmptying && !_heldVacuum.CanEmptyDustBin())
                StopEmptying();
        }

        private void BeginEmptying()
        {
            _isEmptying = true;
            _emptyStartCount = _heldVacuum.StoredDustCount;
            // 비우는 동안에는 빨아들이지 않는다. 비우자마자 다시 차는 것을 막는다.
            _heldVacuum.SetSuctionPaused(true);
            _heldVacuum.RequestBeginEmptying();
        }

        private void StopEmptying()
        {
            if (!_isEmptying)
                return;
            _isEmptying = false;
            if (_heldVacuum != null)
            {
                _heldVacuum.SetSuctionPaused(false);
                _heldVacuum.RequestEndEmptying();
            }
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
            if (_heldVacuum == null)
                return;

            string status = _isEmptying ? "먼지통 비우는 중" :
                _heldVacuum.IsFull ? "먼지통 가득 참 — 비워주세요" :
                _heldVacuum.IsRunning ? "청소 중" : "작동 정지";
            string hint = _heldVacuum.StoredDustCount == 0 ? "먼지통이 비어 있습니다 (짧게 눌러 내려놓기)" :
                _heldVacuum.CanEmptyDustBin() ? "들기 키 꾹: 먼지통 비우기 / 짧게: 내려놓기" :
                "먼지를 버리려면 쓰레기통 가까이 가세요";
            GUI.Box(new Rect(16f, 16f, 360f, 72f),
                $"먼지통 {_heldVacuum.StoredDustCount}/{_heldVacuum.DustCapacity}" +
                $" (흡입 중: {_heldVacuum.ReservedDustCount})\n{status}\n{hint}");
            DrawEmptyingProgress();
        }

        private void DrawEmptyingProgress()
        {
            if (!_isEmptying)
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
                ? 1f - (float)_heldVacuum.StoredDustCount / _emptyStartCount
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
