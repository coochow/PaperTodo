using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PaperTodo;

// === Custom patch (dev branch): About settings page ===
// Version info + documentation links (custom build guide + upstream resources).
// Reserved as the future home for update checks / OTA once that becomes useful.
public sealed partial class AppController
{
    private const string CustomBuildDocsUrl =
        "https://github.com/coochow/PaperTodo/blob/dev/CUSTOM_FEATURES.md";

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
                "개인 커스텀 빌드(dev 패치 브랜치)"),
            Foreground = TrayWeakTextBrush,
            FontSize = AppTypography.Scale(11.5),
            Margin = new Thickness(0, 2, 0, 0)
        });
        content.Children.Add(versionRow);

        content.Children.Add(SettingsFieldLabel(
            SettingsSidebarLocalized("文档", "Docs", "ドキュメント", "문서"),
            topMargin: 4));

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
