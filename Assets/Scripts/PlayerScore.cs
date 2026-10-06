using OverCleaning.Network;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 한 판 동안의 플레이어 전적. 결과 대시보드가 보여 줄 수 있도록 모두에게 내려간다.
    /// 수치는 서버가 올리고, 닉네임만 제 주인이 적는다. 닉네임은 그 사람의 세션에만 있다.
    /// </summary>
    public sealed class PlayerScore : NetworkBehaviour
    {
        private readonly NetworkVariable<FixedString64Bytes> _nickname =
            new NetworkVariable<FixedString64Bytes>(
                writePerm: NetworkVariableWritePermission.Owner);

        private readonly NetworkVariable<int> _cleanedDust = new NetworkVariable<int>();
        private readonly NetworkVariable<int> _crashCount = new NetworkVariable<int>();

        public int CleanedDustCount => _cleanedDust.Value;
        public int CrashCount => _crashCount.Value;

        public string Nickname
        {
            get
            {
                string nickname = _nickname.Value.ToString();
                return string.IsNullOrEmpty(nickname) ? $"플레이어 {OwnerClientId}" : nickname;
            }
        }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner)
                return;
            FixedString64Bytes nickname = default;
            nickname.CopyFromTruncated(GameSession.LocalNickname);
            _nickname.Value = nickname;
        }

        /// <summary>서버에서만 부른다. 빨아들인 먼지를 전적에 더한다.</summary>
        internal void ServerAddCleanedDust(int amount)
        {
            if (IsServer && amount > 0)
                _cleanedDust.Value += amount;
        }

        /// <summary>서버에서만 부른다. 부딪친 횟수를 전적에 더한다.</summary>
        internal void ServerAddCrash()
        {
            if (IsServer)
                _crashCount.Value++;
        }
    }
}
