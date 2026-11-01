using System;
using Unity.Netcode;
using UnityEngine;

namespace OverCleaning.InGame
{
    /// <summary>
    /// 고른 캐릭터의 모델만 켠다. 모든 기기가 같은 모델을 그리도록 고른 번호를 주인이 적어 내려보낸다.
    /// 씬이 바뀌면 플레이어가 새로 스폰되므로, 이 기기에서 고른 번호는 따로 기억해 두고 다시 적는다.
    /// </summary>
    public sealed class PlayerCharacter : NetworkBehaviour
    {
        [Serializable]
        private struct Character
        {
            public string Name;
            public GameObject Model;
        }

        [Tooltip("고를 수 있는 캐릭터. 모델은 모두 Player 아래에 두고, 첫 번째만 켜 둔다.")]
        [SerializeField] private Character[] _characters;

        private readonly NetworkVariable<int> _index =
            new NetworkVariable<int>(writePerm: NetworkVariableWritePermission.Owner);

        /// <summary>이 기기에서 고른 캐릭터 번호. 다음 씬에서 다시 스폰돼도 이어 쓴다.</summary>
        public static int LocalIndex { get; set; }

        public int Count => _characters.Length;

        public string GetName(int index) => _characters[index].Name;

        public override void OnNetworkSpawn()
        {
            _index.OnValueChanged += OnIndexChanged;
            ApplyModel();
        }

        public override void OnNetworkDespawn()
        {
            _index.OnValueChanged -= OnIndexChanged;
        }

        private void Update()
        {
            // 스폰 처리 중의 쓰기는 전달되지 않을 수 있어 스폰이 끝난 뒤에 적는다. 새로 고른 번호도 여기서 적는다.
            if (IsOwner && IsSpawned && _index.Value != LocalIndex)
                _index.Value = LocalIndex;
        }

        private void OnIndexChanged(int previous, int current) => ApplyModel();

        private void ApplyModel()
        {
            for (int index = 0; index < _characters.Length; index++)
                _characters[index].Model.SetActive(index == _index.Value);
        }
    }
}
