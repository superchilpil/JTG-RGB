using System.IO;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace JtgRgb.App.Hardware;

public sealed record OpenRgbDevice(
    uint Id,
    string Name,
    string Vendor,
    string Description,
    string Version,
    int LedCount,
    int ColorCount);

public sealed class OpenRgbSdkClient : IAsyncDisposable
{
    private const int DefaultPort = 6742;
    private const uint RequestControllerCount = 0;
    private const uint RequestControllerData = 1;
    private const uint RequestProtocolVersion = 40;
    private const uint SetClientName = 50;
    private const uint DeviceListUpdated = 100;
    private const uint UpdateLeds = 1050;
    private const uint SetCustomMode = 1100;
    private const uint ClientProtocolVersion = 5;
    private const int MaximumPacketBytes = 16 * 1024 * 1024;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private int _protocolVersion;

    public bool IsConnected => _tcp?.Connected == true && _stream is not null;
    public int ProtocolVersion => _protocolVersion;

    public async Task ConnectAsync(string host = "127.0.0.1", int port = DefaultPort, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsConnected) return;
            await DisconnectCoreAsync();

            var tcp = new TcpClient { NoDelay = true };
            try
            {
                await tcp.ConnectAsync(host, port, cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
                _tcp = tcp;
                _stream = tcp.GetStream();

                await SendPacketAsync(0, SetClientName, Encoding.UTF8.GetBytes("JTG-RGB\0"), cancellationToken);
                await SendPacketAsync(0, RequestProtocolVersion, UInt32Bytes(ClientProtocolVersion), cancellationToken);

                using (var protocolTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    protocolTimeout.CancelAfter(TimeSpan.FromMilliseconds(900));
                    try
                    {
                        var response = await ReadPacketAsync(protocolTimeout.Token);
                        _protocolVersion = response.Id == RequestProtocolVersion && response.Payload.Length >= 4
                            ? (int)ReadUInt32(response.Payload, 0)
                            : 0;
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        _protocolVersion = 0;
                    }
                }

                if (_protocolVersion > ClientProtocolVersion) _protocolVersion = (int)ClientProtocolVersion;
            }
            catch
            {
                tcp.Dispose();
                _tcp = null;
                _stream = null;
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<OpenRgbDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureConnected();
            await SendPacketAsync(0, RequestControllerCount, [], cancellationToken);
            var countPacket = await ReadExpectedPacketAsync(RequestControllerCount, cancellationToken);
            if (countPacket.Payload.Length < 4) throw new InvalidDataException("OpenRGB returned an invalid controller-count packet.");

            var count = ReadUInt32(countPacket.Payload, 0);
            if (count > 256) throw new InvalidDataException($"OpenRGB returned an unreasonable device count ({count}).");

            var ids = new List<uint>((int)count);
            if (_protocolVersion >= 6)
            {
                if (countPacket.Payload.Length < 4 + count * 4) throw new InvalidDataException("OpenRGB returned an incomplete device ID list.");
                for (var i = 0; i < count; i++) ids.Add(ReadUInt32(countPacket.Payload, 4 + (int)i * 4));
            }
            else
            {
                for (uint i = 0; i < count; i++) ids.Add(i);
            }

            var devices = new List<OpenRgbDevice>(ids.Count);
            foreach (var id in ids)
            {
                var requestPayload = _protocolVersion == 0 ? [] : UInt32Bytes((uint)_protocolVersion);
                await SendPacketAsync(id, RequestControllerData, requestPayload, cancellationToken);
                var packet = await ReadExpectedPacketAsync(RequestControllerData, cancellationToken);
                devices.Add(ParseDevice(id, packet.Payload, _protocolVersion));
            }

            return devices;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetDeviceColorAsync(OpenRgbDevice device, byte red, byte green, byte blue, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureConnected();
            if (device.LedCount <= 0) return;

            var colorCount = device.ColorCount > 0 ? device.ColorCount : device.LedCount;
            if (colorCount <= 0) return;
            if (colorCount > ushort.MaxValue)
                throw new InvalidDataException($"OpenRGB reported too many color entries ({colorCount}).");

            // OpenRGB packs RGBColor with red in the least-significant byte:
            // ToRGBColor(r, g, b) == (b << 16) | (g << 8) | r.
            var color = ((uint)blue << 16) | ((uint)green << 8) | red;
            var payload = new byte[6 + colorCount * 4];
            WriteUInt32(payload, 0, (uint)(2 + colorCount * 4));
            WriteUInt16(payload, 4, checked((ushort)colorCount));
            for (var i = 0; i < colorCount; i++)
                WriteUInt32(payload, 6 + i * 4, color);

            // Switching a controller into software/custom mode and immediately
            // updating it can race some OpenRGB device workers.
            await SendPacketAsync(device.Id, SetCustomMode, [], cancellationToken);
            await Task.Delay(50, cancellationToken);
            await SendPacketAsync(device.Id, UpdateLeds, payload, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetAllDevicesColorAsync(IReadOnlyList<OpenRgbDevice> devices, byte red, byte green, byte blue, CancellationToken cancellationToken = default)
    {
        foreach (var device in devices)
            await SetDeviceColorAsync(device, red, green, blue, cancellationToken);
    }

    public async Task RequestRescanAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureConnected();
            await SendPacketAsync(0, 140, [], cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try { await DisconnectCoreAsync(); }
        finally { _gate.Release(); _gate.Dispose(); }
    }

    private async Task DisconnectCoreAsync()
    {
        if (_stream is not null) await _stream.DisposeAsync();
        _tcp?.Dispose();
        _stream = null;
        _tcp = null;
        _protocolVersion = 0;
    }

    private void EnsureConnected()
    {
        if (!IsConnected) throw new InvalidOperationException("Not connected to the OpenRGB SDK server.");
    }

    private async Task SendPacketAsync(uint deviceId, uint packetId, byte[] payload, CancellationToken cancellationToken)
    {
        EnsureConnected();
        var packet = new byte[16 + payload.Length];
        packet[0] = (byte)'O'; packet[1] = (byte)'R'; packet[2] = (byte)'G'; packet[3] = (byte)'B';
        WriteUInt32(packet, 4, deviceId);
        WriteUInt32(packet, 8, packetId);
        WriteUInt32(packet, 12, (uint)payload.Length);
        payload.CopyTo(packet, 16);
        await _stream!.WriteAsync(packet, cancellationToken);
        await _stream.FlushAsync(cancellationToken);
    }

    private async Task<Packet> ReadExpectedPacketAsync(uint expectedId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var packet = await ReadPacketAsync(cancellationToken);
            if (packet.Id == DeviceListUpdated) continue;
            if (packet.Id != expectedId)
                throw new InvalidDataException($"Unexpected OpenRGB packet {packet.Id}; expected {expectedId}.");
            return packet;
        }
        throw new InvalidDataException("Too many OpenRGB notifications arrived while waiting for a response.");
    }

    private async Task<Packet> ReadPacketAsync(CancellationToken cancellationToken)
    {
        EnsureConnected();
        var header = new byte[16];
        await ReadExactlyAsync(_stream!, header, cancellationToken);
        if (header[0] != 'O' || header[1] != 'R' || header[2] != 'G' || header[3] != 'B')
            throw new InvalidDataException("The endpoint did not return a valid OpenRGB SDK packet.");

        var id = ReadUInt32(header, 8);
        var length = ReadUInt32(header, 12);
        if (length > MaximumPacketBytes) throw new InvalidDataException($"OpenRGB packet is too large ({length} bytes).");
        var payload = new byte[(int)length];
        if (payload.Length > 0) await ReadExactlyAsync(_stream!, payload, cancellationToken);
        return new Packet(id, payload);
    }

    private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken);
            if (read == 0) throw new IOException("OpenRGB closed the SDK connection.");
            offset += read;
        }
    }

    private static OpenRgbDevice ParseDevice(uint id, byte[] payload, int protocol)
    {
        var reader = new PacketReader(payload);
        _ = reader.ReadUInt32(); // data_size
        _ = reader.ReadInt32();  // controller type
        var name = reader.ReadString16();
        var vendor = protocol >= 1 ? reader.ReadString16() : "";
        var description = reader.ReadString16();
        var version = reader.ReadString16();
        _ = reader.ReadString16(); // serial
        _ = reader.ReadString16(); // location

        var modeCount = reader.ReadUInt16();
        _ = reader.ReadInt32(); // active mode
        for (var i = 0; i < modeCount; i++) SkipMode(reader, protocol);

        var zoneCount = reader.ReadUInt16();
        for (var i = 0; i < zoneCount; i++) SkipZone(reader, protocol);

        var ledCount = reader.ReadUInt16();
        for (var i = 0; i < ledCount; i++)
        {
            _ = reader.ReadString16();
            if (protocol < 6) _ = reader.ReadUInt32();
        }

        var colorCount = reader.ReadUInt16();
        reader.Skip(checked(colorCount * 4));

        if (protocol >= 5)
        {
            var alternateNames = reader.ReadUInt16();
            for (var i = 0; i < alternateNames; i++) _ = reader.ReadString16();
            _ = reader.ReadUInt32(); // controller flags
        }

        return new OpenRgbDevice(id, string.IsNullOrWhiteSpace(name) ? "Unnamed device" : name,
            vendor, description, version, ledCount, colorCount);
    }

    private static void SkipMode(PacketReader reader, int protocol)
    {
        _ = reader.ReadString16();
        if (protocol < 6) _ = reader.ReadInt32(); // mode value
        _ = reader.ReadUInt32(); // flags
        _ = reader.ReadUInt32(); // speed min
        _ = reader.ReadUInt32(); // speed max
        if (protocol >= 3) { _ = reader.ReadUInt32(); _ = reader.ReadUInt32(); } // brightness min/max
        _ = reader.ReadUInt32(); // colors min
        _ = reader.ReadUInt32(); // colors max
        _ = reader.ReadUInt32(); // speed
        if (protocol >= 3) _ = reader.ReadUInt32(); // brightness
        _ = reader.ReadUInt32(); // direction
        _ = reader.ReadUInt32(); // color mode
        var colors = reader.ReadUInt16();
        reader.Skip(checked(colors * 4));
    }

    private static void SkipZone(PacketReader reader, int protocol)
    {
        _ = reader.ReadString16();
        _ = reader.ReadInt32(); // zone type
        _ = reader.ReadUInt32(); // LEDs min
        _ = reader.ReadUInt32(); // LEDs max
        _ = reader.ReadUInt32(); // LEDs count
        var matrixLength = reader.ReadUInt16();
        if (matrixLength > 0)
        {
            if (matrixLength < 8) throw new InvalidDataException("Invalid OpenRGB matrix map length.");
            reader.Skip(matrixLength);
        }

        if (protocol >= 4)
        {
            var segments = reader.ReadUInt16();
            for (var i = 0; i < segments; i++)
            {
                _ = reader.ReadString16();
                _ = reader.ReadInt32();
                _ = reader.ReadUInt32();
                _ = reader.ReadUInt32();
                if (protocol >= 6)
                {
                    var segmentMatrixLength = reader.ReadUInt16();
                    if (segmentMatrixLength > 0) reader.Skip(segmentMatrixLength);
                    _ = reader.ReadUInt32();
                }
            }
        }
        if (protocol >= 5) _ = reader.ReadUInt32(); // zone flags
    }

    private static byte[] UInt32Bytes(uint value)
    {
        var result = new byte[4];
        WriteUInt32(result, 0, value);
        return result;
    }

    private static uint ReadUInt32(byte[] data, int offset) =>
        System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
    private static void WriteUInt32(byte[] data, int offset, uint value) =>
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);
    private static void WriteUInt16(byte[] data, int offset, ushort value) =>
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, 2), value);

    private sealed record Packet(uint Id, byte[] Payload);

    private sealed class PacketReader(byte[] data)
    {
        private int _offset;
        public ushort ReadUInt16() { Require(2); var v = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(_offset, 2)); _offset += 2; return v; }
        public uint ReadUInt32() { Require(4); var v = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(_offset, 4)); _offset += 4; return v; }
        public int ReadInt32() => unchecked((int)ReadUInt32());
        public string ReadString16()
        {
            var length = ReadUInt16();
            Require(length);
            var value = Encoding.UTF8.GetString(data, _offset, length);
            _offset += length;
            return value.TrimEnd('\0');
        }
        public void Skip(int count) { Require(count); _offset += count; }
        private void Require(int count)
        {
            if (count < 0 || _offset > data.Length - count)
                throw new InvalidDataException("OpenRGB returned truncated or malformed device data.");
        }
    }
}

public sealed class OpenRgbBackendManager : IAsyncDisposable
{
    public const string BundledExecutableRelativePath = "vendor\\OpenRGB\\OpenRGB.exe";
    private readonly OpenRgbSdkClient _client = new();
    private Process? _backendProcess;

    public OpenRgbSdkClient Client => _client;
    public bool IsBundledBackendAvailable => File.Exists(Path.Combine(AppContext.BaseDirectory, BundledExecutableRelativePath));

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.ConnectAsync(cancellationToken: cancellationToken);
            return;
        }
        catch (SocketException) { }
        catch (TimeoutException) { }

        if (!IsBundledBackendAvailable)
            throw new FileNotFoundException(
                "OpenRGB is not running on 127.0.0.1:6742, and the bundled backend was not found. Install/run a compatible OpenRGB PawnIO build, or place OpenRGB.exe at " +
                Path.Combine(AppContext.BaseDirectory, BundledExecutableRelativePath) + ".");

        var executable = Path.Combine(AppContext.BaseDirectory, BundledExecutableRelativePath);
        _backendProcess = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = "--server",
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = true
        }) ?? throw new InvalidOperationException("Windows could not start the bundled OpenRGB backend.");

        var deadline = DateTime.UtcNow.AddSeconds(20);
        Exception? lastError = null;
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(500, cancellationToken);
            try
            {
                await _client.ConnectAsync(cancellationToken: cancellationToken);
                return;
            }
            catch (Exception ex) when (ex is SocketException or TimeoutException or IOException)
            {
                lastError = ex;
            }
        }

        throw new IOException("OpenRGB started but its SDK server did not become available on 127.0.0.1:6742.", lastError);
    }

    public async Task<string> GetPawnIoStatusAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
                if (key is null) return "PawnIO driver service is not registered";
                var imagePath = key.GetValue("ImagePath") as string;
                return string.IsNullOrWhiteSpace(imagePath)
                    ? "PawnIO service entry found; driver path could not be verified"
                    : "PawnIO service entry found (driver registration detected)";
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                return "PawnIO status could not be read with current permissions";
            }
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        _backendProcess?.Dispose();
    }
}
