using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using WireLink.App.Services;
using WireLink.Core;

namespace WireLink.App;

public sealed class MainForm : ChromeForm
{
    private const string HostName = "wirelink.example";
    private const string Origin = "https://" + HostName;
    private readonly Panel content = new() { Dock = DockStyle.Fill };
    private readonly WebView2 web;
    private readonly StartupSurface startupSurface;
    private readonly StartupTrace startup = new();
    private bool dark;
    private bool presented;
    private readonly string root = FindRoot();
    private readonly string configPath;
    private readonly PyOcdService pyocd;
    private Settings settings;
    private CancellationTokenSource? jobCancellation;
    private Task? operation;
    private string activeAction = "";
    private Task? serialOpenOperation;
    private SerialService? serial;
    private bool serialOpening;
    private CancellationTokenSource? serialCancellation;
    private bool closing, allowClose, ready;
    private bool runtimeSetupAttempted;
    private Task<JsonNode>? initialCatalog;
    private string serialTransport = "";
    private readonly List<string> logs = [];
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static readonly string AppVersion = typeof(MainForm).Assembly.GetName().Version!.ToString(3);

    public MainForm()
    {
        Text = "WireLink · 远程烧录工作台";
        AccessibleName = "WireLink · 远程烧录工作台";
        using (var stream = typeof(MainForm).Assembly.GetManifestResourceStream("WireLink.App.Assets.WireLink.ico")
            ?? throw new InvalidOperationException("缺少应用图标资源"))
        using (var embeddedIcon = new Icon(stream))
            Icon = (Icon)embeddedIcon.Clone();
        FormClosed += (_, _) => Icon?.Dispose();
        Size = new(1320, 920);
        MinimumSize = new(1100, 780);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ShowIcon = true; // Shell/taskbar icon only; the custom caption draws no icon.
        configPath = Directory.Exists(Path.Combine(AppContext.BaseDirectory, "engine"))
            ? Path.Combine(root, "data", "settings.json") : Path.Combine(root, ".wirelink-desktop.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        settings = Settings.Load(configPath);
        dark = WindowAppearance.IsDark(settings.Theme);
        var palette = WindowPalette.For(dark);
        BackColor = palette.Background;
        // Set before creating the browser surface, including its first compositor frame.
        Environment.SetEnvironmentVariable("WEBVIEW2_DEFAULT_BACKGROUND_COLOR", palette.Background.ToArgb().ToString("X8"));
        web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = palette.Background };
        startupSurface = new(palette) { FirstPaint = () => startup.Mark("native_first_paint") };
        content.Controls.Add(web);
        content.Controls.Add(startupSurface);
        Controls.Add(content);
        InstallChrome(palette);
        startupSurface.BringToFront();
        pyocd = new(root, Log);
        startup.Mark("form_created");
        FormClosing += ClosingAsync;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowAppearance.ApplyFrame(Handle, dark, WindowPalette.For(dark));
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Begin before Shown, while the independently painted native surface is visible.
        _ = InitializeAsync();
    }

    private void ApplyAppearance(bool isDark)
    {
        dark = isDark;
        var palette = WindowPalette.For(dark);
        BackColor = palette.Background;
        content.BackColor = palette.Background;
        ApplyChromePalette(palette);
        web.DefaultBackgroundColor = palette.Background;
        if (!startupSurface.IsDisposed) startupSurface.ApplyPalette(palette);
        WindowAppearance.ApplyFrame(Handle, dark, palette);
    }

    private void PresentWorkbench(string source)
    {
        if (presented || closing || !ready) return;
        presented = true;
        startupSurface.Visible = false;
        content.Controls.Remove(startupSurface);
        startupSurface.Dispose();
        web.BringToFront();
        startup.Mark("workbench_revealed");
        startup.Save(root, source);
    }

    private static string FindRoot()
    {
        if (Directory.Exists(Path.Combine(AppContext.BaseDirectory, "engine")))
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            return folder.Name.Equals("app", StringComparison.OrdinalIgnoreCase) && folder.Parent != null
                ? folder.Parent.FullName : folder.FullName;
        }
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
                if (Directory.Exists(Path.Combine(directory.FullName, "WireLink.App")) && File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
                    return directory.FullName;
        return AppContext.BaseDirectory;
    }

    private async Task InitializeAsync()
    {
        try
        {
            if (File.Exists(Path.Combine(AppContext.BaseDirectory, "WebView2Loader.dll")))
                CoreWebView2Environment.SetLoaderDllFolderPath(AppContext.BaseDirectory);
            var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WireLink", "WebView2");
            startup.Mark("environment_start");
            var fixedBrowser = Path.Combine(AppContext.BaseDirectory, "browser");
            var useBundledBrowser = false;
            try { CoreWebView2Environment.GetAvailableBrowserVersionString(); }
            catch (WebView2RuntimeNotFoundException) { useBundledBrowser = Directory.Exists(fixedBrowser); }
            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: useBundledBrowser ? fixedBrowser : null, userDataFolder: profile);
            if (closing || IsDisposed) return;
            startup.Mark("environment_ready");
            await web.EnsureCoreWebView2Async(environment);
            if (closing || IsDisposed) return;
            startup.Mark("controller_ready");
            var core = web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.SetVirtualHostNameToFolderMapping(HostName, Path.Combine(AppContext.BaseDirectory, "wwwroot"),
                CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += (_, args) =>
            {
                if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) || uri.GetLeftPart(UriPartial.Authority) != Origin)
                    args.Cancel = true;
            };
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.WebMessageReceived += MessageReceivedAsync;
            // Theme is available before the document's styles or deferred app script run.
            await core.AddScriptToExecuteOnDocumentCreatedAsync("window.__wirelinkSettings = "
                + JsonSerializer.Serialize(settings, Settings.Json) + ";");
            if (closing || IsDisposed) return;
            startupSurface.Status = "正在载入工作台…";
            core.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess && !closing)
                {
                    startupSurface.Status = "界面加载失败，请重新启动";
                    Log("界面加载失败：" + args.WebErrorStatus);
                }
            };
            startup.Mark("navigation_start");
            core.Navigate(Origin + "/index.html");
        }
        catch (Exception ex)
        {
            if (closing || IsDisposed) return;
            var installer = Path.Combine(AppContext.BaseDirectory, "setup", "MicrosoftEdgeWebview2Setup.exe");
            if (ex is WebView2RuntimeNotFoundException && !runtimeSetupAttempted && File.Exists(installer))
            {
                runtimeSetupAttempted = true;
                startupSurface.Status = "首次准备 WebView2 运行时，请保持网络连接…";
                try
                {
                    using var setup = Process.Start(new ProcessStartInfo(installer, "/silent /install")
                        { UseShellExecute = false, CreateNoWindow = true });
                    if (setup == null) throw new IOException("无法启动 WebView2 安装器");
                    await setup.WaitForExitAsync();
                    if (closing || IsDisposed) return;
                    CoreWebView2Environment.GetAvailableBrowserVersionString();
                    await InitializeAsync();
                    return;
                }
                catch (Exception setupError)
                {
                    if (!closing && !IsDisposed) MessageBox.Show(this,
                        "WebView2 准备失败，请联网后重试，或手动运行 app/setup 中的微软安装器。\n" + setupError.Message,
                        "WireLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Close(); return;
                }
            }
            MessageBox.Show(this, "界面初始化失败：" + ex.Message + "\n请确认已安装 Microsoft Edge WebView2 Runtime。", "WireLink", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    private void OnUi(Action action)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired) { try { BeginInvoke(action); } catch (InvalidOperationException) { } }
        else action();
    }

    private void Post(object value) => OnUi(() =>
    {
        if (ready && !web.IsDisposed) web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(value, WebJson));
    });

    private void Log(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var line = $"[{DateTime.Now:HH:mm:ss}] {text}";
        lock (logs)
        {
            logs.Add(line);
            if (logs.Count > 2500) logs.RemoveRange(0, logs.Count - 2500);
        }
        Post(new { type = "log", text = line });
        var match = Regex.Match(text, @"(\d+(?:\.\d+)?)%");
        if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var progress)) Post(new { type = "progress", value = progress });
    }

    private void Persist(Settings value)
    {
        settings = value;
        try { settings.Save(configPath); } catch (IOException ex) { Log("配置保存失败：" + ex.Message); }
    }

    private async void MessageReceivedAsync(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!Uri.TryCreate(args.Source, UriKind.Absolute, out var uri) || uri.GetLeftPart(UriPartial.Authority) != Origin) return;
        string? id = null;
        try
        {
            using var message = JsonDocument.Parse(args.WebMessageAsJson);
            var data = message.RootElement;
            id = data.TryGetProperty("id", out var requestId) ? requestId.GetString() : null;
            var action = data.GetProperty("action").GetString()!;
            var value = data.TryGetProperty("settings", out var saved) ? saved.Deserialize<Settings>(Settings.Json) ?? settings : settings;
            if (closing) throw new InvalidOperationException("程序正在退出");
            switch (action)
            {
                case "ready":
                    startup.Mark("document_ready");
                    ready = true;
                    Post(new { type = "init", settings = JsonSerializer.SerializeToNode(settings, Settings.Json),
                        version = AppVersion, engineReady = File.Exists(pyocd.Python), root });
                    Log($"WireLink {AppVersion} 已就绪 · C# / .NET 8 / WebView2");
                    break;
                case "painted":
                    PresentWorkbench(data.TryGetProperty("source", out var source) ? source.GetString() ?? "unknown" : "unknown");
                    break;
                case "targetsBackground":
                    initialCatalog ??= new PyOcdService(root, _ => { }).CatalogAsync("targets", value, CancellationToken.None);
                    Post(new { type = "data", kind = "targets", value = await initialCatalog });
                    break;
                case "save": Persist(value); break;
                case "theme":
                    ApplyAppearance(data.GetProperty("dark").GetBoolean());
                    break;
                case "browse": Browse(data.GetProperty("key").GetString()!); break;
                case "export": ExportLogs(); break;
                case "clearLog": lock (logs) logs.Clear(); break;
                case "wifi_ports":
                    Post(new { type = "data", kind = "wifi_ports", value = SerialPort.GetPortNames().OrderBy(x => x).ToArray() });
                    break;
                case "cancel": jobCancellation?.Cancel(); break;
                case "ping":
                    Post(new { type = "hostState", busy = jobCancellation != null, action = activeAction,
                        serial = serialOpening ? "opening" : serial != null ? "connected" : "closed" });
                    break;
                case "serialOpen": serialOpenOperation = OpenSerialAsync(value); await serialOpenOperation; break;
                case "serialClose":
                    serialCancellation?.Cancel();
                    if (serialOpening && serialOpenOperation != null) { try { await serialOpenOperation; } catch { } }
                    await CloseSerialAsync();
                    break;
                case "serialSend":
                    if (serial == null || serialOpening) throw new InvalidOperationException("串口尚未连接");
                    var bytes = Validation.SerialData(data.GetProperty("text").GetString() ?? "", value.HexSend, value.Newline);
                    await serial.SendAsync(bytes, serialCancellation!.Token);
                    Post(new { type = "tx", hex = Convert.ToHexString(bytes) });
                    break;
                case "probes": case "targets": case "scan": case "ports":
                case "wifi_status": case "wifi_scan": case "wifi_save": case "wifi_clear":
                case "flash": case "erase": case "inspect": case "reset": case "ota_info": case "ota_update":
                    if (jobCancellation != null) throw new InvalidOperationException("已有任务正在运行");
                    if (serialOpening || serial != null && (action.StartsWith("wifi_") || action.StartsWith("ota_") || serialTransport == "BLE" || value.Transport == "BLE"))
                        throw new InvalidOperationException("请先关闭串口，再进行蓝牙操作或设备升级");
                    ValidateAction(action, value);
                    WifiSetupRequest? wifi = null;
                    if (action.StartsWith("wifi_"))
                    {
                        wifi = new(data.GetProperty("wifiLink").GetString()!, action[5..],
                            data.TryGetProperty("ssid", out var ssid) ? ssid.GetString() ?? "" : "",
                            data.TryGetProperty("password", out var password) ? password.GetString() ?? "" : "");
                        WifiSetupProtocol.Command("00000000", wifi.Operation, wifi.Ssid, wifi.Password);
                    }
                    if (!ConfirmAction(action, value)) break;
                    Persist(value);
                    jobCancellation = new();
                    activeAction = action;
                    Post(new { type = "job", busy = true, action });
                    operation = RunJobAsync(action, value, jobCancellation.Token, wifi);
                    await operation;
                    break;
                default: throw new ArgumentException("不支持的操作");
            }
            if (id != null) Post(new { type = "response", id, ok = true });
        }
        catch (Exception ex)
        {
            if (id != null) Post(new { type = "response", id, ok = false, error = ex.Message });
            else Post(new { type = "error", text = ex.Message });
            Log(ex.Message);
        }
    }

    private static void ValidateAction(string action, Settings value)
    {
        if (action is "flash" or "erase" or "inspect" or "reset")
            PyOcdCommand.Build(value, action, value.Transport == "BLE" ? 1 : null);
        if (action == "ota_update") FirmwareImage.Parse(File.ReadAllBytes(Validation.FilePath(value.OtaImage, ".bin")));
    }

    private bool ConfirmAction(string action, Settings value)
    {
        string? text = action switch
        {
            "wifi_save" => "将保存 WiFi 配置并重启烧录器。继续？",
            "wifi_clear" => "将清除 WiFi 配置并重启烧录器，仅保留设备热点。继续？",
            "erase" => $"目标 {value.Target} 的全部 Flash 将被擦除。继续？",
            "flash" when value.EraseMode == "chip" => $"烧录前将整片擦除目标 {value.Target}。继续？",
            "ota_update" => $"链路：{value.Transport}；设备：{(value.Transport == "USB" ? value.UsbId : value.Transport == "WiFi" ? value.Host : string.IsNullOrWhiteSpace(value.BleAddress) ? value.BleName : value.BleAddress)}\n"
                + (value.VerifyOnly ? "将传输并校验 ESP32 应用，不切换启动分区。" : "升级 ESP32 自身固件，完成后重启并暂时断开 USB / 网络。") + "\n继续？",
            _ => null
        };
        return text == null || MessageBox.Show(this, text, "确认操作", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.OK;
    }

    private async Task RunJobAsync(string action, Settings value, CancellationToken ct, WifiSetupRequest? wifi = null)
    {
        var clock = Stopwatch.StartNew();
        var status = "success";
        void Phase(string text) { Log(text); Post(new { type = "phase", text }); }
        try
        {
            Phase(action switch
            {
                "flash" => "正在烧录目标芯片…", "erase" => "正在擦除…", "inspect" => "正在检测目标…", "reset" => "正在复位…",
                "scan" => "正在扫描蓝牙烧录器…", "ota_update" => "正在准备 ESP32 升级…", _ => "正在读取设备信息…"
            });
            switch (action)
            {
                case "wifi_status": case "wifi_scan": case "wifi_save": case "wifi_clear":
                    Phase(action == "wifi_scan" ? "正在扫描烧录器附近的 2.4 GHz 网络…" : "正在连接 WiFi 配置服务…");
                    Post(new { type = "data", kind = "wifi_setup", value = await WifiSetupService.RunAsync(value, wifi!, ct) });
                    Log(action is "wifi_save" or "wifi_clear" ? "WiFi 配置已写入，烧录器正在重启。" : "WiFi 配置请求完成。");
                    break;
                case "probes": case "targets":
                    var result = await pyocd.CatalogAsync(action, value, ct);
                    Post(new { type = "data", kind = action, value = result });
                    Log($"已读取 {result.AsArray().Count} 个{(action == "probes" ? "探针" : "芯片型号")}");
                    break;
                case "ports":
                    Post(new { type = "data", kind = "ports", value = SerialPort.GetPortNames().OrderBy(x => x).ToArray() });
                    break;
                case "scan":
                    Post(new { type = "data", kind = "scan", value = await BleConnection.ScanAsync(TimeSpan.FromSeconds(6), ct) });
                    break;
                case "ota_info": case "ota_update":
                    await new OtaService(Phase, progress => Post(new { type = "progress", value = progress }),
                        info => Post(new { type = "data", kind = "ota_info", value = info })).RunAsync(value, action == "ota_update", ct);
                    break;
                default:
                    if (value.Transport == "BLE")
                    {
                        Phase("正在连接 BLE DAP 服务…");
                        var address = await BleConnection.ResolveAsync(value, ct);
                        await using var ble = await BleConnection.ConnectAsync(address, "10", ct);
                        await using var bridge = await BleDapBridge.StartAsync(ble, ct, Log);
                        Phase($"蓝牙已连接 · MTU {ble.Mtu}");
                        await pyocd.RunAsync(PyOcdCommand.Build(value, action, bridge.Port), ct);
                    }
                    else await pyocd.RunAsync(PyOcdCommand.Build(value, action), ct);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            status = "cancelled";
            if (action is "flash" or "erase") Log("任务已取消，目标程序可能不完整，请重新烧录。");
            if (action == "ota_update") Log("升级已取消；若设备已经激活或重启，请重新读取状态确认运行分区。");
        }
        catch (Exception ex)
        {
            status = "error";
            var detail = string.IsNullOrWhiteSpace(ex.Message) ? "系统未提供错误描述" : ex.Message;
            Log($"{detail} [{ex.GetType().Name} · 0x{ex.HResult:X8}]");
        }
        finally
        {
            var elapsed = Math.Round(clock.Elapsed.TotalSeconds, 1);
            Log((status == "success" ? "任务完成" : status == "cancelled" ? "任务已取消" : "任务失败") + $" · {elapsed:F1} 秒");
            jobCancellation?.Dispose(); jobCancellation = null;
            activeAction = "";
            Post(new { type = "job", busy = false, action, status, elapsed });
        }
    }

    private async Task OpenSerialAsync(Settings value)
    {
        if (jobCancellation != null || serial != null || serialOpening) throw new InvalidOperationException("请等待当前任务完成或关闭串口连接");
        serialOpening = true;
        serialTransport = value.Transport;
        serialCancellation = new();
        var current = new SerialService();
        serial = current;
        current.Received = bytes => Post(new { type = "rx", hex = Convert.ToHexString(bytes) });
        current.Disconnected = error => OnUi(() => _ = CloseSerialAsync(error));
        Post(new { type = "serial", status = "opening", text = "正在连接…" });
        try
        {
            var label = await current.OpenAsync(value, serialCancellation.Token);
            if (!ReferenceEquals(serial, current)) return;
            Persist(value);
            Post(new { type = "serial", status = "connected", text = label });
            Log("串口已连接 · " + label);
        }
        catch
        {
            await CloseSerialAsync();
            throw;
        }
        finally { serialOpening = false; }
    }

    private async Task CloseSerialAsync(string? error = null)
    {
        var current = serial;
        serial = null;
        serialCancellation?.Cancel();
        if (current != null) await current.DisposeAsync();
        serialCancellation?.Dispose(); serialCancellation = null;
        serialOpening = false;
        Post(new { type = "serial", status = "closed", text = error ?? "串口已断开" });
        if (error != null) Log(error);
    }

    private void Browse(string key)
    {
        var filter = key switch
        {
            "pack" => "CMSIS Device Family Pack|*.pack",
            "image" => "目标固件|*.bin;*.hex;*.elf;*.axf|全部文件|*.*",
            "ota_image" => "ESP32 应用镜像|*.bin",
            _ => throw new ArgumentException("不支持的文件类型")
        };
        using var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true, Title = "选择固件或芯片支持包" };
        if (dialog.ShowDialog(this) == DialogResult.OK) Post(new { type = "file", key, path = dialog.FileName });
    }

    private void ExportLogs()
    {
        using var dialog = new SaveFileDialog { Filter = "日志文件|*.log", FileName = $"wirelink-{DateTime.Now:yyyyMMdd-HHmmss}.log" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        lock (logs) File.WriteAllLines(dialog.FileName, logs, new UTF8Encoding(false));
    }

    private async void ClosingAsync(object? sender, FormClosingEventArgs args)
    {
        if (allowClose) return;
        args.Cancel = true;
        if (closing) return;
        if ((jobCancellation != null || serial != null) && MessageBox.Show(this,
            "仍有活动任务或串口连接。退出将取消任务并释放连接。", "退出 WireLink", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        closing = true;
        Enabled = false;
        jobCancellation?.Cancel();
        serialCancellation?.Cancel();
        try
        {
            if (operation != null) await operation;
            if (serialOpenOperation != null) { try { await serialOpenOperation; } catch { } }
            await CloseSerialAsync();
        }
        finally { allowClose = true; Close(); }
    }
}
