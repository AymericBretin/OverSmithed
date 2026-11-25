using UnityEngine;

namespace OverSmithed.Core
{
    /// <summary>
    /// The save of this machine (what the players have done so far), kept in PlayerPrefs. Online games use the HOST's
    /// save: only the host reads and writes it. For now it only knows if the story intro was seen; the best stars of
    /// each level (FLOW-7) go here too. "New save" = nothing saved yet (or <see cref="ResetAll"/>).
    /// </summary>
    public static class GameProgress
    {
        private const string IntroSeenKey = "progress.introSeen";

        /// <summary>True once the story intro was played to the end or skipped.</summary>
        public static bool IntroSeen => PlayerPrefs.GetInt(IntroSeenKey, 0) == 1;

        public static void MarkIntroSeen()
        {
            if (IntroSeen)
            {
                return;
            }
            PlayerPrefs.SetInt(IntroSeenKey, 1);
            PlayerPrefs.Save();
        }

        /// <summary>Back to a new save (the intro plays again). Settings and controls are not touched.</summary>
        public static void ResetAll()
        {
            PlayerPrefs.DeleteKey(IntroSeenKey);
            PlayerPrefs.Save();
        }
    }
}
