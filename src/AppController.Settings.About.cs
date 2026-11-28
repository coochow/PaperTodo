using System;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
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
        "https://github.com/coochow/PaperTodo/blob/dev/README.zh.md";
    // Custom patch (dev branch): internal mirror of the user manual. GitLab
    // downloaders are intranet users — one link opens the GitLab copy when the
    // internal mirror is reachable (same raw channel the updater already uses)
    // and falls back to the GitHub original otherwise, so a single binary serves
    // both audiences.
    private const string CustomBuildDocsInternalUrl =
        "https://gitlab.vmic.xyz/res/soft/common_tools/-/blob/master/PaperTodo/README.md";
    private const string CustomBuildDocsInternalProbeUrl =
        "https://gitlab.vmic.xyz/res/soft/common_tools/-/raw/master/PaperTodo/README.md";

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

        // Custom patch (dev branch): single manual link that resolves per
        // environment — GitLab mirror when the internal raw channel answers
        // (intranet, same channel the updater uses), GitHub original otherwise.
        content.Children.Add(BuildAboutLinkItem(
            SettingsSidebarLocalized(
                "使用手册（基础 + 定制功能）",
                "User manual (base + custom features)",
                "使用マニュアル（基本 + カスタム機能）",
                "사용 설명서(기본 + 커스텀 기능)"),
            CustomBuildDocsInternalUrl,
            fallbackUrl: CustomBuildDocsUrl,
            isPrimary: true));

        content.Children.Add(BuildAboutMaintenanceSection());

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

    // Custom patch (dev branch): one-click restore for every settings page.
    private UIElement BuildAboutMaintenanceSection()
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(0, 10, 0, 0)
        };
        panel.Children.Add(SettingsFieldLabel(
            SettingsSidebarLocalized("维护", "Maintenance", "メンテナンス", "유지 관리"),
            topMargin: 0));
        panel.Children.Add(new TextBlock
        {
            Text = SettingsSidebarLocalized(
                "一键恢复常规、待办、笔记、视觉、快捷键和实验室页的设置；纸片内容与插件状态不受影响。",
                "Restore General, Todo, Notes, Visual, Shortcuts and Labs settings at once. Paper content and plugin state are kept.",
                "一般、ToDo、ノート、表示、ショートカット、ラボの設定をまとめて既定値に戻します。付箋の内容とプラグインの状態は保持されます。",
                "일반, 할 일, 노트, 보기, 단축키, 실험실 설정을 한 번에 복원합니다. 종이 내용과 플러그인 상태는 유지됩니다."),
            Foreground = TrayWeakTextBrush,
            FontSize = AppTypography.Scale(11.5),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 4, 2, 6)
        });
        var restoreAll = SettingsTextButton(
            SettingsSidebarLocalized(
                "恢复所有默认设置…",
                "Restore all defaults…",
                "すべての既定値に戻す…",
                "모든 기본값 복원…"));
        restoreAll.HorizontalAlignment = HorizontalAlignment.Left;
        restoreAll.Click += (_, _) => ConfirmRestoreAllSettings();
        panel.Children.Add(restoreAll);
        return panel;
    }

    private void ConfirmRestoreAllSettings()
    {
        if (_settingsWindow is not { } owner)
        {
            return;
        }

        var accepted = PaperNoticeDialog.ShowChoice(
            owner,
            SettingsSidebarLocalized(
                "恢复所有默认设置",
                "Restore all defaults",
                "すべての既定値に戻す",
                "모든 기본값 복원"),
            SettingsSidebarLocalized(
                "将把常规、待办、笔记、视觉、快捷键和实验室页的设置恢复为默认值。纸片、待办和笔记内容不会删除，纸片位置不变；待办完成项会整理到底部；实验室页会关闭 MCP 与实验功能；插件启用状态和插件数据保持不变。",
                "This restores General, Todo, Notes, Visual, Shortcuts and Labs settings. Paper, todo and note content is kept, and paper positions do not change. Completed todos move below active ones, and Labs turns off MCP and experimental features. Plugin enablement and plugin data are kept.",
                "一般、ToDo、ノート、表示、ショートカット、ラボの設定を既定値に戻します。付箋・ToDo・ノートの内容と付箋の位置は保持されます。完了済み ToDo は未完了の下へ移動し、ラボでは MCP と実験機能がオフになります。プラグインの有効状態とデータは保持されます。",
                "일반, 할 일, 노트, 보기, 단축키, 실험실 설정을 기본값으로 복원합니다. 종이, 할 일, 노트 내용과 종이 위치는 유지됩니다. 완료된 할 일은 미완료 항목 아래로 이동하고, 실험실의 MCP와 실험 기능은 꺼집니다"),
            SettingsSidebarLocalized("取消", "Cancel", "キャンセル", "취소"),
            SettingsSidebarLocalized("恢复所有默认设置", "Restore all defaults", "すべての既定値に戻す", "모든 기본값 복원"),
            forceTopmost: true);
        if (!accepted)
        {
            return;
        }

        RestoreAllSettingsToDefaults();
        PaperNoticeDialog.Show(
            owner,
            SettingsSidebarLocalized("恢复完成", "Defaults restored", "既定値に戻しました", "복원 완료"),
            SettingsSidebarLocalized(
                "所有设置已恢复为默认值。",
                "All settings have been restored to their defaults.",
                "すべての設定を既定値に戻しました。",
                "모든 설정이 기본값으로 복원되었습니다."),
            forceTopmost: true);
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
        catch (Exception)
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
                "检查更新失败（内网 GitLab 与 GitHub 通道均不可达或被限流），请稍后重试或联系维护者。",
                "The update check failed (both the intranet GitLab and GitHub channels were unreachable or rate-limited). Retry later or contact the maintainer.",
                "アップデートの確認に失敗しました（社内 GitLab と GitHub の両チャネルに到達できないか、制限されています）。後でもう一度お試しください。",
                "업데이트 확인에 실패했습니다(사내 GitLab과 GitHub 채널 모두 접근 불가 또는 제한됨). 나중에 다시 시도하세요."));
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

        bool ok;
        try
        {
            ok = await Task.Run(() => UpdateService.TryUpdateToAsync(update, onProgress));
        }
        catch
        {
            // Custom patch (dev branch): an unexpected updater exception must not escape onto
            // the settings window; the service has already recorded the failing stage.
            ok = false;
        }
        _updateCheckRunning = false;
        if (!ok)
        {
            SetAboutUpdateStatus(SettingsSidebarLocalized(
                "升级失败，请稍后重试或从发布页手动下载。",
                "The update failed. Retry later or download manually from the release page.",
                "アップデートに失敗しました。後で再試行するか、リリースページから手動でダウンロードしてください。",
                "업데이트에 실패했습니다. 나중에 다시 시도하거나 릴리스 페이지에서 수동으로 다운로드하세요."));
            // Custom patch (dev branch): replace the generic failure line with stage-aware text.
            SetAboutUpdateStatus(BuildUpdateFailureStatus());
            button.IsEnabled = true;
        }
        else
        {
            // Custom patch (dev branch): the swap is done; tell the user before the restart
            // takes the window away.
            SetAboutUpdateStatus(SettingsSidebarLocalized(
                "升级完成，正在重启…",
                "Update applied. Restarting…",
                "アップデートを適用しました。再起動します…",
                "업데이트 적용 완료. 재시작 중…"));
        }
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

    // Custom patch (dev branch): compose the failure line from the updater's recorded stage,
// naming the phase in code terms (matches the trace log) so the next report is diagnosable.
    private string BuildUpdateFailureStatus()
    {
        var stage = UpdateService.LastFailureStage;
        if (stage == UpdateService.UpdateFailureStage.AlreadyCurrent)
        {
            return SettingsSidebarLocalized(
                "当前文件已是该版本内容，无需重复升级。若版本号仍显示旧值，请等待下一次发布或从发布页手动下载。",
                "The files already match this release; no re-install needed. If the version label still looks old, wait for the next release or download manually from the release page.",
                "ファイルは最新です（再適用は不要）。表示が古い場合は、次のリリースをお待ちください。",
                "파일이 이미 최신입니다(재설치 불필요). 표시가 그대로라면 다음 버전을 기다리세요.");
        }
        return string.Format(
            SettingsSidebarLocalized(
                "升级失败（{0}）。请稍后重试或从发布页手动下载。诊断日志：update-trace.log（本地应用数据目录）。",
                "The update failed ({0}). Retry later or download manually from the release page. Trace log: update-trace.log in the local app-data folder.",
                "アップデートに失敗しました（{0}）。後で再試行するか、リリースページから手動でダウンロードしてください。診断ログ：update-trace.log（ローカルアプリデータ）。",
                "업데이트 실패({0}). 나중에 다시 시도하거나 릴리스 페이지에서 직접 내려받으세요. 진단 로그: update-trace.log(로컬 앱 데이터)."),
        stage);
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

    private UIElement BuildAboutLinkItem(string label, string url, bool isPrimary = false,
        string? fallbackUrl = null)
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
        border.MouseLeftButtonUp += async (_, e) =>
        {
            // Custom patch (dev branch): when a fallback exists, the primary URL is
            // the internal mirror and the click resolves per environment — probe the
            // mirror's raw channel (the same one the updater already uses); a plain
            // 200 means the intranet can read it, anything else (timeout, SSO
            // redirect, error) falls back to the public original.
            if (fallbackUrl is null)
            {
                OpenUrl(url);
            }
            else if (await TryProbeUrlAsync(url))
            {
                OpenUrl(url);
            }
            else
            {
                OpenUrl(fallbackUrl);
            }
            e.Handled = true;
        };
        return border;
    }

    private static async Task<bool> TryProbeUrlAsync(string url)
    {
        // The manual's blob page requires an HTML session; the raw sibling of the
        // same path answers with a plain 200 to unauthenticated requests on the
        // intranet, which is exactly what the OTA channel relies on.
        var rawUrl = url.Replace("/-/blob/", "/-/raw/");
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var response = await UpdateService.ProbeRawUrlAsync(rawUrl, cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
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
