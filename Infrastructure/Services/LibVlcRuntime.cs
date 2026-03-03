using LibVLCSharp.Shared;
using PortablePlayer.Infrastructure.Diagnostics;

namespace PortablePlayer.Infrastructure.Services;

internal static class LibVlcRuntime
{
    private static readonly object Sync = new();
    private static int _state;

    public static bool EnsureInitialized()
    {
        lock (Sync)
        {
            if (_state == 1)
            {
                return true;
            }

            if (_state == -1)
            {
                return false;
            }

            try
            {
                LibVLCSharp.Shared.Core.Initialize();
                _state = 1;
                AppLog.Info("LibVlcRuntime", "LibVLC core initialized.");
                return true;
            }
            catch (Exception ex)
            {
                _state = -1;
                AppLog.Error("LibVlcRuntime", "LibVLC core initialization failed.", ex);
                return false;
            }
        }
    }
}
