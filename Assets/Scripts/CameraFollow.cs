using UnityEngine;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 자기 캐릭터를 일정한 거리에서 따라간다. 차에 타도 캐릭터가 좌석에 실제로 앉아 있으므로 그대로 따라간다.
    /// 대상은 자기 캐릭터가 스폰될 때 정해진다.
    /// </summary>
    public sealed class CameraFollow : MonoBehaviour
    {
        [SerializeField] private Vector3 _offset = new Vector3(0f, 18f, -13f);
        [Min(0.1f)] [SerializeField] private float _smoothTime = 0.15f;

        private Vector3 _velocity;

        public Transform Target { get; set; }

        private void LateUpdate()
        {
            if (Target == null)
                return;
            transform.position = Vector3.SmoothDamp(transform.position, Target.position + _offset,
                ref _velocity, _smoothTime);
        }
    }
}
