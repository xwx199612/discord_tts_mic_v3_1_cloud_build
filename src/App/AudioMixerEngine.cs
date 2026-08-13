using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace DiscordTtsMic;

public sealed class AudioMixerEngine : IDisposable
{
    public const int SampleRate = 48000;
    public const int Channels = 1;

    private readonly DriverBridgeClient _driver;
    private WasapiCapture? _capture;
    private BufferedWaveProvider? _micBuffer;
    private BufferedWaveProvider? _ttsBuffer;
    private CancellationTokenSource? _pumpCts;
    private Task? _pumpTask;

    private volatile float _micGain = 1f;
    private volatile float _ttsGain = 1f;
    private volatile bool _passMic = true;
    private volatile bool _ttsActive;
    private volatile bool _duckMic = true;
    private volatile float _duckGain = 0.25f;

    public AudioMixerEngine(DriverBridgeClient driver) => _driver = driver;

    public void SetLevels(float micGain, float ttsGain, bool passMic, bool duckMic, float duckGain)
    {
        _micGain = Math.Clamp(micGain, 0f, 2f);
        _ttsGain = Math.Clamp(ttsGain, 0f, 2f);
        _passMic = passMic;
        _duckMic = duckMic;
        _duckGain = Math.Clamp(duckGain, 0f, 1f);
    }

    public void Start(MMDeviceSelection micSelection)
    {
        Stop();
        _micBuffer = MakeBuffer();
        _ttsBuffer = MakeBuffer();

        if (micSelection.Device is not null)
        {
            _capture = new WasapiCapture(micSelection.Device);
            _capture.DataAvailable += CaptureOnDataAvailable;
            _capture.StartRecording();
        }

        _pumpCts = new CancellationTokenSource();
        _pumpTask = Task.Run(() => PumpAsync(_pumpCts.Token));
    }

    private static BufferedWaveProvider MakeBuffer() => new(new WaveFormat(SampleRate, 16, Channels))
    {
        DiscardOnBufferOverflow = true,
        BufferDuration = TimeSpan.FromSeconds(2),
        ReadFully = true
    };

    private void CaptureOnDataAvailable(object? sender, WaveInEventArgs e)
    {
        using var raw = new RawSourceWaveStream(new MemoryStream(e.Buffer, 0, e.BytesRecorded, false, true), _capture!.WaveFormat);
        var sample = raw.ToSampleProvider();
        if (sample.WaveFormat.Channels > 1)
            sample = new StereoToMonoSampleProvider(sample) { LeftVolume = 0.5f, RightVolume = 0.5f };
        if (sample.WaveFormat.SampleRate != SampleRate)
            sample = new WdlResamplingSampleProvider(sample, SampleRate);
        var pcm16 = new SampleToWaveProvider16(sample);
        var temp = new byte[Math.Max(4096, e.BytesRecorded * 2)];
        int n;
        while ((n = pcm16.Read(temp, 0, temp.Length)) > 0)
            _micBuffer?.AddSamples(temp, 0, n);
    }

    public void QueueTtsPcm16(byte[] pcm48kMono16) => _ttsBuffer?.AddSamples(pcm48kMono16, 0, pcm48kMono16.Length);
    public void SetTtsActive(bool active) => _ttsActive = active;

    private async Task PumpAsync(CancellationToken ct)
    {
        const int frameMs = 10;
        int samples = SampleRate * frameMs / 1000;
        int bytes = samples * 2;
        var mic = new byte[bytes];
        var tts = new byte[bytes];
        var output = new byte[bytes];

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(frameMs));
        while (await timer.WaitForNextTickAsync(ct))
        {
            _micBuffer?.Read(mic, 0, bytes);
            _ttsBuffer?.Read(tts, 0, bytes);
            var mg = _passMic ? _micGain * ((_duckMic && _ttsActive) ? _duckGain : 1f) : 0f;
            var tg = _ttsGain;

            for (int i = 0; i < bytes; i += 2)
            {
                short ms = (short)(mic[i] | (mic[i + 1] << 8));
                short ts = (short)(tts[i] | (tts[i + 1] << 8));
                int mixed = (int)(ms * mg + ts * tg);
                mixed = Math.Clamp(mixed, short.MinValue, short.MaxValue);
                output[i] = (byte)(mixed & 0xff);
                output[i + 1] = (byte)((mixed >> 8) & 0xff);
            }

            if (_driver.IsConnected)
                _driver.WritePcm16(output);
        }
    }

    public void Stop()
    {
        try { _capture?.StopRecording(); } catch { }
        _capture?.Dispose();
        _capture = null;
        if (_pumpCts is not null)
        {
            _pumpCts.Cancel();
            try { _pumpTask?.Wait(500); } catch { }
            _pumpCts.Dispose();
            _pumpCts = null;
        }
    }

    public void Dispose() => Stop();
}

public sealed record MMDeviceSelection(string Name, NAudio.CoreAudioApi.MMDevice? Device)
{
    public override string ToString() => Name;
}
