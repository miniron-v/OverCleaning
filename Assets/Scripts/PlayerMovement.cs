using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{
    [SerializeField] private float _moveSpeed = 5f;

    private Rigidbody _rigidbody;

    public Vector3 FacingDirection { get; private set; } = Vector3.forward;

    /// <summary>이동 속도 배율. 무거운 물건을 들면 ItemCarrier가 낮춰 준다.</summary>
    public float SpeedMultiplier { get; set; } = 1f;

    // 방향별 개별 액션 입력. 키 셔플이 이 액션들의 바인딩을 재배치해도 이동 로직은 그대로다.
    private bool _up, _down, _left, _right;

    /// <summary>이동을 막고 있는 이유들. 하나라도 남아 있으면 걷지 못한다.</summary>
    private readonly HashSet<object> _locks = new HashSet<object>();

    /// <summary>
    /// 걷기가 막혀 있는지. 막힌 동안은 몸의 속도를 건드리지 않아, 기절해 튕겨나는 속도나 좌석에 붙는 위치를 덮어쓰지 않는다.
    /// 입력(MoveInput)은 막혀도 계속 갱신된다.
    /// </summary>
    public bool IsLocked => _locks.Count > 0;

    /// <summary>
    /// source 말고 다른 이유로도 막혀 있는지. 차는 운전자를 자기가 묶어 두므로, 그 밖의 이유(기절, 단계 선택 등)로
    /// 운전자가 묶였을 때만 차도 멈춰야 한다.
    /// </summary>
    public bool IsLockedExcept(object source) => _locks.Count > (_locks.Contains(source) ? 1 : 0);

    /// <summary>
    /// 이유(source)마다 따로 걷기를 막고 푼다. 기절, 차, 의자, 단계 선택이 서로의 잠금을 풀어 버리지 않게 한다.
    /// 처음 막힐 때 걷던 속도를 멈춘다. 튕겨내려면 막은 뒤에 속도를 준다.
    /// </summary>
    public void SetLocked(object source, bool isLocked)
    {
        bool wasLocked = IsLocked;
        if (isLocked)
            _locks.Add(source);
        else
            _locks.Remove(source);

        if (!wasLocked && IsLocked && !_rigidbody.isKinematic)
            _rigidbody.linearVelocity = new Vector3(0f, _rigidbody.linearVelocity.y, 0f);
    }

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _rigidbody.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation;
        _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
    }

    // PlayerInput "Send Messages" 방식: 방향 액션마다 On<Action> 콜백이 호출된다.
    private void OnMoveUp(InputValue value) => _up = value.isPressed;
    private void OnMoveDown(InputValue value) => _down = value.isPressed;
    private void OnMoveLeft(InputValue value) => _left = value.isPressed;
    private void OnMoveRight(InputValue value) => _right = value.isPressed;

    /// <summary>
    /// 눌린 상태를 모두 해제한다.
    /// 키 셔플은 액션 맵을 잠시 비활성화했다가 다시 켜는데, 그 사이 눌려 있던 키의 해제 콜백이
    /// 오지 않을 수 있다. 그러면 플레이어가 한 방향으로 계속 미끄러진다. 셔플 직후 호출한다.
    /// </summary>
    public void ResetInputState()
    {
        _up = _down = _left = _right = false;
    }

    /// <summary>
    /// 눌린 방향으로 만든 입력. 차를 운전할 때도 이 값을 쓰므로, 걷기가 막혀 있어도 갱신된다.
    /// </summary>
    public Vector2 MoveInput
    {
        get
        {
            Vector2 input = new Vector2(
                (_right ? 1f : 0f) - (_left ? 1f : 0f),
                (_up ? 1f : 0f) - (_down ? 1f : 0f));

            // 대각선이 빨라지지 않도록 정규화. Composite의 기본 동작을 직접 재현한다.
            if (input.sqrMagnitude > 1f)
                input.Normalize();
            return input;
        }
    }

    private void FixedUpdate()
    {
        if (IsLocked)
            return;

        // 시작 전 카운트다운 동안은 모두 제자리에 묶인다.
        Vector2 input = OverCleaning.InGame.GameRound.IsMovementLocked ? Vector2.zero : MoveInput;
        Vector3 movement = new Vector3(input.x, 0f, input.y) * (_moveSpeed * SpeedMultiplier);
        if (input.sqrMagnitude > 0f)
            FacingDirection = new Vector3(input.x, 0f, input.y).normalized;
        _rigidbody.linearVelocity = new Vector3(movement.x, _rigidbody.linearVelocity.y, movement.z);
    }
}
