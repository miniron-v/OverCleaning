using OverCleaning.InGame;
using OverCleaning.Interaction;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace OverCleaning.Lobby
{
    /// <summary>
    /// 호텔 엘리베이터 앞의 올라가기·내려가기 버튼. 누르면 누른 사람만 다른 층 엘리베이터 앞으로 옮긴다.
    /// 층은 서로 보이지 않을 만큼 떨어진 곳에 따로 지어 두었으므로, 옮긴 뒤 카메라도 바로 따라붙인다.
    /// </summary>
    public sealed class ElevatorButton : MonoBehaviour, IInteractable
    {
        [Tooltip("도착할 층의 엘리베이터 앞.")]
        [SerializeField] private Transform _destination;

        [SerializeField] private string _prompt = "올라가기";

        [SerializeField] private CameraFollow _cameraFollow;

        public bool CanInteract => _destination != null;

        public string Prompt => _prompt;

        /// <summary>몸은 자기 것만 직접 움직이므로 자기 캐릭터만 옮긴다.</summary>
        public void Interact()
        {
            NetworkManager manager = NetworkManager.Singleton;
            NetworkObject player = manager != null && manager.LocalClient != null ? manager.LocalClient.PlayerObject : null;
            if (player == null)
                return;

            Vector3 position = _destination.position;
            player.transform.position = position;
            if (player.TryGetComponent(out Rigidbody body))
            {
                body.position = position;
                body.linearVelocity = Vector3.zero;
            }
            if (player.TryGetComponent(out NetworkTransform networkTransform))
                networkTransform.Teleport(position, player.transform.rotation, player.transform.localScale);
            _cameraFollow.SnapToTarget();
        }
    }
}
