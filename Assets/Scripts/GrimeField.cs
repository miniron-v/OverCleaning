using Unity.Netcode;
using UnityEngine;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 씬에 놓인 얼룩들의 상태를 맡는다. 남은 닦기 횟수는 서버가 깎아 NetworkList로
    /// 내려보내므로 모두(늦게 들어온 사람 포함)가 같은 얼룩을 본다.
    /// 얼룩 배치는 씬에 손으로 하고, 목록의 순서가 곧 얼룩의 번호다.
    /// 먼지처럼 수가 많지 않아서 시드 대신 상태를 그대로 동기화한다.
    /// </summary>
    public sealed class GrimeField : NetworkBehaviour
    {
        [Tooltip("씬에 놓은 얼룩들. 순서가 번호가 되므로 모든 기기가 같은 씬이어야 한다.")]
        [SerializeField] private GrimeSpot[] _spots = new GrimeSpot[0];

        /// <summary>얼룩마다 남은 닦기 횟수. 서버가 깎아 내려보낸다.</summary>
        private readonly NetworkList<int> _remainingWipes = new NetworkList<int>();

        /// <summary>남은 횟수가 채워졌는가. 채워지기 전의 0개를 다 닦은 것으로 오해하지 않게 한다.</summary>
        public bool IsBuilt => IsSpawned && _remainingWipes.Count == _spots.Length;

        /// <summary>아직 남아 있는 얼룩 수. 채워지기 전에는 전부 남은 것으로 친다.</summary>
        public int RemainingCount
        {
            get
            {
                if (!IsBuilt)
                    return _spots.Length;
                int count = 0;
                for (int index = 0; index < _remainingWipes.Count; index++)
                {
                    if (_remainingWipes[index] > 0)
                        count++;
                }

                return count;
            }
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer && _remainingWipes.Count == 0)
            {
                foreach (GrimeSpot spot in _spots)
                    _remainingWipes.Add(spot != null ? spot.WipesToClean : 0);
            }

            _remainingWipes.OnListChanged += OnRemainingChanged;
            // 늦게 들어왔으면 이미 깎인 목록이 통째로 온다. 지금 값대로 겉모습을 맞춘다.
            ApplyAll();
        }

        public override void OnNetworkDespawn()
        {
            _remainingWipes.OnListChanged -= OnRemainingChanged;
        }

        private void OnRemainingChanged(NetworkListEvent<int> change)
        {
            if (change.Index >= 0 && change.Index < _spots.Length && _spots[change.Index] != null)
                _spots[change.Index].ApplyRemaining(change.Value);
        }

        private void ApplyAll()
        {
            int count = Mathf.Min(_remainingWipes.Count, _spots.Length);
            for (int index = 0; index < count; index++)
            {
                if (_spots[index] != null)
                    _spots[index].ApplyRemaining(_remainingWipes[index]);
            }
        }

        /// <summary>범위 안에 아직 남아 있는, 이 종류의 얼룩 번호들을 담아 준다.</summary>
        public int GetSpotsInRange(Vector3 center, float radius, GrimeKind kind, int[] buffer)
        {
            if (!IsBuilt)
                return 0;

            int count = 0;
            float squaredRadius = radius * radius;
            for (int index = 0; index < _spots.Length && count < buffer.Length; index++)
            {
                GrimeSpot spot = _spots[index];
                if (spot == null || spot.Kind != kind || _remainingWipes[index] <= 0)
                    continue;
                if ((spot.transform.position - center).sqrMagnitude > squaredRadius)
                    continue;
                buffer[count++] = index;
            }

            return count;
        }

        /// <summary>
        /// 서버에서만 부른다. 얼룩을 한 번 닦고, 이번에 다 지워졌으면 참을 돌려준다.
        /// 누가 닦았는지의 전적은 부른 쪽(도구)이 챙긴다.
        /// </summary>
        internal bool ServerWipe(int index, int amount)
        {
            if (!IsServer || index < 0 || index >= _remainingWipes.Count)
                return false;
            if (_remainingWipes[index] <= 0)
                return false;

            _remainingWipes[index] = Mathf.Max(0, _remainingWipes[index] - amount);
            return _remainingWipes[index] == 0;
        }
    }
}
