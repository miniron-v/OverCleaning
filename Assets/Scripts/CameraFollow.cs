using UnityEngine;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 자기 캐릭터를 일정한 거리에서 따라간다. 차에 타도 캐릭터가 좌석에 실제로 앉아 있으므로 그대로 따라간다.
    /// 대상은 자기 캐릭터가 스폰될 때 정해진다.
    /// 넓은 곳에서는 시야각을 키워 더 멀리 보여준다.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraFollow : MonoBehaviour
    {
        [SerializeField] private Vector3 _offset = new Vector3(0f, 18f, -13f);
        [Min(0.1f)] [SerializeField] private float _smoothTime = 0.15f;

        [Tooltip("넓게 볼 때의 시야각. 평소 시야각은 카메라에 설정된 값이다.")]
        [Range(1f, 179f)] [SerializeField] private float _wideFieldOfView = 60f;

        [Tooltip("시야각이 바뀌는 속도(초당 도).")]
        [Min(1f)] [SerializeField] private float _zoomSpeed = 60f;

        private Camera _camera;
        private float _normalFieldOfView;
        private Vector3 _velocity;

        public Transform Target { get; set; }

        /// <summary>넓게 볼지. 바꾸면 시야각이 부드럽게 따라간다.</summary>
        public bool IsWide { get; set; }

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _normalFieldOfView = _camera.fieldOfView;
        }

        private void LateUpdate()
        {
            float fieldOfView = IsWide ? _wideFieldOfView : _normalFieldOfView;
            _camera.fieldOfView = Mathf.MoveTowards(_camera.fieldOfView, fieldOfView, _zoomSpeed * Time.deltaTime);

            if (Target == null)
                return;
            transform.position = Vector3.SmoothDamp(transform.position, Target.position + _offset,
                ref _velocity, _smoothTime);
        }
    }
}
