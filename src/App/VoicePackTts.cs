using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SherpaOnnx;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace DiscordTtsMic;

public sealed class VoicePackTts : IDisposable
{
    private OfflineTts? _tts;
    private VoicePackManifest? _manifest;
    private string? _folder;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public bool IsLoaded => _manifest is not null && (_tts is not null || !IsSherpaEngine(_manifest.Engine));
    public string DisplayName => _manifest?.Name ?? "No custom voice pack";
    public string EngineName => _manifest?.Engine ?? "none";
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

        manifest.Engine = string.IsNullOrWhiteSpace(manifest.Engine) ? "sherpa-vits" : manifest.Engine.Trim().ToLowerInvariant();

        if (IsSherpaEngine(manifest.Engine))
        {
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
        }
        else if (manifest.Engine is "gpt-sovits-http" or "cosyvoice-http" or "http")
        {
            if (string.IsNullOrWhiteSpace(manifest.Endpoint))
                throw new InvalidDataException("HTTP voice pack needs an endpoint in voicepack.json.");
        }
        else if (manifest.Engine == "command")
        {
            string exe = Resolve(folder, manifest.Executable, required: true);
            manifest.Executable = exe;
            if (string.IsNullOrWhiteSpace(manifest.Arguments))
                throw new InvalidDataException("Command voice pack needs arguments in voicepack.json.");
        }
        else
        {
            throw new InvalidDataException($"Unsupported voice-pack engine: {manifest.Engine}");
        }

        _manifest = manifest;
        _folder = folder;
        SpeakerId = Math.Max(0, manifest.SpeakerId);
        Speed = manifest.Speed <= 0 ? 1.0f : manifest.Speed;
    }

    public byte[] SynthesizePcm48kMono16(string text)
    {
        if (_manifest is null) throw new InvalidOperationException("No custom voice pack is loaded.");

        if (IsSherpaEngine(_manifest.Engine))
            return SynthesizeSherpa(text);

        if (_manifest.Engine is "gpt-sovits-http" or "cosyvoice-http" or "http")
            return SynthesizeHttpAsync(text).GetAwaiter().GetResult();

        if (_manifest.Engine == "command")
            return SynthesizeCommand(text);

        throw new InvalidOperationException($"Unsupported engine: {_manifest.Engine}");
    }

    private byte[] SynthesizeSherpa(string text)
    {
        if (_tts is null) throw new InvalidOperationException("Sherpa voice pack is not initialized.");

        var gen = new OfflineTtsGenerationConfig
        {
            Sid = SpeakerId,
            Speed = Math.Clamp(Speed, 0.25f, 4.0f),
            SilenceScale = 0.2f
        };

        var audio = _tts.GenerateWithConfig(text, gen, null);
        string temp = TempWav();
        try
        {
            if (!audio.SaveToWaveFile(temp))
                throw new InvalidOperationException("Custom voice pack failed to generate audio.");
            return WaveToPcm48kMono16(temp);
        }
        finally { TryDelete(temp); }
    }

    private async Task<byte[]> SynthesizeHttpAsync(string text)
    {
        if (_manifest is null || _folder is null) throw new InvalidOperationException();

        using var request = new HttpRequestMessage(HttpMethod.Post, _manifest.Endpoint);
        string engine = _manifest.Engine;

        if (!string.IsNullOrWhiteSpace(_manifest.RequestJson))
        {
            string body = Expand(_manifest.RequestJson, text);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }
        else if (engine == "gpt-sovits-http")
        {
            var payload = new Dictionary<string, object?>
            {
                ["text"] = text,
                ["text_lang"] = _manifest.TextLang,
                ["ref_audio_path"] = ResolveOptionalForPayload(_manifest.ReferenceAudio),
                ["prompt_lang"] = _manifest.PromptLang,
                ["prompt_text"] = _manifest.PromptText,
                ["text_split_method"] = "cut5",
                ["batch_size"] = 1,
                ["media_type"] = "wav",
                ["streaming_mode"] = false,
                ["speed_factor"] = Speed
            };
            request.Content = JsonContent.Create(payload);
        }
        else if (engine == "cosyvoice-http")
        {
            var payload = new Dictionary<string, object?>
            {
                ["text"] = text,
                ["speaker"] = _manifest.Speaker,
                ["spk_id"] = SpeakerId,
                ["speed"] = Speed,
                ["prompt_text"] = _manifest.PromptText,
                ["reference_audio"] = ResolveOptionalForPayload(_manifest.ReferenceAudio)
            };
            request.Content = JsonContent.Create(payload);
        }
        else
        {
            request.Content = JsonContent.Create(new { text, speakerId = SpeakerId, speed = Speed });
        }

        foreach (var kv in _manifest.Headers ?? new Dictionary<string, string>())
            request.Headers.TryAddWithoutValidation(kv.Key, Expand(kv.Value, text));

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode)
        {
            string error = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"TTS HTTP {(int)response.StatusCode}: {error}");
        }

        byte[] bytes = await response.Content.ReadAsByteArrayAsync();
        if (bytes.Length < 44) throw new InvalidDataException("TTS endpoint returned no usable audio.");
        return WaveBytesToPcm48kMono16(bytes);
    }

    private byte[] SynthesizeCommand(string text)
    {
        if (_manifest is null || _folder is null) throw new InvalidOperationException();

        string inputTxt = Path.Combine(Path.GetTempPath(), $"discord_tts_{Guid.NewGuid():N}.txt");
        string outputWav = TempWav();
        File.WriteAllText(inputTxt, text, new UTF8Encoding(false));

        try
        {
            string args = Expand(_manifest.Arguments, text)
                .Replace("{inputTextFile}", Quote(inputTxt), StringComparison.Ordinal)
                .Replace("{outputWav}", Quote(outputWav), StringComparison.Ordinal);

            var psi = new ProcessStartInfo
            {
                FileName = _manifest.Executable,
                Arguments = args,
                WorkingDirectory = _folder,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start voice-pack executable.");
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(Math.Max(10_000, _manifest.TimeoutSeconds * 1000)))
            {
                try { process.Kill(true); } catch { }
                throw new TimeoutException("Voice-pack executable timed out.");
            }
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Voice-pack executable failed ({process.ExitCode}).\n{stderr}\n{stdout}");
            if (!File.Exists(outputWav))
                throw new FileNotFoundException("Voice-pack executable did not create the requested WAV file.", outputWav);

            return WaveToPcm48kMono16(outputWav);
        }
        finally
        {
            TryDelete(inputTxt);
            TryDelete(outputWav);
        }
    }

    private string Expand(string? template, string text)
    {
        if (string.IsNullOrEmpty(template)) return "";
        string folder = _folder ?? "";
        string refAudio = ResolveOptionalForPayload(_manifest?.ReferenceAudio);
        return template
            .Replace("{text}", JsonEncodedText.Encode(text).ToString(), StringComparison.Ordinal)
            .Replace("{textRaw}", text, StringComparison.Ordinal)
            .Replace("{folder}", folder, StringComparison.Ordinal)
            .Replace("{referenceAudio}", refAudio, StringComparison.Ordinal)
            .Replace("{speakerId}", SpeakerId.ToString(), StringComparison.Ordinal)
            .Replace("{speaker}", _manifest?.Speaker ?? "", StringComparison.Ordinal)
            .Replace("{speed}", Speed.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{textLang}", _manifest?.TextLang ?? "auto", StringComparison.Ordinal)
            .Replace("{promptLang}", _manifest?.PromptLang ?? "auto", StringComparison.Ordinal)
            .Replace("{promptText}", JsonEncodedText.Encode(_manifest?.PromptText ?? "").ToString(), StringComparison.Ordinal);
    }

    private string ResolveOptionalForPayload(string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) return "";
        if (_folder is null) return relative;
        string full = Path.IsPathRooted(relative) ? relative : Path.Combine(_folder, relative);
        return Path.GetFullPath(full);
    }

    private static byte[] WaveBytesToPcm48kMono16(byte[] wav)
    {
        using var ms = new MemoryStream(wav, writable: false);
        using var reader = new WaveFileReader(ms);
        return ReaderToPcm(reader);
    }

    private static byte[] WaveToPcm48kMono16(string path)
    {
        using var reader = new AudioFileReader(path);
        return SampleToPcm(reader);
    }

    private static byte[] ReaderToPcm(WaveFileReader reader) => SampleToPcm(reader.ToSampleProvider());

    private static byte[] SampleToPcm(ISampleProvider sample)
    {
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
            .FirstOrDefault() ?? throw new InvalidDataException("No voicepack.json and no ONNX model was found.");

        var tokens = Path.Combine(folder, "tokens.txt");
        if (!File.Exists(tokens))
            throw new InvalidDataException("Auto-detected ONNX voice pack needs tokens.txt. For GPT-SoVITS/CosyVoice/other engines, add voicepack.json.");

        var dataDir = Directory.Exists(Path.Combine(folder, "espeak-ng-data")) ? "espeak-ng-data" : "";
        var lexicon = File.Exists(Path.Combine(folder, "lexicon.txt")) ? "lexicon.txt" : "";
        var fsts = Directory.GetFiles(folder, "*.fst", SearchOption.TopDirectoryOnly).Select(Path.GetFileName).Where(x => x is not null).Cast<string>().ToArray();

        return new VoicePackManifest
        {
            Name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            Engine = "sherpa-vits",
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

    private static bool IsSherpaEngine(string? engine) => string.IsNullOrWhiteSpace(engine) || engine is "sherpa-vits" or "vits" or "sherpa";

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

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
    private static string TempWav() => Path.Combine(Path.GetTempPath(), $"discord_tts_{Guid.NewGuid():N}.wav");
    private static void TryDelete(string path) { try { File.Delete(path); } catch { } }

    private void DisposeTts()
    {
        _tts?.Dispose();
        _tts = null;
    }

    public void Dispose()
    {
        DisposeTts();
        _http.Dispose();
    }
}

public sealed class VoicePackManifest
{
    public string Name { get; set; } = "Custom voice";
    public string Engine { get; set; } = "sherpa-vits";

    // sherpa-onnx VITS
    public string Model { get; set; } = "model.onnx";
    public string Tokens { get; set; } = "tokens.txt";
    public string DataDir { get; set; } = "espeak-ng-data";
    public string Lexicon { get; set; } = "";
    public string[] RuleFsts { get; set; } = Array.Empty<string>();
    public int Threads { get; set; } = 2;

    // common controls
    public int SpeakerId { get; set; }
    public string Speaker { get; set; } = "";
    public float Speed { get; set; } = 1.0f;
    public int TimeoutSeconds { get; set; } = 180;

    // GPT-SoVITS / CosyVoice / generic HTTP
    public string Endpoint { get; set; } = "";
    public string TextLang { get; set; } = "auto";
    public string PromptLang { get; set; } = "auto";
    public string PromptText { get; set; } = "";
    public string ReferenceAudio { get; set; } = "";
    public string RequestJson { get; set; } = "";
    public Dictionary<string, string> Headers { get; set; } = new();

    // portable/custom command adapter
    public string Executable { get; set; } = "";
    public string Arguments { get; set; } = "";
}
