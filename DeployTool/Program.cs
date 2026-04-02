using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DeployTool;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}

class MainForm : Form
{
    private readonly Button _btnCloud;
    private readonly Button _btnBot;
    private readonly Button _btnBotFast;
    private readonly Button _btnAll;
    private readonly TextBox _logBox;
    private readonly string _scriptsDir;
    private bool _running;

    public MainForm()
    {
        Text = "HearthBot 部署工具";
        Size = new Size(600, 520);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;

        // 找到 Scripts 目录
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        _scriptsDir = Path.Combine(appDir, "Scripts");
        if (!Directory.Exists(_scriptsDir))
        {
            // 开发时从 DeployTool/bin 向上找
            var dir = Directory.GetParent(appDir.TrimEnd(Path.DirectorySeparatorChar));
            for (int i = 0; i < 5 && dir != null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "Scripts");
                if (File.Exists(Path.Combine(candidate, "deploy_cloud.ps1")))
                {
                    _scriptsDir = candidate;
                    break;
                }
            }
        }

        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 50,
            Padding = new Padding(8, 10, 8, 5),
            AutoSize = false
        };

        _btnCloud = MakeButton("上传云控服务器", Color.FromArgb(0, 122, 204));
        _btnBot = MakeButton("上传脚本(混淆)", Color.FromArgb(46, 139, 87));
        _btnBotFast = MakeButton("上传脚本(快速)", Color.FromArgb(255, 140, 0));
        _btnAll = MakeButton("全部上传", Color.FromArgb(178, 34, 34));

        _btnCloud.Click += (_, _) => RunScript("deploy_cloud.ps1", "");
        _btnBot.Click += (_, _) => RunScript("deploy_bot.ps1", "");
        _btnBotFast.Click += (_, _) => RunScript("deploy_bot.ps1", "-SkipObfuscation");
        _btnAll.Click += async (_, _) =>
        {
            await RunScriptAsync("deploy_cloud.ps1", "");
            if (!_running) return; // 被中断
            await RunScriptAsync("deploy_bot.ps1", "-SkipObfuscation");
        };

        panel.Controls.AddRange(new Control[] { _btnCloud, _btnBot, _btnBotFast, _btnAll });

        _logBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.FromArgb(200, 200, 200),
            Font = new Font("Consolas", 9f),
            WordWrap = true
        };

        Controls.Add(_logBox);
        Controls.Add(panel);

        AppendLog("就绪。点击按钮开始部署。");
        AppendLog($"脚本目录: {_scriptsDir}");
    }

    private Button MakeButton(string text, Color color)
    {
        return new Button
        {
            Text = text,
            Width = 130,
            Height = 32,
            FlatStyle = FlatStyle.Flat,
            BackColor = color,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
            Margin = new Padding(3)
        };
    }

    private void SetButtonsEnabled(bool enabled)
    {
        _btnCloud.Enabled = enabled;
        _btnBot.Enabled = enabled;
        _btnBotFast.Enabled = enabled;
        _btnAll.Enabled = enabled;
    }

    private async void RunScript(string script, string args)
    {
        await RunScriptAsync(script, args);
    }

    private Task RunScriptAsync(string script, string args)
    {
        var tcs = new TaskCompletionSource();
        var scriptPath = Path.Combine(_scriptsDir, script);

        if (!File.Exists(scriptPath))
        {
            AppendLog($"错误: 找不到 {scriptPath}");
            tcs.SetResult();
            return tcs.Task;
        }

        _running = true;
        SetButtonsEnabled(false);
        _logBox.Clear();
        AppendLog($"▶ 执行: {script} {args}");
        AppendLog(new string('─', 50));

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" {args}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
                BeginInvoke(() => AppendLog(e.Data));
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
                BeginInvoke(() => AppendLog($"[错误] {e.Data}"));
        };
        proc.Exited += (_, _) =>
        {
            BeginInvoke(() =>
            {
                var code = proc.ExitCode;
                AppendLog(new string('─', 50));
                AppendLog(code == 0 ? "✔ 完成！" : $"✘ 失败 (退出码: {code})");
                SetButtonsEnabled(true);
                _running = code == 0;
                proc.Dispose();
                tcs.SetResult();
            });
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        return tcs.Task;
    }

    private void AppendLog(string text)
    {
        _logBox.AppendText(text + Environment.NewLine);
    }
}
