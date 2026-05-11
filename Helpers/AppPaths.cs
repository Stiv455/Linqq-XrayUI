using System;
using System.IO;

namespace LinqqXrayVPN.Helpers
{
    public static class AppPaths
    {
        public static string LocalAppDataDir { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LinqqXrayVPN");

        public static string UpdatesDir { get; } = Path.Combine(LocalAppDataDir, "Updates");
    }
}
