using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SherpaOnnx;
using System.Text.Json;

namespace DiscordTtsMic;

public sealed class VoicePackTts : IDisposable
{
    private OfflineTts? _tts;
    private VoicePackManifest? _manifest;
    private string? _folder;

    public bool IsLoaded => _tts is not null && _manifest is not null;
    public string DisplayName => _manifest?.Name ?? "No custom voice pack";
    public string? Folder => _folder;
    public int SpeakerId { get; set; }
    public float Speed { get; set; } = 1.0f;

    public void Load(string folder)
    {
        DisposeTts();
        folder = Path.GetFullPath(folder);
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);

        var manifestPath = Path.Combine(folder, "voicepack.json");
        VoicePackManifest manifest;

        if (File.Exists(manifestPath))
        {
            manifest = JsonSerializer.Deserialize<VoicePackManifest>(File.ReadAllText(manifestPath), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new InvalidDataException("Invalid voicepack.json");
        }
        else
        {
            manifest = AutoDetect(folder);
        }

        string model = Resolve(folder, manifest.Model, required: true);
        string tokens = Resolve(folder, manifest.Tokens, required: true);
        string dataDir = Resolve(folder, manifest.DataDir, required: false, isDirectory: true);
        string lexicon = Resolve(folder, manifest.Lexicon, required: false);
        string ruleFsts = ResolveList(folder, manifest.RuleFsts);

        var config = new OfflineTtsConfig();
        config.Model.Vits.Model = model;
        config.Model.Vits.Tokens = tokens;
        config.Model.Vits.DataDir = dataDir;
        config.Model.Vits.Lexicon = lexicon;
        config.Model.NumThreads = Math.Clamp(manifest.Threads <= 0 ? 2 : manifest.Threads, 1, 16);
        config.Model.Provider = "cpu";
        config.Model.Debug = 0;
        config.RuleFsts = ruleFsts;
        config.MaxNumSentences = 1;

        _tts = new OfflineTts(config);
        _manifest = manifest;
        _folder = folder;
        SpeakerId = Math.Max(0, manifest.SpeakerId);
        Speed = manifest.Speed <= 0 ? 1.0f : manifest.Speed;
    }

    public byte[] SynthesizePcm48kMono16(string text)
    {
        if (_tts is null) throw new InvalidOperationException("No custom voice pack is loaded.");

        var gen = new OfflineTtsGenerationConfig
        {
            Sid = SpeakerId,
            Speed = Math.Clamp(Speed, 0.25f, 4.0f),
            SilenceScale = 0.2f
        };

        var audio = _tts.GenerateWithConfig(text, gen, null);
        string temp = Path.Combine(Path.GetTempPath(), $"discord_tts_{Guid.NewGuid():N}.wav");
        try
        {
            if (!audio.SaveToWaveFile(temp))
                throw new InvalidOperationException("Custom voice pack failed to generate audio.");
            return WaveToPcm48kMono16(temp);
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }

    private static byte[] WaveToPcm48kMono16(string path)
    {
        using var reader = new AudioFileReader(path);
        ISampleProvider sample = reader;
        if (sample.WaveFormat.Channels > 1)
            sample = new StereoToMonoSampleProvider(sample) { LeftVolume = 0.5f, RightVolume = 0.5f };
        if (sample.WaveFormat.SampleRate != AudioMixerEngine.SampleRate)
            sample = new WdlResamplingSampleProvider(sample, AudioMixerEngine.SampleRate);

        var pcm = new SampleToWaveProvider16(sample);
        using var output = new MemoryStream();
        var buffer = new byte[16384];
        int n;
        while ((n = pcm.Read(buffer, 0, buffer.Length)) > 0)
            output.Write(buffer, 0, n);
        return output.ToArray();
    }

    private static VoicePackManifest AutoDetect(string folder)
    {
        var model = Directory.GetFiles(folder, "*.onnx", SearchOption.TopDirectoryOnly)
            .OrderByDescending(p => Path.GetFileName(p).Equals("model.onnx", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault() ?? throw new InvalidDataException("Voice pack needs an ONNX model file.");

        var tokens = Path.Combine(folder, "tokens.txt");
        if (!File.Exists(tokens))
            throw new InvalidDataException("Voice pack needs tokens.txt. For Piper voices, use a sherpa-onnx compatible voice package.");

        var dataDir = Directory.Exists(Path.Combine(folder, "espeak-ng-data")) ? "espeak-ng-data" : "";
        var lexicon = File.Exists(Path.Combine(folder, "lexicon.txt")) ? "lexicon.txt" : "";
        var fsts = Directory.GetFiles(folder, "*.fst", SearchOption.TopDirectoryOnly).Select(Path.GetFileName).Where(x => x is not null).Cast<string>().ToArray();

        return new VoicePackManifest
        {
            Name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            Model = Path.GetFileName(model),
            Tokens = "tokens.txt",
            DataDir = dataDir,
            Lexicon = lexicon,
            RuleFsts = fsts,
            SpeakerId = 0,
            Speed = 1.0f,
            Threads = 2
        };
    }

    private static string Resolve(string folder, string? relative, bool required, bool isDirectory = false)
    {
        if (string.IsNullOrWhiteSpace(relative))
        {
            if (required) throw new InvalidDataException("Required voice pack path is missing.");
            return "";
        }
        var full = Path.IsPathRooted(relative) ? relative : Path.Combine(folder, relative);
        bool exists = isDirectory ? Directory.Exists(full) : File.Exists(full);
        if (required && !exists) throw new FileNotFoundException($"Voice pack file not found: {full}");
        return exists ? Path.GetFullPath(full) : "";
    }

    private static string ResolveList(string folder, string[]? files)
    {
        if (files is null || files.Length == 0) return "";
        return string.Join(',', files.Select(x => Resolve(folder, x, required: true)));
    }

    private void DisposeTts()
    {
        _tts?.Dispose();
        _tts = null;
    }

    public void Dispose() => DisposeTts();
}

public sealed class VoicePackManifest
{
    public string Name { get; set; } = "Custom voice";
    public string Model { get; set; } = "model.onnx";
    public string Tokens { get; set; } = "tokens.txt";
    public string DataDir { get; set; } = "espeak-ng-data";
    public string Lexicon { get; set; } = "";
    public string[] RuleFsts { get; set; } = Array.Empty<string>();
    public int SpeakerId { get; set; }
    public float Speed { get; set; } = 1.0f;
    public int Threads { get; set; } = 2;
}
