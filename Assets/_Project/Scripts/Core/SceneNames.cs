namespace OverSmithed.Core
{
    /// <summary>
    /// Scene names used by the game flow. Every scene listed here must be in the build scene list,
    /// otherwise Netcode cannot load it on clients.
    /// </summary>
    public static class SceneNames
    {
        public const string Bootstrap = "Bootstrap";
        public const string Menu = "Menu";
        public const string Lobby = "Lobby";
        public const string WorldMap = "WorldMap";

        /// <summary>First level (the graybox shop). Later levels come with LevelDefinition (FLOW-6).</summary>
        public const string Game = "Game";

        /// <summary>Story cutscene played the first time level 1 starts (UI-13).</summary>
        public const string StoryIntro = "StoryIntro";
    }
}
