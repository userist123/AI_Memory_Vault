using System.Diagnostics;
using System.Net.Http;
using System.IO;
using System.Media;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.Windows.Media.Animation;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Jarvis.Desktop;

public partial class MainWindow : Window
{
    private readonly HttpClient _client = new()
    {
        BaseAddress = new Uri("http://127.0.0.1:3000"),
        Timeout = TimeSpan.FromSeconds(8)
    };
    private readonly HttpClient _forgeClient = new()
    {
        BaseAddress = new Uri("http://127.0.0.1:3000"),
        Timeout = TimeSpan.FromSeconds(180)
    };
    private readonly HttpClient _voiceClient = new()
    {
        BaseAddress = new Uri("http://127.0.0.1:8002"),
        Timeout = TimeSpan.FromSeconds(120)
    };
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _telemetryTimer = new() { Interval = TimeSpan.FromSeconds(8) };
    private readonly List<ChatTurn> _conversationHistory = [];
    private Process? _backendProcess;
    private Process? _voiceProcess;
    private SpeechRecognitionEngine? _recognizer;
    private SpeechSynthesizer? _speechSynthesizer;
    private Storyboard? _reactorStoryboard;
    private string _repositoryRoot = string.Empty;
    private bool _backendStartedByHost;
    private bool _voiceStartedByHost;
    private bool _neuralVoiceAvailable;
    private bool _busy;
    private bool _connectInProgress;
    private bool _telemetryBusy;
    private bool _councilBusy;
    private bool _forgeBusy;
    private bool _planBusy;
    private bool _skillPlaceholder = true;
    private string _lastBlueprint = string.Empty;
    private ForgePlanPayload? _lastPlan;
    private string _lastPlanSpecification = string.Empty;
    private string _lastForgeMode = "blueprint";
    private string _lastExportDirectory = string.Empty;
    private string _lastLiveState = string.Empty;
    private bool _closing;
    private CancellationTokenSource? _liveStreamCancellation;
    private Task? _liveStreamTask;
    private bool _voiceMode;
    private bool _awaitingVoiceCommand;
    private bool _messagePlaceholder = true;
    private bool _searchPlaceholder = true;
    private readonly object _playbackLock = new();
    private SoundPlayer? _activeSoundPlayer;
    private CancellationTokenSource? _speechCancellation;

    private string SessionHistoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JarvisDesktop",
        "conversation-history.json"
    );
    public MainWindow()
    {
        InitializeComponent();
        _clockTimer.Tick += (_, _) => UpdateClock();
        _telemetryTimer.Tick += TelemetryTimer_Tick;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateClock();
        _clockTimer.Start();
        _telemetryTimer.Start();
        StartReactorAnimation();
        AddActivity("BOOT", "Native C# shell initialized", "WPF / local session");
        LoadConversationHistory();
        RenderConversationHistory();
        await ConnectCoreAsync();
    }

    private void UpdateClock()
    {
        ClockLabel.Text = DateTime.Now.ToString("HH:mm:ss");
        DateLabel.Text = DateTime.Now.ToString("dddd, dd MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("ro-RO")).ToUpperInvariant();
    }

    private async Task ConnectCoreAsync()
    {
        if (_connectInProgress)
        {
            return;
        }

        _connectInProgress = true;
        try
        {
            SetConnection("CONNECTING", Brushes.Orange);
            _repositoryRoot = FindRepositoryRoot();

            if (!await IsCoreOnlineAsync())
            {
                StartHiddenCore();
                RuntimeState.Text = "STARTING CORE";
                RuntimeDetail.Text = "native host supervising engine";
                for (var attempt = 0; attempt < 30; attempt++)
                {
                    await Task.Delay(500);
                    if (await IsCoreOnlineAsync())
                    {
                        break;
                    }
                }
            }
            else
            {
                RuntimeDetail.Text = "existing unified gateway";
                AddActivity("LINK", "Connected to existing Jarvis core", "port 3000");
            }

            if (await IsCoreOnlineAsync())
            {
                SetConnection("ONLINE", Brushes.LightGreen);
                CoreBadge.Text = "ONLINE";
                CoreBadge.Foreground = Brushes.LightGreen;
                RuntimeState.Text = "READY";
                RuntimeDetail.Text = "OODA + Memory + Agents";
                await RefreshTelemetryAsync();
                await StartNeuralVoiceAsync();
                StartVoiceMode();
                StartLiveStream();
            }
            else
            {
                SetConnection("OFFLINE", Brushes.OrangeRed);
                CoreBadge.Text = "OFFLINE";
                CoreBadge.Foreground = Brushes.OrangeRed;
                RuntimeState.Text = "CORE OFFLINE";
                RuntimeDetail.Text = "Python runtime unavailable";
                AddActivity("ERROR", "Could not reach the cognitive core", "check Python installation");
            }
        }
        finally
        {
            _connectInProgress = false;
        }
    }

    private void StartReactorAnimation()
    {
        OuterRing.RenderTransform = new RotateTransform();
        InnerRing.RenderTransform = new RotateTransform();
        CorePulse.RenderTransform = new ScaleTransform(1, 1);

        var outerRotation = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(26)))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        var innerRotation = new DoubleAnimation(360, 0, new Duration(TimeSpan.FromSeconds(18)))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        var pulse = new DoubleAnimation(1, 1.08, new Duration(TimeSpan.FromSeconds(1.8)))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };

        Storyboard.SetTarget(outerRotation, OuterRing);
        Storyboard.SetTargetProperty(outerRotation, new PropertyPath("(UIElement.RenderTransform).(RotateTransform.Angle)"));
        Storyboard.SetTarget(innerRotation, InnerRing);
        Storyboard.SetTargetProperty(innerRotation, new PropertyPath("(UIElement.RenderTransform).(RotateTransform.Angle)"));
        Storyboard.SetTarget(pulse, CorePulse);
        Storyboard.SetTargetProperty(pulse, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));

        _reactorStoryboard = new Storyboard();
        _reactorStoryboard.Children.Add(outerRotation);
        _reactorStoryboard.Children.Add(innerRotation);
        _reactorStoryboard.Children.Add(pulse);
        _reactorStoryboard.Begin(this, true);
    }

    private void TelemetryTimer_Tick(object? sender, EventArgs e)
    {
        if (_telemetryBusy)
        {
            return;
        }

        _ = RefreshLiveStateAsync();
    }

    private async Task RefreshLiveStateAsync()
    {
        _telemetryBusy = true;
        try
        {
            var online = await IsCoreOnlineAsync();
            if (!online)
            {
                if (ConnectionLabel.Text != "OFFLINE")
                {
                    SetConnection("OFFLINE", Brushes.OrangeRed);
                    CoreBadge.Text = "OFFLINE";
                    CoreBadge.Foreground = Brushes.OrangeRed;
                    RuntimeState.Text = "CORE OFFLINE";
                    RuntimeDetail.Text = "waiting for reconnect";
                    AddActivity("WARN", "Cognitive core is unreachable", "use RECONNECT CORE");
                }

                if (_backendStartedByHost && (_backendProcess is null || _backendProcess.HasExited))
                {
                    await ConnectCoreAsync();
                }

                return;
            }

            if (ConnectionLabel.Text != "ONLINE")
            {
                SetConnection("ONLINE", Brushes.LightGreen);
                CoreBadge.Text = "ONLINE";
                CoreBadge.Foreground = Brushes.LightGreen;
                RuntimeState.Text = "READY";
                RuntimeDetail.Text = "OODA + Memory + Agents";
                AddActivity("LINK", "Cognitive core recovered", "local gateway online");
            }

            await RefreshTelemetryAsync(false);
        }
        finally
        {
            _telemetryBusy = false;
        }
    }

    private void Voice_Click(object sender, RoutedEventArgs e)
    {
        if (_voiceMode)
        {
            StopVoiceMode();
            return;
        }

        StartVoiceMode();
    }

    private void StartVoiceMode()
    {
        if (_voiceMode)
        {
            return;
        }

        try
        {
            _recognizer ??= CreateSpeechRecognizer();
            _recognizer.SpeechRecognized -= Recognizer_SpeechRecognized;
            _recognizer.SpeechRecognized += Recognizer_SpeechRecognized;
            _recognizer.SpeechRecognitionRejected -= Recognizer_SpeechRecognitionRejected;
            _recognizer.SpeechRecognitionRejected += Recognizer_SpeechRecognitionRejected;
            _recognizer.SetInputToDefaultAudioDevice();
            _recognizer.RecognizeAsync(RecognizeMode.Multiple);
            _voiceMode = true;
            _awaitingVoiceCommand = false;
            VoiceButton.Content = "MIC ON";
            VoiceStatusLabel.Text = "SAY JARVIS";
            VoiceStatusLabel.Foreground = Brushes.LightGreen;
            VoiceIndicator.Fill = Brushes.LightGreen;
            AddActivity("VOICE", "Voice mode activated", "say Jarvis followed by a command");
        }
        catch (Exception ex)
        {
            StopVoiceMode();
            VoiceStatusLabel.Text = "VOICE ERROR";
            VoiceStatusLabel.Foreground = Brushes.OrangeRed;
            VoiceIndicator.Fill = Brushes.OrangeRed;
            AddActivity("ERROR", "Voice mode unavailable", ex.Message);
        }
    }

    private SpeechRecognitionEngine CreateSpeechRecognizer()
    {
        var recognizerInfo = SpeechRecognitionEngine.InstalledRecognizers()
            .OrderByDescending(info => info.Culture.Name.StartsWith("ro", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(info => info.Culture.Name.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        if (recognizerInfo is null)
        {
            throw new InvalidOperationException("Windows Speech Recognition has no installed language pack.");
        }

        var recognizer = new SpeechRecognitionEngine(recognizerInfo);
        recognizer.LoadGrammar(new DictationGrammar());
        return recognizer;
    }

    private SpeechSynthesizer CreateSpeechSynthesizer()
    {
        var synthesizer = new SpeechSynthesizer();
        var voice = synthesizer.GetInstalledVoices()
            .Select(installed => installed.VoiceInfo)
            .OrderByDescending(info => info.Culture.Name.StartsWith("ro", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(info => info.Culture.Name.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        if (voice is not null)
        {
            synthesizer.SelectVoice(voice.Name);
        }

        synthesizer.Rate = 0;
        synthesizer.Volume = 100;
        return synthesizer;
    }

    private void Recognizer_SpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
    {
        if (!_voiceMode || e.Result.Confidence < 0.42)
        {
            return;
        }

        var phrase = e.Result.Text.Trim();
        if (phrase.Length == 0)
        {
            return;
        }

        Dispatcher.Invoke(() => HandleVoicePhrase(phrase));
    }

    private void Recognizer_SpeechRecognitionRejected(object? sender, SpeechRecognitionRejectedEventArgs e)
    {
        if (_voiceMode)
        {
            Dispatcher.BeginInvoke(() => VoiceStatusLabel.Text = _awaitingVoiceCommand ? "LISTENING" : "SAY JARVIS");
        }
    }

    private void HandleVoicePhrase(string phrase)
    {
        if (!_voiceMode)
        {
            return;
        }

        CancelSpeechOutput();

        if (TryExtractVoiceCommand(phrase, out var command))
        {
            _awaitingVoiceCommand = false;
            if (command.Length == 0)
            {
                VoiceStatusLabel.Text = "LISTENING";
                SpeakText("Da, te ascult.");
                return;
            }

            VoiceStatusLabel.Text = "THINKING";
            _ = SendMessageAsync(command, true);
            return;
        }

        if (_awaitingVoiceCommand)
        {
            _awaitingVoiceCommand = false;
            VoiceStatusLabel.Text = "THINKING";
            _ = SendMessageAsync(phrase, true);
        }
    }

    private static bool TryExtractVoiceCommand(string phrase, out string command)
    {
        var prefixes = new[] { "hey jarvis", "hei jarvis", "salut jarvis", "jarvis" };
        foreach (var prefix in prefixes)
        {
            if (phrase.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                command = phrase[prefix.Length..].Trim(' ', ',', ':', ';', '.');
                return true;
            }
        }

        command = string.Empty;
        return false;
    }

    private void SpeakText(string text)
    {
        if (!_voiceMode || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var speech = PrepareSpeechText(text);
        if (speech.Length == 0)
        {
            return;
        }

        CancelSpeechOutput();
        VoiceStatusLabel.Text = "SPEAKING";
        if (_neuralVoiceAvailable)
        {
            _speechCancellation = new CancellationTokenSource();
            _ = SpeakNeuralAsync(speech, _speechCancellation.Token);
            return;
        }

        SpeakWithSapi(speech);
    }

    private static string PrepareSpeechText(string text)
    {
        var speech = System.Text.RegularExpressions.Regex.Replace(text, "```[\\s\\S]*?```", " ");
        speech = System.Text.RegularExpressions.Regex.Replace(speech, "`([^`]*)`", "$1");
        speech = System.Text.RegularExpressions.Regex.Replace(speech, "https?://\\S+", "");
        speech = System.Text.RegularExpressions.Regex.Replace(speech, "^\\s{0,3}#{1,6}\\s*", string.Empty, System.Text.RegularExpressions.RegexOptions.Multiline);
        speech = System.Text.RegularExpressions.Regex.Replace(speech, "^\\s*[-*+]\\s+", string.Empty, System.Text.RegularExpressions.RegexOptions.Multiline);
        speech = System.Text.RegularExpressions.Regex.Replace(speech, "[*_~]+", string.Empty);
        speech = System.Text.RegularExpressions.Regex.Replace(speech, "\\s+", " ").Trim();
        return speech.Length > 1800 ? speech[..1800] + " Restul detaliilor rămâne afișat în interfață." : speech;
    }

    private async Task SpeakNeuralAsync(string text, CancellationToken cancellationToken)
    {
        SoundPlayer? player = null;
        try
        {
            using var response = await _voiceClient.PostAsJsonAsync("/tts", new { text }, cancellationToken);
            response.EnsureSuccessStatusCode();
            var audioBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new MemoryStream(audioBytes);
            player = new SoundPlayer(stream);
            lock (_playbackLock)
            {
                _activeSoundPlayer = player;
            }
            player.Load();
            cancellationToken.ThrowIfCancellationRequested();
            player.PlaySync();
            Dispatcher.BeginInvoke(() =>
            {
                if (_voiceMode && !cancellationToken.IsCancellationRequested)
                {
                    VoiceStatusLabel.Text = "SAY JARVIS";
                    VoiceStatusLabel.Foreground = Brushes.LightGreen;
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _neuralVoiceAvailable = false;
            Dispatcher.BeginInvoke(() =>
            {
                VoiceEngineLabel.Text = "SAPI FALLBACK";
                VoiceEngineLabel.Foreground = Brushes.Orange;
                AddActivity("WARN", "Neural voice failed; using local fallback", ex.Message);
                SpeakWithSapi(text);
            });
        }
        finally
        {
            lock (_playbackLock)
            {
                if (ReferenceEquals(_activeSoundPlayer, player))
                {
                    _activeSoundPlayer = null;
                }
            }
            player?.Dispose();
        }
    }

    private void SpeakWithSapi(string text)
    {
        try
        {
            _speechSynthesizer ??= CreateSpeechSynthesizer();
            _speechSynthesizer.SpeakAsyncCancelAll();
            _speechSynthesizer.SpeakAsync(text);
            VoiceEngineLabel.Text = "SAPI FALLBACK";
            VoiceEngineLabel.Foreground = Brushes.Orange;
            VoiceStatusLabel.Text = "SPEAKING";
        }
        catch (Exception ex)
        {
            AddActivity("WARN", "Voice output failed", ex.Message);
        }
    }

    private void CancelSpeechOutput()
    {
        _speechCancellation?.Cancel();
        _speechCancellation = null;
        lock (_playbackLock)
        {
            try
            {
                _activeSoundPlayer?.Stop();
            }
            catch
            {
            }
            _activeSoundPlayer = null;
        }
        _speechSynthesizer?.SpeakAsyncCancelAll();
    }

    private void SpeechSynthesizer_SpeakCompleted(object? sender, SpeakCompletedEventArgs e)
    {
        if (_voiceMode)
        {
            Dispatcher.BeginInvoke(() =>
            {
                VoiceStatusLabel.Text = "SAY JARVIS";
                VoiceStatusLabel.Foreground = Brushes.LightGreen;
            });
        }
    }
    private void StopVoiceMode()
    {
        _voiceMode = false;
        _awaitingVoiceCommand = false;

        try
        {
            _recognizer?.RecognizeAsyncCancel();
        }
        catch
        {
        }

        _recognizer?.Dispose();
        _recognizer = null;
        CancelSpeechOutput();
        VoiceButton.Content = "MIC OFF";
        VoiceStatusLabel.Text = "VOICE OFF";
        VoiceStatusLabel.Foreground = Brushes.Gray;
        VoiceIndicator.Fill = Brushes.Gray;
        VoiceEngineLabel.Text = _neuralVoiceAvailable ? "PIPER NEURAL / RO" : "SAPI FALLBACK";
        AddActivity("VOICE", "Voice mode deactivated", "text channel remains available");
        _connectInProgress = false;
    }
    private void StartLiveStream()
    {
        if (_closing || (_liveStreamTask is not null && !_liveStreamTask.IsCompleted))
        {
            return;
        }

        _liveStreamCancellation?.Cancel();
        _liveStreamCancellation = new CancellationTokenSource();
        _liveStreamTask = Task.Run(() => ReceiveLiveStreamAsync(_liveStreamCancellation.Token));
    }

    private async Task ReceiveLiveStreamAsync(CancellationToken cancellationToken)
    {
        while (!_closing && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(new Uri("ws://127.0.0.1:3000/ws"), cancellationToken);
                await Dispatcher.BeginInvoke(() =>
                {
                    StreamStatusLabel.Text = "WS ONLINE";
                    StreamStatusLabel.Foreground = Brushes.LightGreen;
                    AddActivity("LIVE", "Executive event stream connected", "WebSocket / OODA");
                });

                var buffer = new byte[8192];
                var message = new StringBuilder();
                while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    var result = await socket.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    if (result.MessageType != WebSocketMessageType.Text)
                    {
                        continue;
                    }

                    message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    if (result.EndOfMessage)
                    {
                        ProcessLiveEvent(message.ToString());
                        message.Clear();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                await Dispatcher.BeginInvoke(() =>
                {
                    StreamStatusLabel.Text = "WS RETRY";
                    StreamStatusLabel.Foreground = Brushes.Orange;
                    AddActivity("LIVE", "Event stream reconnect scheduled", ex.Message);
                });
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        await Dispatcher.BeginInvoke(() =>
        {
            StreamStatusLabel.Text = "WS CLOSED";
            StreamStatusLabel.Foreground = Brushes.Gray;
        });
    }

    private void ProcessLiveEvent(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("state", out var state))
            {
                return;
            }

            var stateText = state.ValueKind == JsonValueKind.String ? state.GetString() : state.GetRawText();
            if (string.IsNullOrWhiteSpace(stateText) || stateText == _lastLiveState)
            {
                return;
            }

            _lastLiveState = stateText;
            Dispatcher.BeginInvoke(() =>
            {
                RuntimeState.Text = stateText.ToUpperInvariant();
                RuntimeDetail.Text = "live executive event";
                AddActivity("LIVE", "OODA state transition", stateText);
            });
        }
        catch
        {
        }
    }
    private async Task<bool> IsCoreOnlineAsync()
    {
        try
        {
            using var response = await _client.GetAsync("/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> IsNeuralVoiceOnlineAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var payload = await _voiceClient.GetFromJsonAsync<VoiceHealthPayload>("/health", timeout.Token);
            return payload?.Status.Equals("online", StringComparison.OrdinalIgnoreCase) == true && payload.ModelReady;
        }
        catch
        {
            return false;
        }
    }

    private async Task StartNeuralVoiceAsync()
    {
        VoiceEngineLabel.Text = "NEURAL LINKING";
        if (!await IsNeuralVoiceOnlineAsync())
        {
            StartHiddenVoice();
            for (var attempt = 0; attempt < 30; attempt++)
            {
                await Task.Delay(500);
                if (await IsNeuralVoiceOnlineAsync())
                {
                    break;
                }
            }
        }

        _neuralVoiceAvailable = await IsNeuralVoiceOnlineAsync();
        VoiceEngineLabel.Text = _neuralVoiceAvailable ? "PIPER NEURAL / RO" : "SAPI FALLBACK";
        VoiceEngineLabel.Foreground = _neuralVoiceAvailable ? Brushes.LightGreen : Brushes.Orange;
        AddActivity(_neuralVoiceAvailable ? "VOICE" : "WARN", _neuralVoiceAvailable ? "Romanian neural voice online" : "Neural voice unavailable", _neuralVoiceAvailable ? "Piper / ro_RO-mihai-medium" : "Windows SAPI desktop voice");
    }

    private void StartHiddenVoice()
    {
        if (string.IsNullOrWhiteSpace(_repositoryRoot))
        {
            return;
        }

        var voiceRoot = Path.Combine(_repositoryRoot, "projects", "jarvis_web");
        var voiceScript = Path.Combine(voiceRoot, "voice_server.py");
        if (!File.Exists(voiceScript))
        {
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("JARVIS_PYTHON") ?? "python",
            Arguments = "voice_server.py",
            WorkingDirectory = voiceRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        startInfo.Environment["PYTHONPATH"] = _repositoryRoot + ";" + voiceRoot;
        startInfo.Environment["JARVIS_TTS_PORT"] = "8002";
        startInfo.Environment["JARVIS_TTS_MODEL"] = "ro_RO-mihai-medium";
        startInfo.Environment["PIPER_DATA_DIR"] = Path.Combine(voiceRoot, "voice_models");

        try
        {
            _voiceProcess = Process.Start(startInfo);
            _voiceStartedByHost = _voiceProcess is not null;
            AddActivity("BOOT", "Neural voice engine launched silently", "Piper Romanian TTS");
        }
        catch (Exception ex)
        {
            AddActivity("WARN", "Neural voice launch failed", ex.Message);
        }
    }

    private void StartHiddenCore()
    {
        if (string.IsNullOrWhiteSpace(_repositoryRoot))
        {
            return;
        }

        var cognitiveRoot = Path.Combine(_repositoryRoot, "projects", "jarvis_cognitive_brain");
        if (!Directory.Exists(cognitiveRoot))
        {
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("JARVIS_PYTHON") ?? "python",
            Arguments = "unified_server.py --host 127.0.0.1 --port 3000",
            WorkingDirectory = cognitiveRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.Environment["PYTHONPATH"] = _repositoryRoot + ";" + cognitiveRoot;
        startInfo.Environment["JARVIS_UNIFIED_PORT"] = "3000";
        startInfo.Environment["JARVIS_BACKEND_AUDIO"] = "0";
        startInfo.Environment["JARVIS_OLLAMA_MODEL"] = "qwen2.5-coder:7b";

        try
        {
            _backendProcess = Process.Start(startInfo);
            _backendStartedByHost = _backendProcess is not null;
            AddActivity("BOOT", "Cognitive engine launched silently", "C# supervisor");
        }
        catch (Exception ex)
        {
            AddActivity("ERROR", "Engine launch failed", ex.Message);
        }
    }

    private string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "projects", "jarvis_cognitive_brain")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }

        return Environment.GetEnvironmentVariable("JARVIS_VAULT_ROOT") ?? string.Empty;
    }

    private async Task RefreshTelemetryAsync(bool recordActivity = true)
    {
        try
        {
            var metrics = await _client.GetFromJsonAsync<MetricsPayload>("/api/v1/metrics") ?? new MetricsPayload();
            MemoryMetric.Text = metrics.MemoryItems.ToString("N0");
            AgentsMetric.Text = metrics.AgentsOnline + "/" + metrics.AgentsTotal;
            SkillsMetric.Text = metrics.SkillsOperational.ToString("N0");
            ProposalsMetric.Text = metrics.ProposalsPending.ToString();
            CoreProgress.Value = Math.Clamp(35 + metrics.AgentsOnline * 2, 35, 100);
            await LoadAgentsAsync();
            await LoadProposalsAsync();
            await LoadSkillsAsync();
            LastSyncLabel.Text = "LAST SYNC " + DateTime.Now.ToString("HH:mm:ss");
            if (recordActivity)
            {
                AddActivity("SYNC", "Telemetry refreshed", metrics.Engine);
            }
        }
        catch (Exception ex)
        {
            AddActivity("ERROR", "Telemetry refresh failed", ex.Message);
        }
    }

    private async Task LoadAgentsAsync()
    {
        var payload = await _client.GetFromJsonAsync<AgentsPayload>("/api/v1/agents");
        AgentList.Children.Clear();
        foreach (var agent in (payload?.Agents ?? []).Take(6))
        {
            var row = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(13, 22, 32)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(35, 54, 68)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(9, 8, 9, 8),
                Margin = new Thickness(0, 0, 0, 7)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = agent.Name ?? agent.Id ?? "Agent", FontSize = 11, Foreground = Brushes.White });
            stack.Children.Add(new TextBlock { Text = agent.Domain ?? "Cognitive specialist", FontSize = 9, Foreground = new SolidColorBrush(Color.FromRgb(120, 144, 160)), Margin = new Thickness(0, 3, 0, 0) });
            grid.Children.Add(stack);
            var status = new TextBlock { Text = agent.Status ?? "ONLINE", FontSize = 9, Foreground = Brushes.LightGreen, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(status, 1);
            grid.Children.Add(status);
            row.Child = grid;
            AgentList.Children.Add(row);
        }
    }

    private async void QuickStatus_Click(object sender, RoutedEventArgs e)
    {
        AddActivity("COMMAND", "System status requested", "refreshing live telemetry");
        await RefreshTelemetryAsync();
    }

    private async void SyncMemory_Click(object sender, RoutedEventArgs e)
    {
        AddActivity("MEMORY", "Canonical memory sync requested", "refreshing SQLite/WAL index");
        await RefreshTelemetryAsync();
        await SearchMemoryAsync();
    }

    private void RunCouncil_Click(object sender, RoutedEventArgs e)
    {
        _ = RunCouncilReviewAsync();
    }

    private void VoiceBriefing_Click(object sender, RoutedEventArgs e)
    {
        if (!_voiceMode)
        {
            StartVoiceMode();
        }

        var briefing = $"Jarvis este online cu {AgentsMetric.Text} agenți activi, {MemoryMetric.Text} noduri de memorie și {ProposalsMetric.Text} propuneri în review.";
        AddChatMessage("JARVIS", briefing, false);
        AddActivity("VOICE", "Operational briefing generated", "telemetry summary");
        SpeakText(briefing);
    }

    private void RefreshProposals_Click(object sender, RoutedEventArgs e)
    {
        _ = LoadProposalsAsync();
    }

    private async Task RunCouncilReviewAsync()
    {
        if (_councilBusy)
        {
            return;
        }

        var query = _messagePlaceholder ? "Review the current Jarvis system state and identify risks, evidence and next actions." : MessageBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            query = "Review the current Jarvis system state and identify risks, evidence and next actions.";
        }

        _councilBusy = true;
        CouncilButton.IsEnabled = false;
        CouncilButton.Content = "COUNCIL RUNNING";
        CouncilVerdictLabel.Text = "COUNCIL RUNNING";
        CouncilVerdictLabel.Foreground = Brushes.Orange;
        CouncilResults.Children.Clear();
        CouncilResults.Children.Add(new TextBlock { Text = "Retrieval, Verifier and Critic are working in parallel...", FontSize = 11, Foreground = Brushes.LightGray });
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var response = await _client.PostAsJsonAsync("/api/v1/council/review", new { query, draft = query });
            var payload = await response.Content.ReadFromJsonAsync<CouncilPayload>();
            if (!response.IsSuccessStatusCode || payload is null)
            {
                throw new InvalidOperationException("Council review request failed.");
            }

            stopwatch.Stop();
            CouncilVerdictLabel.Text = "COUNCIL COMPLETE";
            CouncilVerdictLabel.Foreground = Brushes.LightGreen;
            CouncilLatencyLabel.Text = stopwatch.ElapsedMilliseconds.ToString("N0") + " ms";
            CouncilResults.Children.Clear();
            AddCouncilResult("RETRIEVAL", payload.Retrieval);
            AddCouncilResult("VERIFIER", payload.Verification);
            AddCouncilResult("CRITIC", payload.Critique);
            AddActivity("COUNCIL", "Parallel specialist review completed", stopwatch.ElapsedMilliseconds.ToString("N0") + " ms");
        }
        catch (Exception ex)
        {
            CouncilVerdictLabel.Text = "COUNCIL ERROR";
            CouncilVerdictLabel.Foreground = Brushes.OrangeRed;
            CouncilResults.Children.Clear();
            CouncilResults.Children.Add(new TextBlock { Text = ex.Message, TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Brushes.OrangeRed });
            AddActivity("ERROR", "Council review failed", ex.Message);
        }
        finally
        {
            CouncilButton.IsEnabled = true;
            CouncilButton.Content = "RUN COUNCIL";
            _councilBusy = false;
        }
    }

    private void AddCouncilResult(string role, JsonElement result)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(13, 24, 34)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(38, 67, 82)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 8)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = role, FontSize = 9, Foreground = Brushes.Cyan });
        stack.Children.Add(new TextBlock { Text = FormatJson(result), TextWrapping = TextWrapping.Wrap, MaxHeight = 130, FontSize = 10, Foreground = Brushes.LightGray, Margin = new Thickness(0, 5, 0, 0) });
        card.Child = stack;
        CouncilResults.Children.Add(card);
    }

    private static string FormatJson(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return "No result returned.";
        }

        try
        {
            return JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return value.ToString();
        }
    }

    private async Task LoadProposalsAsync()
    {
        try
        {
            var payload = await _client.GetFromJsonAsync<ProposalsPayload>("/api/v1/proposals");
            ProposalList.Children.Clear();
            var proposals = payload?.Pending ?? [];
            if (proposals.Count == 0)
            {
                ProposalList.Children.Add(new TextBlock { Text = "No proposals awaiting review.", TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Brushes.Gray });
                return;
            }

            foreach (var proposal in proposals.Take(5))
            {
                var card = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(16, 23, 34)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(75, 61, 36)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                var stack = new StackPanel();
                stack.Children.Add(new TextBlock { Text = proposal.Type ?? "KNOWLEDGE", FontSize = 9, Foreground = Brushes.Orange });
                stack.Children.Add(new TextBlock { Text = proposal.Content ?? "Untitled proposal", TextWrapping = TextWrapping.Wrap, MaxHeight = 70, FontSize = 10, Margin = new Thickness(0, 5, 0, 8) });
                var actions = new StackPanel { Orientation = Orientation.Horizontal };
                var approve = new Button { Content = "APPROVE", Tag = proposal.CandidateId, Style = (Style)FindResource("ActionButton"), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 6, 0) };
                approve.Click += ProposalApprove_Click;
                var reject = new Button { Content = "REJECT", Tag = proposal.CandidateId, Style = (Style)FindResource("ActionButton"), Padding = new Thickness(8, 5, 8, 5) };
                reject.Click += ProposalReject_Click;
                actions.Children.Add(approve);
                actions.Children.Add(reject);
                stack.Children.Add(actions);
                card.Child = stack;
                ProposalList.Children.Add(card);
            }
        }
        catch (Exception ex)
        {
            ProposalList.Children.Clear();
            ProposalList.Children.Add(new TextBlock { Text = "Proposal queue unavailable: " + ex.Message, TextWrapping = TextWrapping.Wrap, FontSize = 10, Foreground = Brushes.OrangeRed });
        }
    }

    private void ProposalApprove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is string candidateId)
        {
            _ = DecideProposalAsync(candidateId, "APPROVED");
        }
    }

    private void ProposalReject_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is string candidateId)
        {
            _ = DecideProposalAsync(candidateId, "REJECTED");
        }
    }

    private async Task DecideProposalAsync(string candidateId, string decision)
    {
        try
        {
            using var response = await _client.PostAsJsonAsync("/api/v1/proposals/" + Uri.EscapeDataString(candidateId) + "/decision", new { decision });
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("Proposal decision failed.");
            }

            AddActivity("MEMORY", "Proposal " + decision.ToLowerInvariant(), candidateId);
            await RefreshTelemetryAsync(false);
        }
        catch (Exception ex)
        {
            AddActivity("ERROR", "Proposal decision failed", ex.Message);
        }
    }
    private void PlanMission_Click(object sender, RoutedEventArgs e)
    {
        _ = RunForgePlanAsync();
    }

    private async Task RunForgePlanAsync()
    {
        if (_planBusy || _forgeBusy)
        {
            return;
        }

        var specification = ForgeSpecBox.Text.Trim();
        var language = (ForgeLanguageBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "csharp";
        if (specification.Length < 20)
        {
            ForgeMissionLabel.Text = "SPEC TOO SHORT";
            return;
        }

        _planBusy = true;
        PlanMissionButton.IsEnabled = false;
        BuildBlueprintButton.IsEnabled = false;
        ImplementProgramButton.IsEnabled = false;
        ForgeMissionLabel.Text = "COUNCIL DECOMPOSING";
        ForgeArchitectureLabel.Text = "JARVIS is mapping bounded contexts, ownership, dependencies, risks and acceptance gates...";
        ForgePhaseList.Children.Clear();
        ForgePhaseList.Children.Add(new TextBlock { Text = "Analyzing mission graph...", FontSize = 10, Foreground = Brushes.Orange });
        AddActivity("FORGE", "Mission decomposition started", language);

        try
        {
            using var response = await _forgeClient.PostAsJsonAsync("/api/v1/forge/decompose", new { spec = specification, language });
            var payload = await response.Content.ReadFromJsonAsync<ForgePlanPayload>();
            if (!response.IsSuccessStatusCode || payload?.Plan is null)
            {
                throw new InvalidOperationException("Mission decomposition request failed.");
            }

            _lastPlan = payload;
            _lastPlanSpecification = specification;
            RenderForgePlan(payload.Plan);
            ForgeOutput.Text = JsonSerializer.Serialize(payload.Plan, new JsonSerializerOptions { WriteIndented = true });
            ExportBlueprintButton.IsEnabled = true;
            ForgeStatusLabel.Text = "MISSION MAPPED";
            ForgeStatusLabel.Foreground = Brushes.LightGreen;
            AddActivity("FORGE", "Mission graph decomposed", payload.DurationMs.ToString("N0") + " ms");
            if (_voiceMode)
            {
                SpeakText("Misiunea a fost descompusă în faze executabile și porți de calitate.");
            }
        }
        catch (Exception ex)
        {
            ForgeMissionLabel.Text = "DECOMPOSITION ERROR";
            ForgeArchitectureLabel.Text = ex.Message;
            ForgePhaseList.Children.Clear();
            ForgePhaseList.Children.Add(new TextBlock { Text = "The mission graph could not be loaded.", FontSize = 10, Foreground = Brushes.OrangeRed });
            AddActivity("ERROR", "Mission decomposition failed", ex.Message);
        }
        finally
        {
            PlanMissionButton.IsEnabled = true;
            BuildBlueprintButton.IsEnabled = true;
            ImplementProgramButton.IsEnabled = true;
            _planBusy = false;
        }
    }

    private void RenderForgePlan(ForgePlan plan)
    {
        ForgeMissionLabel.Text = (plan.Phases?.Count ?? 0).ToString("N0") + " PHASES / " + (plan.Risks?.Count ?? 0).ToString("N0") + " RISKS";
        var architecture = plan.Architecture is { Count: > 0 }
            ? string.Join("  •  ", plan.Architecture.Take(3))
            : plan.NextSlice ?? "Architecture graph ready.";
        ForgeArchitectureLabel.Text = architecture;
        ForgePhaseList.Children.Clear();

        foreach (var phase in plan.Phases ?? [])
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = phase.Id ?? "P?", FontSize = 9, Foreground = Brushes.Cyan, VerticalAlignment = VerticalAlignment.Top });
            var details = new StackPanel();
            details.Children.Add(new TextBlock { Text = phase.Name ?? "Unnamed phase", FontSize = 10, Foreground = Brushes.White });
            details.Children.Add(new TextBlock { Text = phase.Owner ?? "Council", FontSize = 9, Foreground = Brushes.Gray, Margin = new Thickness(0, 2, 0, 0) });
            var deliverables = phase.Deliverables is { Count: > 0 } ? "Deliver: " + string.Join(", ", phase.Deliverables.Take(3)) : "Deliverables pending";
            details.Children.Add(new TextBlock { Text = deliverables, FontSize = 9, Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(details, 1);
            row.Children.Add(details);
            var phaseStatus = phase.Status ?? "QUEUED";
            var statusColor = phaseStatus.Equals("READY", StringComparison.OrdinalIgnoreCase) || phaseStatus.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase)
                ? Brushes.LightGreen
                : phaseStatus.Equals("IN PROGRESS", StringComparison.OrdinalIgnoreCase)
                    ? Brushes.Cyan
                    : Brushes.Orange;
            var status = new TextBlock { Text = phaseStatus, FontSize = 8, Foreground = statusColor, VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumn(status, 2);
            row.Children.Add(status);
            ForgePhaseList.Children.Add(row);
        }

        if (!string.IsNullOrWhiteSpace(plan.NextSlice))
        {
            ForgePhaseList.Children.Add(new TextBlock { Text = "NEXT SLICE  //  " + plan.NextSlice, FontSize = 9, Foreground = Brushes.Cyan, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
        }
    }

    private async Task RunForgeAsync(string mode)
    {
        if (_forgeBusy || _planBusy)
        {
            return;
        }

        var specification = ForgeSpecBox.Text.Trim();
        var language = (ForgeLanguageBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "csharp";
        if (specification.Length < 20)
        {
            ForgeStatusLabel.Text = "SPEC TOO SHORT";
            ForgeOutput.Text = "Describe the product, users, constraints and integrations before generating a package.";
            return;
        }

        if (!string.Equals(_lastPlanSpecification, specification, StringComparison.Ordinal))
        {
            _lastPlan = null;
            ForgeMissionLabel.Text = "DECOMPOSITION STANDBY";
            ForgeArchitectureLabel.Text = "Run mission decomposition to map phases, risks and quality gates.";
            ForgePhaseList.Children.Clear();
            ForgePhaseList.Children.Add(new TextBlock { Text = "No mission phases loaded.", FontSize = 10, Foreground = Brushes.Gray });
        }

        _forgeBusy = true;
        _lastForgeMode = mode;
        PlanMissionButton.IsEnabled = false;
        BuildBlueprintButton.IsEnabled = false;
        ImplementProgramButton.IsEnabled = false;
        ExportBlueprintButton.IsEnabled = false;
        ForgeStatusLabel.Text = mode == "implementation" ? "DESIGNING SLICE" : "ARCHITECTING";
        ForgeStatusLabel.Foreground = Brushes.Orange;
        ForgeOutput.Text = "JARVIS is decomposing the system into bounded contexts, contracts and delivery slices...";
        AddActivity("FORGE", "Program Forge started", mode);

        try
        {
            using var response = await _forgeClient.PostAsJsonAsync("/api/v1/forge", new { spec = specification, language, mode });
            var payload = await response.Content.ReadFromJsonAsync<ForgePayload>();
            if (!response.IsSuccessStatusCode || payload is null)
            {
                throw new InvalidOperationException("Program Forge request failed.");
            }

            _lastBlueprint = payload.Response ?? string.Empty;
            ForgeOutput.Text = _lastBlueprint;
            ForgeStatusLabel.Text = mode == "implementation" ? "SLICE READY" : "BLUEPRINT READY";
            ForgeStatusLabel.Foreground = Brushes.LightGreen;
            ExportBlueprintButton.IsEnabled = _lastBlueprint.Length > 0 || _lastPlan is not null;
            AddActivity("FORGE", "Software package generated", payload.DurationMs.ToString("N0") + " ms");
            if (_voiceMode)
            {
                SpeakText(mode == "implementation" ? "Vertical slice-ul este gata pentru export." : "Blueprint-ul programului este gata.");
            }
        }
        catch (Exception ex)
        {
            ForgeStatusLabel.Text = "FORGE ERROR";
            ForgeStatusLabel.Foreground = Brushes.OrangeRed;
            ForgeOutput.Text = ex.Message;
            AddActivity("ERROR", "Program Forge failed", ex.Message);
        }
        finally
        {
            PlanMissionButton.IsEnabled = true;
            BuildBlueprintButton.IsEnabled = true;
            ImplementProgramButton.IsEnabled = true;
            _forgeBusy = false;
        }
    }

    private void ForgeSpecBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (ForgeSpecBox.Text.StartsWith("Build a production-grade", StringComparison.OrdinalIgnoreCase))
        {
            ForgeSpecBox.Clear();
            ForgeSpecBox.Foreground = Brushes.White;
        }
    }
    private void BuildBlueprint_Click(object sender, RoutedEventArgs e)
    {
        _ = RunForgeAsync("blueprint");
    }

    private void ImplementProgram_Click(object sender, RoutedEventArgs e)
    {
        _ = RunForgeAsync("implementation");
    }

    private List<string> ExportCodeFiles(string markdown, string rootDirectory)
    {
        var files = new List<string>();
        var root = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fence = ((char)96).ToString() + ((char)96).ToString() + ((char)96).ToString();
        var pattern = "(?ms)^###\\s*FILE:\\s*(?<path>[^\\r\\n]+)\\r?\\n" + fence + "[^\\r\\n]*\\r?\\n(?<body>.*?)\\r?\\n" + fence;
        var matches = System.Text.RegularExpressions.Regex.Matches(markdown, pattern);

        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var relativePath = match.Groups["path"].Value.Trim().Replace('/', Path.DirectorySeparatorChar);
            var segments = relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            if (Path.IsPathRooted(relativePath) || segments.Any(segment => segment == "..") || relativePath.Length == 0)
            {
                continue;
            }

            var target = Path.GetFullPath(Path.Combine(rootDirectory, relativePath));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.WriteAllText(target, match.Groups["body"].Value, new UTF8Encoding(false));
            files.Add(Path.GetRelativePath(rootDirectory, target));
        }

        return files;
    }
    private void ExportBlueprint_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastBlueprint) && _lastPlan is null)
        {
            return;
        }

        var root = string.IsNullOrWhiteSpace(_repositoryRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JarvisDesktop", "generated-programs")
            : Path.Combine(_repositoryRoot, "projects", "generated_programs");
        var directory = Path.Combine(root, "jarvis_package_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(directory);
        var exportedFiles = new List<string>();
        if (!string.IsNullOrWhiteSpace(_lastBlueprint))
        {
            var blueprintPath = Path.Combine(directory, "BLUEPRINT.md");
            File.WriteAllText(blueprintPath, _lastBlueprint, new UTF8Encoding(false));
            exportedFiles.Add("BLUEPRINT.md");
            exportedFiles.AddRange(ExportCodeFiles(_lastBlueprint, directory));
        }
        if (_lastPlan is not null)
        {
            File.WriteAllText(Path.Combine(directory, "MISSION_PLAN.json"), JsonSerializer.Serialize(_lastPlan.Plan, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            exportedFiles.Add("MISSION_PLAN.json");
        }
        var language = (ForgeLanguageBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "csharp";
        File.WriteAllText(Path.Combine(directory, "forge-manifest.json"), JsonSerializer.Serialize(new
        {
            generated_at = DateTimeOffset.Now,
            language,
            mode = _lastForgeMode,
            source = "JARVIS Program Forge",
            files = exportedFiles.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(file => file).ToArray(),
            plan_included = _lastPlan is not null
        }, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        _lastExportDirectory = directory;
        VerifyPackageButton.IsEnabled = true;
        AddActivity("FORGE", "Software package exported", directory);
        ForgeStatusLabel.Text = "PACKAGE EXPORTED / " + exportedFiles.Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString("N0") + " FILES";
    }

    private void VerifyPackage_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastExportDirectory) || !Directory.Exists(_lastExportDirectory))
        {
            ForgeStatusLabel.Text = "NO PACKAGE";
            return;
        }

        try
        {
            var files = Directory.EnumerateFiles(_lastExportDirectory, "*", SearchOption.AllDirectories).ToList();
            var sourceFiles = files.Where(file => new[] { ".cs", ".csproj", ".cpp", ".h", ".hpp", ".vcxproj", ".cmake" }.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase) || Path.GetFileName(file).Equals("CMakeLists.txt", StringComparison.OrdinalIgnoreCase)).ToList();
            var implementationPackage = _lastForgeMode.Equals("implementation", StringComparison.OrdinalIgnoreCase);
            var checks = new[]
            {
                (Name: "manifest", Passed: File.Exists(Path.Combine(_lastExportDirectory, "forge-manifest.json"))),
                (Name: "mission or blueprint", Passed: File.Exists(Path.Combine(_lastExportDirectory, "MISSION_PLAN.json")) || File.Exists(Path.Combine(_lastExportDirectory, "BLUEPRINT.md"))),
                (Name: implementationPackage ? "source artifacts" : "source artifacts (not required for plan)", Passed: !implementationPackage || sourceFiles.Count > 0),
                (Name: "file size safety", Passed: files.All(file => new FileInfo(file).Length <= 2_000_000)),
            };
            var passed = checks.All(check => check.Passed);
            var reportLines = new List<string>
            {
                "# JARVIS Package Verification",
                "",
                "- Status: " + (passed ? "PASS" : "REVIEW"),
                "- Generated: " + DateTimeOffset.Now.ToString("O"),
                "- Files: " + files.Count.ToString("N0"),
                "- Source artifacts: " + sourceFiles.Count.ToString("N0"),
                "",
                "## Checks",
            };
            reportLines.AddRange(checks.Select(check => "- [" + (check.Passed ? "x" : " ") + "] " + check.Name));
            File.WriteAllLines(Path.Combine(_lastExportDirectory, "VERIFY_REPORT.md"), reportLines, new UTF8Encoding(false));
            ForgeStatusLabel.Text = passed ? "PACKAGE VERIFIED" : "PACKAGE NEEDS REVIEW";
            ForgeStatusLabel.Foreground = passed ? Brushes.LightGreen : Brushes.Orange;
            AddActivity("VERIFY", passed ? "Package quality gate passed" : "Package quality gate needs review", files.Count.ToString("N0") + " files");
        }
        catch (Exception ex)
        {
            ForgeStatusLabel.Text = "VERIFY ERROR";
            ForgeStatusLabel.Foreground = Brushes.OrangeRed;
            AddActivity("ERROR", "Package verification failed", ex.Message);
        }
    }

    private async Task LoadSkillsAsync(string query = "")
    {
        try
        {
            var payload = await _client.GetFromJsonAsync<SkillsPayload>("/api/v1/skills?q=" + Uri.EscapeDataString(query));
            var skills = payload?.Skills ?? [];
            SkillCountLabel.Text = skills.Count.ToString("N0") + " SKILLS INDEXED";
            SkillList.Children.Clear();
            if (skills.Count == 0)
            {
                SkillList.Children.Add(new TextBlock { Text = "No matching skills.", FontSize = 10, Foreground = Brushes.Gray });
                return;
            }

            foreach (var skill in skills.Take(14))
            {
                var row = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(13, 22, 32)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(35, 54, 68)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(0, 0, 0, 5)
                };
                row.Child = new TextBlock { Text = skill.Name ?? skill.Id ?? "Skill", FontSize = 10, Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap };
                SkillList.Children.Add(row);
            }

            if (skills.Count > 14)
            {
                SkillList.Children.Add(new TextBlock { Text = "+" + (skills.Count - 14).ToString("N0") + " more skills in the index", FontSize = 9, Foreground = Brushes.Cyan, Margin = new Thickness(0, 3, 0, 0) });
            }
        }
        catch (Exception ex)
        {
            SkillCountLabel.Text = "SKILLS OFFLINE";
            SkillList.Children.Clear();
            SkillList.Children.Add(new TextBlock { Text = ex.Message, TextWrapping = TextWrapping.Wrap, FontSize = 10, Foreground = Brushes.OrangeRed });
        }
    }

    private void SearchSkills_Click(object sender, RoutedEventArgs e)
    {
        _ = LoadSkillsAsync(_skillPlaceholder ? string.Empty : SkillSearchBox.Text.Trim());
    }

    private void SkillSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            SearchSkills_Click(sender, e);
        }
    }

    private void SkillSearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_skillPlaceholder)
        {
            SkillSearchBox.Clear();
            SkillSearchBox.Foreground = Brushes.White;
            _skillPlaceholder = false;
        }
    }

    private void LoadConversationHistory()
    {
        try
        {
            if (!File.Exists(SessionHistoryPath))
            {
                return;
            }

            var history = JsonSerializer.Deserialize<List<ChatTurn>>(File.ReadAllText(SessionHistoryPath));
            if (history is not null)
            {
                _conversationHistory.AddRange(history.TakeLast(40));
            }
        }
        catch (Exception ex)
        {
            AddActivity("WARN", "Session history restore failed", ex.Message);
        }
    }

    private void RenderConversationHistory()
    {
        foreach (var turn in _conversationHistory.TakeLast(8))
        {
            AddChatMessage(turn.Role == "user" ? "YOU" : "JARVIS", turn.Content, turn.Role == "user");
        }
    }

    private void SaveConversationHistory()
    {
        try
        {
            var directory = Path.GetDirectoryName(SessionHistoryPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(SessionHistoryPath, JsonSerializer.Serialize(_conversationHistory.TakeLast(40), new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            AddActivity("WARN", "Session history save failed", ex.Message);
        }
    }
    private async Task SendMessageAsync(string? voiceMessage = null, bool speakResponse = false)
    {
        if (_busy)
        {
            return;
        }

        var fromVoice = !string.IsNullOrWhiteSpace(voiceMessage);
        var message = fromVoice ? voiceMessage!.Trim() : MessageBox.Text.Trim();
        if ((!fromVoice && _messagePlaceholder) || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _busy = true;
        SendButton.IsEnabled = false;
        ChatStatus.Text = "THINKING";
        AddChatMessage("YOU", message, true);
        _conversationHistory.Add(new ChatTurn("user", message));
        AddActivity("OBSERVE", "Incoming command", message);

        try
        {
            var response = await _client.PostAsJsonAsync("/api/v1/chat", new { message, source = "desktop-csharp", history = _conversationHistory.TakeLast(12).Select(turn => new { role = turn.Role, content = turn.Content }).ToArray() });
            var payload = await response.Content.ReadFromJsonAsync<ChatPayload>();
            if (!response.IsSuccessStatusCode || payload is null)
            {
                throw new InvalidOperationException(payload?.Reply ?? "Unified chat request failed.");
            }

            var reply = payload.Reply ?? "No textual response.";
            _conversationHistory.Add(new ChatTurn("assistant", reply));
            while (_conversationHistory.Count > 24)
            {
                _conversationHistory.RemoveAt(0);
            }
            AddChatMessage("JARVIS", reply, false);
            if (speakResponse || _voiceMode)
            {
                SpeakText(reply);
            }
            AddActivity("REASON", "Cognitive response completed", (payload.Intent ?? "conversation") + " · " + payload.DurationMs.ToString("N0") + " ms");
            LatencyLabel.Text = payload.DurationMs.ToString("N0") + " ms";
            ChatStatus.Text = "READY";
        }
        catch (Exception ex)
        {
            AddChatMessage("JARVIS", "Core unavailable: " + ex.Message, false);
            AddActivity("ERROR", "Cognitive request failed", ex.Message);
            ChatStatus.Text = "OFFLINE";
        }
        finally
        {
            _busy = false;
            SendButton.IsEnabled = true;
        }
    }

    private async Task SearchMemoryAsync()
    {
        var query = _searchPlaceholder ? string.Empty : SearchBox.Text.Trim();

        try
        {
            var payload = await _client.GetFromJsonAsync<SearchPayload>("/api/v1/search?q=" + Uri.EscapeDataString(query));
            SearchResults.Children.Clear();
            var rows = payload?.Results ?? [];
            if (rows.Count == 0)
            {
                SearchResults.Children.Add(new TextBlock { Text = "No matching canonical memory.", TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(120, 144, 160)), Margin = new Thickness(0, 0, 0, 18) });
                return;
            }

            foreach (var result in rows.Take(5))
            {
                var card = new Border { Background = new SolidColorBrush(Color.FromRgb(16, 23, 34)), BorderBrush = new SolidColorBrush(Color.FromRgb(38, 54, 71)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 7) };
                var stack = new StackPanel();
                stack.Children.Add(new TextBlock { Text = result.Title ?? result.Category ?? "Memory", FontSize = 11, Foreground = Brushes.White });
                stack.Children.Add(new TextBlock { Text = result.Summary ?? result.Content ?? string.Empty, TextWrapping = TextWrapping.Wrap, FontSize = 9, Foreground = new SolidColorBrush(Color.FromRgb(120, 144, 160)), Margin = new Thickness(0, 4, 0, 0) });
                card.Child = stack;
                SearchResults.Children.Add(card);
            }
        }
        catch (Exception ex)
        {
            SearchResults.Children.Clear();
            SearchResults.Children.Add(new TextBlock { Text = "Memory unavailable: " + ex.Message, TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Brushes.OrangeRed });
        }
    }

    private void AddChatMessage(string sender, string text, bool user)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(user ? Color.FromRgb(24, 35, 47) : Color.FromRgb(16, 37, 49)),
            BorderBrush = new SolidColorBrush(user ? Color.FromRgb(42, 58, 74) : Color.FromRgb(32, 104, 121)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = user ? new Thickness(80, 0, 0, 10) : new Thickness(0, 0, 80, 10)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = sender + " // " + (user ? "COMMAND" : "COGNITIVE CORE"), FontSize = 9, Foreground = user ? Brushes.LightGreen : Brushes.Cyan });
        stack.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 5, 0, 0) });
        border.Child = stack;
        ChatMessages.Children.Add(border);
        ChatScroll.ScrollToEnd();
    }

    private void AddActivity(string kind, string message, string detail)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 9) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = DateTime.Now.ToString("HH:mm:ss"), FontSize = 9, Foreground = new SolidColorBrush(Color.FromRgb(120, 144, 160)) });
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = message, FontSize = 11 });
        stack.Children.Add(new TextBlock { Text = detail, FontSize = 9, Foreground = new SolidColorBrush(Color.FromRgb(120, 144, 160)), Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(stack, 1);
        row.Children.Add(stack);
        var label = new TextBlock { Text = kind, FontSize = 9, Foreground = kind == "ERROR" ? Brushes.OrangeRed : Brushes.Cyan, Margin = new Thickness(12, 0, 0, 0) };
        Grid.SetColumn(label, 2);
        row.Children.Add(label);
        ActivityLog.Children.Insert(0, row);
        while (ActivityLog.Children.Count > 8)
        {
            ActivityLog.Children.RemoveAt(ActivityLog.Children.Count - 1);
        }
    }

    private void SetConnection(string state, Brush color)
    {
        ConnectionLabel.Text = state;
        ConnectionLabel.Foreground = color;
        ConnectionIndicator.Fill = color;
    }

    private void MessageBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_messagePlaceholder)
        {
            MessageBox.Clear();
            MessageBox.Foreground = Brushes.White;
            _messagePlaceholder = false;
        }
    }

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (_searchPlaceholder)
        {
            SearchBox.Clear();
            SearchBox.Foreground = Brushes.White;
            _searchPlaceholder = false;
        }
    }

    private void MessageBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
        {
            e.Handled = true;
            _ = SendMessageAsync();
        }
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            _ = SearchMemoryAsync();
        }
    }

    private void Send_Click(object sender, RoutedEventArgs e) => _ = SendMessageAsync();
    private void Search_Click(object sender, RoutedEventArgs e) => _ = SearchMemoryAsync();
    private void Diagnostics_Click(object sender, RoutedEventArgs e) => _ = RefreshTelemetryAsync();
    private void Reconnect_Click(object sender, RoutedEventArgs e) => _ = ConnectCoreAsync();

    private void Overview_Click(object sender, RoutedEventArgs e) => MainScroll.ScrollToHome();
    private void Memory_Click(object sender, RoutedEventArgs e) => SearchBox.Focus();
    private void Council_Click(object sender, RoutedEventArgs e) => CouncilPanel.BringIntoView();
    private void Execution_Click(object sender, RoutedEventArgs e) => ActivityPanel.BringIntoView();
    private void Settings_Click(object sender, RoutedEventArgs e) => AddActivity("INFO", "Settings are environment-driven", "JARVIS_* configuration");

    private void TopBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;
        _liveStreamCancellation?.Cancel();
        SaveConversationHistory();
        _clockTimer.Stop();
        _telemetryTimer.Stop();
        _reactorStoryboard?.Remove(this);
        StopVoiceMode();
        _speechSynthesizer?.Dispose();
        if (_voiceStartedByHost && _voiceProcess is not null && !_voiceProcess.HasExited)
        {
            try
            {
                _voiceProcess.Kill(entireProcessTree: true);
                _voiceProcess.Dispose();
            }
            catch
            {
            }
        }
        _voiceClient.Dispose();
        _forgeClient.Dispose();
        _client.Dispose();
        if (_backendStartedByHost && _backendProcess is not null && !_backendProcess.HasExited)
        {
            try
            {
                _backendProcess.Kill(entireProcessTree: true);
                _backendProcess.Dispose();
            }
            catch
            {
            }
        }
    }

    private sealed record ChatTurn(string Role, string Content);

    private sealed class VoiceHealthPayload
    {
        [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
        [JsonPropertyName("model_ready")] public bool ModelReady { get; set; }
    }

    private sealed class ForgePayload
    {
        [JsonPropertyName("response")] public string? Response { get; set; }
        [JsonPropertyName("mode")] public string? Mode { get; set; }
        [JsonPropertyName("duration_ms")] public double DurationMs { get; set; }
    }

    private sealed class ForgePlanPayload
    {
        [JsonPropertyName("plan")] public ForgePlan Plan { get; set; } = new();
        [JsonPropertyName("duration_ms")] public double DurationMs { get; set; }
    }

    private sealed class ForgePlan
    {
        [JsonPropertyName("mission")] public string? Mission { get; set; }
        [JsonPropertyName("architecture")] public List<string> Architecture { get; set; } = [];
        [JsonPropertyName("phases")] public List<ForgePhase> Phases { get; set; } = [];
        [JsonPropertyName("risks")] public List<ForgeRisk> Risks { get; set; } = [];
        [JsonPropertyName("next_slice")] public string? NextSlice { get; set; }
    }

    private sealed class ForgePhase
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("owner")] public string? Owner { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("deliverables")] public List<string> Deliverables { get; set; } = [];
        [JsonPropertyName("acceptance")] public List<string> Acceptance { get; set; } = [];
    }

    private sealed class ForgeRisk
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("risk")] public string? Risk { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("mitigation")] public string? Mitigation { get; set; }
    }

    private sealed class SkillsPayload
    {
        [JsonPropertyName("skills")] public List<SkillPayload> Skills { get; set; } = [];
    }

    private sealed class SkillPayload
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
    }
    private sealed class CouncilPayload
    {
        [JsonPropertyName("retrieval")] public JsonElement Retrieval { get; set; }
        [JsonPropertyName("verification")] public JsonElement Verification { get; set; }
        [JsonPropertyName("critique")] public JsonElement Critique { get; set; }
    }

    private sealed class ProposalsPayload
    {
        [JsonPropertyName("pending")] public List<ProposalPayload> Pending { get; set; } = [];
    }

    private sealed class ProposalPayload
    {
        [JsonPropertyName("candidate_id")] public string? CandidateId { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("content")] public string? Content { get; set; }
    }
    private sealed class MetricsPayload
    {
        [JsonPropertyName("memory_items")] public int MemoryItems { get; set; }
        [JsonPropertyName("agents_online")] public int AgentsOnline { get; set; }
        [JsonPropertyName("agents_total")] public int AgentsTotal { get; set; }
        [JsonPropertyName("skills_operational")] public int SkillsOperational { get; set; }
        [JsonPropertyName("proposals_pending")] public int ProposalsPending { get; set; }
        [JsonPropertyName("engine")] public string Engine { get; set; } = string.Empty;
    }

    private sealed class AgentsPayload
    {
        [JsonPropertyName("agents")] public List<AgentPayload> Agents { get; set; } = [];
    }

    private sealed class AgentPayload
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("domain")] public string? Domain { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
    }

    private sealed class ChatPayload
    {
        [JsonPropertyName("reply")] public string? Reply { get; set; }
        [JsonPropertyName("intent")] public string? Intent { get; set; }
        [JsonPropertyName("duration_ms")] public double DurationMs { get; set; }
    }

    private sealed class SearchPayload
    {
        [JsonPropertyName("results")] public List<MemoryPayload> Results { get; set; } = [];
    }

    private sealed class MemoryPayload
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("category")] public string? Category { get; set; }
        [JsonPropertyName("summary")] public string? Summary { get; set; }
        [JsonPropertyName("content")] public string? Content { get; set; }
    }
}
