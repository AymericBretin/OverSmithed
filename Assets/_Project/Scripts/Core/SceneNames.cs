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
        public const string Game = "Game";
    }
}
