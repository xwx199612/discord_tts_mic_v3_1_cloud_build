using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Speech.Synthesis;

namespace DiscordTtsMic;

public sealed class MainForm : Form
{
    private readonly ComboBox _voice = new() { Width = 330, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _mic = new() { Width = 420, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _text = new() { Multiline = true, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 14) };
    private readonly TrackBar _micVol = new() { Minimum = 0, Maximum = 200, Value = 100, TickFrequency = 25, Width = 220 };
    private readonly TrackBar _ttsVol = new() { Minimum = 0, Maximum = 200, Value = 100, TickFrequency = 25, Width = 220 };
    private readonly CheckBox _passMic = new() { Text = "Pass physical microphone", Checked = true, AutoSize = true };
    private readonly CheckBox _duck = new() { Text = "Duck microphone while TTS speaks", Checked = true, AutoSize = true };
    private readonly Label _driver = new() { AutoSize = true, Text = "Driver: checking…" };
    private readonly Label _status = new() { AutoSize = true, Text = "Ready" };
    private readonly Button _speak = new() { Text = "Speak (Enter)", AutoSize = true };
    private readonly Button _restartAudio = new() { Text = "Restart audio", AutoSize = true };

    private readonly DriverBridgeClient _bridge = new();
    private readonly AudioMixerEngine _audio;

    public MainForm()
    {
        _audio = new AudioMixerEngine(_bridge);
        Text = "Discord TTS Microphone v3.1 — Driver Bring-up";
        Width = 900; Height = 620; StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(BuildUi());

        Load += (_, _) => InitializeAll();
        FormClosed += (_, _) => { _audio.Dispose(); _bridge.Dispose(); };
        _speak.Click += async (_, _) => await SpeakAsync();
        _restartAudio.Click += (_, _) => RestartAudio();
        _mic.SelectedIndexChanged += (_, _) => RestartAudio();
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
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 8 };
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.Controls.Add(Row("Voice", _voice), 0, 0);
        t.Controls.Add(Row("Physical mic", _mic, _restartAudio), 0, 1);
        t.Controls.Add(Row("Mic gain", _micVol, _passMic), 0, 2);
        t.Controls.Add(Row("TTS gain", _ttsVol, _duck), 0, 3);
        t.Controls.Add(_driver, 0, 4);
        t.Controls.Add(_text, 0, 5);
        t.Controls.Add(Row("", _speak), 0, 6);
        t.Controls.Add(_status, 0, 7);
        return t;
    }

    private static Control Row(string label, params Control[] controls)
    {
        var p = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        if (label.Length > 0) p.Controls.Add(new Label { Text = label, Width = 100, AutoSize = false, Margin = new Padding(3, 8, 3, 3) });
        foreach (var c in controls) p.Controls.Add(c);
        return p;
    }

    private void InitializeAll()
    {
        using (var s = new SpeechSynthesizer())
            foreach (var v in s.GetInstalledVoices().Where(v => v.Enabled)) _voice.Items.Add(v.VoiceInfo.Name);
        if (_voice.Items.Count > 0) _voice.SelectedIndex = 0;

        using var e = new MMDeviceEnumerator();
        foreach (var d in e.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            _mic.Items.Add(new MMDeviceSelection(d.FriendlyName, d));
        if (_mic.Items.Count > 0) _mic.SelectedIndex = 0;

        _driver.Text = _bridge.TryConnect(out var ds) ? "Driver: connected — Discord TTS Virtual Microphone" : "Driver: NOT INSTALLED — app-only test mode";
        _status.Text = ds;
        RestartAudio();
    }

    private void RestartAudio()
    {
        if (_mic.SelectedItem is MMDeviceSelection sel)
        {
            try { _audio.Start(sel); ApplyLevels(); _status.Text = _bridge.IsConnected ? "Audio mixer running." : "Mixer running; install v3 driver to expose it to Discord."; }
            catch (Exception ex) { _status.Text = ex.Message; }
        }
    }

    private void ApplyLevels() => _audio.SetLevels(_micVol.Value / 100f, _ttsVol.Value / 100f, _passMic.Checked, _duck.Checked, 0.25f);

    private async Task SpeakAsync()
    {
        var text = _text.Text.Trim();
        if (text.Length == 0) return;
        try
        {
            using var synth = new SpeechSynthesizer();
            if (_voice.SelectedItem is string vn) synth.SelectVoice(vn);
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
            using var outMs = new MemoryStream();
            var buf = new byte[8192]; int n;
            while ((n = pcm.Read(buf, 0, buf.Length)) > 0) outMs.Write(buf, 0, n);
            _audio.SetTtsActive(true);
            _audio.QueueTtsPcm16(outMs.ToArray());
            var durationMs = outMs.Length / 2.0 / AudioMixerEngine.SampleRate * 1000;
            _status.Text = "TTS queued to virtual microphone mixer.";
            _text.Clear();
            await Task.Delay((int)durationMs + 100);
            _audio.SetTtsActive(false);
        }
        catch (Exception ex) { _audio.SetTtsActive(false); MessageBox.Show(ex.Message, "TTS error"); }
    }
}
