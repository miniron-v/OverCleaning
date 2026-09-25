using System;
using OverCleaning.Network;
using OverCleaning.Stages;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace OverCleaning.Lobby
{
    /// <summary>
    /// 대기방의 단계 선택 상태를 모든 클라이언트에 맞춘다.
    ///
    /// 단계 선택을 연 사람이 주도권을 갖고, 그 사람만 스테이지를 넘기거나 시작할 수 있다.
    /// 주도권이 있는 동안 모든 클라이언트에 단계 선택 화면이 열려 같은 스테이지를 본다.
    ///
    /// 여는 방법(맵 선택 오브젝트, 버튼 등)은 이 클래스가 알지 못한다.
    /// 무엇이든 RequestOpen/RequestClose/ToggleControl만 부르면 된다.
    /// 요청은 서버가 검사해서 반영하므로, 동시에 눌러도 먼저 도착한 한 명만 주도권을 얻는다.
    /// </summary>
    public sealed class StageSelection : NetworkBehaviour
    {
        /// <summary>주도권을 가진 사람이 없을 때의 클라이언트 ID.</summary>
        public const ulong NoController = ulong.MaxValue;

        [SerializeField] private StageCatalog _catalog;

        private readonly NetworkVariable<ulong> _controllerClientId = new NetworkVariable<ulong>(NoController);
        private readonly NetworkVariable<FixedString64Bytes> _controllerNickname =
            new NetworkVariable<FixedString64Bytes>();
        private readonly NetworkVariable<int> _stageIndex = new NetworkVariable<int>();

        /// <summary>열림, 주도권, 보고 있는 스테이지 중 하나라도 바뀌면 알린다.</summary>
        public event Action StateChanged;

        /// <summary>누군가 주도권을 쥐고 있어 단계 선택 화면이 열려 있는지.</summary>
        public bool IsOpen => IsSpawned && _controllerClientId.Value != NoController;

        /// <summary>이 기기의 플레이어가 주도권을 가졌는지.</summary>
        public bool IsLocalController => IsOpen && _controllerClientId.Value == NetworkManager.LocalClientId;

        public string ControllerNickname => _controllerNickname.Value.ToString();
        public int StageIndex => _stageIndex.Value;
        public int StageCount => _catalog != null ? _catalog.Count : 0;
        public StageDefinition CurrentStage => _catalog != null ? _catalog.Get(StageIndex) : null;

        public override void OnNetworkSpawn()
        {
            _controllerClientId.OnValueChanged += OnControllerChanged;
            _controllerNickname.OnValueChanged += OnNicknameChanged;
            _stageIndex.OnValueChanged += OnStageIndexChanged;
            if (IsServer)
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;

            // 늦게 들어온 사람도 이미 열려 있는 화면을 바로 보도록 현재 값으로 한 번 알린다.
            StateChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            _controllerClientId.OnValueChanged -= OnControllerChanged;
            _controllerNickname.OnValueChanged -= OnNicknameChanged;
            _stageIndex.OnValueChanged -= OnStageIndexChanged;
            if (IsServer && NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            StateChanged?.Invoke();
        }

        /// <summary>주도권을 요청해 단계 선택을 연다. 이미 누가 쥐고 있으면 무시된다.</summary>
        public void RequestOpen()
        {
            if (!IsSpawned || IsOpen)
                return;
            FixedString64Bytes nickname = default;
            nickname.CopyFromTruncated(GameSession.LocalNickname);
            ClaimControlRpc(nickname);
        }

        /// <summary>주도권을 내려놓아 단계 선택을 닫는다. 주도권이 있을 때만 통한다.</summary>
        public void RequestClose()
        {
            if (IsLocalController)
                ReleaseControlRpc();
        }

        /// <summary>주도권이 있으면 닫고, 비어 있으면 연다.</summary>
        public void ToggleControl()
        {
            if (IsLocalController)
                RequestClose();
            else
                RequestOpen();
        }

        /// <summary>이전(-1) 또는 다음(+1) 스테이지를 본다.</summary>
        public void RequestBrowse(int direction)
        {
            if (IsLocalController && direction != 0)
                BrowseRpc(Math.Sign(direction));
        }

        /// <summary>보고 있는 스테이지를 시작한다.</summary>
        public void RequestStartStage()
        {
            if (IsLocalController)
                StartStageRpc();
        }

        [Rpc(SendTo.Server)]
        private void ClaimControlRpc(FixedString64Bytes nickname, RpcParams rpcParams = default)
        {
            if (_controllerClientId.Value != NoController)
                return;
            // 이름을 먼저 채워, 주도권이 바뀌었다는 알림을 받았을 때 이름이 비어 있지 않게 한다.
            _controllerNickname.Value = nickname;
            _controllerClientId.Value = rpcParams.Receive.SenderClientId;
        }

        [Rpc(SendTo.Server)]
        private void ReleaseControlRpc(RpcParams rpcParams = default)
        {
            if (IsSender(rpcParams))
                ClearController();
        }

        [Rpc(SendTo.Server)]
        private void BrowseRpc(int direction, RpcParams rpcParams = default)
        {
            if (!IsSender(rpcParams) || StageCount == 0)
                return;
            _stageIndex.Value = Mathf.Clamp(_stageIndex.Value + direction, 0, StageCount - 1);
        }

        [Rpc(SendTo.Server)]
        private void StartStageRpc(RpcParams rpcParams = default)
        {
            if (!IsSender(rpcParams))
                return;

            StageDefinition stage = CurrentStage;
            if (stage == null || string.IsNullOrEmpty(stage.SceneName))
            {
                Debug.LogError($"{StageIndex}번 스테이지에 시작할 씬이 지정되어 있지 않습니다.", this);
                return;
            }

            // 씬이 바뀌면 이 오브젝트도 사라지지만, 전환 중에 다른 요청이 끼어들지 않도록 먼저 닫는다.
            ClearController();
            GameSession.LoadStageScene(stage.SceneName);
        }

        /// <summary>주도권을 쥔 사람이 보낸 요청인지. 서버에서만 쓴다.</summary>
        private bool IsSender(RpcParams rpcParams)
        {
            return _controllerClientId.Value != NoController &&
                   _controllerClientId.Value == rpcParams.Receive.SenderClientId;
        }

        /// <summary>주도권을 쥔 채로 나가면 아무도 닫을 수 없게 되므로 서버가 대신 풀어준다.</summary>
        private void OnClientDisconnected(ulong clientId)
        {
            if (_controllerClientId.Value == clientId)
                ClearController();
        }

        private void ClearController()
        {
            _controllerClientId.Value = NoController;
            _controllerNickname.Value = default;
        }

        private void OnControllerChanged(ulong previous, ulong current) => StateChanged?.Invoke();
        private void OnNicknameChanged(FixedString64Bytes previous, FixedString64Bytes current) =>
            StateChanged?.Invoke();
        private void OnStageIndexChanged(int previous, int current) => StateChanged?.Invoke();
    }
}
