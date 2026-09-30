using UnityEngine;
using UnityEngine.InputSystem;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 청소기를 든 사람 쪽 처리. 기기 로직은 Vacuum이 맡고 여기서는 사람이 하는 일만 한다:
    /// 든 청소기를 보는 쪽으로 돌리고, 먼지통 비우기를 누르고 있는 동안 진행한다.
    ///
    /// 든 청소기는 자기 자식이 되므로 따로 등록해 두지 않고 그때그때 찾는다.
    /// 들고 내려놓기는 상호작용 키가 맡으므로 이 컴포넌트는 관여하지 않는다.
    /// </summary>
    [RequireComponent(typeof(PlayerMovement))]
    [DefaultExecutionOrder(100)]
    public sealed class VacuumCarrier : MonoBehaviour
    {
        [Tooltip("먼지통을 다 비우기까지 키를 누르고 있어야 하는 시간(초).")]
        [Min(0.1f)] [SerializeField] private float _emptyDuration = 2f;

        private PlayerMovement _playerMovement;
        private Camera _statusCamera;
        private Vacuum _heldVacuum;
        private bool _emptyKeyHeld;
        private float _emptyElapsedTime;

        private bool IsEmptying => _emptyElapsedTime > 0f;

        private void Awake()
        {
            _playerMovement = GetComponent<PlayerMovement>();
        }

        private void OnDisable()
        {
            StopEmptying();
            _emptyKeyHeld = false;
        }

        private void Update()
        {
            // 서버가 들린 청소기를 내 자식으로 붙여 준다. 남이 든 청소기는 그 사람 밑에 있으므로
            // 여기서 찾히지 않는다. 그래서 따로 누가 들었는지 물어볼 필요가 없다.
            _heldVacuum = GetComponentInChildren<Vacuum>();
        }

        private void FixedUpdate()
        {
            if (_heldVacuum != null)
                _heldVacuum.AimAt(_playerMovement.FacingDirection);
        }

        private void LateUpdate()
        {
            UpdateEmptying();
        }

        // PlayerInput "Send Messages" 방식: EmptyBin 액션의 눌림과 떼임이 모두 들어온다.
        private void OnEmptyBin(InputValue value)
        {
            // Send Messages는 꺼진 컴포넌트에도 전달되므로 직접 걸러낸다.
            if (!isActiveAndEnabled)
                return;
            _emptyKeyHeld = value.isPressed;
        }

        /// <summary>
        /// 누르고 있는 동안에만 진행한다. 손을 떼거나 쓰레기통에서 멀어지면 처음부터 다시 해야 한다.
        /// </summary>
        private void UpdateEmptying()
        {
            if (!_emptyKeyHeld || _heldVacuum == null || !_heldVacuum.CanEmptyDustBin())
            {
                StopEmptying();
                return;
            }

            // 비우는 동안에는 빨아들이지 않는다. 비우자마자 다시 차는 것을 막는다.
            _heldVacuum.SetSuctionPaused(true);
            _emptyElapsedTime += Time.deltaTime;
            if (_emptyElapsedTime < _emptyDuration)
                return;

            _heldVacuum.TryEmptyDustBin();
            StopEmptying();
        }

        private void StopEmptying()
        {
            _emptyElapsedTime = 0f;
            if (_heldVacuum != null)
                _heldVacuum.SetSuctionPaused(false);
        }

        private void OnGUI()
        {
            if (_heldVacuum == null)
                return;

            string status = IsEmptying ? "먼지통 비우는 중" :
                _heldVacuum.IsFull ? "먼지통 가득 참 — 비워주세요" :
                _heldVacuum.IsRunning ? "청소 중" : "작동 정지";
            string emptyHint = _heldVacuum.StoredDustCount == 0 ? "먼지통이 비어 있습니다" :
                _heldVacuum.CanEmptyDustBin() ? "먼지통 비우기 키를 누르고 계세요" :
                "먼지를 버리려면 쓰레기통 가까이 가세요";
            GUI.Box(new Rect(16f, 16f, 360f, 72f),
                $"먼지통 {_heldVacuum.StoredDustCount}/{_heldVacuum.DustCapacity}" +
                $" (흡입 중: {_heldVacuum.ReservedDustCount})\n{status}\n{emptyHint}");
            DrawEmptyingProgress();
        }

        private void DrawEmptyingProgress()
        {
            if (!IsEmptying)
                return;
            if (_statusCamera == null)
                _statusCamera = Camera.main;
            if (_statusCamera == null)
                return;

            Vector3 screenPosition = _statusCamera.WorldToScreenPoint(transform.position + Vector3.up * 2.3f);
            if (screenPosition.z <= 0f)
                return;

            float progress = Mathf.Clamp01(_emptyElapsedTime / _emptyDuration);
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
