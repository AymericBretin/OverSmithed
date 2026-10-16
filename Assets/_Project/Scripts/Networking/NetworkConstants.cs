namespace OverSmithed.Networking
{
    public static class NetworkConstants
    {
        /// <summary>Maximum number of players in a session, all machines combined.</summary>
        public const int MaxPlayers = 4;

        /// <summary>
        /// Maximum number of machines in an online session. Every machine hosts at least one player,
        /// so it can never exceed <see cref="MaxPlayers"/>.
        /// </summary>
        public const int MaxConnections = MaxPlayers;

        /// <summary>Port used by the local-only host (loopback, never exposed on the network).</summary>
        public const ushort LocalPort = 7777;
    }
}
