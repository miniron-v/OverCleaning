using Unity.Netcode;
using UnityEngine;

namespace OverCleaning.Lobby
{
    /// <summary>
    /// 자기 캐릭터가 사무실 밖에 있으면 사무실을 그리지 않는다. 보는 사람마다 따로 정한다.
    /// 콜라이더는 그대로 두어 밖에서도 벽에 부딪힌다.
    /// </summary>
    public sealed class OfficeCulling : MonoBehaviour
    {
        [Tooltip("사무실 안으로 볼 영역. 이 오브젝트 기준 로컬 좌표다.")]
        [SerializeField] private Bounds _interior = new Bounds(Vector3.zero, new Vector3(15f, 10f, 11f));

        private Renderer[] _renderers;
        private bool _isVisible = true;

        private void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
        }

        private void Update()
        {
            NetworkManager manager = NetworkManager.Singleton;
            NetworkObject player = manager != null && manager.LocalClient != null ? manager.LocalClient.PlayerObject : null;
            if (player == null)
                return;

            Vector3 local = transform.InverseTransformPoint(player.transform.position);
            local.y = _interior.center.y;
            SetVisible(_interior.Contains(local));
        }

        private void SetVisible(bool isVisible)
        {
            if (_isVisible == isVisible)
                return;
            _isVisible = isVisible;
            foreach (Renderer officeRenderer in _renderers)
                officeRenderer.enabled = isVisible;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(_interior.center, _interior.size);
        }
    }
}
