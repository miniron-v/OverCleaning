using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{
    [SerializeField] private float _moveSpeed = 5f;

    private Rigidbody _rigidbody;

    // 방향별 개별 액션 입력. 키 셔플이 이 액션들의 바인딩을 재배치해도 이동 로직은 그대로다.
    private bool _up, _down, _left, _right;

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

    private void FixedUpdate()
    {
        Vector2 input = new Vector2(
            (_right ? 1f : 0f) - (_left ? 1f : 0f),
            (_up ? 1f : 0f) - (_down ? 1f : 0f));

        // 대각선이 빨라지지 않도록 정규화. Composite의 기본 동작을 직접 재현한다.
        if (input.sqrMagnitude > 1f)
            input.Normalize();

        Vector3 movement = new Vector3(input.x, 0f, input.y) * _moveSpeed;
        _rigidbody.linearVelocity = new Vector3(movement.x, _rigidbody.linearVelocity.y, movement.z);
    }
}
