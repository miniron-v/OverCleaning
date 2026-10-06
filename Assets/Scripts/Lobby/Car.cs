using System.Collections.Generic;
using OverCleaning.Interaction;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace OverCleaning.Lobby
{
    /// <summary>
    /// 대기방 마을의 차. 상호작용하면 빈 자리 중 가장 앞자리에 타고, 다시 하면 앉은 쪽 옆으로 내린다.
    ///
    /// 탄 순서는 서버가 목록으로 들고 있고, 목록의 순서가 곧 좌석 순서다.
    /// 누가 내리면 목록에서 빠지면서 뒷사람이 한 칸씩 앞자리로 당겨진다.
    /// 맨 앞자리(운전석)에 앉은 사람이 차의 소유권을 받아 자기 방향키로 운전한다.
    ///
    /// 탄 사람은 모든 클라이언트가 각자 좌석 자리에 그린다.
    /// 탄 사람의 위치를 네트워크로만 받으면 운전자와 탄 사람의 지연이 겹쳐 다른 화면에서 차 밖으로 밀려 보인다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class Car : NetworkBehaviour, IInteractable
    {
        /// <summary>내린 사람이 차 몸체에서 이만큼 떨어져야 다시 부딪히게 한다. 캐릭터 반지름보다 조금 크다.</summary>
        private const float LeavingClearance = 0.5f;

        [Tooltip("타는 순서대로: 운전석, 조수석, 운전석 뒤, 조수석 뒤. 차 바로 아래 자식이어야 한다.")]
        [SerializeField] private Transform[] _seats;

        [Tooltip("걷는 속도(5)의 4배.")]
        [Min(0f)] [SerializeField] private float _speed = 20f;

        [Min(1f)] [SerializeField] private float _turnSpeed = 270f;

        [Tooltip("내릴 때 차 중심에서 옆으로 떨어질 거리.")]
        [Min(0f)] [SerializeField] private float _exitDistance = 2f;

        [Tooltip("탈 때와 자리를 옮길 때 좌석까지 움직이는 속도.")]
        [Min(0.1f)] [SerializeField] private float _seatMoveSpeed = 4f;

        private readonly NetworkList<ulong> _occupants = new NetworkList<ulong>();
        private readonly List<NetworkObject> _seatedPlayers = new List<NetworkObject>();
        private Dictionary<NetworkObject, Vector3> _localPositions = new Dictionary<NetworkObject, Vector3>();
        private readonly List<NetworkObject> _leavingPlayers = new List<NetworkObject>();
        private Rigidbody _rigidbody;
        private Collider _bodyCollider;

        public bool CanInteract => IsSpawned && (IsLocalSeated || _occupants.Count < _seats.Length);

        public string Prompt => IsLocalSeated ? "내리기" : "타기";

        private bool IsLocalSeated => IsSpawned && _occupants.Contains(NetworkManager.LocalClientId);

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _rigidbody.constraints = RigidbodyConstraints.FreezePositionY |
                                     RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            _bodyCollider = GetComponentInChildren<Collider>();
        }

        public override void OnNetworkSpawn()
        {
            _occupants.OnListChanged += OnOccupantsChanged;
            if (IsServer)
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            UpdatePhysicsAuthority();
            ApplySeating();
        }

        public override void OnNetworkDespawn()
        {
            _occupants.OnListChanged -= OnOccupantsChanged;
            if (IsServer && NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        public override void OnGainedOwnership() => UpdatePhysicsAuthority();
        public override void OnLostOwnership() => UpdatePhysicsAuthority();

        public void Interact()
        {
            if (IsLocalSeated)
                ExitRpc();
            else
                BoardRpc();
        }

        [Rpc(SendTo.Server)]
        private void BoardRpc(RpcParams rpcParams = default)
        {
            ulong clientId = rpcParams.Receive.SenderClientId;
            if (_occupants.Contains(clientId) || _occupants.Count >= _seats.Length)
                return;
            _occupants.Add(clientId);
            UpdateDriver();
        }

        [Rpc(SendTo.Server)]
        private void ExitRpc(RpcParams rpcParams = default)
        {
            if (_occupants.Remove(rpcParams.Receive.SenderClientId))
                UpdateDriver();
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (_occupants.Remove(clientId))
                UpdateDriver();
        }

        /// <summary>운전석에 앉은 사람에게 소유권을 넘긴다. 비면 서버가 다시 갖는다. 서버에서만 쓴다.</summary>
        private void UpdateDriver()
        {
            if (_occupants.Count > 0)
            {
                if (OwnerClientId != _occupants[0])
                    NetworkObject.ChangeOwnership(_occupants[0]);
            }
            else if (!IsOwnedByServer)
            {
                NetworkObject.RemoveOwnership();
            }
        }

        /// <summary>물리는 소유자만 맡는다. 나머지는 동기화된 위치만 따른다.</summary>
        private void UpdatePhysicsAuthority()
        {
            _rigidbody.isKinematic = !IsOwner;
        }

        private void OnOccupantsChanged(NetworkListEvent<ulong> change) => ApplySeating();

        /// <summary>목록이 바뀔 때마다 모든 클라이언트에서 누가 어느 자리에 앉았는지 다시 맞춘다.</summary>
        private void ApplySeating()
        {
            List<NetworkObject> previous = new List<NetworkObject>(_seatedPlayers);
            _seatedPlayers.Clear();
            foreach (ulong clientId in _occupants)
                _seatedPlayers.Add(FindPlayer(clientId));

            for (int seatIndex = 0; seatIndex < previous.Count; seatIndex++)
            {
                NetworkObject player = previous[seatIndex];
                if (player == null || _seatedPlayers.Contains(player))
                    continue;
                SetSeated(player, false);
                // 높이는 동기화하지 않으므로 화면마다 좌석 높이에서 땅으로 내려 준다.
                Vector3 grounded = player.transform.position;
                grounded.y = 0f;
                player.transform.position = grounded;
                // 남의 화면에서는 내릴 자리로 옮겨졌다는 소식이 늦게 오므로, 차를 벗어나기 전에 부딪히면 차가 밀린다.
                _leavingPlayers.Add(player);
                if (player.IsOwner)
                    PlaceBeside(player, seatIndex);
            }

            // 계속 앉아 있는 사람은 지금 자리에서, 새로 탄 사람은 서 있던 자리에서 좌석으로 옮겨 간다.
            Dictionary<NetworkObject, Vector3> localPositions = new Dictionary<NetworkObject, Vector3>();
            foreach (NetworkObject player in _seatedPlayers)
            {
                if (player == null)
                    continue;
                _leavingPlayers.Remove(player);
                SetSeated(player, true);
                localPositions[player] = _localPositions.TryGetValue(player, out Vector3 current)
                    ? current
                    : transform.InverseTransformPoint(player.transform.position);
            }

            _localPositions = localPositions;
        }

        /// <summary>
        /// 앉은 동안은 걷지 못하고 부딪히지도 않는다. 콜라이더가 남아 있으면 차가 탄 사람에게 막힌다.
        /// 콜라이더는 내린 뒤 차를 벗어났을 때 다시 켠다.
        /// 몸은 자기 것만 직접 움직이므로 물리 설정은 자기 캐릭터만 바꾼다.
        /// </summary>
        private static void SetSeated(NetworkObject player, bool isSeated)
        {
            if (isSeated && player.TryGetComponent(out Collider body))
                body.enabled = false;
            if (!player.IsOwner)
                return;

            if (player.TryGetComponent(out PlayerMovement movement))
                movement.enabled = !isSeated;
            if (player.TryGetComponent(out Rigidbody playerBody))
            {
                playerBody.isKinematic = isSeated;
                // 좌석 위치는 차가 그려진 뒤에 맞추므로, 보간이 남아 있으면 한 박자 늦게 따라온다.
                playerBody.interpolation = isSeated ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;
            }
        }

        /// <summary>앉았던 좌석 쪽 옆에 내려놓는다. 운전석 쪽은 왼쪽, 조수석 쪽은 오른쪽이다.</summary>
        private void PlaceBeside(NetworkObject player, int seatIndex)
        {
            Vector3 seat = _seats[Mathf.Min(seatIndex, _seats.Length - 1)].localPosition;
            float side = seat.x < 0f ? -1f : 1f;
            Vector3 position = transform.TransformPoint(new Vector3(side * _exitDistance, 0f, seat.z));
            position.y = 0f;

            player.transform.position = position;
            if (player.TryGetComponent(out Rigidbody playerBody))
                playerBody.position = position;
            // 남의 화면에서 좌석부터 차를 가로질러 미끄러지지 않고 바로 옮겨지게 한다.
            if (player.TryGetComponent(out NetworkTransform networkTransform))
                networkTransform.Teleport(position, player.transform.rotation, player.transform.localScale);
        }

        private NetworkObject FindPlayer(ulong clientId)
        {
            foreach (NetworkObject spawned in NetworkManager.SpawnManager.SpawnedObjectsList)
            {
                if (spawned.IsPlayerObject && spawned.OwnerClientId == clientId)
                    return spawned;
            }

            return null;
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsOwner)
                return;

            Vector2 input = GetDriverInput();
            if (input.sqrMagnitude < 0.01f)
            {
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
                return;
            }

            // 캐릭터처럼 누른 방향으로 차를 돌리고, 차가 보는 쪽으로 달린다.
            Quaternion target = Quaternion.LookRotation(new Vector3(input.x, 0f, input.y), Vector3.up);
            Quaternion rotation = Quaternion.RotateTowards(_rigidbody.rotation, target, _turnSpeed * Time.fixedDeltaTime);
            _rigidbody.MoveRotation(rotation);
            _rigidbody.linearVelocity = rotation * Vector3.forward * (_speed * input.magnitude);
            _rigidbody.angularVelocity = Vector3.zero;
        }

        /// <summary>자기가 운전석에 앉았을 때만 자기 방향키 입력을 준다. 아니면 멈춰 있는다.</summary>
        private Vector2 GetDriverInput()
        {
            if (_occupants.Count == 0 || _occupants[0] != NetworkManager.LocalClientId)
                return Vector2.zero;
            NetworkObject driver = NetworkManager.LocalClient.PlayerObject;
            return driver != null && driver.TryGetComponent(out PlayerMovement movement) ? movement.MoveInput : Vector2.zero;
        }

        /// <summary>
        /// 차가 그려질 위치가 정해진 뒤에 탄 사람을 좌석에 맞춘다. 위치는 차 기준으로 들고 있어 차와 함께 움직인다.
        /// 남의 캐릭터는 네트워크로 받은 위치를 덮어써서 차와 어긋나지 않게 한다.
        /// </summary>
        private void LateUpdate()
        {
            for (int seatIndex = 0; seatIndex < _seatedPlayers.Count && seatIndex < _seats.Length; seatIndex++)
            {
                NetworkObject player = _seatedPlayers[seatIndex];
                if (player == null)
                    continue;
                Vector3 local = Vector3.MoveTowards(_localPositions[player], _seats[seatIndex].localPosition,
                    _seatMoveSpeed * Time.deltaTime);
                _localPositions[player] = local;
                player.transform.position = transform.TransformPoint(local);
            }

            for (int index = _leavingPlayers.Count - 1; index >= 0; index--)
            {
                NetworkObject player = _leavingPlayers[index];
                if (player != null && !IsClearOfBody(player))
                    continue;
                if (player != null && player.TryGetComponent(out Collider body))
                    body.enabled = true;
                _leavingPlayers.RemoveAt(index);
            }
        }

        private bool IsClearOfBody(NetworkObject player)
        {
            Vector3 point = player.transform.position + Vector3.up * 0.5f;
            return (_bodyCollider.ClosestPoint(point) - point).sqrMagnitude > LeavingClearance * LeavingClearance;
        }
    }
}
