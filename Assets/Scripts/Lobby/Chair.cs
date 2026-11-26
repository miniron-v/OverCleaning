using OverCleaning.InGame;
using OverCleaning.Interaction;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace OverCleaning.Lobby
{
    /// <summary>
    /// 앉을 수 있는 자리. 상호작용하면 앉고, 다시 하면 일어나 의자 뒤(-Z)에 선다.
    /// 체크인 카운터 의자는 앉은 동안 컴퓨터 화면을 띄워 캐릭터를 고르게 하고, 소파 자리는 앉기만 한다.
    ///
    /// 누가 앉았는지는 서버가 정하고 모든 클라이언트가 같은 사람을 앉혀 그린다.
    /// 앉은 사람의 콜라이더를 모든 화면에서 꺼야 지나가던 사람이 부딪혀 기절시키지 않는다.
    /// </summary>
    public sealed class Chair : NetworkBehaviour, IInteractable
    {
        private const ulong NoOccupant = ulong.MaxValue;

        [Tooltip("앉을 자리. 앞(+Z)이 앉아서 바라볼 쪽이어야 한다.")]
        [SerializeField] private Transform _seat;

        [Tooltip("일어날 때 이 오브젝트 중심에서 -Z로 떨어질 거리.")]
        [Min(0f)] [SerializeField] private float _exitDistance = 0.9f;

        [Tooltip("앉은 동안 캐릭터 선택 화면을 띄울지.")]
        [SerializeField] private bool _opensCharacterSelect = true;

        private readonly NetworkVariable<ulong> _occupant = new NetworkVariable<ulong>(NoOccupant);
        private NetworkObject _seatedPlayer;
        private GUIStyle _boxStyle;
        private GUIStyle _buttonStyle;

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

        /// <summary>늦게 들어오면 앉은 사람이 의자보다 늦게 스폰될 수 있어, 다 받은 뒤 다시 앉힌다.</summary>
        protected override void OnNetworkSessionSynchronized() => ApplySeating();

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
                : FindPlayer(_occupant.Value);
            if (_seatedPlayer != null)
                SetSeated(_seatedPlayer, true);
        }

        /// <summary>GetPlayerNetworkObject는 클라이언트에서 남의 플레이어를 찾지 못해 스폰 목록에서 직접 찾는다.</summary>
        private NetworkObject FindPlayer(ulong clientId)
        {
            foreach (NetworkObject spawned in NetworkManager.SpawnManager.SpawnedObjectsList)
            {
                if (spawned.IsPlayerObject && spawned.OwnerClientId == clientId)
                    return spawned;
            }

            return null;
        }

        /// <summary>몸은 자기 것만 직접 움직이므로 조작과 위치는 자기 캐릭터만 바꾼다.</summary>
        private void SetSeated(NetworkObject player, bool isSeated)
        {
            if (player.TryGetComponent(out Collider body))
                body.enabled = !isSeated;
            if (player.TryGetComponent(out PlayerModelFacing facing))
            {
                facing.FacingTarget = isSeated ? _seat : null;
                facing.SeatPoint = isSeated ? _seat : null;
            }
            if (!player.IsOwner)
                return;

            if (player.TryGetComponent(out PlayerMovement movement))
                movement.SetLocked(this, isSeated);
            if (player.TryGetComponent(out Rigidbody playerBody))
                playerBody.isKinematic = isSeated;

            // 높이는 다른 화면에 전해지지 않으므로 몸은 늘 바닥에 둔다. 앉은 높이는 모델이 맡는다.
            Vector3 position = isSeated ? _seat.position : transform.position - transform.forward * _exitDistance;
            position.y = 0f;
            player.transform.position = position;
            if (playerBody != null)
                playerBody.position = position;
            if (player.TryGetComponent(out NetworkTransform networkTransform))
                networkTransform.Teleport(position, player.transform.rotation, player.transform.localScale);
        }

        private void OnGUI()
        {
            if (!_opensCharacterSelect || !IsLocalSeated || _seatedPlayer == null || !_seatedPlayer.TryGetComponent(out PlayerCharacter character))
                return;

            // 크기는 확대(GUI.matrix) 없이 값으로 정한다. 처음 화면(360×200, 기본 글자 13, 기본 여백 4)의 두 배다.
            const float width = 720f;
            const float height = 400f;
            const float buttonWidth = 200f;
            const float buttonHeight = 120f;
            const float buttonGap = 20f;
            const float buttonBottomMargin = 60f;
            const int fontSize = 26;
            const int padding = 8;

            _boxStyle ??= new GUIStyle(GUI.skin.box) { fontSize = fontSize, padding = new RectOffset(padding, padding, padding, padding) };
            _buttonStyle ??= new GUIStyle(GUI.skin.button) { fontSize = fontSize };

            float left = (Screen.width - width) * 0.5f;
            float top = (Screen.height - height) * 0.5f;
            GUI.Box(new Rect(left, top, width, height), "컴퓨터\n\n캐릭터 선택", _boxStyle);

            float rowWidth = character.Count * buttonWidth + (character.Count - 1) * buttonGap;
            float buttonLeft = left + (width - rowWidth) * 0.5f;
            float buttonTop = top + height - buttonHeight - buttonBottomMargin;
            for (int index = 0; index < character.Count; index++)
            {
                Rect button = new Rect(buttonLeft + index * (buttonWidth + buttonGap), buttonTop, buttonWidth, buttonHeight);
                bool isSelected = index == PlayerCharacter.LocalIndex;
                if (GUI.Toggle(button, isSelected, character.GetName(index), _buttonStyle) && !isSelected)
                    PlayerCharacter.LocalIndex = index;
            }
        }
    }
}
