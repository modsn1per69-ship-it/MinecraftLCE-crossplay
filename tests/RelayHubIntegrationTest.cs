using System;
using System.IO;
using System.Net.Sockets;
using System.Text;

internal static class RelayHubIntegrationTest
{
    private sealed class Frame
    {
        public byte Type;
        public uint PeerId;
        public byte[] Data;
    }

    private static void WriteLine(NetworkStream stream, string line)
    {
        byte[] data = Encoding.ASCII.GetBytes(line + "\n");
        stream.Write(data, 0, data.Length);
        stream.Flush();
    }

    private static string ReadLine(NetworkStream stream)
    {
        MemoryStream data = new MemoryStream();
        for (;;)
        {
            int value = stream.ReadByte();
            if (value < 0) throw new EndOfStreamException();
            if (value == '\n') return Encoding.ASCII.GetString(data.ToArray()).TrimEnd('\r');
            data.WriteByte((byte)value);
        }
    }

    private static void ReadExact(NetworkStream stream, byte[] data, int count)
    {
        int offset = 0;
        while (offset < count)
        {
            int received = stream.Read(data, offset, count - offset);
            if (received <= 0) throw new EndOfStreamException();
            offset += received;
        }
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

    private static Frame ReadFrame(NetworkStream stream)
    {
        byte[] header = new byte[9];
        ReadExact(stream, header, header.Length);
        int length = (int)ReadUInt32(header, 5);
        byte[] data = new byte[length];
        ReadExact(stream, data, length);
        return new Frame { Type = header[0], PeerId = ReadUInt32(header, 1), Data = data };
    }

    private static void WriteFrame(NetworkStream stream, byte type, uint peerId, byte[] data)
    {
        byte[] header = new byte[9];
        header[0] = type;
        WriteUInt32(header, 1, peerId);
        WriteUInt32(header, 5, (uint)data.Length);
        stream.Write(header, 0, header.Length);
        stream.Write(data, 0, data.Length);
        stream.Flush();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static int Main(string[] args)
    {
        string relayHost = args.Length > 1 ? args[0] : "127.0.0.1";
        int port = args.Length > 1 ? Int32.Parse(args[1])
            : args.Length > 0 ? Int32.Parse(args[0]) : 61001;
        const string handshake = "local-test 584111F7-1.0.10.0-lce1.2.3-net495-proto39 V2";
        TcpClient host = new TcpClient(relayHost, port);
        NetworkStream hostStream = host.GetStream();
        WriteLine(hostStream, "HOST " + handshake);
        Require(ReadLine(hostStream) == "WAITING", "host did not enter WAITING");

        TcpClient xbox = new TcpClient(relayHost, port);
        NetworkStream xboxStream = xbox.GetStream();
        WriteLine(xboxStream, "JOIN local-test 584111F7-1.0.10.0-lce1.2.3-net495-proto39");
        Require(ReadLine(xboxStream) == "READY", "legacy Xbox client was not upgraded to the hub");
        Require(ReadLine(hostStream) == "READY", "host was not ready");
        Frame xboxJoin = ReadFrame(hostStream);
        Require(xboxJoin.Type == (byte)'J' && xboxJoin.PeerId == 2, "missing Xbox join frame");

        TcpClient ps3 = new TcpClient(relayHost, port);
        NetworkStream ps3Stream = ps3.GetStream();
        WriteLine(ps3Stream, "JOIN " + handshake);
        Require(ReadLine(ps3Stream) == "READY", "PS3 client was not ready");
        Frame ps3Join = ReadFrame(hostStream);
        Require(ps3Join.Type == (byte)'J' && ps3Join.PeerId == 3, "missing PS3 join frame");

        byte[] fromXbox = Encoding.ASCII.GetBytes("FROM_XBOX");
        xboxStream.Write(fromXbox, 0, fromXbox.Length);
        xboxStream.Flush();
        Frame xboxData = ReadFrame(hostStream);
        Require(xboxData.Type == (byte)'D' && xboxData.PeerId == 2
            && Encoding.ASCII.GetString(xboxData.Data) == "FROM_XBOX", "Xbox data was not routed to host");

        byte[] fromPs3 = Encoding.ASCII.GetBytes("FROM_PS3");
        ps3Stream.Write(fromPs3, 0, fromPs3.Length);
        ps3Stream.Flush();
        Frame ps3Data = ReadFrame(hostStream);
        Require(ps3Data.Type == (byte)'D' && ps3Data.PeerId == 3
            && Encoding.ASCII.GetString(ps3Data.Data) == "FROM_PS3", "PS3 data was not routed to host");

        WriteFrame(hostStream, (byte)'D', 2, Encoding.ASCII.GetBytes("TO_XBOX"));
        byte[] toXbox = new byte[7];
        ReadExact(xboxStream, toXbox, toXbox.Length);
        Require(Encoding.ASCII.GetString(toXbox) == "TO_XBOX", "host data was not routed to Xbox");

        WriteFrame(hostStream, (byte)'D', 3, Encoding.ASCII.GetBytes("TO_PS3"));
        byte[] toPs3 = new byte[6];
        ReadExact(ps3Stream, toPs3, toPs3.Length);
        Require(Encoding.ASCII.GetString(toPs3) == "TO_PS3", "host data was not routed to PS3");

        xbox.Close();
        Frame leave = ReadFrame(hostStream);
        Require(leave.Type == (byte)'L' && leave.PeerId == 2, "missing Xbox leave frame");

        WriteFrame(hostStream, (byte)'D', 2, Encoding.ASCII.GetBytes("STALE_XBOX_DATA"));
        WriteFrame(hostStream, (byte)'D', 3, Encoding.ASCII.GetBytes("PS3_STILL_CONNECTED"));
        byte[] afterXboxLeave = new byte[19];
        ReadExact(ps3Stream, afterXboxLeave, afterXboxLeave.Length);
        Require(Encoding.ASCII.GetString(afterXboxLeave) == "PS3_STILL_CONNECTED",
            "Xbox disconnect tore down the remaining hub session");

        ps3.Close();
        host.Close();
        Console.WriteLine("RELAY_HUB_3_PEER_OK");
        return 0;
    }
}
