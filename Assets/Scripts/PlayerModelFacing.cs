using UnityEngine;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 캐릭터 모델을 실제로 이동한 방향으로 돌린다.
    /// 루트를 돌리면 들고 있는 청소기가 충돌 검사 없이 함께 돌아가므로 모델만 돌린다.
    /// 입력 대신 위치 변화를 쓰므로 원격 플레이어도 동기화된 위치만으로 같은 방향을 본다.
    /// </summary>
    public sealed class PlayerModelFacing : MonoBehaviour
    {
        [SerializeField] private Transform _model;
        [Min(1f)] [SerializeField] private float _turnSpeed = 720f;

        private Vector3 _previousPosition;

        private void OnEnable()
        {
            _previousPosition = transform.position;
        }

        private void LateUpdate()
        {
            Vector3 delta = transform.position - _previousPosition;
            _previousPosition = transform.position;
            delta.y = 0f;
            if (delta.sqrMagnitude < 0.000001f)
                return;

            Quaternion target = Quaternion.LookRotation(delta, Vector3.up);
            _model.rotation = Quaternion.RotateTowards(_model.rotation, target, _turnSpeed * Time.deltaTime);
        }
    }
}
