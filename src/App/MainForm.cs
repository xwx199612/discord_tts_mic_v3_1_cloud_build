using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Speech.Synthesis;
using System.Text;

namespace DiscordTtsMic;

public sealed class MainForm : Form
{
    private readonly ComboBox _voice = new() { Width = 330, DropDownStyle = ComboBoxStyle.DropDownList };
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
    private bool _speaking;

    public MainForm()
    {
        Text = "Discord TTS — VB-CABLE Edition (Long Text)";
        Width = 920;
        Height = 650;
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(BuildUi());

        Load += (_, _) => InitializeAll();
        FormClosed += (_, _) => _audio.Dispose();
        _speak.Click += async (_, _) => await SpeakAsync();
        _restartAudio.Click += (_, _) => RestartAudio();
        _mic.SelectedIndexChanged += (_, _) => RestartAudio();
        _output.SelectedIndexChanged += (_, _) => RestartAudio();
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
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 9 };
        for (int i = 0; i < 6; i++) t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        t.Controls.Add(Row("Voice", _voice), 0, 0);
        t.Controls.Add(Row("Physical mic", _mic), 0, 1);
        t.Controls.Add(Row("VB-CABLE out", _output, _restartAudio), 0, 2);
        t.Controls.Add(Row("Mic gain", _micVol, _passMic), 0, 3);
        t.Controls.Add(Row("TTS gain", _ttsVol, _duck), 0, 4);
        t.Controls.Add(_route, 0, 5);
        t.Controls.Add(_text, 0, 6);
        t.Controls.Add(Row("", _speak), 0, 7);
        t.Controls.Add(_status, 0, 8);
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
        RestartAudio();
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

        _speaking = true;
        _speak.Enabled = false;
        try
        {
            var chunks = SplitForSpeech(text, 350);
            string? voiceName = _voice.SelectedItem as string;
            int done = 0;

            foreach (var chunk in chunks)
            {
                byte[] pcmBytes = await Task.Run(() => SynthesizeChunk(chunk, voiceName));
                _audio.QueueTtsPcm16(pcmBytes);
                done++;
                _status.Text = $"Speaking through VB-CABLE… prepared {done}/{chunks.Count}, queued {_audio.PendingTtsDuration.TotalSeconds:F1}s";

                // Keep memory bounded for very large input: do not synthesize minutes of audio ahead.
                while (_audio.PendingTtsDuration > TimeSpan.FromSeconds(20))
                    await Task.Delay(100);
            }

            _text.Clear();
            while (_audio.HasPendingTts)
            {
                _status.Text = $"Speaking through VB-CABLE… remaining {_audio.PendingTtsDuration.TotalSeconds:F1}s";
                await Task.Delay(100);
            }

            // Small tail allowance for the WASAPI/VB-CABLE output buffer.
            await Task.Delay(250);
            _status.Text = "Ready. Discord Input Device = CABLE Output.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "TTS error");
            _status.Text = "TTS error.";
        }
        finally
        {
            _speaking = false;
            _speak.Enabled = true;
        }
    }

    private static byte[] SynthesizeChunk(string text, string? voiceName)
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
