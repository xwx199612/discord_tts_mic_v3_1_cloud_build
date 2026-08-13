using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Collections.Concurrent;

namespace DiscordTtsMic;

public sealed class AudioMixerEngine : IDisposable
{
    public const int SampleRate = 48000;
    public const int Channels = 1;

    private WasapiCapture? _capture;
    private WasapiOut? _output;
    private MediaFoundationResampler? _outputResampler;
    private BufferedWaveProvider? _micBuffer;
    private BufferedWaveProvider? _mixedBuffer;
    private CancellationTokenSource? _pumpCts;
    private Task? _pumpTask;

    private readonly ConcurrentQueue<byte[]> _ttsQueue = new();
    private byte[]? _ttsCurrent;
    private int _ttsCurrentOffset;
    private long _queuedTtsBytes;

    private volatile float _micGain = 1f;
    private volatile float _ttsGain = 1f;
    private volatile bool _passMic = true;
    private volatile bool _duckMic = true;
    private volatile float _duckGain = 0.25f;

    public bool HasPendingTts => Interlocked.Read(ref _queuedTtsBytes) > 0 || _ttsCurrent is not null || !_ttsQueue.IsEmpty;
    public TimeSpan PendingTtsDuration => TimeSpan.FromSeconds(Math.Max(0, Interlocked.Read(ref _queuedTtsBytes)) / 2.0 / SampleRate);

    public void SetLevels(float micGain, float ttsGain, bool passMic, bool duckMic, float duckGain)
    {
        _micGain = Math.Clamp(micGain, 0f, 2f);
        _ttsGain = Math.Clamp(ttsGain, 0f, 2f);
        _passMic = passMic;
        _duckMic = duckMic;
        _duckGain = Math.Clamp(duckGain, 0f, 1f);
    }

    public void Start(MMDeviceSelection micSelection, MMDeviceSelection outputSelection)
    {
        Stop(clearTts: false);

        if (outputSelection.Device is null)
            throw new InvalidOperationException("No VB-CABLE playback device selected.");

        _micBuffer = MakeMonoBuffer(2);
        _mixedBuffer = MakeMonoBuffer(4);

        _output = new WasapiOut(outputSelection.Device, AudioClientShareMode.Shared, true, 80);
        _outputResampler = new MediaFoundationResampler(_mixedBuffer, _output.OutputWaveFormat)
        {
            ResamplerQuality = 60
        };
        _output.Init(_outputResampler);
        _output.Play();

        if (micSelection.Device is not null)
        {
            _capture = new WasapiCapture(micSelection.Device);
            _capture.DataAvailable += CaptureOnDataAvailable;
            _capture.StartRecording();
        }

        _pumpCts = new CancellationTokenSource();
        _pumpTask = Task.Run(() => PumpAsync(_pumpCts.Token));
    }

    private static BufferedWaveProvider MakeMonoBuffer(int seconds) => new(new WaveFormat(SampleRate, 16, Channels))
    {
        DiscardOnBufferOverflow = true,
        BufferDuration = TimeSpan.FromSeconds(seconds),
        ReadFully = true
    };

    private void CaptureOnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_capture is null || _micBuffer is null || e.BytesRecorded <= 0)
            return;

        using var raw = new RawSourceWaveStream(
            new MemoryStream(e.Buffer, 0, e.BytesRecorded, writable: false, publiclyVisible: true),
            _capture.WaveFormat);

        ISampleProvider sample = raw.ToSampleProvider();
        if (sample.WaveFormat.Channels > 1)
            sample = new StereoToMonoSampleProvider(sample) { LeftVolume = 0.5f, RightVolume = 0.5f };
        if (sample.WaveFormat.SampleRate != SampleRate)
            sample = new WdlResamplingSampleProvider(sample, SampleRate);

        var pcm16 = new SampleToWaveProvider16(sample);
        var temp = new byte[Math.Max(8192, e.BytesRecorded * 4)];
        int n;
        while ((n = pcm16.Read(temp, 0, temp.Length)) > 0)
            _micBuffer.AddSamples(temp, 0, n);
    }

    public void QueueTtsPcm16(byte[] pcm48kMono16)
    {
        if (pcm48kMono16.Length == 0) return;
        _ttsQueue.Enqueue(pcm48kMono16);
        Interlocked.Add(ref _queuedTtsBytes, pcm48kMono16.Length);
    }

    public void ClearTtsQueue()
    {
        while (_ttsQueue.TryDequeue(out _)) { }
        _ttsCurrent = null;
        _ttsCurrentOffset = 0;
        Interlocked.Exchange(ref _queuedTtsBytes, 0);
    }

    private int ReadTts(byte[] destination, int count)
    {
        Array.Clear(destination, 0, count);
        int written = 0;

        while (written < count)
        {
            if (_ttsCurrent is null || _ttsCurrentOffset >= _ttsCurrent.Length)
            {
                _ttsCurrent = null;
                _ttsCurrentOffset = 0;
                if (!_ttsQueue.TryDequeue(out _ttsCurrent))
                    break;
            }

            int available = _ttsCurrent.Length - _ttsCurrentOffset;
            int take = Math.Min(count - written, available);
            Buffer.BlockCopy(_ttsCurrent, _ttsCurrentOffset, destination, written, take);
            _ttsCurrentOffset += take;
            written += take;
            Interlocked.Add(ref _queuedTtsBytes, -take);

            if (_ttsCurrentOffset >= _ttsCurrent.Length)
            {
                _ttsCurrent = null;
                _ttsCurrentOffset = 0;
            }
        }

        return written;
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        const int frameMs = 10;
        int samples = SampleRate * frameMs / 1000;
        int bytes = samples * 2;
        var mic = new byte[bytes];
        var tts = new byte[bytes];
        var mixed = new byte[bytes];

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(frameMs));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                _micBuffer?.Read(mic, 0, bytes);
                int ttsBytes = ReadTts(tts, bytes);
                bool ttsActive = ttsBytes > 0 || HasPendingTts;

                var mg = _passMic ? _micGain * ((_duckMic && ttsActive) ? _duckGain : 1f) : 0f;
                var tg = _ttsGain;

                for (int i = 0; i < bytes; i += 2)
                {
                    short micSample = (short)(mic[i] | (mic[i + 1] << 8));
                    short ttsSample = (short)(tts[i] | (tts[i + 1] << 8));
                    int value = (int)(micSample * mg + ttsSample * tg);
                    value = Math.Clamp(value, short.MinValue, short.MaxValue);
                    mixed[i] = (byte)(value & 0xff);
                    mixed[i + 1] = (byte)((value >> 8) & 0xff);
                }

                _mixedBuffer?.AddSamples(mixed, 0, mixed.Length);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Stop(bool clearTts = true)
    {
        if (_pumpCts is not null)
        {
            _pumpCts.Cancel();
            try { _pumpTask?.Wait(500); } catch { }
            _pumpCts.Dispose();
            _pumpCts = null;
            _pumpTask = null;
        }

        try { _capture?.StopRecording(); } catch { }
        _capture?.Dispose();
        _capture = null;

        try { _output?.Stop(); } catch { }
        _output?.Dispose();
        _output = null;

        _outputResampler?.Dispose();
        _outputResampler = null;
        _micBuffer = null;
        _mixedBuffer = null;

        if (clearTts)
            ClearTtsQueue();
    }

    public void Dispose() => Stop();
}

public sealed record MMDeviceSelection(string Name, MMDevice? Device)
{
    public override string ToString() => Name;
}
