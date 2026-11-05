using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PaperTodo;

// === Custom patch (dev branch): About settings page ===
// Version info, documentation links (custom build guide + upstream resources) and
// the in-app update check / OTA entry point backed by UpdateService.
public sealed partial class AppController
{
    private const string CustomBuildDocsUrl =
        "https://github.com/coochow/PaperTodo/blob/dev/CUSTOM_FEATURES.md";

    private UpdateService.UpdateInfo? _pendingUpdate;
    private bool _updateCheckRunning;
    private string _updateCheckStatus = "";
    private TextBlock? _aboutUpdateStatusText;
    private UIElement? _aboutUpdateActionPanel;

    private UIElement BuildAboutSettingsPage()
    {
        var content = new StackPanel
        {
            Margin = new Thickness(0, 4, 0, 0)
        };

        content.Children.Add(SettingsFieldLabel(
            SettingsSidebarLocalized("关于", "About", "情報", "정보")));

        var versionRow = new StackPanel
        {
            Margin = new Thickness(0, 6, 0, 10)
        };
        versionRow.Children.Add(new TextBlock
        {
            Text = AppDisplayName,
            Foreground = TrayTextBrush,
            FontSize = AppTypography.Scale(15),
            FontWeight = FontWeights.SemiBold
        });
        versionRow.Children.Add(new TextBlock
        {
            Text = SettingsSidebarLocalized(
                "个人定制构建（dev 补丁分支）",
                "Personal custom build (dev patch branch)",
                "個人カスタムビルド（dev パッチブランチ）",
                "개인 커스텀 빌드(dev 패치브랜치)"),
            Foreground = TrayWeakTextBrush,
            FontSize = AppTypography.Scale(11.5),
            Margin = new Thickness(0, 2, 0, 0)
        });
        content.Children.Add(versionRow);

        // Custom patch (dev branch): update check / OTA lives here, not in a live region,
        // because the page rebuilds on every navigation anyway.
        content.Children.Add(SettingsFieldLabel(
            SettingsSidebarLocalized("更新", "Updates", "アップデート", "업데이트"),
            topMargin: 4));
        content.Children.Add(BuildAboutUpdateSection());
        content.Children.Add(SettingsToggle(
            SettingsSidebarLocalized(
                "启动时自动检查更新",
                "Check for updates at startup",
                "起動時にアップデートを確認",
                "시작 시 업데이트 확인"),
            State.UpdateCheckEnabled,
            () => SetSettingFromUi("updates.auto_check_enabled", !State.UpdateCheckEnabled)));

        content.Children.Add(SettingsFieldLabel(
            SettingsSidebarLocalized("文档", "Docs", "ドキュメント", "문서"),
            topMargin: 10));

        content.Children.Add(BuildAboutLinkItem(
            SettingsSidebarLocalized(
                "使用手册（基础 + 定制功能）",
                "User manual (base + custom features)",
                "使用マニュアル（基本 + カスタム機能）",
                "사용 설명서(기본 + 커스텀 기능)"),
            CustomBuildDocsUrl,
            isPrimary: true));

        return new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(0, 3, 0, 5),
            Margin = new Thickness(0, 1, 0, 3),
            Child = content
        };
    }

    private UIElement BuildAboutUpdateSection()
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(2, 2, 2, 1)
        };

        _aboutUpdateStatusText = new TextBlock
        {
            Foreground = TrayWeakTextBrush,
            FontSize = AppTypography.Scale(12),
            Text = _updateCheckStatus,
            TextWrapping = TextWrapping.Wrap
        };
        panel.Children.Add(_aboutUpdateStatusText);

        _aboutUpdateActionPanel = BuildAboutUpdateActionPanel();
        panel.Children.Add(_aboutUpdateActionPanel);

        return panel;
    }

    // Custom patch (dev branch): opens settings directly on the About page; used by the
    // update-available tray balloon click.
    internal void OpenAboutSettingsPage()
    {
        ShowSettingsWindow(SettingsPage.About);
    }

    private UIElement BuildAboutUpdateActionPanel()
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(0, 6, 0, 0)
        };

        if (_pendingUpdate is { } update)
        {
            var notes = new TextBlock
            {
                Foreground = TrayWeakTextBrush,
                FontSize = AppTypography.Scale(11.5),
                Text = TrimReleaseNotes(update.ReleaseNotes),
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(new ScrollViewer
            {
                Content = notes,
                MaxHeight = 160,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(0, 0, 0, 6)
            });

            var releaseLink = BuildAboutLinkItem(
                SettingsSidebarLocalized(
                    "查看发布页（手动下载）",
                    "Open release page (manual download)",
                    "リリースページを開く（手動ダウンロード）",
                    "릴리스 페이지 열기(수동 다운로드)"),
                update.ReleaseUrl);
            panel.Children.Add(releaseLink);

            var applyButton = BuildAboutActionButton(
                SettingsSidebarLocalized(
                    $"升级到 v{update.Version}",
                    $"Update to v{update.Version}",
                    $"v{update.Version} に更新",
                    $"v{update.Version}(으)로 업데이트"));
            applyButton.Click += async (_, _) => await OnApplyUpdateClick(applyButton);
            panel.Children.Add(applyButton);
        }
        else
        {
            var checkButton = BuildAboutActionButton(
                SettingsSidebarLocalized("检查更新", "Check for updates", "アップデートを確認", "업데이트 확인"));
            checkButton.Click += async (_, _) => await OnCheckUpdateClick(checkButton);
            checkButton.IsEnabled = !_updateCheckRunning;
            panel.Children.Add(checkButton);
        }

        return panel;
    }

    private Button BuildAboutActionButton(string label)
    {
        return new Button
        {
            Content = new TextBlock
            {
                Text = label,
                FontSize = AppTypography.Scale(12),
                Foreground = TrayTextBrush
            },
            Height = 30,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(2, 1, 2, 1),
            Padding = new Thickness(10, 0, 10, 0),
            Cursor = Cursors.Hand,
            Background = Theme.Tint((byte)(Theme.IsDark ? 34 : 18)),
            BorderThickness = new Thickness(1),
            BorderBrush = Theme.PaperBorderBrush,
            Focusable = false
        };
    }

    private async Task OnCheckUpdateClick(Button button)
    {
        if (_updateCheckRunning)
        {
            return;
        }
        _updateCheckRunning = true;
        button.IsEnabled = false;
        SetAboutUpdateStatus(SettingsSidebarLocalized(
            "正在检查更新…", "Checking for updates…", "アップデートを確認中…", "업데이트 확인 중…"));
        bool checkFailed = false;
        try
        {
            _pendingUpdate = await Task.Run(UpdateService.CheckForUpdateAsync);
        }
        catch
        {
            _pendingUpdate = null;
            checkFailed = true;
        }
        _updateCheckRunning = false;

        if (_pendingUpdate is { } found)
        {
            SetAboutUpdateStatus(string.Format(
                SettingsSidebarLocalized(
                    "发现新版本 v{0}",
                    "New version v{0} available",
                    "新バージョン v{0} があります",
                    "새 버전 v{0} 사용 가능"),
                found.Version));
        }
        else if (checkFailed)
        {
            SetAboutUpdateStatus(SettingsSidebarLocalized(
                "检查更新失败，请稍后重试。",
                "The update check failed. Please try again later.",
                "アップデートの確認に失敗しました。後でもう一度お試しください。",
                "업데이트 확인에 실패했습니다. 나중에 다시 시도하세요."));
        }
        else
        {
            SetAboutUpdateStatus(SettingsSidebarLocalized(
                "已是最新版本。", "You are on the latest version.", "最新バージョンです。", "최신 버전입니다."));
        }
        RefreshAboutUpdateActionPanel();
    }

    private async Task OnApplyUpdateClick(Button button)
    {
        if (_pendingUpdate is not { } update || _updateCheckRunning)
        {
            return;
        }
        _updateCheckRunning = true;
        button.IsEnabled = false;
        SetAboutUpdateStatus(SettingsSidebarLocalized(
            "正在下载更新…", "Downloading update…", "アップデートをダウンロード中…", "업데이트 다운로드 중…"));

        var dispatcher = Application.Current.Dispatcher;
        Action<UpdateService.UpdateProgress>? onProgress = p =>
        {
            dispatcher.BeginInvoke(() =>
            {
                var percent = p.TotalBytes > 0 ? (double)p.CompletedBytes / p.TotalBytes * 100 : 0;
                SetAboutUpdateStatus(string.Format(
                    SettingsSidebarLocalized(
                        "正在下载更新（{0}MB / {1}MB，{2:0}%）",
                        "Downloading update ({0} MB / {1} MB, {2:0}%)",
                        "アップデートをダウンロード中（{0}MB / {1}MB、{2:0}%）",
                        "업데이트 다운로드 중({0}MB / {1}MB, {2:0}%)"),
                    p.CompletedBytes / 1024 / 1024,
                    p.TotalBytes > 0 ? p.TotalBytes / 1024 / 1024 : 0,
                    percent));
            });
        };

        var ok = await Task.Run(() => UpdateService.TryUpdateToAsync(update, onProgress));
        _updateCheckRunning = false;
        if (!ok)
        {
            SetAboutUpdateStatus(SettingsSidebarLocalized(
                "升级失败，请稍后重试或从发布页手动下载。",
                "The update failed. Retry later or download manually from the release page.",
                "アップデートに失敗しました。後で再試行するか、リリースページから手動でダウンロードしてください。",
                "업데이트에 실패했습니다. 나중에 다시 시도하거나 릴리스 페이지에서 수동으로 다운로드하세요."));
            button.IsEnabled = true;
        }
        // On success the app restarts itself; nothing further to update here.
    }

    private void SetAboutUpdateStatus(string text)
    {
        _updateCheckStatus = text;
        if (_aboutUpdateStatusText is { } status)
        {
            status.Text = text;
        }
    }

    private void RefreshAboutUpdateActionPanel()
    {
        if (_aboutUpdateActionPanel is not { } host ||
            LogicalTreeHelper.GetParent(host) is not Panel parent)
        {
            return;
        }
        var index = parent.Children.IndexOf(host);
        if (index < 0)
        {
            return;
        }
        parent.Children.RemoveAt(index);
        _aboutUpdateActionPanel = BuildAboutUpdateActionPanel();
        parent.Children.Insert(index, _aboutUpdateActionPanel);
    }

    private static string TrimReleaseNotes(string notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return "";
        }
        var collapsed = notes.Replace("\r\n", "\n").Trim();
        return collapsed.Length <= 600 ? collapsed : collapsed[..600] + "…";
    }

    private UIElement BuildAboutLinkItem(string label, string url, bool isPrimary = false)
    {
        var text = new TextBlock
        {
            Text = label,
            Foreground = isPrimary ? TrayTextBrush : TrayWeakTextBrush,
            FontSize = AppTypography.Scale(12),
            FontWeight = isPrimary ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var border = new Border
        {
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Height = 30,
            Margin = new Thickness(2, 1, 2, 1),
            Padding = new Thickness(6, 0, 0, 0),
            ToolTip = url,
            Child = text
        };
        ToolTipService.SetInitialShowDelay(border, 200);
        ToolTipService.SetShowDuration(border, 12000);
        border.MouseEnter += (_, _) => text.Foreground = Theme.LinkBrush;
        border.MouseLeave += (_, _) => text.Foreground = isPrimary ? TrayTextBrush : TrayWeakTextBrush;
        border.MouseLeftButtonUp += (_, e) =>
        {
            OpenUrl(url);
            e.Handled = true;
        };
        return border;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Shell association missing or blocked; nothing actionable to surface.
        }
    }
}
