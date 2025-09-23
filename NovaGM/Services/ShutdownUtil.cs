// File: Services/ShutdownUtil.cs
using System;
using NovaGM.Services.Multiplayer;
using NovaGM.Services.Streaming;

namespace NovaGM.Services
{
    public static class ShutdownUtil
    {
        public static void HardExit()
        {
            try
            {
                // stop input readers
                GameCoordinator.Instance.Cancel();
            }
            catch { }

            try
            {
                // stop SSE/web
                LocalBroadcaster.Instance.Complete();
                ServicesHost.Stop();   // safe no-op if not started
            }
            catch { }

            try
            {
                Environment.Exit(0);
            }
            catch { /* as last resort */ }
        }
    }
}
