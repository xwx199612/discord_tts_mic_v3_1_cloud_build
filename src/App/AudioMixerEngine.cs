using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace DiscordTtsMic;

/// <summary>
/// Captures the physical microphone, mixes it with TTS as 48 kHz mono PCM16,
/// then plays the mixed stream into a selected Windows playback endpoint.
/// For VB-CABLE, select CABLE Input as the playback endpoint and use
/// CABLE Output as Discord's microphone input.
/// </summary>
public sealed class AudioMixerEngine : IDisposable
{
    public const int SampleRate = 48000;
    public const int Channels = 1;

    private WasapiCapture? _capture;
    private WasapiOut? _output;
    private MediaFoundationResampler? _outputResampler;
    private BufferedWaveProvider? _micBuffer;
    private BufferedWaveProvider? _ttsBuffer;
    private BufferedWaveProvider? _mixedBuffer;
    private CancellationTokenSource? _pumpCts;
    private Task? _pumpTask;

    private volatile float _micGain = 1f;
    private volatile float _ttsGain = 1f;
    private volatile bool _passMic = true;
    private volatile bool _ttsActive;
    private volatile bool _duckMic = true;
    private volatile float _duckGain = 0.25f;

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
        Stop();

        if (outputSelection.Device is null)
            throw new InvalidOperationException("No VB-CABLE playback device selected.");

        _micBuffer = MakeMonoBuffer();
        _ttsBuffer = MakeMonoBuffer();
        _mixedBuffer = MakeMonoBuffer(bufferSeconds: 3);

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

    private static BufferedWaveProvider MakeMonoBuffer(int bufferSeconds = 2) => new(new WaveFormat(SampleRate, 16, Channels))
    {
        DiscardOnBufferOverflow = true,
        BufferDuration = TimeSpan.FromSeconds(bufferSeconds),
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
        {
            sample = new StereoToMonoSampleProvider(sample)
            {
                LeftVolume = 0.5f,
                RightVolume = 0.5f
            };
        }

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
        _ttsBuffer?.AddSamples(pcm48kMono16, 0, pcm48kMono16.Length);
    }

    public void SetTtsActive(bool active) => _ttsActive = active;

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
                _ttsBuffer?.Read(tts, 0, bytes);

                var mg = _passMic ? _micGain * ((_duckMic && _ttsActive) ? _duckGain : 1f) : 0f;
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

    public void Stop()
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
        _ttsBuffer = null;
        _mixedBuffer = null;
    }

    public void Dispose() => Stop();
}

public sealed record MMDeviceSelection(string Name, MMDevice? Device)
{
    public override string ToString() => Name;
}
