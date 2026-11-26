using UnityEngine;

namespace OverSmithed.Core
{
    /// <summary>
    /// The save of this machine (what the players have done so far), kept in PlayerPrefs. Online games use the HOST's
    /// save: only the host reads and writes it (the world map sends the host's records to the clients). It knows if
    /// the story intro was seen and the best stars + coins of each level (by level number). FLOW-7 (#62) calls
    /// <see cref="RecordResult"/> after each shift. "New save" = nothing saved yet (or <see cref="ResetAll"/>).
    /// </summary>
    public static class GameProgress
    {
        private const string IntroSeenKey = "progress.introSeen";
        // Reset clears the records of levels 1 to this number.
        private const int MaxLevelNumber = 32;

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

        /// <summary>
        /// The best result of a level. False if it was never finished (then stars and coins are 0).
        /// </summary>
        public static bool TryGetBest(int levelNumber, out int stars, out int coins)
        {
            var played = PlayerPrefs.HasKey(StarsKey(levelNumber));
            stars = PlayerPrefs.GetInt(StarsKey(levelNumber), 0);
            coins = PlayerPrefs.GetInt(CoinsKey(levelNumber), 0);
            return played;
        }

        /// <summary>
        /// Saves the result of a finished shift. Stars and coins each keep their own best (a run with fewer stars can
        /// still beat the best coins).
        /// </summary>
        public static void RecordResult(int levelNumber, int stars, int coins)
        {
            TryGetBest(levelNumber, out var bestStars, out var bestCoins);
            PlayerPrefs.SetInt(StarsKey(levelNumber), Mathf.Max(bestStars, Mathf.Clamp(stars, 0, 3)));
            PlayerPrefs.SetInt(CoinsKey(levelNumber), Mathf.Max(bestCoins, coins));
            PlayerPrefs.Save();
        }

        /// <summary>Back to a new save (the intro plays again, no records). Settings and controls are not touched.</summary>
        public static void ResetAll()
        {
            PlayerPrefs.DeleteKey(IntroSeenKey);
            for (var level = 1; level <= MaxLevelNumber; level++)
            {
                PlayerPrefs.DeleteKey(StarsKey(level));
                PlayerPrefs.DeleteKey(CoinsKey(level));
            }
            PlayerPrefs.Save();
        }

        private static string StarsKey(int levelNumber) => $"progress.level{levelNumber}.bestStars";
        private static string CoinsKey(int levelNumber) => $"progress.level{levelNumber}.bestCoins";
    }
}
