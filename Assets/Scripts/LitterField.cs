using Unity.Netcode;
using UnityEngine;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 씬에 흩어 놓은 쓰레기들의 상태를 맡는다. 주운 것은 서버가 정해 NetworkList로
    /// 내려보내므로 모두(늦게 들어온 사람 포함)가 같은 쓰레기를 본다.
    /// 배치는 씬에 손으로 하고, 목록의 순서가 곧 쓰레기의 번호다.
    /// </summary>
    public sealed class LitterField : NetworkBehaviour
    {
        [Tooltip("씬에 놓은 쓰레기들. 순서가 번호가 되므로 모든 기기가 같은 씬이어야 한다.")]
        [SerializeField] private GameObject[] _litters = new GameObject[0];

        /// <summary>쓰레기마다 주웠는지(1이면 주웠다). 서버가 올려 내려보낸다.</summary>
        private readonly NetworkList<int> _collected = new NetworkList<int>();

        /// <summary>목록이 채워졌는가. 채워지기 전의 0개를 다 주운 것으로 오해하지 않게 한다.</summary>
        public bool IsBuilt => IsSpawned && _collected.Count == _litters.Length;

        /// <summary>아직 남아 있는 쓰레기 수. 채워지기 전에는 전부 남은 것으로 친다.</summary>
        public int RemainingCount
        {
            get
            {
                if (!IsBuilt)
                    return _litters.Length;
                int count = 0;
                for (int index = 0; index < _collected.Count; index++)
                {
                    if (_collected[index] == 0)
                        count++;
                }

                return count;
            }
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer && _collected.Count == 0)
            {
                foreach (GameObject litter in _litters)
                    _collected.Add(litter != null ? 0 : 1);
            }

            _collected.OnListChanged += OnCollectedChanged;
            // 늦게 들어왔으면 이미 주운 목록이 통째로 온다. 지금 값대로 겉모습을 맞춘다.
            ApplyAll();
        }

        public override void OnNetworkDespawn()
        {
            _collected.OnListChanged -= OnCollectedChanged;
        }

        private void OnCollectedChanged(NetworkListEvent<int> change)
        {
            if (change.Index >= 0 && change.Index < _litters.Length && _litters[change.Index] != null)
                _litters[change.Index].SetActive(change.Value == 0);
        }

        private void ApplyAll()
        {
            int count = Mathf.Min(_collected.Count, _litters.Length);
            for (int index = 0; index < count; index++)
            {
                if (_litters[index] != null)
                    _litters[index].SetActive(_collected[index] == 0);
            }
        }

        /// <summary>범위 안에 아직 남아 있는 쓰레기 번호들을 담아 준다.</summary>
        public int GetLittersInRange(Vector3 center, float radius, int[] buffer)
        {
            if (!IsBuilt)
                return 0;

            int count = 0;
            float squaredRadius = radius * radius;
            for (int index = 0; index < _litters.Length && count < buffer.Length; index++)
            {
                if (_litters[index] == null || _collected[index] != 0)
                    continue;
                if ((_litters[index].transform.position - center).sqrMagnitude > squaredRadius)
                    continue;
                buffer[count++] = index;
            }

            return count;
        }

        /// <summary>서버에서만 부른다. 쓰레기를 줍고, 처음 주운 것이면 참을 돌려준다.</summary>
        internal bool ServerCollect(int index)
        {
            if (!IsServer || index < 0 || index >= _collected.Count)
                return false;
            if (_collected[index] != 0)
                return false;

            _collected[index] = 1;
            return true;
        }
    }
}
