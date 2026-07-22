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

    private static string ReadLine(NetworkStream stream)
    {
        MemoryStream buffer = new MemoryStream();
        while (buffer.Length < 4096)
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
        lock (Sync)
        {
            if (HubHosts.ContainsKey(sessionId))
            {
                WriteLine(stream, "ERROR session-in-use");
                client.Close();
                return;
            }
            HubHosts.Add(sessionId, host);
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
        lock (Sync)
        {
            HubHosts.TryGetValue(sessionId, out host);
            if (host != null && !host.Closed && String.Equals(host.BuildId, buildId, StringComparison.Ordinal))
            {
                peer = new Peer { Id = host.NextPeerId++, Client = client, Stream = stream };
                host.Peers.Add(peer.Id, peer);
            }
        }

        if (host == null || host.Closed)
        {
            WriteLine(stream, "ERROR host-not-found");
            client.Close();
            return;
        }
        if (peer == null)
        {
            WriteLine(stream, "ERROR build-mismatch");
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
            lock (Sync)
            {
                if (Hosts.ContainsKey(sessionId))
                {
                    WriteLine(stream, "ERROR session-in-use");
                    client.Close();
                    return;
                }
                Hosts.Add(sessionId, new WaitingHost { BuildId = buildId, Client = client });
            }
            WriteLine(stream, "WAITING");
            Log("host waiting session=" + sessionId);
            return;
        }

        WaitingHost host = null;
        lock (Sync)
        {
            if (Hosts.TryGetValue(sessionId, out host))
            {
                Hosts.Remove(sessionId);
            }
        }
        if (host == null)
        {
            WriteLine(stream, "ERROR host-not-found");
            client.Close();
            return;
        }
        if (!String.Equals(host.BuildId, buildId, StringComparison.Ordinal))
        {
            WriteLine(stream, "ERROR build-mismatch");
            host.Client.Close();
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
        try
        {
            NetworkStream stream = client.GetStream();
            string handshake = ReadLine(stream);
            string[] parts = handshake.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                WriteLine(stream, "ERROR invalid-handshake");
                client.Close();
                return;
            }

            string role = parts[0].ToUpperInvariant();
            string sessionId = parts[1];
            string buildId = parts[2];
            bool hubProtocol = parts.Length >= 4 && String.Equals(parts[3], "V2", StringComparison.OrdinalIgnoreCase);
            Log("handshake role=" + role + " session=" + sessionId + " build=" + buildId + " protocol=" + (hubProtocol ? "V2" : "V1"));

            if (role != "HOST" && role != "JOIN")
            {
                WriteLine(stream, "ERROR unknown-role");
                client.Close();
                return;
            }
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
    }

    public static int Main(string[] args)
    {
        int port = args.Length > 0 ? Int32.Parse(args[0]) : 61000;
        LogPath = args.Length > 1 ? args[1] : Path.Combine(Environment.CurrentDirectory, "local-relay.log");
        IPAddress bindAddress = args.Length > 2 ? IPAddress.Parse(args[2]) : IPAddress.Loopback;
        VerboseTraffic = args.Length > 3 && String.Equals(args[3], "--verbose-traffic", StringComparison.OrdinalIgnoreCase);
        LogWriter = new StreamWriter(new FileStream(LogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8, 65536);

        TcpListener listener = new TcpListener(bindAddress, port);
        listener.Start();
        Log("listening " + bindAddress + ":" + port);
        for (;;)
        {
            TcpClient client = listener.AcceptTcpClient();
            Thread thread = new Thread(HandleClient);
            thread.IsBackground = true;
            thread.Start(client);
        }
    }
}
