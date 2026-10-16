using Unity.Netcode;

namespace OverSmithed.Networking
{
    /// <summary>
    /// Server-side gate for incoming connections. The host refuses a machine when the game is already
    /// launched or when the roster is full; the refusal reason reaches the client as its DisconnectReason.
    /// </summary>
    public static class ConnectionApproval
    {
        public static void Install(NetworkManager networkManager)
        {
            networkManager.NetworkConfig.ConnectionApproval = true;
            networkManager.ConnectionApprovalCallback = Approve;
        }

        private static void Approve(NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            // Players are spawned from the roster, never automatically.
            response.CreatePlayerObject = false;
            response.Pending = false;

            // The host approves its own local client.
            if (request.ClientNetworkId == NetworkManager.ServerClientId)
            {
                response.Approved = true;
                return;
            }

            var networkManager = NetworkManager.Singleton;
            var roster = SessionRoster.Instance;
            string refusal = null;
            if (roster != null && roster.IsLocked)
            {
                refusal = "La partie a déjà commencé.";
            }
            else if ((roster != null && roster.Count >= NetworkConstants.MaxPlayers)
                     || networkManager.ConnectedClientsIds.Count >= NetworkConstants.MaxConnections)
            {
                refusal = "La partie est pleine.";
            }

            response.Approved = refusal == null;
            response.Reason = refusal;
        }
    }
}
