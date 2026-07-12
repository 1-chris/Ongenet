using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ongenet.Core.Services;

/// <summary>
/// Binary IPC protocol between the DAW and <c>Ongenet.PluginHost</c> over a named pipe.
/// Messages: Ping, LoadPlugin, ProcessAudio, SetParameter, GetLatency.
/// </summary>
public static class PluginHostIpc
{
    public const int Magic = 0x4F504850; // "OPHP"
    public const string PipePrefix = "oph";

    public enum MessageType : byte
    {
        Ping = 1,
        Pong = 2,
        LoadPlugin = 3,
        LoadPluginResult = 4,
        ProcessAudio = 5,
        ProcessAudioResult = 6,
        SetParameter = 7,
        SetParameterResult = 8,
        GetLatency = 9,
        GetLatencyResult = 10,
        Shutdown = 11,
        Error = 255,
    }

    public static string CreatePipeName(string instanceId)
    {
        var id = instanceId.AsSpan();
        if (id.Length > 16)
            id = id[..16];
        return $"{PipePrefix}.{id}";
    }

    public static string NewInstanceId() => Guid.NewGuid().ToString("N")[..12];

    public static async Task WriteMessageAsync(Stream stream, MessageType type, ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        var header = new byte[9];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0), Magic);
        header[4] = (byte)type;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(5), payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        if (!payload.IsEmpty)
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task<(MessageType Type, byte[] Payload)> ReadMessageAsync(Stream stream,
        CancellationToken cancellationToken = default)
    {
        var header = await ReadExactAsync(stream, 9, cancellationToken).ConfigureAwait(false);
        var magic = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0));
        if (magic != Magic)
            throw new InvalidDataException($"Invalid plugin host magic: 0x{magic:X8}");

        var type = (MessageType)header[4];
        var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(5));
        if (length < 0)
            throw new InvalidDataException($"Invalid payload length: {length}");

        var payload = length == 0 ? Array.Empty<byte>() : await ReadExactAsync(stream, length, cancellationToken).ConfigureAwait(false);
        return (type, payload);
    }

    public static byte[] EncodeLoadPlugin(string modulePath, string uid, string displayName)
    {
        using var ms = new MemoryStream();
        WriteString(ms, modulePath);
        WriteString(ms, uid);
        WriteString(ms, displayName);
        return ms.ToArray();
    }

    public static (string ModulePath, string Uid, string DisplayName) DecodeLoadPlugin(ReadOnlySpan<byte> payload)
    {
        var offset = 0;
        var path = ReadString(payload, ref offset);
        var uid = ReadString(payload, ref offset);
        var name = ReadString(payload, ref offset);
        return (path, uid, name);
    }

    public static byte[] EncodeLoadPluginResult(bool success, string? error = null)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(success ? (byte)1 : (byte)0);
        WriteString(ms, error ?? "");
        return ms.ToArray();
    }

    public static (bool Success, string Error) DecodeLoadPluginResult(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty) return (false, "empty response");
        var offset = 0;
        var success = payload[0] != 0;
        offset = 1;
        var error = ReadString(payload, ref offset);
        return (success, error);
    }

    public static byte[] EncodeProcessAudio(int sampleRate, int channels, int frameCount, ReadOnlySpan<float> samples)
    {
        using var ms = new MemoryStream(16 + samples.Length * 4);
        WriteInt32(ms, sampleRate);
        WriteInt32(ms, channels);
        WriteInt32(ms, frameCount);
        foreach (var sample in samples)
            WriteFloat(ms, sample);
        return ms.ToArray();
    }

    public static (int SampleRate, int Channels, int FrameCount, float[] Samples) DecodeProcessAudio(ReadOnlySpan<byte> payload)
    {
        var offset = 0;
        var sampleRate = ReadInt32(payload, ref offset);
        var channels = ReadInt32(payload, ref offset);
        var frameCount = ReadInt32(payload, ref offset);
        var count = frameCount * channels;
        var samples = new float[count];
        for (var i = 0; i < count; i++)
            samples[i] = ReadFloat(payload, ref offset);
        return (sampleRate, channels, frameCount, samples);
    }

    public static byte[] EncodeSetParameter(uint paramId, double value)
    {
        var payload = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0), paramId);
        BinaryPrimitives.WriteDoubleLittleEndian(payload.AsSpan(4), value);
        return payload;
    }

    public static (uint ParamId, double Value) DecodeSetParameter(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 12)
            throw new InvalidDataException("SetParameter payload too short");
        return (
            BinaryPrimitives.ReadUInt32LittleEndian(payload),
            BinaryPrimitives.ReadDoubleLittleEndian(payload.Slice(4)));
    }

    public static byte[] EncodeSetParameterResult(bool success) => new[] { success ? (byte)1 : (byte)0 };

    public static bool DecodeSetParameterResult(ReadOnlySpan<byte> payload)
        => !payload.IsEmpty && payload[0] != 0;

    public static byte[] EncodeLatency(int samples)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(payload, samples);
        return payload;
    }

    public static int DecodeLatency(ReadOnlySpan<byte> payload)
        => payload.Length >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(payload) : 0;

    public static byte[] EncodeError(string message)
    {
        using var ms = new MemoryStream();
        WriteString(ms, message);
        return ms.ToArray();
    }

    public static string DecodeError(ReadOnlySpan<byte> payload)
    {
        var offset = 0;
        return ReadString(payload, ref offset);
    }

    /// <summary>Client side of the plugin-host pipe (used by the DAW process).</summary>
    public sealed class Client : IDisposable
    {
        private readonly NamedPipeClientStream _pipe;
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        public Client(NamedPipeClientStream pipe) => _pipe = pipe;

        public bool IsConnected => _pipe.IsConnected;

        public static async Task<Client> ConnectAsync(string pipeName, TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);
            await pipe.ConnectAsync(cts.Token).ConfigureAwait(false);
            return new Client(pipe);
        }

        public async Task PingAsync(CancellationToken cancellationToken = default)
        {
            var response = await SendAsync(MessageType.Ping, ReadOnlyMemory<byte>.Empty, cancellationToken)
                .ConfigureAwait(false);
            if (response.Type != MessageType.Pong)
                throw new InvalidOperationException($"Expected Pong, got {response.Type}");
        }

        public async Task<(bool Success, string Error)> LoadPluginAsync(string modulePath, string uid, string displayName,
            CancellationToken cancellationToken = default)
        {
            var payload = EncodeLoadPlugin(modulePath, uid, displayName);
            var response = await SendAsync(MessageType.LoadPlugin, payload, cancellationToken).ConfigureAwait(false);
            if (response.Type == MessageType.Error)
                return (false, DecodeError(response.Payload));
            if (response.Type != MessageType.LoadPluginResult)
                throw new InvalidOperationException($"Expected LoadPluginResult, got {response.Type}");
            return DecodeLoadPluginResult(response.Payload);
        }

        public async Task<float[]> ProcessAudioAsync(int sampleRate, int channels, int frameCount, float[] input,
            CancellationToken cancellationToken = default)
        {
            var payload = EncodeProcessAudio(sampleRate, channels, frameCount, input);
            var response = await SendAsync(MessageType.ProcessAudio, payload, cancellationToken).ConfigureAwait(false);
            if (response.Type == MessageType.Error)
                throw new InvalidOperationException(DecodeError(response.Payload));
            if (response.Type != MessageType.ProcessAudioResult)
                throw new InvalidOperationException($"Expected ProcessAudioResult, got {response.Type}");
            return DecodeProcessAudio(response.Payload).Samples;
        }

        public async Task<bool> SetParameterAsync(uint paramId, double value, CancellationToken cancellationToken = default)
        {
            var payload = EncodeSetParameter(paramId, value);
            var response = await SendAsync(MessageType.SetParameter, payload, cancellationToken).ConfigureAwait(false);
            if (response.Type == MessageType.Error)
                return false;
            if (response.Type != MessageType.SetParameterResult)
                throw new InvalidOperationException($"Expected SetParameterResult, got {response.Type}");
            return DecodeSetParameterResult(response.Payload);
        }

        public async Task<int> GetLatencyAsync(CancellationToken cancellationToken = default)
        {
            var response = await SendAsync(MessageType.GetLatency, ReadOnlyMemory<byte>.Empty, cancellationToken)
                .ConfigureAwait(false);
            if (response.Type == MessageType.Error)
                return 0;
            if (response.Type != MessageType.GetLatencyResult)
                throw new InvalidOperationException($"Expected GetLatencyResult, got {response.Type}");
            return DecodeLatency(response.Payload);
        }

        public Task SendShutdownAsync(CancellationToken cancellationToken = default)
            => WriteMessageAsync(_pipe, MessageType.Shutdown, ReadOnlyMemory<byte>.Empty, cancellationToken);

        private async Task<(MessageType Type, byte[] Payload)> SendAsync(MessageType type, ReadOnlyMemory<byte> payload,
            CancellationToken cancellationToken)
        {
            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await WriteMessageAsync(_pipe, type, payload, cancellationToken).ConfigureAwait(false);
                return await ReadMessageAsync(_pipe, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public void Dispose()
        {
            _sendLock.Dispose();
            _pipe.Dispose();
        }
    }

    /// <summary>Server side of the plugin-host pipe (used by the child host process).</summary>
    public sealed class Server : IDisposable
    {
        private readonly NamedPipeServerStream _pipe;

        public Server(NamedPipeServerStream pipe) => _pipe = pipe;

        /// <summary>Creates a server-side pipe that is listening for a client connection.</summary>
        public static Server CreateListening(string pipeName)
        {
            var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            return new Server(pipe);
        }

        public Task WaitForClientAsync(CancellationToken cancellationToken = default)
            => _pipe.WaitForConnectionAsync(cancellationToken);

        public static async Task<Server> WaitForConnectionAsync(string pipeName, CancellationToken cancellationToken = default)
        {
            var server = CreateListening(pipeName);
            await server.WaitForClientAsync(cancellationToken).ConfigureAwait(false);
            return server;
        }

        public async Task RunAsync(Func<MessageType, byte[], Task<(MessageType Type, byte[] Payload)?>> handler,
            CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested && _pipe.IsConnected)
            {
                (MessageType type, byte[] payload) message;
                try
                {
                    message = await ReadMessageAsync(_pipe, cancellationToken).ConfigureAwait(false);
                }
                catch (EndOfStreamException)
                {
                    break;
                }
                catch (IOException)
                {
                    break;
                }

                if (message.type == MessageType.Shutdown)
                    break;

                var reply = await handler(message.type, message.payload).ConfigureAwait(false);
                if (reply is null)
                    break;

                await WriteMessageAsync(_pipe, reply.Value.Type, reply.Value.Payload, cancellationToken).ConfigureAwait(false);
            }
        }

        public void Dispose() => _pipe.Dispose();
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        var read = 0;
        while (read < count)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, count - read), cancellationToken).ConfigureAwait(false);
            if (n == 0)
                throw new EndOfStreamException("Plugin host pipe closed");
            read += n;
        }

        return buffer;
    }

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? "");
        WriteInt32(stream, bytes.Length);
        stream.Write(bytes);
    }

    private static string ReadString(ReadOnlySpan<byte> payload, ref int offset)
    {
        var length = ReadInt32(payload, ref offset);
        if (length < 0 || offset + length > payload.Length)
            throw new InvalidDataException("Invalid string length in plugin host message");
        var value = Encoding.UTF8.GetString(payload.Slice(offset, length));
        offset += length;
        return value;
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buf, value);
        stream.Write(buf);
    }

    private static int ReadInt32(ReadOnlySpan<byte> payload, ref int offset)
    {
        if (offset + 4 > payload.Length)
            throw new InvalidDataException("Unexpected end of plugin host payload");
        var value = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(offset));
        offset += 4;
        return value;
    }

    private static void WriteFloat(Stream stream, float value)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(buf, value);
        stream.Write(buf);
    }

    private static float ReadFloat(ReadOnlySpan<byte> payload, ref int offset)
    {
        if (offset + 4 > payload.Length)
            throw new InvalidDataException("Unexpected end of plugin host payload");
        var value = BinaryPrimitives.ReadSingleLittleEndian(payload.Slice(offset));
        offset += 4;
        return value;
    }
}
