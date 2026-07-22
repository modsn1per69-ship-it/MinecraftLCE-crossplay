using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

internal static class LocalRelayServer
{
    private const int MaxFrameSize = 16 * 1024 * 1024;
    private const int MaxHandshakeLength = 4096;

    private sealed class WaitingHost
    {
        public string BuildId;
        public TcpClient Client;
    }

    private sealed class Peer
    {
        public uint Id;
        public TcpClient Client;
        public NetworkStream Stream;
        public readonly object WriteLock = new object();
    }

    private sealed class HostSession
    {
        public string SessionId;
        public string BuildId;
        public TcpClient Client;
        public NetworkStream Stream;
        public readonly Dictionary<uint, Peer> Peers = new Dictionary<uint, Peer>();
        public readonly object WriteLock = new object();
        public uint NextPeerId = 2;
        public bool ReadySent;
        public bool Closed;
    }

    private static readonly object Sync = new object();
    private static readonly object LogSync = new object();
    private static readonly Dictionary<string, WaitingHost> Hosts = new Dictionary<string, WaitingHost>();
    private static readonly Dictionary<string, HostSession> HubHosts = new Dictionary<string, HostSession>();
    private static string LogPath;
    private static StreamWriter LogWriter;
    private static bool VerboseTraffic;
    private static string AccessToken = "";
    private static int MaxSessions = 64;
    private static int MaxPeersPerSession = 8;
    private static int HandshakeTimeoutMs = 10000;
    private static Semaphore HandshakeSlots;

    private static void Log(string message)
    {
        string line = DateTime.Now.ToString("O") + " " + message;
        lock (LogSync)
        {
            try { Console.WriteLine(line); } catch (IOException) { }
            try
            {
                if (LogWriter != null)
                {
                    LogWriter.WriteLine(line);
                    LogWriter.Flush();
                }
            }
            catch (IOException) { }
        }
    }

    private static void LogTraffic(string message)
    {
        if (VerboseTraffic)
        {
            Log(message);
        }
    }

    private static int ReadEnvironmentInt(string name, int fallback, int minimum, int maximum)
    {
        string value = Environment.GetEnvironmentVariable(name);
        int parsed;
        if (!String.IsNullOrEmpty(value) && Int32.TryParse(value, out parsed)
            && parsed >= minimum && parsed <= maximum)
        {
            return parsed;
        }
        return fallback;
    }

    private static bool ConstantTimeEquals(string expected, string actual)
    {
        expected = expected ?? "";
        actual = actual ?? "";
        int difference = expected.Length ^ actual.Length;
        int length = Math.Max(expected.Length, actual.Length);
        for (int i = 0; i < length; i++)
        {
            char left = i < expected.Length ? expected[i] : (char)0;
            char right = i < actual.Length ? actual[i] : (char)0;
            difference |= left ^ right;
        }
        return difference == 0;
    }

    private static bool IsValidHandshakeValue(string value, int maximumLength)
    {
        if (String.IsNullOrEmpty(value) || value.Length > maximumLength)
        {
            return false;
        }
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] <= ' ' || value[i] > '~')
            {
                return false;
            }
        }
        return true;
    }

    private static string RemoteAddress(TcpClient client)
    {
        try
        {
            IPEndPoint endpoint = client.Client.RemoteEndPoint as IPEndPoint;
            return endpoint != null ? endpoint.Address.ToString() : "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    private static string ReadLine(NetworkStream stream)
    {
        MemoryStream buffer = new MemoryStream();
        while (buffer.Length < MaxHandshakeLength)
        {
            int value = stream.ReadByte();
            if (value < 0)
            {
                throw new EndOfStreamException();
            }
            if (value == '\n')
            {
                return Encoding.UTF8.GetString(buffer.ToArray()).TrimEnd('\r');
            }
            buffer.WriteByte((byte)value);
        }
        throw new InvalidDataException("Handshake line is too long.");
    }

    private static void WriteLine(NetworkStream stream, string line)
    {
        byte[] data = Encoding.UTF8.GetBytes(line + "\n");
        stream.Write(data, 0, data.Length);
        stream.Flush();
    }

    private static bool ReadExact(NetworkStream stream, byte[] buffer, int offset, int count)
    {
        while (count > 0)
        {
            int received = stream.Read(buffer, offset, count);
            if (received <= 0)
            {
                return false;
            }
            offset += received;
            count -= received;
        }
        return true;
    }

    private static uint ReadUInt32(byte[] data, int offset)
    {
        return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16)
            | ((uint)data[offset + 2] << 8) | data[offset + 3];
    }

    private static void WriteUInt32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)(value >> 16);
        data[offset + 2] = (byte)(value >> 8);
        data[offset + 3] = (byte)value;
    }

    private static void WriteHostFrame(HostSession host, byte type, uint peerId, byte[] data, int offset, int count)
    {
        byte[] header = new byte[9];
        header[0] = type;
        WriteUInt32(header, 1, peerId);
        WriteUInt32(header, 5, (uint)count);
        lock (host.WriteLock)
        {
            host.Stream.Write(header, 0, header.Length);
            if (count > 0)
            {
                host.Stream.Write(data, offset, count);
            }
        }
    }

    private static void CloseHub(HostSession host, string reason)
    {
        List<Peer> peers;
        lock (Sync)
        {
            if (host.Closed)
            {
                return;
            }
            host.Closed = true;
            HostSession registered;
            if (HubHosts.TryGetValue(host.SessionId, out registered) && Object.ReferenceEquals(registered, host))
            {
                HubHosts.Remove(host.SessionId);
            }
            peers = new List<Peer>(host.Peers.Values);
            host.Peers.Clear();
        }

        try { host.Client.Close(); } catch { }
        foreach (Peer peer in peers)
        {
            try { peer.Client.Close(); } catch { }
        }
        Log("hub closed session=" + host.SessionId + " reason=" + reason);
    }

    private static void HostReader(object state)
    {
        HostSession host = (HostSession)state;
        byte[] header = new byte[9];
        try
        {
            while (ReadExact(host.Stream, header, 0, header.Length))
            {
                byte type = header[0];
                uint peerId = ReadUInt32(header, 1);
                uint length = ReadUInt32(header, 5);
                if (type != (byte)'D' || peerId < 2 || length > MaxFrameSize)
                {
                    throw new InvalidDataException("invalid host frame header=" + BitConverter.ToString(header)
                        + " type=" + (char)type + " peer=" + peerId + " length=" + length);
                }

                byte[] payload = new byte[(int)length];
                if (length > 0 && !ReadExact(host.Stream, payload, 0, (int)length))
                {
                    throw new EndOfStreamException();
                }

                Peer peer = null;
                lock (Sync)
                {
                    host.Peers.TryGetValue(peerId, out peer);
                }
                if (peer != null)
                {
                    try
                    {
                        lock (peer.WriteLock)
                        {
                            peer.Stream.Write(payload, 0, payload.Length);
                        }
                        LogTraffic("host-to-peer peer=" + peerId + " bytes=" + payload.Length);
                    }
                    catch (Exception error)
                    {
                        lock (Sync)
                        {
                            Peer registered;
                            if (host.Peers.TryGetValue(peerId, out registered)
                                && Object.ReferenceEquals(registered, peer))
                            {
                                host.Peers.Remove(peerId);
                            }
                        }
                        try { peer.Client.Close(); } catch { }
                        Log("host-to-peer stopped peer=" + peerId + " error=" + error.Message);
                    }
                }
            }
        }
        catch (Exception error)
        {
            Log("host reader stopped session=" + host.SessionId + " error=" + error.Message);
        }
        CloseHub(host, "host-disconnected");
    }

    private static void RegisterHubHost(TcpClient client, NetworkStream stream, string sessionId, string buildId)
    {
        HostSession host = new HostSession
        {
            SessionId = sessionId,
            BuildId = buildId,
            Client = client,
            Stream = stream
        };
        string error = null;
        lock (Sync)
        {
            if (HubHosts.ContainsKey(sessionId))
            {
                error = "session-in-use";
            }
            else if (HubHosts.Count >= MaxSessions)
            {
                error = "server-full";
            }
            else
            {
                HubHosts.Add(sessionId, host);
            }
        }
        if (error != null)
        {
            WriteLine(stream, "ERROR " + error);
            client.Close();
            return;
        }
        WriteLine(stream, "WAITING");
        Log("hub host waiting session=" + sessionId);
        Thread reader = new Thread(HostReader);
        reader.IsBackground = true;
        reader.Start(host);
    }

    private static void JoinHub(TcpClient client, NetworkStream stream, string sessionId, string buildId)
    {
        HostSession host = null;
        Peer peer = null;
        string rejection = null;
        lock (Sync)
        {
            HubHosts.TryGetValue(sessionId, out host);
            if (host == null || host.Closed)
            {
                rejection = "host-not-found";
            }
            else if (!String.Equals(host.BuildId, buildId, StringComparison.Ordinal))
            {
                rejection = "build-mismatch";
            }
            else if (host.Peers.Count >= MaxPeersPerSession)
            {
                rejection = "session-full";
            }
            else
            {
                peer = new Peer { Id = host.NextPeerId++, Client = client, Stream = stream };
                host.Peers.Add(peer.Id, peer);
            }
        }

        if (rejection != null)
        {
            WriteLine(stream, "ERROR " + rejection);
            client.Close();
            return;
        }

        try
        {
            lock (host.WriteLock)
            {
                if (!host.ReadySent)
                {
                    WriteLine(host.Stream, "READY");
                    host.ReadySent = true;
                }
            }
            WriteHostFrame(host, (byte)'J', peer.Id, new byte[0], 0, 0);
            WriteLine(stream, "READY");
            Log("hub peer ready session=" + sessionId + " peer=" + peer.Id);

            byte[] buffer = new byte[65536];
            for (;;)
            {
                int count = stream.Read(buffer, 0, buffer.Length);
                if (count == 0)
                {
                    break;
                }
                WriteHostFrame(host, (byte)'D', peer.Id, buffer, 0, count);
                LogTraffic("peer-to-host peer=" + peer.Id + " bytes=" + count);
            }
        }
        catch (Exception error)
        {
            Log("hub peer stopped peer=" + peer.Id + " error=" + error.Message);
        }
        finally
        {
            bool removed;
            lock (Sync)
            {
                removed = host.Peers.Remove(peer.Id);
            }
            if (removed && !host.Closed)
            {
                try { WriteHostFrame(host, (byte)'L', peer.Id, new byte[0], 0, 0); } catch { }
            }
            client.Close();
            Log("hub peer left session=" + sessionId + " peer=" + peer.Id);
        }
    }

    private static bool HasHubHost(string sessionId)
    {
        lock (Sync)
        {
            HostSession host;
            return HubHosts.TryGetValue(sessionId, out host) && !host.Closed;
        }
    }

    private static void Bridge(NetworkStream source, NetworkStream destination, string direction)
    {
        byte[] buffer = new byte[65536];
        long total = 0;
        try
        {
            for (;;)
            {
                int count = source.Read(buffer, 0, buffer.Length);
                if (count == 0)
                {
                    break;
                }
                destination.Write(buffer, 0, count);
                destination.Flush();
                total += count;
                int previewLength = Math.Min(count, 512);
                Log(direction + " bytes=" + count + " total=" + total
                    + " hex=" + BitConverter.ToString(buffer, 0, previewLength));
            }
        }
        catch (Exception error)
        {
            Log(direction + " stopped: " + error.Message);
        }
    }

    private static void HandleLegacy(TcpClient client, NetworkStream stream, string role, string sessionId, string buildId)
    {
        if (role == "HOST")
        {
            string error = null;
            lock (Sync)
            {
                if (Hosts.ContainsKey(sessionId))
                {
                    error = "session-in-use";
                }
                else if (Hosts.Count >= MaxSessions)
                {
                    error = "server-full";
                }
                else
                {
                    Hosts.Add(sessionId, new WaitingHost { BuildId = buildId, Client = client });
                }
            }
            if (error != null)
            {
                WriteLine(stream, "ERROR " + error);
                client.Close();
                return;
            }
            WriteLine(stream, "WAITING");
            Log("host waiting session=" + sessionId);
            return;
        }

        WaitingHost host = null;
        lock (Sync)
        {
            WaitingHost candidate;
            if (Hosts.TryGetValue(sessionId, out candidate)
                && String.Equals(candidate.BuildId, buildId, StringComparison.Ordinal))
            {
                host = candidate;
                Hosts.Remove(sessionId);
            }
        }
        if (host == null)
        {
            WriteLine(stream, "ERROR host-not-found");
            client.Close();
            return;
        }
        NetworkStream hostStream = host.Client.GetStream();
        WriteLine(hostStream, "READY");
        WriteLine(stream, "READY");
        Log("bridge ready session=" + sessionId);
        Thread hostToClient = new Thread(new ThreadStart(delegate { Bridge(hostStream, stream, "host-to-client"); }));
        hostToClient.IsBackground = true;
        hostToClient.Start();
        Bridge(stream, hostStream, "client-to-host");
        host.Client.Close();
        client.Close();
    }

    private static void HandleClient(object state)
    {
        TcpClient client = (TcpClient)state;
        bool handshakeSlotHeld = true;
        try
        {
            client.NoDelay = true;
            client.ReceiveTimeout = HandshakeTimeoutMs;
            NetworkStream stream = client.GetStream();
            string handshake = ReadLine(stream);
            string[] parts = handshake.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 || parts.Length > 5)
            {
                WriteLine(stream, "ERROR invalid-handshake");
                client.Close();
                return;
            }

            string role = parts[0].ToUpperInvariant();
            string sessionId = parts[1];
            string buildId = parts[2];
            bool hubProtocol = parts.Length >= 4 && String.Equals(parts[3], "V2", StringComparison.OrdinalIgnoreCase);
            string suppliedToken = parts.Length >= 5 ? parts[4] : "";

            if ((role != "HOST" && role != "JOIN")
                || !IsValidHandshakeValue(sessionId, 64)
                || !IsValidHandshakeValue(buildId, 160))
            {
                WriteLine(stream, "ERROR invalid-handshake");
                client.Close();
                return;
            }
            if (!String.IsNullOrEmpty(AccessToken)
                && (!hubProtocol || !ConstantTimeEquals(AccessToken, suppliedToken)))
            {
                Log("authentication failed remote=" + RemoteAddress(client));
                WriteLine(stream, "ERROR unauthorized");
                client.Close();
                return;
            }

            client.ReceiveTimeout = 0;
            HandshakeSlots.Release();
            handshakeSlotHeld = false;
            Log("handshake role=" + role + " session=" + sessionId + " build=" + buildId
                + " protocol=" + (hubProtocol ? "V2" : "V1") + " remote=" + RemoteAddress(client));

            if (hubProtocol)
            {
                if (role == "HOST") RegisterHubHost(client, stream, sessionId, buildId);
                else JoinHub(client, stream, sessionId, buildId);
                return;
            }

            // Older Xbox builds omit the V2 marker but use the same raw peer stream.
            // Admit them to an existing multi-peer hub instead of the one-client bridge.
            if (role == "JOIN" && HasHubHost(sessionId))
            {
                Log("upgrading legacy join to hub session=" + sessionId);
                JoinHub(client, stream, sessionId, buildId);
                return;
            }
            HandleLegacy(client, stream, role, sessionId, buildId);
        }
        catch (Exception error)
        {
            Log("connection failed: " + error.Message);
            client.Close();
        }
        finally
        {
            if (handshakeSlotHeld)
            {
                HandshakeSlots.Release();
            }
        }
    }

    public static int Main(string[] args)
    {
        int port = args.Length > 0 ? Int32.Parse(args[0])
            : ReadEnvironmentInt("CONSOLE_LEGACY_RELAY_PORT", 61000, 1, 65535);
        string configuredLogPath = Environment.GetEnvironmentVariable("CONSOLE_LEGACY_RELAY_LOG_PATH");
        LogPath = args.Length > 1 ? args[1]
            : !String.IsNullOrEmpty(configuredLogPath) ? configuredLogPath
            : Path.Combine(Environment.CurrentDirectory, "local-relay.log");
        string configuredBindAddress = Environment.GetEnvironmentVariable("CONSOLE_LEGACY_RELAY_BIND_ADDRESS");
        IPAddress bindAddress = args.Length > 2 ? IPAddress.Parse(args[2])
            : !String.IsNullOrEmpty(configuredBindAddress) ? IPAddress.Parse(configuredBindAddress)
            : IPAddress.Loopback;
        VerboseTraffic = args.Length > 3 && String.Equals(args[3], "--verbose-traffic", StringComparison.OrdinalIgnoreCase);
        AccessToken = Environment.GetEnvironmentVariable("CONSOLE_LEGACY_RELAY_TOKEN") ?? "";
        if (!String.IsNullOrEmpty(AccessToken) && !IsValidHandshakeValue(AccessToken, 128))
        {
            Console.Error.WriteLine("CONSOLE_LEGACY_RELAY_TOKEN must be 1-128 printable characters without spaces.");
            return 2;
        }
        MaxSessions = ReadEnvironmentInt("CONSOLE_LEGACY_RELAY_MAX_SESSIONS", 64, 1, 4096);
        MaxPeersPerSession = ReadEnvironmentInt("CONSOLE_LEGACY_RELAY_MAX_PEERS", 8, 1, 64);
        HandshakeTimeoutMs = ReadEnvironmentInt("CONSOLE_LEGACY_RELAY_HANDSHAKE_TIMEOUT_MS", 10000, 1000, 120000);
        int maxPendingHandshakes = ReadEnvironmentInt("CONSOLE_LEGACY_RELAY_MAX_PENDING_HANDSHAKES", 64, 1, 4096);
        HandshakeSlots = new Semaphore(maxPendingHandshakes, maxPendingHandshakes);

        string logDirectory = Path.GetDirectoryName(Path.GetFullPath(LogPath));
        if (!String.IsNullOrEmpty(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }
        LogWriter = new StreamWriter(new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8, 65536);

        TcpListener listener = new TcpListener(bindAddress, port);
        listener.Start();
        Log("listening " + bindAddress + ":" + port + " auth="
            + (String.IsNullOrEmpty(AccessToken) ? "disabled" : "required")
            + " maxSessions=" + MaxSessions + " maxPeers=" + MaxPeersPerSession);
        if (!IPAddress.IsLoopback(bindAddress) && String.IsNullOrEmpty(AccessToken))
        {
            Log("WARNING non-loopback listener has no access token; restrict it with a firewall or VPN");
        }
        for (;;)
        {
            TcpClient client = listener.AcceptTcpClient();
            if (!HandshakeSlots.WaitOne(0))
            {
                try
                {
                    WriteLine(client.GetStream(), "ERROR server-busy");
                }
                catch { }
                client.Close();
                Log("connection rejected reason=too-many-pending-handshakes");
                continue;
            }
            Thread thread = new Thread(HandleClient);
            thread.IsBackground = true;
            thread.Start(client);
        }
    }
}
