using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace OverSmithed.Networking
{
    /// <summary>
    /// Initializes Unity Gaming Services and signs the player in anonymously (needed by Sessions and Relay).
    /// Only used for online play: local play never touches the services.
    /// </summary>
    public static class ServicesInitializer
    {
        private static Task _signInTask;

        // Domain reload is disabled on entering Play Mode: statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _signInTask = null;
        }

        public static Task EnsureSignedInAsync()
        {
            // A failed attempt can be retried; a pending or successful one is shared.
            if (_signInTask == null || _signInTask.IsFaulted || _signInTask.IsCanceled)
            {
                _signInTask = SignInAsync();
            }
            return _signInTask;
        }

        private static async Task SignInAsync()
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                var options = new InitializationOptions();
                var profile = GetProfile();
                if (profile != null)
                {
                    options.SetProfile(profile);
                }
                await UnityServices.InitializeAsync(options);
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
            Debug.Log($"[Services] Signed in as {AuthenticationService.Instance.PlayerId} (profile {AuthenticationService.Instance.Profile}).");
        }

        // Every Multiplayer Play Mode instance shares the same PlayerPrefs, hence the same anonymous
        // account: two instances would be the same player and could not join each other's session.
        // Each virtual player runs from its own project copy, so its data path gives it its own profile.
        private static string GetProfile()
        {
#if UNITY_EDITOR
            if (!Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor)
            {
                return "mppm_" + Hash128.Compute(Application.dataPath).ToString().Substring(0, 8);
            }
#endif
            return null;
        }
    }
}
