using System.Threading.Tasks;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace OverCleaning.Network
{
    /// <summary>
    /// 방 생성과 참가를 담당한다. Relay 연결은 세션 옵션이 대신 처리하므로
    /// 이 클래스는 방 코드와 현재 세션만 들고 있는다.
    /// </summary>
    public static class GameSession
    {
        public const int MaxPlayers = 4;
        private const string NicknameProperty = "nickname";

        public static ISession Current { get; private set; }

        /// <summary>참가에 쓰는 방 코드. 세션이 없으면 빈 문자열이다.</summary>
        public static string Code => Current != null ? Current.Code : string.Empty;

        public static async Task<ISession> CreateAsync(string nickname)
        {
            await GameServices.SignInAsync();

            SessionOptions options = new SessionOptions
            {
                MaxPlayers = MaxPlayers,
                IsPrivate = true,
                PlayerProperties = BuildPlayerProperties(nickname),
            }.WithRelayNetwork();

            Current = await MultiplayerService.Instance.CreateSessionAsync(options);
            Debug.Log($"방을 만들었습니다. 코드: {Current.Code}");
            return Current;
        }

        public static async Task<ISession> JoinAsync(string code, string nickname)
        {
            await GameServices.SignInAsync();

            JoinSessionOptions options = new JoinSessionOptions
            {
                PlayerProperties = BuildPlayerProperties(nickname),
            };

            // 코드는 대소문자를 가리지 않도록 서버가 쓰는 대문자로 맞춘다.
            Current = await MultiplayerService.Instance
                .JoinSessionByCodeAsync(code.Trim().ToUpperInvariant(), options);
            Debug.Log($"방에 참가했습니다. 코드: {Current.Code}");
            return Current;
        }

        public static async Task LeaveAsync()
        {
            if (Current == null)
                return;
            await Current.LeaveAsync();
            Current = null;
        }

        private static System.Collections.Generic.Dictionary<string, PlayerProperty> BuildPlayerProperties(
            string nickname)
        {
            return new System.Collections.Generic.Dictionary<string, PlayerProperty>
            {
                // 같은 방의 다른 참가자도 읽어야 하므로 공개 범위로 둔다.
                [NicknameProperty] = new PlayerProperty(nickname, VisibilityPropertyOptions.Member),
            };
        }

        /// <summary>참가자의 닉네임. 값이 없으면 빈 문자열이다.</summary>
        public static string GetNickname(IReadOnlyPlayer player)
        {
            if (player?.Properties != null &&
                player.Properties.TryGetValue(NicknameProperty, out PlayerProperty property))
                return property.Value;
            return string.Empty;
        }
    }
}
