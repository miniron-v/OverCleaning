using Unity.Netcode;
using UnityEngine;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 빗자루로 쓸어 모은 먼지 뭉치. 쓸어 올수록 커지고, 들어서 쓰레기통 옆에서
    /// 꾹 누르면 한 번에 쏟아 버린다. 비어 있으면 보이지도 집히지도 않는다.
    ///
    /// 수량은 서버가 정하고, 커지는 모습은 수량을 받은 각 기기가 그린다.
    /// 자리도 서버가 옮긴다 — 놓여 있는 동안 빗자루질하는 자리로 끌려와 모인다.
    /// </summary>
    public sealed class DustClump : CarriableItem
    {
        [Tooltip("쏟을 쓰레기통.")]
        [SerializeField] private TrashCan _trashCan;
        [Tooltip("커지는 몸통. 모인 수량의 세제곱근에 비례해 커진다.")]
        [SerializeField] private Transform _ball;
        [Tooltip("먼지 1개일 때의 지름(m).")]
        [Min(0.05f)] [SerializeField] private float _baseDiameter = 0.25f;
        [Tooltip("한 뭉치가 담을 수 있는 먼지 수. 차면 빗자루가 새 뭉치를 깐다.")]
        [Min(1)] [SerializeField] private int _capacity = 30;
        [Tooltip("터질 때 먼지를 되돌릴 DustField. 비우면 부딪쳐도 터지지 않는다.")]
        [SerializeField] private DustField _dustField;
        [Tooltip("이보다 세게 부딪쳐야 터진다. 올리면 더 세게 달려들어야 한다.")]
        [Min(0.01f)] [SerializeField] private float _burstImpulse = 0.5f;

        /// <summary>서버가 정한다. 뭉친 먼지 수. 모두가 같은 크기를 본다.</summary>
        private readonly NetworkVariable<int> _storedDust = new NetworkVariable<int>();

        /// <summary>서버에서만 쓴다. 놓여 있는 동안의 낙하 속도.</summary>
        private float _fallSpeed;

        private ParticleSystem _puffSystem;
        private Material _puffMaterial;
        private Texture2D _puffTexture;

        public int StoredDustCount => _storedDust.Value;
        public int Capacity => _capacity;
        public bool HasSpace => _storedDust.Value < _capacity;

        protected override string ItemName => "뭉친 먼지";

        /// <summary>빈 뭉치는 집을 수 없다. 보이지도 않으므로 집혀서도 안 된다.</summary>
        public override bool CanInteract => StoredDustCount > 0 && base.CanInteract;

        /// <summary>안아 든 것처럼 몸 앞에 들린다.</summary>
        protected override Vector3 HeldLocalPosition => new Vector3(0f, 0.55f, 0.45f);

        /// <summary>든 사람은 뭉치가 향한 쪽을 본다. 뭉치가 돌면 몸도 함께 돈다.</summary>
        public override Vector3 HeldFacingDirection => transform.forward;

        /// <summary>
        /// 들고 있으면 보는 쪽으로 돈다. 제 자리가 몸 중심이 아니라 몸 앞이라,
        /// 돌 때 자리도 함께 돌려 준다. 돌지 않으면 뭉치가 늘 한쪽에 붙어 있어
        /// 몸이 그쪽만 보게 된다.
        /// </summary>
        public override void AimAt(Vector3 facingDirection)
        {
            if (!IsHeld || !IsOwner || facingDirection.sqrMagnitude < 0.000001f)
                return;

            Quaternion target = Quaternion.LookRotation(facingDirection, Vector3.up);
            float alpha = 1f - Mathf.Exp(-10f * Time.fixedDeltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, alpha);
            transform.localPosition = transform.localRotation * HeldLocalPosition;
        }

        public override void OnNetworkSpawn()
        {
            _storedDust.OnValueChanged += OnStoredDustChanged;
            base.OnNetworkSpawn();
            UpdateBody();
        }

        public override void OnNetworkDespawn()
        {
            _storedDust.OnValueChanged -= OnStoredDustChanged;
            base.OnNetworkDespawn();
        }

        private void OnStoredDustChanged(int previousCount, int newCount) => UpdateBody();

        /// <summary>들리거나 놓일 때 Collider가 다시 켜지므로, 빈 몸 숨김도 다시 맞춘다.</summary>
        protected override void OnCarryChanged()
        {
            _fallSpeed = 0f;
            UpdateBody();
        }

        /// <summary>
        /// 놓여 있는 동안 중력을 받는다. 든 손 높이에서 놓여도 바닥까지 떨어진다.
        /// 자리는 서버가 옮기므로 낙하도 서버가 하고, NetworkTransform이 내려보낸다.
        /// </summary>
        private void Update()
        {
            if (!IsServer || !IsSpawned || IsHeld || StoredDustCount <= 0)
                return;

            // 몸통 바로 위에서 아래로 쏘아 바닥을 찾는다. 자기 Collider는 지나친다.
            Vector3 origin = transform.position + Vector3.up * 0.1f;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, _castBuffer, 10f,
                _obstacleLayers, QueryTriggerInteraction.Ignore);
            float floor = float.NegativeInfinity;
            for (int index = 0; index < count; index++)
            {
                if (!IsIgnoredCollider(_castBuffer[index].collider, null))
                    floor = Mathf.Max(floor, _castBuffer[index].point.y);
            }
            if (float.IsNegativeInfinity(floor))
                return;

            Vector3 position = transform.position;
            if (position.y <= floor + 0.005f)
            {
                _fallSpeed = 0f;
                return;
            }

            _fallSpeed += 9.81f * Time.deltaTime;
            position.y = Mathf.Max(floor, position.y - _fallSpeed * Time.deltaTime);
            transform.position = position;
            Physics.SyncTransforms();
        }

        /// <summary>
        /// 놓인 뭉치에 부딪치면 터진다. 미는 판정은 미는 사람의 기기에서 한다 —
        /// 남의 캐릭터는 위치만 따라올 뿐이라 그 기기에서는 충돌이 제대로 잡히지 않는다.
        /// </summary>
        private void OnCollisionEnter(Collision collision)
        {
            if (!IsSpawned || IsHeld || StoredDustCount <= 0 || collision.rigidbody == null)
                return;
            NetworkObject pusher = collision.rigidbody.GetComponent<NetworkObject>();
            if (pusher == null || !pusher.IsLocalPlayer)
                return;
            if (collision.impulse.magnitude < _burstImpulse)
                return;

            RequestBurstRpc();
        }

        /// <summary>터짐은 서버가 확정한다. 수량을 비우고 모두에게 흩어짐을 알린다.</summary>
        [Rpc(SendTo.Server)]
        private void RequestBurstRpc()
        {
            if (IsHeld || _storedDust.Value <= 0 || _dustField == null)
                return;

            int amount = _storedDust.Value;
            _storedDust.Value = 0;
            // 좌표 대신 시드를 내려보낸다. 같은 시드로 계산하면 모두 같은 자리에
            // 먼지가 놓인다. 쓰레기통이 쏟아지는 방식과 같다.
            BurstRpc(transform.position, amount, Random.Range(1, int.MaxValue));
        }

        /// <summary>모든 기기에서 먼지를 되뿌리고 뭉게 연기를 피운다.</summary>
        [Rpc(SendTo.Everyone)]
        private void BurstRpc(Vector3 center, int amount, int seed)
        {
            float radius = 0.7f + BallRadius(amount);
            if (_dustField != null)
                _dustField.SpillDust(center, radius, amount, seed);
            PlayPuff(center, amount);
        }

        /// <summary>
        /// 내려놓을 때 공의 크기만큼 사람에게서 밀어 둔다. 몸 바로 앞에 놓으면
        /// 커진 공이 사람과 겹쳐 물리가 사람을 밀어낸다.
        /// </summary>
        protected override Vector3 DropOffset
        {
            get
            {
                PlayerMovement holder = HolderBody != null ? HolderBody.GetComponent<PlayerMovement>() : null;
                Vector3 direction = holder != null ? holder.FacingDirection : transform.forward;
                return direction * (0.55f + BallRadius(StoredDustCount));
            }
        }

        /// <summary>먼지 수에 따른 공의 반지름. 세제곱근으로 커진다.</summary>
        private float BallRadius(int count) =>
            _baseDiameter * Mathf.Pow(Mathf.Max(1, count), 1f / 3f) * 0.5f;

        /// <summary>수량에 맞춰 몸집을 바꾼다. 비면 숨고, 쌓이면 세제곱근으로 커진다.</summary>
        private void UpdateBody()
        {
            bool hasDust = StoredDustCount > 0;
            if (_ball != null)
            {
                _ball.gameObject.SetActive(hasDust);
                if (hasDust)
                {
                    float diameter = BallRadius(StoredDustCount) * 2f;
                    _ball.localScale = Vector3.one * diameter;
                    _ball.localPosition = new Vector3(0f, diameter * 0.5f, 0f);
                }
            }

            // 빈 뭉치가 보이지 않는 벽처럼 길을 막거나 집히지 않게 한다.
            if (_bodyCollider != null)
                _bodyCollider.enabled = hasDust;
        }

        /// <summary>
        /// 터진 자리에서 뭉게뭉게 피어오르는 연기. 연출일 뿐이라 기기마다 모양이 달라도 된다.
        /// </summary>
        private void PlayPuff(Vector3 center, int amount)
        {
            if (_puffSystem == null)
                CreatePuffSystem();

            float radius = BallRadius(amount);
            int puffCount = Mathf.Clamp(8 + amount / 2, 8, 24);
            var emitParams = new ParticleSystem.EmitParams();
            for (int index = 0; index < puffCount; index++)
            {
                Vector3 direction = Random.onUnitSphere;
                direction.y = Mathf.Abs(direction.y) * 0.6f + 0.2f;
                emitParams.position = center + Vector3.up * radius * 0.5f;
                emitParams.velocity = direction.normalized * Random.Range(0.5f, 1.6f);
                emitParams.startSize = Random.Range(0.3f, 0.6f) + radius * 0.5f;
                emitParams.startLifetime = Random.Range(0.5f, 0.9f);
                emitParams.startColor = new Color(0.52f, 0.44f, 0.34f, 0.85f);
                _puffSystem.Emit(emitParams, 1);
            }

            _puffSystem.Play();
        }

        private void CreatePuffSystem()
        {
            var child = new GameObject("Dust Puffs");
            child.transform.SetParent(transform, false);
            _puffSystem = child.AddComponent<ParticleSystem>();
            _puffSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _puffSystem.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startSpeed = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 128;
            var emission = _puffSystem.emission;
            emission.enabled = false;
            var shape = _puffSystem.shape;
            shape.enabled = false;

            // 피어오르며 커지고, 끝에서 옅어지며 사라진다. 뭉게뭉게한 느낌을 준다.
            var sizeOverLifetime = _puffSystem.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
                1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.6f));
            var colorOverLifetime = _puffSystem.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLifetime.color = gradient;

            _puffTexture = CreatePuffTexture();
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
                shader = Shader.Find("Particles/Standard Unlit");
            _puffMaterial = new Material(shader);
            _puffMaterial.SetFloat("_Surface", 1f);
            _puffMaterial.SetOverrideTag("RenderType", "Transparent");
            _puffMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _puffMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _puffMaterial.SetFloat("_ZWrite", 0f);
            _puffMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            _puffMaterial.SetTexture("_BaseMap", _puffTexture);
            _puffMaterial.mainTexture = _puffTexture;

            var puffRenderer = child.GetComponent<ParticleSystemRenderer>();
            puffRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            puffRenderer.sharedMaterial = _puffMaterial;
        }

        /// <summary>가장자리가 부드럽게 흐려지는 둥근 연기 모양을 만든다.</summary>
        private static Texture2D CreatePuffTexture()
        {
            const int resolution = 64;
            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, true)
            {
                name = "Dust Puff",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    Vector2 point = new Vector2(x, y) / (resolution - 1f) * 2f - Vector2.one;
                    float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 1f, point.magnitude));
                    pixels[y * resolution + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(true, true);
            return texture;
        }

        // NetworkBehaviour도 OnDestroy에서 정리할 것이 있으므로 가리지 않고 이어서 부른다.
        public override void OnDestroy()
        {
            base.OnDestroy();
            if (_puffMaterial != null)
                Destroy(_puffMaterial);
            if (_puffTexture != null)
                Destroy(_puffTexture);
            if (_puffSystem != null)
                Destroy(_puffSystem.gameObject);
        }

        /// <summary>서버에서만 부른다. 빗자루가 쓸어 온 먼지를 더한다. 자리는 그대로다.</summary>
        internal void ServerAddDust(int amount)
        {
            if (IsServer && amount > 0)
                _storedDust.Value += amount;
        }

        /// <summary>
        /// 서버에서만 부른다. 빈 뭉치를 새로 쓸 자리에 깐다. 한 번 깔린 뭉치는
        /// 움직이지 않는다 — 들어서 옮기는 것만 된다.
        /// </summary>
        internal void ServerPlaceAt(Vector3 position)
        {
            if (!IsServer || IsHeld)
                return;
            position.y = transform.position.y;
            transform.position = position;
            Physics.SyncTransforms();
        }

        /// <summary>쓰레기통 옆에서 꾹 누르면 모은 먼지를 한 번에 쏟아 버린다.</summary>
        public override bool CanHoldUse =>
            IsHeld && StoredDustCount > 0 && _trashCan != null &&
            _trashCan.CanReceiveDust(HandPosition, HolderBody);

        public override void BeginHoldUse()
        {
            if (IsSpawned && IsHeldByLocalPlayer)
                DumpRpc();
        }

        /// <summary>든 사람의 손 높이. 쓰레기통까지의 거리를 재는 기준이다.</summary>
        private Vector3 HandPosition => HolderBody.position + Vector3.up * 0.5f;

        /// <summary>
        /// 쏟기는 서버가 확정한다. 쏟은 사람의 전적에 더하고, 다 비면 빈손이 어색하지
        /// 않게 바로 내려놓아 준다. 빈 뭉치는 내려놓이는 순간 보이지 않게 된다.
        /// </summary>
        [Rpc(SendTo.Server)]
        private void DumpRpc(RpcParams rpcParams = default)
        {
            if (!IsFromHolder(rpcParams) || StoredDustCount <= 0 || _trashCan == null)
                return;
            if (!_trashCan.TryReceiveDust(HandPosition, HolderBody, _storedDust.Value))
                return;

            PlayerScore score = HolderBody != null ? HolderBody.GetComponent<PlayerScore>() : null;
            if (score != null)
                score.ServerAddCleanedDust(_storedDust.Value);
            _storedDust.Value = 0;
            ServerForceDrop();
        }

        private void OnGUI()
        {
            if (!IsHeldByLocalPlayer || StoredDustCount <= 0)
                return;
            string hint = CanHoldUse ? "들기 키 꾹: 쓰레기통에 쏟기 / 짧게: 내려놓기"
                : "쓰레기통 가까이 가서 꾹 누르면 쏟습니다 (짧게: 내려놓기)";
            GUI.Box(new Rect(16f, 16f, 360f, 48f), $"뭉친 먼지 {StoredDustCount}/{_capacity}\n{hint}");
        }
    }
}
