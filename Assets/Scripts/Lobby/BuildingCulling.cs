using Unity.Netcode;
using UnityEngine;

namespace OverCleaning.Lobby
{
    /// <summary>
    /// 카메라와 자기 캐릭터 사이를 가리는 건물을 그리지 않는다. 보는 사람마다 따로 정한다.
    /// 건물 묶음의 부모에 붙이며, 자식 Renderer 하나를 건물 하나로 본다.
    /// 콜라이더는 그대로 두어 안 보여도 부딪힌다.
    /// </summary>
    public sealed class BuildingCulling : MonoBehaviour
    {
        [Tooltip("캐릭터 발에서 이만큼 올라간 곳이 가려지는지 본다.")]
        [Min(0f)] [SerializeField] private float _targetHeight = 1f;

        private Renderer[] _buildings;

        private void Awake()
        {
            _buildings = GetComponentsInChildren<Renderer>(true);
        }

        private void LateUpdate()
        {
            NetworkManager manager = NetworkManager.Singleton;
            NetworkObject player = manager != null && manager.LocalClient != null ? manager.LocalClient.PlayerObject : null;
            Camera mainCamera = Camera.main;
            if (player == null || mainCamera == null)
                return;

            Vector3 origin = mainCamera.transform.position;
            Vector3 toTarget = player.transform.position + Vector3.up * _targetHeight - origin;
            Ray ray = new Ray(origin, toTarget);
            float targetDistance = toTarget.magnitude;

            foreach (Renderer building in _buildings)
            {
                bool isBlocking = building.bounds.IntersectRay(ray, out float distance) && distance < targetDistance;
                building.enabled = !isBlocking;
            }
        }
    }
}
