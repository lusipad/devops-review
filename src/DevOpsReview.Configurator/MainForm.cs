using System.Security.Principal;

namespace DevOpsReview.Configurator;

internal sealed class MainForm : Form
{
    private readonly ConfigurationService configuration = new();
    private readonly TextBox azureDevOpsUrl = new();
    private readonly TextBox localPath = new();
    private readonly Button testButton = new();
    private readonly Button saveButton = new();
    private readonly Button openExtensionButton = new();
    private readonly Label status = new();
    private TestResult? successfulTest;
    private CancellationTokenSource? testCancellation;

    public MainForm()
    {
        Text = "DevOps Review 配置";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 9F);
        ClientSize = new Size(760, 500);
        MinimumSize = new Size(720, 480);
        StartPosition = FormStartPosition.CenterScreen;

        var title = new Label
        {
            Text = "两步完成配置",
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 4),
        };
        var identity = new Label
        {
            Text = $"当前 Windows 身份：{WindowsIdentity.GetCurrent().Name}",
            AutoSize = true,
            ForeColor = Color.DimGray,
        };
        var intro = new Label
        {
            Text = "无需输入账号或密码。Azure DevOps 将使用当前 Windows 身份协商 Kerberos/NTLM。",
            AutoSize = true,
            ForeColor = Color.DimGray,
        };

        azureDevOpsUrl.Dock = DockStyle.Fill;
        azureDevOpsUrl.PlaceholderText =
            "例如：http://server:8080/tfs/DefaultCollection/Project/_git/Repository/pullrequest/42";
        azureDevOpsUrl.TextChanged += InputChanged;

        localPath.Dock = DockStyle.Fill;
        localPath.TextChanged += InputChanged;
        var browse = new Button { Text = "浏览…", AutoSize = true };
        browse.Click += BrowseClicked;

        testButton.Text = "测试连接";
        testButton.AutoSize = true;
        testButton.Click += TestClicked;
        saveButton.Text = "保存并完成";
        saveButton.AutoSize = true;
        saveButton.Enabled = false;
        saveButton.Click += SaveClicked;
        openExtensionButton.Text = "打开扩展目录";
        openExtensionButton.AutoSize = true;
        openExtensionButton.Click += OpenExtensionClicked;

        status.AutoSize = true;
        status.MaximumSize = new Size(650, 0);
        status.ForeColor = Color.DimGray;

        var pathRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 2,
        };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.Controls.Add(localPath, 0, 0);
        pathRow.Controls.Add(browse, 1, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
        };
        buttons.Controls.Add(testButton);
        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(openExtensionButton);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(28),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(title);
        layout.Controls.Add(identity);
        layout.Controls.Add(intro);
        layout.Controls.Add(CreateFieldLabel("Azure DevOps PR 或仓库地址"));
        layout.Controls.Add(azureDevOpsUrl);
        layout.Controls.Add(CreateHint("地址会自动解析服务器、Collection、项目和仓库。"));
        layout.Controls.Add(CreateFieldLabel("本地仓库目录"));
        layout.Controls.Add(pathRow);
        layout.Controls.Add(CreateHint("请选择已 clone 且 origin 对应上述仓库的根目录。"));
        layout.Controls.Add(buttons);
        layout.Controls.Add(status);
        Controls.Add(layout);

        Shown += FormShown;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            testCancellation?.Dispose();
        }

        base.Dispose(disposing);
    }

    private static Label CreateFieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
        Margin = new Padding(0, 18, 0, 5),
    };

    private static Label CreateHint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = Color.DimGray,
        Margin = new Padding(0, 4, 0, 0),
    };

    private async void FormShown(object? sender, EventArgs e)
    {
        try
        {
            var values = await configuration.LoadAsync(CancellationToken.None);
            if (values is not null)
            {
                azureDevOpsUrl.Text = values.AzureDevOpsUrl;
                localPath.Text = values.LocalPath;
                status.Text = $"已加载现有配置：{configuration.ConfigPath}";
            }
        }
        catch (Exception exception)
        {
            ShowError($"现有配置无法读取：{exception.Message}");
        }
    }

    private void BrowseClicked(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择本地 Git 仓库根目录",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(localPath.Text) ? localPath.Text : string.Empty,
            ShowNewFolderButton = false,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            localPath.Text = dialog.SelectedPath;
        }
    }

    private async void TestClicked(object? sender, EventArgs e)
    {
        SetBusy(true);
        status.ForeColor = Color.DimGray;
        status.Text = "正在检查本地 Git、Windows 身份、Azure DevOps 和 Codex…";
        testCancellation?.Cancel();
        testCancellation?.Dispose();
        testCancellation = new CancellationTokenSource();
        try
        {
            successfulTest = await ConfigurationService.TestAsync(
                azureDevOpsUrl.Text.Trim(),
                localPath.Text.Trim(),
                testCancellation.Token);
            saveButton.Enabled = true;
            status.ForeColor = Color.DarkGreen;
            status.Text =
                $"连接成功。Windows 身份有效，origin={successfulTest.RemoteUrl}，Codex 已登录。";
        }
        catch (Exception exception)
        {
            successfulTest = null;
            saveButton.Enabled = false;
            ShowError(exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void SaveClicked(object? sender, EventArgs e)
    {
        if (successfulTest is null)
        {
            return;
        }

        SetBusy(true);
        try
        {
            await configuration.SaveAsync(successfulTest, CancellationToken.None);
            status.ForeColor = Color.DarkGreen;
            status.Text = $"配置已保存：{configuration.ConfigPath}";
            MessageBox.Show(
                this,
                "配置已保存。现在请在 Chrome 或 Edge 的扩展管理页加载安装目录中的 extension 文件夹。",
                "DevOps Review",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            ShowError($"保存失败：{exception.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OpenExtensionClicked(object? sender, EventArgs e)
    {
        var extensionPath = Path.Combine(AppContext.BaseDirectory, "extension");
        if (!Directory.Exists(extensionPath))
        {
            extensionPath = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "extension"));
        }

        if (!Directory.Exists(extensionPath))
        {
            ShowError($"扩展目录不存在：{extensionPath}");
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = extensionPath,
            UseShellExecute = true,
        });
    }

    private void InputChanged(object? sender, EventArgs e)
    {
        successfulTest = null;
        saveButton.Enabled = false;
    }

    private void SetBusy(bool busy)
    {
        testButton.Enabled = !busy;
        azureDevOpsUrl.Enabled = !busy;
        localPath.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private void ShowError(string message)
    {
        status.ForeColor = Color.Firebrick;
        status.Text = message;
    }
}
