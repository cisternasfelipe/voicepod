using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VoiceFlow.Core.Abstractions;
using VoiceFlow.Core.Models;

namespace VoiceFlow.Audio;

/// <summary>
/// WASAPI shared-mode capture driven directly through <see cref="AudioClient"/>.
///
/// Initialising the audio client costs around 400 ms, which would be added to every
/// dictation if it happened on the hotkey. Instead the client is initialised ahead of time
/// (<see cref="Prepare"/>) and reused: starting a recording is then just AudioClient.Start.
/// Whatever the device delivers is downmixed to mono and resampled to 16 kHz, which is what
/// the Parakeet recognizer expects.
/// </summary>
public sealed class AudioCaptureService : IAudioCaptureService
{
    /// <summary>Shared-mode buffer length, in 100-nanosecond units (100 ms).</summary>
    private const long BufferDuration = 100 * 10_000L;

    private readonly ILogger<AudioCaptureService> _logger;
    private readonly object _sync = new();

    private MMDeviceEnumerator? _enumerator;
    private PreparedDevice? _prepared;
    private CancellationTokenSource? _captureCancellation;
    private Thread? _captureThread;
    private List<float>? _monoSamples;
    private Timer? _maxDurationTimer;
    private bool _disposed;

    public AudioCaptureService(ILogger<AudioCaptureService> logger) => _logger = logger;

    public bool IsRecording { get; private set; }

    public event EventHandler<AudioLevelEventArgs>? LevelChanged;

    public event EventHandler? MaxDurationReached;

    public IReadOnlyList<AudioDeviceInfo> GetInputDevices()
    {
        var devices = new List<AudioDeviceInfo>();

        try
        {
            var enumerator = GetEnumerator();
            string? defaultId = null;

            try
            {
                using var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                defaultId = defaultDevice.ID;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "No default capture device available");
            }

            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                devices.Add(new AudioDeviceInfo(device.ID, device.FriendlyName, device.ID == defaultId));
                device.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate capture devices");
        }

        return devices;
    }

    /// <summary>
    /// Opens and initialises the capture device so the next <see cref="Start"/> is immediate.
    /// Safe to call repeatedly; it only does work when the device changed.
    /// </summary>
    public void Prepare(string? deviceId)
    {
        lock (_sync)
        {
            if (IsRecording || _disposed)
            {
                return;
            }

            if (_prepared is not null && _prepared.Matches(deviceId))
            {
                return;
            }

            DisposePrepared();

            try
            {
                var device = ResolveDevice(deviceId);
                var client = device.AudioClient;
                var format = client.MixFormat;

                var waitHandle = new AutoResetEvent(false);

                client.Initialize(
                    AudioClientShareMode.Shared,
                    AudioClientStreamFlags.EventCallback,
                    BufferDuration,
                    0,
                    format,
                    Guid.Empty);

                client.SetEventHandle(waitHandle.SafeWaitHandle.DangerousGetHandle());

                _prepared = new PreparedDevice(deviceId, device, client, format, waitHandle);

                _logger.LogInformation(
                    "Capture device ready: {Device} at {Rate} Hz, {Channels} ch",
                    device.FriendlyName,
                    format.SampleRate,
                    format.Channels);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not prepare the capture device in advance");
                DisposePrepared();
            }
        }
    }

    public void Start(string? deviceId, TimeSpan maxDuration)
    {
        lock (_sync)
        {
            if (IsRecording)
            {
                return;
            }

            if (_prepared is null || !_prepared.Matches(deviceId))
            {
                // Not warmed up (first run, or the device changed): pay the cost now.
                DisposePrepared();
                PrepareCore(deviceId);
            }

            var prepared = _prepared
                ?? throw new InvalidOperationException("No hay ningún dispositivo de captura disponible.");

            _monoSamples = new List<float>(prepared.Format.SampleRate * 8);
            _captureCancellation = new CancellationTokenSource();

            var token = _captureCancellation.Token;
            _captureThread = new Thread(() => CaptureLoop(prepared, token))
            {
                IsBackground = true,
                Name = "VoiceFlow.Capture",
                Priority = ThreadPriority.AboveNormal
            };

            prepared.Client.Start();
            _captureThread.Start();
            IsRecording = true;

            if (maxDuration > TimeSpan.Zero)
            {
                _maxDurationTimer = new Timer(
                    _ => MaxDurationReached?.Invoke(this, EventArgs.Empty),
                    null,
                    maxDuration,
                    Timeout.InfiniteTimeSpan);
            }

            _logger.LogInformation("Recording started on {Device}", prepared.Device.FriendlyName);
        }
    }

    public RecordedAudio Stop()
    {
        List<float>? samples;
        int sourceRate;

        lock (_sync)
        {
            if (!IsRecording)
            {
                return RecordedAudio.Empty;
            }

            sourceRate = _prepared?.Format.SampleRate ?? RecordedAudio.TargetSampleRate;
            StopCaptureCore();
            samples = _monoSamples;
            _monoSamples = null;
        }

        if (samples is null || samples.Count == 0)
        {
            return RecordedAudio.Empty;
        }

        var resampled = Resample(samples.ToArray(), sourceRate, RecordedAudio.TargetSampleRate);
        return new RecordedAudio(resampled);
    }

    public void Cancel()
    {
        lock (_sync)
        {
            if (!IsRecording)
            {
                return;
            }

            StopCaptureCore();
            _monoSamples = null;
        }
    }

    private void PrepareCore(string? deviceId)
    {
        var device = ResolveDevice(deviceId);
        var client = device.AudioClient;
        var format = client.MixFormat;
        var waitHandle = new AutoResetEvent(false);

        client.Initialize(
            AudioClientShareMode.Shared,
            AudioClientStreamFlags.EventCallback,
            BufferDuration,
            0,
            format,
            Guid.Empty);

        client.SetEventHandle(waitHandle.SafeWaitHandle.DangerousGetHandle());
        _prepared = new PreparedDevice(deviceId, device, client, format, waitHandle);
    }

    private void StopCaptureCore()
    {
        _maxDurationTimer?.Dispose();
        _maxDurationTimer = null;

        _captureCancellation?.Cancel();

        var prepared = _prepared;

        try
        {
            prepared?.Client.Stop();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AudioClient.Stop threw");
        }

        // The loop wakes up on the event handle, so nudge it before joining.
        prepared?.WaitHandle.Set();
        _captureThread?.Join(TimeSpan.FromSeconds(2));
        _captureThread = null;

        _captureCancellation?.Dispose();
        _captureCancellation = null;

        try
        {
            // Reset makes the already-initialised client reusable for the next dictation.
            prepared?.Client.Reset();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AudioClient.Reset threw; the device will be prepared again");
            DisposePrepared();
        }

        IsRecording = false;
    }

    /// <summary>Reads packets until the recording is stopped.</summary>
    private void CaptureLoop(PreparedDevice prepared, CancellationToken cancellationToken)
    {
        try
        {
            var capture = prepared.Client.AudioCaptureClient;
            var blockAlign = prepared.Format.BlockAlign;
            var buffer = new byte[prepared.Format.AverageBytesPerSecond];

            while (!cancellationToken.IsCancellationRequested)
            {
                prepared.WaitHandle.WaitOne(50);

                while (!cancellationToken.IsCancellationRequested && capture.GetNextPacketSize() > 0)
                {
                    var pointer = capture.GetBuffer(out var frames, out var flags);

                    if (frames == 0)
                    {
                        capture.ReleaseBuffer(frames);
                        continue;
                    }

                    var bytes = frames * blockAlign;

                    if (buffer.Length < bytes)
                    {
                        buffer = new byte[bytes];
                    }

                    if ((flags & AudioClientBufferFlags.Silent) != 0)
                    {
                        Array.Clear(buffer, 0, bytes);
                    }
                    else
                    {
                        Marshal.Copy(pointer, buffer, 0, bytes);
                    }

                    capture.ReleaseBuffer(frames);
                    Append(buffer, bytes, prepared.Format);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Capture loop failed");
        }
    }

    private void Append(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        var frames = ReadMonoFrames(buffer, bytesRecorded, format);

        if (frames.Length == 0)
        {
            return;
        }

        lock (_sync)
        {
            _monoSamples?.AddRange(frames);
        }

        RaiseLevel(frames);
    }

    private void RaiseLevel(float[] frames)
    {
        var handler = LevelChanged;
        if (handler is null)
        {
            return;
        }

        float peak = 0;
        double sumOfSquares = 0;

        foreach (var sample in frames)
        {
            var magnitude = Math.Abs(sample);
            if (magnitude > peak)
            {
                peak = magnitude;
            }

            sumOfSquares += sample * (double)sample;
        }

        var rms = (float)Math.Sqrt(sumOfSquares / frames.Length);
        handler(this, new AudioLevelEventArgs(Math.Min(peak, 1f), Math.Min(rms, 1f)));
    }

    private MMDeviceEnumerator GetEnumerator() => _enumerator ??= new MMDeviceEnumerator();

    private MMDevice ResolveDevice(string? deviceId)
    {
        var enumerator = GetEnumerator();

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            try
            {
                return enumerator.GetDevice(deviceId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Configured capture device is unavailable, falling back to the default one");
            }
        }

        return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
    }

    private void DisposePrepared()
    {
        _prepared?.Dispose();
        _prepared = null;
    }

    /// <summary>Converts a raw WASAPI buffer into mono float samples in [-1, 1].</summary>
    internal static float[] ReadMonoFrames(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        var channels = Math.Max(1, format.Channels);
        var bytesPerSample = format.BitsPerSample / 8;
        if (bytesPerSample == 0)
        {
            return [];
        }

        var totalSamples = bytesRecorded / bytesPerSample;
        var frameCount = totalSamples / channels;
        if (frameCount == 0)
        {
            return [];
        }

        var result = new float[frameCount];

        for (var frame = 0; frame < frameCount; frame++)
        {
            float sum = 0;

            for (var channel = 0; channel < channels; channel++)
            {
                var offset = ((frame * channels) + channel) * bytesPerSample;
                sum += ReadSample(buffer, offset, format, bytesPerSample);
            }

            result[frame] = sum / channels;
        }

        return result;
    }

    private static float ReadSample(byte[] buffer, int offset, WaveFormat format, int bytesPerSample)
    {
        if (format.Encoding == WaveFormatEncoding.IeeeFloat && bytesPerSample == 4)
        {
            return BitConverter.ToSingle(buffer, offset);
        }

        switch (bytesPerSample)
        {
            case 2:
                return BitConverter.ToInt16(buffer, offset) / 32768f;

            case 3:
                var raw = (buffer[offset + 2] << 16) | (buffer[offset + 1] << 8) | buffer[offset];
                if (raw > 0x7FFFFF)
                {
                    raw -= 0x1000000;
                }

                return raw / 8388608f;

            case 4:
                return BitConverter.ToInt32(buffer, offset) / 2147483648f;

            case 1:
                return (buffer[offset] - 128) / 128f;

            default:
                return 0f;
        }
    }

    /// <summary>Sample-rate conversion of a mono buffer using the WDL resampler bundled with NAudio.</summary>
    internal static float[] Resample(float[] samples, int sourceRate, int targetRate)
    {
        if (sourceRate == targetRate || samples.Length == 0)
        {
            return samples;
        }

        ISampleProvider source = new MemorySampleProvider(samples, sourceRate);
        var resampler = new WdlResamplingSampleProvider(source, targetRate);

        var estimated = (int)(samples.LongLength * (targetRate / (double)sourceRate)) + 16;
        var output = new List<float>(estimated);
        var block = new float[4096];

        int read;
        while ((read = resampler.Read(block, 0, block.Length)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                output.Add(block[i]);
            }
        }

        return output.ToArray();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Cancel();

        lock (_sync)
        {
            DisposePrepared();
            _enumerator?.Dispose();
            _enumerator = null;
        }
    }

    /// <summary>An initialised capture endpoint, ready for AudioClient.Start.</summary>
    private sealed class PreparedDevice : IDisposable
    {
        public PreparedDevice(
            string? deviceId,
            MMDevice device,
            AudioClient client,
            WaveFormat format,
            AutoResetEvent waitHandle)
        {
            DeviceId = deviceId;
            Device = device;
            Client = client;
            Format = format;
            WaitHandle = waitHandle;
        }

        public string? DeviceId { get; }

        public MMDevice Device { get; }

        public AudioClient Client { get; }

        public WaveFormat Format { get; }

        public AutoResetEvent WaitHandle { get; }

        public bool Matches(string? deviceId) =>
            string.Equals(DeviceId ?? string.Empty, deviceId ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        public void Dispose()
        {
            try
            {
                Client.Dispose();
                Device.Dispose();
            }
            catch (Exception)
            {
                // The endpoint may already be gone; nothing useful to do here.
            }

            WaitHandle.Dispose();
        }
    }
}

/// <summary>Reads an in-memory mono float buffer as an <see cref="ISampleProvider"/>.</summary>
internal sealed class MemorySampleProvider : ISampleProvider
{
    private readonly float[] _samples;
    private int _position;

    public MemorySampleProvider(float[] samples, int sampleRate)
    {
        _samples = samples;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var available = Math.Min(count, _samples.Length - _position);
        if (available <= 0)
        {
            return 0;
        }

        Array.Copy(_samples, _position, buffer, offset, available);
        _position += available;
        return available;
    }
}
