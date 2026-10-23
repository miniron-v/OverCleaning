using OverCleaning.InGame;
using OverCleaning.Interaction;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace OverCleaning.Lobby
{
    /// <summary>
    /// 사무실 의자. 상호작용하면 앉아 책상의 컴퓨터를 보고, 다시 하면 일어나 의자 뒤에 선다.
    /// 앉은 동안 컴퓨터 화면을 띄운다. 캐릭터 커스텀을 넣을 자리로, 고를 항목은 아직 없다.
    ///
    /// 누가 앉았는지는 서버가 정하고 모든 클라이언트가 같은 사람을 앉혀 그린다.
    /// 앉은 사람의 콜라이더를 모든 화면에서 꺼야 지나가던 사람이 부딪혀 기절시키지 않는다.
    /// </summary>
    public sealed class Chair : NetworkBehaviour, IInteractable
    {
        private const ulong NoOccupant = ulong.MaxValue;

        [Tooltip("앉을 자리. 앞(+Z)이 컴퓨터 쪽이어야 한다.")]
        [SerializeField] private Transform _seat;

        [Tooltip("일어날 때 의자 중심에서 뒤로 떨어질 거리.")]
        [Min(0f)] [SerializeField] private float _exitDistance = 0.9f;

        private readonly NetworkVariable<ulong> _occupant = new NetworkVariable<ulong>(NoOccupant);
        private NetworkObject _seatedPlayer;

        public bool CanInteract => IsSpawned && (_occupant.Value == NoOccupant || IsLocalSeated);

        public string Prompt => IsLocalSeated ? "일어나기" : "앉기";

        private bool IsLocalSeated => IsSpawned && _occupant.Value == NetworkManager.LocalClientId;

        public override void OnNetworkSpawn()
        {
            _occupant.OnValueChanged += OnOccupantChanged;
            if (IsServer)
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            ApplySeating();
        }

        public override void OnNetworkDespawn()
        {
            _occupant.OnValueChanged -= OnOccupantChanged;
            if (IsServer && NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        public void Interact() => ToggleSeatRpc();

        [Rpc(SendTo.Server)]
        private void ToggleSeatRpc(RpcParams rpcParams = default)
        {
            ulong clientId = rpcParams.Receive.SenderClientId;
            if (_occupant.Value == clientId)
                _occupant.Value = NoOccupant;
            else if (_occupant.Value == NoOccupant)
                _occupant.Value = clientId;
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (_occupant.Value == clientId)
                _occupant.Value = NoOccupant;
        }

        private void OnOccupantChanged(ulong previous, ulong current) => ApplySeating();

        private void ApplySeating()
        {
            if (_seatedPlayer != null)
                SetSeated(_seatedPlayer, false);
            _seatedPlayer = _occupant.Value == NoOccupant
                ? null
                : NetworkManager.SpawnManager.GetPlayerNetworkObject(_occupant.Value);
            if (_seatedPlayer != null)
                SetSeated(_seatedPlayer, true);
        }

        /// <summary>몸은 자기 것만 직접 움직이므로 조작과 위치는 자기 캐릭터만 바꾼다.</summary>
        private void SetSeated(NetworkObject player, bool isSeated)
        {
            if (player.TryGetComponent(out Collider body))
                body.enabled = !isSeated;
            if (player.TryGetComponent(out PlayerModelFacing facing))
                facing.FacingTarget = isSeated ? _seat : null;
            if (!player.IsOwner)
                return;

            if (player.TryGetComponent(out PlayerMovement movement))
                movement.enabled = !isSeated;
            if (player.TryGetComponent(out Rigidbody playerBody))
                playerBody.isKinematic = isSeated;

            Vector3 position = isSeated ? _seat.position : transform.position - transform.forward * _exitDistance;
            if (!isSeated)
                position.y = 0f;
            player.transform.position = position;
            if (playerBody != null)
                playerBody.position = position;
            if (player.TryGetComponent(out NetworkTransform networkTransform))
                networkTransform.Teleport(position, player.transform.rotation, player.transform.localScale);
        }

        private void OnGUI()
        {
            if (!IsLocalSeated)
                return;

            const float width = 360f;
            const float height = 200f;
            GUI.Box(new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height),
                "컴퓨터\n\n캐릭터 커스텀 (준비 중)");
        }
    }
}
