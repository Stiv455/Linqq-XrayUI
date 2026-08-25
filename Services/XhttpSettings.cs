namespace LinqqXrayVPN.Services
{
    internal static class XhttpSettings
    {
        public const string Auto = "auto";
        public const string PacketUp = "packet-up";
        public const string StreamUp = "stream-up";
        public const string StreamOne = "stream-one";

        public static readonly string[] Modes = [Auto, PacketUp, StreamUp, StreamOne];

        public static string NormalizeMode(string? value)
        {
            value = value?.Trim().ToLowerInvariant();
            return value is Auto or PacketUp or StreamUp or StreamOne ? value : string.Empty;
        }

        public static string NormalizeNetwork(string? value)
        {
            value = value?.Trim().ToLowerInvariant();
            return value is "splithttp" or "xhttp" ? "xhttp" : value ?? "tcp";
        }
    }
}
