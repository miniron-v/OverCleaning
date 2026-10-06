using UnityEngine;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 캐릭터 모델을 실제로 이동한 방향으로 돌리되, 물건을 들었으면 그 물건 쪽을 본다.
    /// 루트를 돌리면 들고 있는 청소기가 충돌 검사 없이 함께 돌아가므로 모델만 돌린다.
    /// 이동은 위치 변화로, 든 물건은 자식 관계로 읽으므로 둘 다 입력 없이도
    /// 원격 플레이어가 같은 방향을 본다.
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

            // 든 물건이 있으면 그쪽을 본다. 장착한 물건은 몸 중심에 붙어 방향이 없으므로
            // 그때는 이동 방향을 그대로 쓴다.
            Vector3 direction = Vector3.zero;
            CarriableItem heldItem = GetComponentInChildren<CarriableItem>();
            if (heldItem != null)
                direction = heldItem.transform.position - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.000001f)
            {
                direction = delta;
                direction.y = 0f;
            }
            if (direction.sqrMagnitude < 0.000001f)
                return;

            Quaternion target = Quaternion.LookRotation(direction, Vector3.up);
            _model.rotation = Quaternion.RotateTowards(_model.rotation, target, _turnSpeed * Time.deltaTime);
        }
    }
}
