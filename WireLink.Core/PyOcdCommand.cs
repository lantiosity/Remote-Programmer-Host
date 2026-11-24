using System.Text.RegularExpressions;

namespace WireLink.Core;

public static class PyOcdCommand
{
    public static List<string> Build(Settings s, string action, int? blePort = null)
    {
        Validation.Link(s);
        if (action is not ("flash" or "erase" or "reset" or "inspect")) throw new ArgumentException("未知烧录操作");
        if (string.IsNullOrWhiteSpace(s.Target) || !Regex.IsMatch(s.Target, @"^[a-zA-Z0-9_.+-]+$"))
            throw new ArgumentException("请填写 pyOCD 使用的芯片型号");
        if (s.Target == "cortex_m" && action is "flash" or "erase")
            throw new ArgumentException("cortex_m 不包含 Flash 算法，请选择实际芯片型号或加载 DFP 支持包");
        var hz = Validation.Number(s.Frequency, "SWD 时钟 / kHz", 10, 50000) * 1000;
        if (s.ConnectMode is not ("halt" or "pre-reset" or "under-reset" or "attach"))
            throw new ArgumentException("连接模式无效");
        string uid = s.Transport switch
        {
            "WiFi" => $"remote:{s.Host}:{Validation.Number(s.DapPort, "DAP 端口", 1, 65535)}",
            "BLE" => $"remote:127.0.0.1:{blePort ?? throw new ArgumentException("蓝牙桥尚未就绪")}",
            _ => string.IsNullOrWhiteSpace(s.UsbId) ? throw new ArgumentException("请扫描并选择 USB 探针") : s.UsbId
        };
        List<string> args = ["-u", "-m", "pyocd", action is "flash" or "erase" ? action : "commander",
            "--no-config", "--color", "never", "-W", "-t", s.Target, "-f", hz.ToString(), "-M", s.ConnectMode, "--probe", uid];
        if (!string.IsNullOrWhiteSpace(s.Pack)) args.AddRange(["--pack", Validation.FilePath(s.Pack, ".pack")]);
        if (action == "flash")
        {
            var path = Validation.FilePath(s.Image, ".bin", ".hex", ".elf", ".axf");
            if (s.EraseMode is not ("sector" or "auto" or "chip")) throw new ArgumentException("擦除策略无效");
            args.AddRange(["--erase", s.EraseMode]);
            if (Path.GetExtension(path).Equals(".bin", StringComparison.OrdinalIgnoreCase))
                args.AddRange(["--base-address", "0x" + Validation.Number(s.BaseAddress, "BIN 起始地址", 0, uint.MaxValue).ToString("x")]);
            if (Path.GetExtension(path).Equals(".axf", StringComparison.OrdinalIgnoreCase)) args.AddRange(["--format", "elf"]);
            if (!s.ResetAfter) args.AddRange(["--no-reset", "-O", "resume_on_disconnect=false"]);
            args.Add(path);
        }
        else if (action == "erase") args.Add("--chip");
        else if (action == "reset") args.AddRange(["-c", "reset"]);
        else args.AddRange(["-c", "show target", "-c", "read32 0xE000ED00 1"]);
        return args;
    }
}
