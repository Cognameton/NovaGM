// File: Services/ServicesHost.cs
using System;
using NovaGM.Services.Multiplayer;
using NovaGM.Services.Streaming;

namespace NovaGM.Services
{
    public static class ServicesHost
    {
        private static LocalServer? _server;

        public static void Start(int port, bool allowLan)
        {
            Stop();
            _server = new LocalServer(GameCoordinator.Instance);
            _server.Start(port, allowLan);
        }

        public static void Stop()
        {
            try { _server?.Dispose(); } catch { }
            _server = null;
        }
    }
}
