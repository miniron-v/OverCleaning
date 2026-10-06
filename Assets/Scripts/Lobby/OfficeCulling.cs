using System.Collections.Generic;
using OverCleaning.InGame;
using Unity.Netcode;
using UnityEngine;

namespace OverCleaning.Lobby
{
    /// <summary>
    /// 자기 캐릭터가 사무실 밖에 있으면 사무실 내부와 그 안의 다른 캐릭터를 그리지 않고,
    /// 같은 크기의 반투명 외벽만 보여준다. 마을을 넓게 보도록 카메라도 넓힌다.
    /// 보는 사람마다 따로 정한다. 콜라이더는 그대로 두어 밖에서도 벽에 부딪힌다.
    /// </summary>
    public sealed class OfficeCulling : MonoBehaviour
    {
        [Tooltip("사무실 안으로 볼 영역. 이 오브젝트 기준 로컬 좌표다.")]
        [SerializeField] private Bounds _interior = new Bounds(Vector3.zero, new Vector3(15f, 10f, 11f));

        [Tooltip("밖에서 숨길 내부(바닥, 벽, 가구). 문은 넣지 않아 밖에서도 보인다.")]
        [SerializeField] private Transform _interiorRoot;

        [Tooltip("밖에서만 보일 반투명 외벽. 콜라이더가 없어야 한다.")]
        [SerializeField] private GameObject _exterior;

        [SerializeField] private CameraFollow _cameraFollow;

        private readonly List<Renderer> _playerRenderers = new List<Renderer>();
        private Renderer[] _interiorRenderers;
        private bool _isInside = true;

        private void Awake()
        {
            _interiorRenderers = _interiorRoot.GetComponentsInChildren<Renderer>(true);
            _exterior.SetActive(false);
        }

        private void Update()
        {
            NetworkManager manager = NetworkManager.Singleton;
            NetworkObject player = manager != null && manager.LocalClient != null ? manager.LocalClient.PlayerObject : null;
            if (player == null)
                return;

            SetInside(Contains(player.transform.position));
            HideOtherPlayersInside(manager, player);
        }

        private bool Contains(Vector3 position)
        {
            Vector3 local = transform.InverseTransformPoint(position);
            local.y = _interior.center.y;
            return _interior.Contains(local);
        }

        private void SetInside(bool isInside)
        {
            if (_isInside == isInside)
                return;
            _isInside = isInside;
            _cameraFollow.IsWide = !isInside;
            _exterior.SetActive(!isInside);
            foreach (Renderer interiorRenderer in _interiorRenderers)
                interiorRenderer.enabled = isInside;
        }

        /// <summary>밖에서 보면 사무실이 비어 보이므로 안에 있는 사람도 보이지 않아야 한다.</summary>
        private void HideOtherPlayersInside(NetworkManager manager, NetworkObject localPlayer)
        {
            foreach (NetworkObject spawned in manager.SpawnManager.SpawnedObjectsList)
            {
                if (!spawned.IsPlayerObject || spawned == localPlayer)
                    continue;
                bool isVisible = _isInside || !Contains(spawned.transform.position);
                spawned.GetComponentsInChildren(true, _playerRenderers);
                foreach (Renderer playerRenderer in _playerRenderers)
                    playerRenderer.enabled = isVisible;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(_interior.center, _interior.size);
        }
    }
}
