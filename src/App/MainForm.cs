using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Speech.Synthesis;
using System.Text;

namespace DiscordTtsMic;

public sealed class MainForm : Form
{
    private readonly ComboBox _engine = new() { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _voice = new() { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _loadPack = new() { Text = "Load voice pack…", AutoSize = true };
    private readonly Label _packName = new() { AutoSize = true, Text = "No custom voice pack loaded" };
    private readonly NumericUpDown _speaker = new() { Minimum = 0, Maximum = 9999, Value = 0, Width = 80 };
    private readonly NumericUpDown _speed = new() { Minimum = 25, Maximum = 400, Value = 100, Increment = 5, Width = 80 };
    private readonly ComboBox _mic = new() { Width = 420, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _output = new() { Width = 420, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _text = new() { Multiline = true, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 14), ScrollBars = ScrollBars.Vertical, MaxLength = 200000 };
    private readonly TrackBar _micVol = new() { Minimum = 0, Maximum = 200, Value = 100, TickFrequency = 25, Width = 220 };
    private readonly TrackBar _ttsVol = new() { Minimum = 0, Maximum = 200, Value = 100, TickFrequency = 25, Width = 220 };
    private readonly CheckBox _passMic = new() { Text = "Pass physical microphone", Checked = true, AutoSize = true };
    private readonly CheckBox _duck = new() { Text = "Duck microphone while TTS speaks", Checked = true, AutoSize = true };
    private readonly Label _route = new() { AutoSize = true, Text = "Route: not started" };
    private readonly Label _status = new() { AutoSize = true, Text = "Ready" };
    private readonly Button _speak = new() { Text = "Speak (Enter)", AutoSize = true };
    private readonly Button _restartAudio = new() { Text = "Restart audio", AutoSize = true };

    private readonly AudioMixerEngine _audio = new();
    private readonly VoicePackTts _voicePack = new();
    private bool _speaking;

    public MainForm()
    {
        Text = "Discord TTS — VB-CABLE Edition (Custom Voice Packs)";
        Width = 980;
        Height = 720;
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(BuildUi());

        Load += (_, _) => InitializeAll();
        FormClosed += (_, _) => { _audio.Dispose(); _voicePack.Dispose(); };
        _speak.Click += async (_, _) => await SpeakAsync();
        _loadPack.Click += (_, _) => LoadVoicePack();
        _restartAudio.Click += (_, _) => RestartAudio();
        _mic.SelectedIndexChanged += (_, _) => RestartAudio();
        _output.SelectedIndexChanged += (_, _) => RestartAudio();
        _engine.SelectedIndexChanged += (_, _) => UpdateVoiceControls();
        _micVol.Scroll += (_, _) => ApplyLevels();
        _ttsVol.Scroll += (_, _) => ApplyLevels();
        _passMic.CheckedChanged += (_, _) => ApplyLevels();
        _duck.CheckedChanged += (_, _) => ApplyLevels();
        _text.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                await SpeakAsync();
            }
        };
    }

    private Control BuildUi()
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 12 };
        for (int i = 0; i < 9; i++) t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        t.Controls.Add(Row("TTS engine", _engine), 0, 0);
        t.Controls.Add(Row("Windows voice", _voice), 0, 1);
        t.Controls.Add(Row("Voice pack", _loadPack, _packName), 0, 2);
        t.Controls.Add(Row("Pack options", new Label { Text = "Speaker ID", AutoSize = true, Margin = new Padding(3, 8, 3, 3) }, _speaker,
            new Label { Text = "Speed %", AutoSize = true, Margin = new Padding(15, 8, 3, 3) }, _speed), 0, 3);
        t.Controls.Add(Row("Physical mic", _mic), 0, 4);
        t.Controls.Add(Row("VB-CABLE out", _output, _restartAudio), 0, 5);
        t.Controls.Add(Row("Mic gain", _micVol, _passMic), 0, 6);
        t.Controls.Add(Row("TTS gain", _ttsVol, _duck), 0, 7);
        t.Controls.Add(_route, 0, 8);
        t.Controls.Add(_text, 0, 9);
        t.Controls.Add(Row("", _speak), 0, 10);
        t.Controls.Add(_status, 0, 11);
        return t;
    }

    private static Control Row(string label, params Control[] controls)
    {
        var p = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        if (label.Length > 0)
            p.Controls.Add(new Label { Text = label, Width = 110, AutoSize = false, Margin = new Padding(3, 8, 3, 3) });
        foreach (var c in controls) p.Controls.Add(c);
        return p;
    }

    private void InitializeAll()
    {
        _engine.Items.Add("Windows / SAPI");
        _engine.Items.Add("Custom voice pack");
        _engine.SelectedIndex = 0;

        using (var synth = new SpeechSynthesizer())
        {
            foreach (var v in synth.GetInstalledVoices().Where(v => v.Enabled))
                _voice.Items.Add(v.VoiceInfo.Name);
        }
        if (_voice.Items.Count > 0) _voice.SelectedIndex = 0;

        using var enumerator = new MMDeviceEnumerator();
        foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            _mic.Items.Add(new MMDeviceSelection(d.FriendlyName, d));
        foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            _output.Items.Add(new MMDeviceSelection(d.FriendlyName, d));

        if (_mic.Items.Count > 0) _mic.SelectedIndex = 0;

        int cableIndex = -1;
        for (int i = 0; i < _output.Items.Count; i++)
        {
            if (_output.Items[i] is MMDeviceSelection item && item.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase))
            {
                cableIndex = i;
                break;
            }
        }

        if (cableIndex >= 0)
        {
            _output.SelectedIndex = cableIndex;
            _status.Text = "VB-CABLE detected. Discord Input Device = CABLE Output.";
        }
        else
        {
            if (_output.Items.Count > 0) _output.SelectedIndex = 0;
            _status.Text = "VB-CABLE not detected. Install VB-CABLE and select CABLE Input.";
        }

        UpdateVoiceControls();
        RestartAudio();
    }

    private void UpdateVoiceControls()
    {
        bool custom = _engine.SelectedIndex == 1;
        _voice.Enabled = !custom;
        _loadPack.Enabled = custom;
        _speaker.Enabled = custom;
        _speed.Enabled = custom;
    }

    private void LoadVoicePack()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select a sherpa-onnx VITS voice-pack folder (model.onnx + tokens.txt, optionally voicepack.json)",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            Cursor = Cursors.WaitCursor;
            _status.Text = "Loading custom voice pack…";
            Application.DoEvents();
            _voicePack.Load(dialog.SelectedPath);
            _speaker.Value = Math.Min(_speaker.Maximum, Math.Max(_speaker.Minimum, _voicePack.SpeakerId));
            _speed.Value = Math.Min(_speed.Maximum, Math.Max(_speed.Minimum, (decimal)(_voicePack.Speed * 100f)));
            _packName.Text = $"{_voicePack.DisplayName}  ({dialog.SelectedPath})";
            _engine.SelectedIndex = 1;
            _status.Text = "Custom voice pack loaded. Ready.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Voice pack load error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _status.Text = "Voice pack load failed.";
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void RestartAudio()
    {
        if (_mic.SelectedItem is not MMDeviceSelection mic || _output.SelectedItem is not MMDeviceSelection output)
            return;
        try
        {
            _audio.Start(mic, output);
            ApplyLevels();
            _route.Text = $"Route: {mic.Name} + TTS  →  {output.Name}";
            _status.Text = output.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase)
                ? "Running. Discord Input Device = CABLE Output."
                : "Running, but selected output is not CABLE Input.";
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }

    private void ApplyLevels() => _audio.SetLevels(_micVol.Value / 100f, _ttsVol.Value / 100f, _passMic.Checked, _duck.Checked, 0.25f);

    private async Task SpeakAsync()
    {
        var text = _text.Text.Trim();
        if (text.Length == 0 || _speaking) return;

        bool custom = _engine.SelectedIndex == 1;
        if (custom && !_voicePack.IsLoaded)
        {
            MessageBox.Show(this, "Load a custom voice pack first.", "No voice pack");
            return;
        }

        _speaking = true;
        _speak.Enabled = false;
        _loadPack.Enabled = false;
        try
        {
            var chunks = SplitForSpeech(text, custom ? 180 : 350);
            string? voiceName = _voice.SelectedItem as string;
            int done = 0;

            if (custom)
            {
                _voicePack.SpeakerId = (int)_speaker.Value;
                _voicePack.Speed = (float)_speed.Value / 100f;
            }

            foreach (var chunk in chunks)
            {
                byte[] pcmBytes = await Task.Run(() => custom
                    ? _voicePack.SynthesizePcm48kMono16(chunk)
                    : SynthesizeWindowsChunk(chunk, voiceName));

                _audio.QueueTtsPcm16(pcmBytes);
                done++;
                _status.Text = $"Speaking… prepared {done}/{chunks.Count}, queued {_audio.PendingTtsDuration.TotalSeconds:F1}s";

                while (_audio.PendingTtsDuration > TimeSpan.FromSeconds(20))
                    await Task.Delay(100);
            }

            _text.Clear();
            while (_audio.HasPendingTts)
            {
                _status.Text = $"Speaking… remaining {_audio.PendingTtsDuration.TotalSeconds:F1}s";
                await Task.Delay(100);
            }

            await Task.Delay(250);
            _status.Text = "Ready. Discord Input Device = CABLE Output.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "TTS error");
            _status.Text = "TTS error.";
        }
        finally
        {
            _speaking = false;
            _speak.Enabled = true;
            _loadPack.Enabled = _engine.SelectedIndex == 1;
        }
    }

    private static byte[] SynthesizeWindowsChunk(string text, string? voiceName)
    {
        using var synth = new SpeechSynthesizer();
        if (!string.IsNullOrWhiteSpace(voiceName)) synth.SelectVoice(voiceName);
        using var ms = new MemoryStream();
        synth.SetOutputToWaveStream(ms);
        synth.Speak(text);
        ms.Position = 0;

        using var reader = new WaveFileReader(ms);
        ISampleProvider sample = reader.ToSampleProvider();
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

    private static List<string> SplitForSpeech(string text, int targetLength)
    {
        var result = new List<string>();
        var current = new StringBuilder();

        void Flush()
        {
            string s = current.ToString().Trim();
            if (s.Length > 0) result.Add(s);
            current.Clear();
        }

        foreach (char ch in text)
        {
            current.Append(ch);
            bool sentenceEnd = ch is '。' or '！' or '？' or '；' or '.' or '!' or '?' or ';' or '\n' or '\r';
            if (current.Length >= targetLength && sentenceEnd)
                Flush();
            else if (current.Length >= targetLength * 2)
                Flush();
        }
        Flush();
        return result;
    }
}
