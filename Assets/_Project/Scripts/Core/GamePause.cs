using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OverSmithed.Core
{
    /// <summary>
    /// Is the pause menu open on this machine? Local only: nothing goes on the network.
    /// In a local game the pause menu also stops time (<c>Time.timeScale</c> = 0: shift timer, movement, physics and
    /// animations freeze). Online it doesn't: the game keeps running for the other machines, only the players of this
    /// machine stop (<see cref="BlocksPlayerInput"/>, read by NetworkPlayer).
    /// Time always runs again when the menu closes or the scene changes.
    /// </summary>
    public static class GamePause
    {
        private static int _closedFrame = -1;

        /// <summary>True while the pause menu is open on this machine.</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>True while the pause menu holds time stopped (local games).</summary>
        public static bool IsTimeStopped { get; private set; }

        /// <summary>
        /// The players of this machine must not act: the menu is open, or it closed this frame (the press on
        /// "Resume" must not also be an Interact).
        /// </summary>
        public static bool BlocksPlayerInput => IsOpen || Time.frameCount <= _closedFrame;

        /// <summary>Raised when the menu opens or closes (later: the audio manager can lower the music).</summary>
        public static event Action Changed;

        /// <summary>Opens the pause. <paramref name="stopTime"/>: true in a local game.</summary>
        public static void Open(bool stopTime)
        {
            if (IsOpen)
            {
                return;
            }
            IsOpen = true;
            SetTimeStopped(stopTime);
            Changed?.Invoke();
        }

        /// <summary>Closes the pause; time runs again.</summary>
        public static void Close()
        {
            if (!IsOpen)
            {
                return;
            }
            IsOpen = false;
            _closedFrame = Time.frameCount;
            SetTimeStopped(false);
            Changed?.Invoke();
        }

        /// <summary>
        /// Stops or restarts time while the menu stays open. Leaving the level (back to the map, quit) restarts it
        /// first: a scene must never load with time stopped.
        /// </summary>
        public static void SetTimeStopped(bool stopped)
        {
            stopped &= IsOpen;
            if (stopped == IsTimeStopped)
            {
                return;
            }
            IsTimeStopped = stopped;
            Time.timeScale = stopped ? 0f : 1f;
        }

        // Domain reload is off: forget everything when Play Mode starts. A scene change always closes the pause
        // (the menu belongs to the level scene), even if the menu could not do it itself.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            IsOpen = false;
            IsTimeStopped = false;
            _closedFrame = -1;
            Changed = null;
            Time.timeScale = 1f;
            SceneManager.activeSceneChanged -= CloseOnSceneChange;
            SceneManager.activeSceneChanged += CloseOnSceneChange;
        }

        private static void CloseOnSceneChange(Scene from, Scene to)
        {
            Close();
        }
    }
}
