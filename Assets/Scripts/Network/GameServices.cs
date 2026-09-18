using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace OverCleaning.Network
{
    /// <summary>
    /// Unity Services 초기화와 익명 로그인을 한 번만 수행한다.
    /// 방 생성·참가는 로그인된 플레이어 ID를 전제로 하므로 시작 화면에서 미리 끝내둔다.
    /// </summary>
    public static class GameServices
    {
        private static Task _signInTask;

        public static bool IsSignedIn => UnityServices.State == ServicesInitializationState.Initialized &&
                                         AuthenticationService.Instance.IsSignedIn;

        public static string PlayerId => IsSignedIn ? AuthenticationService.Instance.PlayerId : null;

        /// <summary>
        /// 초기화와 로그인을 보장한다. 여러 번 불러도 진행 중인 하나의 작업을 공유한다.
        /// </summary>
        public static Task SignInAsync()
        {
            // 실패한 Task를 캐시하면 재시도가 막히므로 완료되지 않았거나 성공한 경우만 재사용한다.
            if (_signInTask == null || _signInTask.IsFaulted || _signInTask.IsCanceled)
                _signInTask = RunSignInAsync();
            return _signInTask;
        }

        private static async Task RunSignInAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                // Multiplayer Play Mode의 가상 플레이어는 같은 기기에서 동시에 로그인한다.
                // 프로필을 나누지 않으면 같은 계정을 공유해 서로의 세션을 밀어낸다.
                InitializationOptions options = new InitializationOptions();
                options.SetProfile(ResolveProfile());
                await UnityServices.InitializeAsync(options);
            }
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            Debug.Log($"Unity Services 로그인 완료: {AuthenticationService.Instance.PlayerId}");
        }

        /// <summary>
        /// 인증 계정을 나누기 위한 내부 식별자. 화면에 노출되는 닉네임과는 무관하다.
        /// </summary>
        private static string ResolveProfile()
        {
#if UNITY_EDITOR
            // 가상 플레이어는 각각 별도 프로세스로 실행된다.
            // 태그는 사용자가 지정해야 생기므로, 항상 값이 있는 프로세스 ID로 프로필을 나눈다.
            return $"Editor{System.Diagnostics.Process.GetCurrentProcess().Id}";
#else
            return "Default";
#endif
        }
    }
}
