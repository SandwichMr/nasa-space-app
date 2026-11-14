using System;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.TLS;

namespace Trainer.Unity.Networking
{
    /// <summary>
    /// Server side of the Unity Transport WebSocket binding. WebSocket (TCP) is used on every platform so desktop
    /// players, WebGL players and the server share one socket type. Reliability and ordering come from TCP:
    /// the binding sends on <c>NetworkPipeline.Null</c>. Switching to UDP would require a reliable pipeline.
    /// </summary>
    [Serializable]
    public sealed class WebSocketHostOptions
    {
        /// <summary>IPv4/IPv6 address to listen on. 0.0.0.0 = every interface; 127.0.0.1 = this machine only
        /// (e.g. behind a TLS reverse proxy on the same server).</summary>
        public string BindAddress = "0.0.0.0";
        /// <summary>0 picks a free port (tests); read the real one from <see cref="UnityTransportHost.Port"/>.</summary>
        public int Port = 7777;
        /// <summary>WebSocket URL path accepted by the server. Must match the clients' path.</summary>
        public string Path = "/";
        /// <summary>Optional WSS: PEM certificate chain issued by a public CA (browsers reject self-signed).
        /// Leave both empty for plain ws:// or when a reverse proxy terminates TLS.</summary>
        public string CertificatePemPath = "";
        public string PrivateKeyPemPath = "";
        /// <summary>A client silent for this long is dropped (Unity Transport default 30000 ms).</summary>
        public int DisconnectTimeoutMs = NetworkParameterConstants.DisconnectTimeoutMS;

        public WebSocketHostOptions Clone() => (WebSocketHostOptions)MemberwiseClone();

        internal ushort ValidatedPort() => WebSocketTransportSettings.ToPort(Port, allowZero: true);

        internal NetworkSettings CreateSettings()
        {
            ValidatedPort();
            bool hasCertificate = !string.IsNullOrEmpty(CertificatePemPath);
            if (hasCertificate != !string.IsNullOrEmpty(PrivateKeyPemPath))
                throw new ArgumentException("WSS needs both CertificatePemPath and PrivateKeyPemPath.");
            var settings = new NetworkSettings(Allocator.Temp);
            try
            {
                settings.WithNetworkConfigParameters(
                    disconnectTimeoutMS: WebSocketTransportSettings.Positive(DisconnectTimeoutMs, nameof(DisconnectTimeoutMs)));
                settings.WithWebSocketParameters(path: WebSocketTransportSettings.ToPath(Path));
                if (hasCertificate)
                    settings.WithSecureServerParameters(certificate: File.ReadAllText(CertificatePemPath),
                        privateKey: File.ReadAllText(PrivateKeyPemPath));
                return settings;
            }
            catch
            {
                settings.Dispose();
                throw;
            }
        }
    }

    /// <summary>
    /// Client side of the Unity Transport WebSocket binding (desktop and WebGL players).
    /// A WebGL page served over HTTPS must use <see cref="Secure"/> (wss://); browsers block ws:// there.
    /// </summary>
    [Serializable]
    public sealed class WebSocketClientOptions
    {
        /// <summary>Server hostname or IP address.</summary>
        public string Address = "127.0.0.1";
        public int Port = 7777;
        public string Path = "/";
        /// <summary>Use wss:// (TLS). The certificate is checked against the OS/browser trust store.</summary>
        public bool Secure;
        /// <summary>Name on the server certificate when <see cref="Secure"/>; empty = <see cref="Address"/>.</summary>
        public string ServerName = "";
        /// <summary>Time per connection attempt and number of attempts before the link is reported lost
        /// (MaxConnectionAttempts). Defaults give about 10 s instead of the transport's 60 s.</summary>
        public int ConnectTimeoutMs = NetworkParameterConstants.ConnectTimeoutMS;
        public int MaxConnectAttempts = 10;
        /// <summary>A silent host is reported lost after this long (Unity Transport default 30000 ms).</summary>
        public int DisconnectTimeoutMs = NetworkParameterConstants.DisconnectTimeoutMS;

        public WebSocketClientOptions Clone() => (WebSocketClientOptions)MemberwiseClone();

        internal ushort ValidatedPort() => WebSocketTransportSettings.ToPort(Port, allowZero: false);

        internal NetworkSettings CreateSettings()
        {
            if (string.IsNullOrWhiteSpace(Address)) throw new ArgumentException("Server address is empty.");
            ValidatedPort();
            var settings = new NetworkSettings(Allocator.Temp);
            try
            {
                settings.WithNetworkConfigParameters(
                    connectTimeoutMS: WebSocketTransportSettings.Positive(ConnectTimeoutMs, nameof(ConnectTimeoutMs)),
                    maxConnectAttempts: WebSocketTransportSettings.Positive(MaxConnectAttempts, nameof(MaxConnectAttempts)),
                    disconnectTimeoutMS: WebSocketTransportSettings.Positive(DisconnectTimeoutMs, nameof(DisconnectTimeoutMs)));
                settings.WithWebSocketParameters(path: WebSocketTransportSettings.ToPath(Path));
                if (Secure)
                    settings.WithSecureClientParameters(serverName: string.IsNullOrWhiteSpace(ServerName)
                        ? Address.Trim() : ServerName.Trim());
                return settings;
            }
            catch
            {
                settings.Dispose();
                throw;
            }
        }
    }

    internal static class WebSocketTransportSettings
    {
        internal static ushort ToPort(int port, bool allowZero)
        {
            if (port < (allowZero ? 0 : 1) || port > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(port), port, "Invalid port.");
            return (ushort)port;
        }

        internal static int Positive(int value, string name)
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(name, value, "Must be positive.");
            return value;
        }

        internal static FixedString128Bytes ToPath(string path)
        {
            string value = string.IsNullOrWhiteSpace(path) ? "/" : path.Trim();
            if (value[0] != '/') value = "/" + value;
            if (Encoding.UTF8.GetByteCount(value) > FixedString128Bytes.UTF8MaxLengthInBytes)
                throw new ArgumentException("WebSocket path is too long.", nameof(path));
            return new FixedString128Bytes(value);
        }
    }
}
