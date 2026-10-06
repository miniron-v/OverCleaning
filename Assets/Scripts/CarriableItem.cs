using OverCleaning.Interaction;
using Unity.Netcode;
using UnityEngine;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 상호작용 키로 들어 나를 수 있는 물건.
    ///
    /// 누가 들고 있는지는 서버가 정한다. 상호작용은 요청일 뿐이고, 자리 검사도 서버가 해서
    /// 동시에 눌러도 먼저 도착한 한 명만 든다. 서버가 TrySetParent로 든 사람에게 붙이면
    /// 부모 관계와 위치·회전은 NGO(부모 동기화 + NetworkTransform)가 모든 기기에 맞춰 준다.
    ///
    /// 상호작용으로는 들기만 한다. 들린 뒤의 내려놓기는 든 사람의 ItemCarrier가
    /// 같은 키의 짧게/길게를 가려서 처리한다.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public abstract class CarriableItem : NetworkBehaviour, IInteractable
    {
        /// <summary>아무도 들고 있지 않을 때의 클라이언트 ID.</summary>
        public const ulong NoHolder = ulong.MaxValue;

        /// <summary>
        /// 자리 검사에서 상자를 줄여 볼 두께. 바닥에 딱 붙어 놓이는 물건은 검사 상자의 밑면이
        /// 바닥 표면과 같은 평면에 놓여, 제가 딛고 설 바닥을 장애물로 세어 버린다.
        /// </summary>
        private const float PlacementSkin = 0.01f;

        /// <summary>드는 방식. 물건마다 에디터에서 고른다.</summary>
        public enum CarryMode
        {
            /// <summary>장착: 물건이 든 사람의 정해진 자리로 붙어 함께 움직인다. 예: 청소기.</summary>
            Equip,

            /// <summary>들기: 잡은 자리에서 그대로, 그 간격을 유지한 채 따라온다. 예: 쓰레기통.</summary>
            Carry,
        }

        [Tooltip("장착은 든 사람의 정해진 자리로 붙고, 들기는 잡은 자리 그대로 들려 따라온다.")]
        [SerializeField] private CarryMode _carryMode = CarryMode.Equip;

        [Tooltip("들기 물건이 보는 쪽을 따라 도는 속도. 클수록 빨리 따라붙는다.")]
        [Min(0.1f)] [SerializeField] private float _carryFollowSharpness = 8f;

        [Tooltip("물건을 가리거나 놓지 못하게 막는 벽과 장애물 레이어.")]
        [SerializeField] protected LayerMask _obstacleLayers = ~0;

        /// <summary>서버가 정한다. 들고 있는 사람.</summary>
        private readonly NetworkVariable<ulong> _holderClientId = new NetworkVariable<ulong>(NoHolder);

        protected BoxCollider _bodyCollider;
        protected readonly Collider[] _overlapBuffer = new Collider[32];
        protected readonly RaycastHit[] _castBuffer = new RaycastHit[32];

        /// <summary>잡는 순간의 간격과 각도. 든 사람이 보는 방향 기준으로 기억해 둔다.</summary>
        private Vector3 _carryGrabOffset;
        private float _carryGrabYaw;

        public bool IsHeld => HolderBody != null;

        /// <summary>지금 이 물건을 든 사람. 들려 있지 않으면 null이다.</summary>
        protected Rigidbody HolderBody { get; private set; }

        /// <summary>이 기기의 플레이어가 들고 있는지.</summary>
        public bool IsHeldByLocalPlayer =>
            IsSpawned && _holderClientId.Value == NetworkManager.LocalClientId;

        /// <summary>안내 문구에 쓸 이름. 예: "청소기".</summary>
        protected abstract string ItemName { get; }

        /// <summary>들었을 때 든 사람 기준으로 놓일 자리.</summary>
        protected virtual Vector3 HeldLocalPosition => Vector3.zero;

        /// <summary>내려놓을 때 든 사람에게서 밀어낼 방향과 거리. 사람 몸과 겹치지 않게 한다.</summary>
        protected virtual Vector3 DropOffset => transform.forward * 0.3f;

        /// <summary>
        /// 들린 물건은 ItemCarrier가 다루므로 상호작용 대상에서 빠진다. 손이 찬 사람에게는
        /// 다른 물건도 대상이 아니다. 한 번에 하나만 들 수 있어서 눌러 봐야 소용이 없다.
        /// </summary>
        public virtual bool CanInteract => isActiveAndEnabled && !IsHeld && !IsLocalPlayerCarrying;

        public virtual string Prompt => $"{ItemName} 들기";

        private static bool IsLocalPlayerCarrying
        {
            get
            {
                NetworkManager manager = NetworkManager.Singleton;
                if (manager == null || !manager.IsListening || manager.SpawnManager == null)
                    return false;
                NetworkObject player = manager.SpawnManager.GetLocalPlayerObject();
                return player != null && player.GetComponentInChildren<CarriableItem>() != null;
            }
        }

        protected virtual void Awake()
        {
            _bodyCollider = GetComponent<BoxCollider>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
                NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;

            // 늦게 들어오면 이미 든 사람에게 붙은 채로 스폰된다. 현재 부모 기준으로 맞춘다.
            OnNetworkObjectParentChanged(
                transform.parent != null ? transform.parent.GetComponent<NetworkObject>() : null);
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        /// <summary>
        /// 서버가 부모를 바꾸면 NGO가 모든 기기에서 이것을 불러 준다.
        /// 여기서 상태를 읽으므로 들림/놓임을 따로 전파할 필요가 없다.
        /// </summary>
        public override void OnNetworkObjectParentChanged(NetworkObject parentNetworkObject)
        {
            HolderBody = parentNetworkObject != null ? parentNetworkObject.GetComponent<Rigidbody>() : null;

            // 재등록하여 놓은 Collider가 사람의 복합 Collider로 남지 않게 한다.
            _bodyCollider.enabled = false;
            _bodyCollider.enabled = true;
            // 장착만 정해진 자리로 붙인다. 들기는 잡은 자리 그대로 들려 따라온다.
            if (HolderBody != null && _carryMode == CarryMode.Equip)
                transform.localPosition = HeldLocalPosition;
            if (HolderBody != null && _carryMode == CarryMode.Carry)
            {
                // 잡는 순간의 간격을 보는 방향 기준으로 기억한다. 몸을 돌리면 간격째로 따라온다.
                Quaternion inverseFacing = Quaternion.Inverse(
                    Quaternion.LookRotation(GetHolderFacing(), Vector3.up));
                _carryGrabOffset = inverseFacing * transform.localPosition;
                _carryGrabYaw = (inverseFacing * transform.localRotation).eulerAngles.y;
            }
            Physics.SyncTransforms();
            OnCarryChanged();
        }

        /// <summary>들리거나 내려놓인 직후에 불린다. 들린 상태에 따라 달라지는 것을 여기서 맞춘다.</summary>
        protected virtual void OnCarryChanged()
        {
        }

        /// <summary>
        /// 든 사람이 보는 쪽에 반응한다. 들기 물건은 잡았을 때의 간격을 유지한 채
        /// 보는 방향을 따라 부드럽게 돌아 따라온다. 든 사람의 기기에서만 불린다.
        /// </summary>
        public virtual void AimAt(Vector3 facingDirection)
        {
            if (_carryMode != CarryMode.Carry || !IsHeld || !IsOwner ||
                facingDirection.sqrMagnitude < 0.000001f)
                return;

            // 든 사람의 몸통은 돌지 않으므로 로컬 좌표가 곧 사람 기준 간격이다.
            Quaternion facing = Quaternion.LookRotation(facingDirection, Vector3.up);
            float alpha = 1f - Mathf.Exp(-_carryFollowSharpness * Time.fixedDeltaTime);
            transform.localPosition = Vector3.Lerp(
                transform.localPosition, facing * _carryGrabOffset, alpha);
            transform.localRotation = Quaternion.Slerp(
                transform.localRotation, facing * Quaternion.Euler(0f, _carryGrabYaw, 0f), alpha);
        }

        /// <summary>든 사람이 보는 방향. 알 수 없으면 정면으로 친다.</summary>
        private Vector3 GetHolderFacing()
        {
            PlayerMovement holderMovement =
                HolderBody != null ? HolderBody.GetComponent<PlayerMovement>() : null;
            Vector3 facingDirection =
                holderMovement != null ? holderMovement.FacingDirection : Vector3.forward;
            return facingDirection.sqrMagnitude > 0.000001f ? facingDirection : Vector3.forward;
        }

        /// <summary>들기를 서버에 요청한다. 자기 캐릭터를 조작하는 클라이언트에서만 불린다.</summary>
        public virtual void Interact()
        {
            if (!IsSpawned)
            {
                Debug.LogWarning($"네트워크에 올라와 있지 않아 들 수 없습니다({ItemName}). " +
                    "이 오브젝트에 NetworkObject를 붙였는지, 씬을 NGO 씬 매니저로 불러왔는지 확인하세요.", this);
                return;
            }

            RequestPickUpRpc();
        }

        /// <summary>내려놓기를 서버에 요청한다. 든 사람의 기기에서 부른다.</summary>
        public void RequestDrop()
        {
            if (IsSpawned && IsHeldByLocalPlayer)
                RequestDropRpc();
        }

        /// <summary>이 요청을 보낸 사람이 지금 들고 있는 사람인가. 아닌 요청은 버린다.</summary>
        protected bool IsFromHolder(RpcParams rpcParams)
        {
            return _holderClientId.Value == rpcParams.Receive.SenderClientId;
        }

        /// <summary>들기를 서버가 판정한다. 누가 눌렀는지는 보낸 사람으로 알 수 있다.</summary>
        [Rpc(SendTo.Server)]
        private void RequestPickUpRpc(RpcParams rpcParams = default)
        {
            if (_holderClientId.Value == NoHolder)
                ServerPickUp(rpcParams.Receive.SenderClientId);
            else
                Debug.Log($"{_holderClientId.Value}번 플레이어가 이미 들고 있습니다({ItemName}).", this);
        }

        [Rpc(SendTo.Server)]
        private void RequestDropRpc(RpcParams rpcParams = default)
        {
            if (IsFromHolder(rpcParams))
                ServerDrop();
        }

        private void ServerPickUp(ulong clientId)
        {
            if (!CanServerPickUp())
                return;

            // 서버에서만 부르므로 남의 플레이어도 조회할 수 있다.
            NetworkObject player = NetworkManager.SpawnManager.GetPlayerNetworkObject(clientId);
            Rigidbody holderBody = player != null ? player.GetComponent<Rigidbody>() : null;
            if (holderBody == null)
            {
                Debug.LogWarning($"{clientId}번 플레이어의 Rigidbody를 찾지 못해 들 수 없습니다({ItemName}).", this);
                return;
            }
            // 한 번에 하나만 든다. 들린 물건은 든 사람의 자식이므로 자식만 보면 된다.
            if (holderBody.GetComponentInChildren<CarriableItem>() != null)
            {
                Debug.Log($"이미 다른 것을 들고 있어 들 수 없습니다({ItemName}).", this);
                return;
            }
            // 장착은 물건이 든 사람 자리로 옮겨 가므로 가는 길을 검사한다. 들어 올리는 동안
            // 든 사람과 겹치는 것은 당연하므로 장애물로 보지 않는다. 들기는 안 움직이니 안 본다.
            if (_carryMode == CarryMode.Equip &&
                !CanPlace(transform.position, holderBody.position, holderBody, true))
            {
                Debug.LogWarning($"들 자리가 막혀 있어 들 수 없습니다({ItemName}).", this);
                return;
            }
            if (!NetworkObject.TrySetParent(player))
            {
                Debug.LogWarning($"든 사람에게 붙이지 못했습니다({ItemName}). AutoObjectParentSync 설정을 확인하세요.", this);
                return;
            }

            _holderClientId.Value = clientId;
            // 든 사람만 아는 방향 조작을 전파할 수 있도록 그 사람에게 쓰기 권한을 넘긴다.
            NetworkObject.ChangeOwnership(clientId);
        }

        /// <summary>내려놓기 자리를 찾을 때 바라보는 쪽부터 넓혀 가는 각도들.</summary>
        private static readonly float[] DropSearchAngles = { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 135f, -135f, 180f };

        private void ServerDrop()
        {
            Rigidbody holderBody = HolderBody;
            ServerRelease(holderBody != null ? FindDropPosition(holderBody) : transform.position);
        }

        /// <summary>
        /// 바라보는 쪽을 먼저 보고, 막혀 있으면 좌우로 각도를 넓혀 가며 놓을 자리를 찾는다.
        /// 사방이 다 막혀 있으면 들려 있던 그 자리에 놓는다. 내려놓기가 거부되는 경우는 없다.
        /// </summary>
        private Vector3 FindDropPosition(Rigidbody holderBody)
        {
            Vector3 offset = DropOffset;
            float distance = offset.magnitude;
            Vector3 forward = distance > 0.0001f ? offset / distance : transform.forward;

            foreach (float angle in DropSearchAngles)
            {
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * forward;
                Vector3 destination = holderBody.position + direction * distance;
                if (CanPlace(holderBody.position, destination, holderBody, false) &&
                    HasGroundSupport(destination))
                    return destination;
            }

            return transform.position;
        }

        /// <summary>
        /// 서버가 물건을 바닥에 놓는다. 부모를 떼고 권한을 되찾아 최종 자리를 박으면
        /// NGO가 모든 기기(늦게 들어온 사람 포함)에 같은 위치와 방향을 맞춰 준다.
        /// </summary>
        private void ServerRelease(Vector3 destination)
        {
            _holderClientId.Value = NoHolder;
            NetworkObject.TryRemoveParent();
            NetworkObject.RemoveOwnership();
            transform.position = destination;
            Physics.SyncTransforms();
        }

        /// <summary>서버에서만 부른다. 부딪친 사람의 물건을 그 자리에 떨어뜨릴 때 쓴다.</summary>
        internal void ServerForceDrop()
        {
            if (IsServer && IsHeld)
                ServerDrop();
        }

        /// <summary>들 수 있는 상태인지. 넘어져 있는 것처럼 들면 안 되는 사정을 여기서 막는다.</summary>
        protected virtual bool CanServerPickUp() => true;

        /// <summary>들고 있던 사람이 나가면 그 자리에 놓아둔다. 아니면 아무도 들 수 없게 된다.</summary>
        private void OnClientDisconnected(ulong clientId)
        {
            if (_holderClientId.Value == clientId)
                ServerRelease(transform.position);
        }

        protected bool IsIgnoredCollider(Collider other, Rigidbody ignoredBody)
        {
            return other == _bodyCollider || (ignoredBody != null && other.attachedRigidbody == ignoredBody);
        }

        /// <summary>
        /// 놓으려는 자리가 비어 있는지, 가는 길에 얇은 벽을 가로지르지 않는지 본다.
        ///
        /// 검사하는 높이는 출발 자리 기준이다. 들어 올린 높이에서 바닥으로 내려오는 경로를
        /// 그대로 쓰면 검사 상자가 바닥을 향해 파고들어 늘 막힌다. 들어 올리고 내리는 것은
        /// 벽을 지나는 것과 상관이 없으므로 수평으로만 본다.
        /// </summary>
        protected bool CanPlace(Vector3 origin, Vector3 destination, Rigidbody ignoredBody,
            bool ignoreBodyAtDestination)
        {
            Physics.SyncTransforms();
            Vector3 halfExtents = Vector3.Max(
                Vector3.Scale(_bodyCollider.size, transform.lossyScale) * 0.5f - Vector3.one * PlacementSkin,
                Vector3.one * 0.001f);
            Quaternion rotation = transform.rotation;
            Vector3 currentCenter = origin + rotation * Vector3.Scale(_bodyCollider.center, transform.lossyScale);
            Vector3 displacement = destination - origin;
            int count = Physics.OverlapBoxNonAlloc(currentCenter + displacement, halfExtents,
                _overlapBuffer, rotation, _obstacleLayers, QueryTriggerInteraction.Ignore);
            if (count == _overlapBuffer.Length)
                return false;
            for (int index = 0; index < count; index++)
            {
                if (!IsIgnoredCollider(_overlapBuffer[index], ignoreBodyAtDestination ? ignoredBody : null))
                    return false;
            }

            // 들거나 놓을 때 위치만 바꾸어 얇은 벽 반대편으로 통과하지 않도록 검사한다.
            if (displacement.sqrMagnitude < 0.000001f)
                return true;
            count = Physics.BoxCastNonAlloc(currentCenter, halfExtents, displacement.normalized,
                _castBuffer, rotation, displacement.magnitude, _obstacleLayers, QueryTriggerInteraction.Ignore);
            if (count == _castBuffer.Length)
                return false;
            for (int index = 0; index < count; index++)
            {
                if (!IsIgnoredCollider(_castBuffer[index].collider, ignoredBody))
                    return false;
            }

            return true;
        }

        protected bool HasGroundSupport(Vector3 destination)
        {
            Vector3 halfExtents = _bodyCollider.size * 0.5f;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = _bodyCollider.center +
                        new Vector3(x * halfExtents.x, -halfExtents.y + 0.1f, z * halfExtents.z);
                    Vector3 origin = destination + transform.rotation *
                        Vector3.Scale(corner, transform.lossyScale);
                    int count = Physics.RaycastNonAlloc(origin, Vector3.down, _castBuffer,
                        0.2f, _obstacleLayers, QueryTriggerInteraction.Ignore);
                    bool supported = false;
                    for (int index = 0; index < count; index++)
                    {
                        if (!IsIgnoredCollider(_castBuffer[index].collider, HolderBody) &&
                            _castBuffer[index].normal.y > 0.99f)
                            supported = true;
                    }
                    if (!supported)
                        return false;
                }
            }

            return true;
        }
    }
}
