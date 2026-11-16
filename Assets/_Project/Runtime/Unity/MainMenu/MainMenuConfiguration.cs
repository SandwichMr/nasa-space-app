using System;
using UnityEngine;
namespace Trainer.Unity.MainMenu
{
    [CreateAssetMenu(menuName = "Trainer/Main menu configuration")]
    public sealed class MainMenuConfiguration : ScriptableObject
    {
        public string publicHost = "";
        public int publicPort;
        public string path = "/";
        public bool secure;
        [Range(3, 60)] public float timeoutSeconds = 15;
        public string Endpoint => (secure ? "wss" : "ws") + "://" + publicHost + ":" + publicPort + path;
        public bool HasEndpoint => Uri.CheckHostName(publicHost ?? "") != UriHostNameType.Unknown
            && !string.Equals(publicHost, "localhost", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(publicHost) && publicPort > 0 && publicPort <= 65535
            && !publicHost.Contains(":") && !publicHost.Contains("/")
            && (!System.Net.IPAddress.TryParse(publicHost, out var ip) || IsPublic(ip))
            && !string.IsNullOrEmpty(path) && path.StartsWith("/") && !path.Contains("@") && !path.Contains("?");
        private static bool IsPublic(System.Net.IPAddress ip)
        {
            byte[] b = ip.GetAddressBytes();
            return b.Length == 4 && b[0] != 0 && b[0] != 10 && b[0] != 127 && b[0] < 224
                && !(b[0] == 192 && b[1] == 168) && !(b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                && !(b[0] == 169 && b[1] == 254);
        }
    }

}
