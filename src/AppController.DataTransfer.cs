using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace PaperTodo;

// Custom patch (dev branch): Markdown export/import for notes and todos.
public sealed partial class AppController
{
    private const int MaxPaperCount = 200;

    private UIElement BuildDataTransferSettingsSection()
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(0, 14, 0, 0)
        };
        panel.Children.Add(SettingsSectionLabel(Strings.Get("SettingsDataTransfer")));

        var hint = new TextBlock
        {
            Text = Strings.Get("TipSettingsDataTransfer"),
            Foreground = TrayWeakTextBrush,
            FontSize = AppTypography.Scale(11.5),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 2, 8, 6)
        };
        panel.Children.Add(hint);

        var exportButton = BuildDataTransferButton(Strings.Get("DataExportAll"));
        exportButton.Click += (_, _) => ExportAllPapersToMarkdownZip();
        panel.Children.Add(exportButton);

        var importButton = BuildDataTransferButton(Strings.Get("DataImport"));
        importButton.Click += (_, _) => ImportPapersFromMarkdown();
        panel.Children.Add(importButton);
        return panel;
    }

    private Button BuildDataTransferButton(string label)
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

    private void ExportAllPapersToMarkdownZip()
    {
        var owner = _settingsWindow;
        if (State.Papers.Count == 0)
        {
            PaperNoticeDialog.Show(
                owner!,
                Strings.Get("DataExportEmptyTitle"),
                Strings.Get("DataExportEmpty"),
                forceTopmost: true);
            return;
        }

        foreach (var window in _windows.Values.ToList())
        {
            try
            {
                window.CommitPendingEditsForSave();
            }
            catch
            {
                // One failing editor must not abort the whole export.
            }
        }

        var dialog = new SaveFileDialog
        {
            Title = Strings.Get("DataExportDialogTitle"),
            Filter = Strings.Get("DataZipFileDialogFilter"),
            FileName = "PaperTodo-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip",
            DefaultExt = ".zip",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(owner) != true)
        {
            return;
        }

        try
        {
            var summary = PaperMarkdownExportService.ExportAllToZip(
                State.Papers.ToList(),
                _imageStore,
                dialog.FileName,
                UpdateService.CurrentVersion);
            var message = Strings.Format(
                "DataExportSuccess",
                summary.Notes + summary.Todos,
                summary.Notes,
                summary.Todos,
                dialog.FileName);
            if (summary.Warnings.Count > 0)
            {
                message += "\r\n\r\n" + Strings.Format("DataExportWarnings", summary.Warnings.Count);
            }
            PaperNoticeDialog.Show(
                owner!,
                Strings.Get("DataExportSuccessTitle"),
                message,
                forceTopmost: true);
        }
        catch (Exception ex)
        {
            PaperNoticeDialog.Show(
                owner!,
                Strings.Get("DataTransferFailureTitle"),
                Strings.Format("DataExportFailureMessage", ex.Message),
                forceTopmost: true);
        }
    }

    private void ImportPapersFromMarkdown()
    {
        var owner = _settingsWindow;
        var dialog = new OpenFileDialog
        {
            Title = Strings.Get("DataImportDialogTitle"),
            Filter = Strings.Get("DataImportFileDialogFilter"),
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(owner) != true)
        {
            return;
        }

        try
        {
            using var batch = PaperImportBatch.Read(dialog.FileNames);
            if (batch.Sources.Count == 0)
            {
                ShowImportNotice(owner, 0, 0, 0, batch.Failures, Array.Empty<string>());
                return;
            }

            var available = Math.Max(0, MaxPaperCount - State.Papers.Count);
            if (available == 0)
            {
                PaperNoticeDialog.Show(
                    owner!,
                    Strings.Get("DataImportLimitTitle"),
                    Strings.Get("DataImportLimitMessage"),
                    forceTopmost: true);
                return;
            }

            var sources = batch.Sources;
            var skipped = 0;
            if (sources.Count > available)
            {
                var proceed = PaperNoticeDialog.ShowChoice(
                    owner!,
                    Strings.Get("DataImportLimitTitle"),
                    Strings.Format("DataImportLimitChoice", available, sources.Count),
                    Strings.Get("DataCommonCancel"),
                    Strings.Format("DataImportTakeAvailable", available),
                    forceTopmost: true);
                if (!proceed)
                {
                    return;
                }

                skipped = sources.Count - available;
                sources = sources.Take(available).ToList();
            }

            var failures = new List<string>(batch.Failures);
            var imageWarnings = new List<string>();
            var notes = 0;
            var todos = 0;
            foreach (var source in sources)
            {
                PaperData? paper = null;
                try
                {
                    paper = CreatePaper(source.Type, show: false);
                    if (paper == null)
                    {
                        failures.Add(source.SourceLabel);
                        break;
                    }

                    var title = PaperTitles.CleanCustomTitle(source.Title, State.MaxTitleLength);
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        paper.Title = title;
                    }

                    if (source.Type == PaperTypes.Todo)
                    {
                        paper.Items.Clear();
                        var order = 0;
                        foreach (var item in source.TodoItems)
                        {
                            paper.Items.Add(new PaperItem
                            {
                                Text = item.Text,
                                Done = item.Done,
                                Order = order++
                            });
                        }

                        if (paper.Items.Count == 0)
                        {
                            paper.Items.Add(new PaperItem
                            {
                                Text = FallbackTodoText(source.Content),
                                Done = false,
                                Order = 0
                            });
                        }

                        // Custom patch (dev branch): imported todos follow the same
                        // display policy — active items first, completed items after,
                        // preserving the file's completed-group order (newest first
                        // for files exported by this build).
                        TodoRules.ApplyCompletedOrdering(
                            paper.Items,
                            State.AutoMoveCompletedTodosToBottom);
                    }
                    else
                    {
                        paper.Content = PaperMarkdownImageImporter.ImportForNote(
                            _imageStore,
                            paper.Id,
                            source.Content ?? "",
                            source.MarkdownDirectory,
                            imageWarnings);
                    }

                    // Custom patch (dev branch): bulk import must not flood the desktop with
                    // expanded windows; imported papers start as capsules and expand on click.
                    paper.IsCollapsed = true;

                    if (!TryCommitExternalMutation())
                    {
                        RollbackExternalCreatedPaper(paper);
                        paper = null;
                        failures.Add(source.SourceLabel + ": " + Strings.Get("DataImportSaveFailed"));
                        continue;
                    }

                    RunExternalPostCommitUi(() => FinalizeExternalPaperCreated(paper, show: true));
                    if (source.Type == PaperTypes.Todo)
                    {
                        todos++;
                    }
                    else
                    {
                        notes++;
                    }
                }
                catch (Exception ex)
                {
                    if (paper != null)
                    {
                        RollbackExternalCreatedPaper(paper);
                    }

                    failures.Add(source.SourceLabel + ": " + ex.Message);
                }
            }

            ShowImportNotice(owner, notes, todos, skipped, failures, imageWarnings);
        }
        catch (Exception ex)
        {
            PaperNoticeDialog.Show(
                owner!,
                Strings.Get("DataTransferFailureTitle"),
                Strings.Format("DataImportFailureMessage", ex.Message),
                forceTopmost: true);
        }
    }

    private static void ShowImportNotice(
        Window? owner,
        int notes,
        int todos,
        int skipped,
        IReadOnlyList<string> failures,
        IReadOnlyList<string> imageWarnings)
    {
        var imported = notes + todos;
        var message = imported > 0
            ? Strings.Format("DataImportSuccess", imported, notes, todos)
            : Strings.Get("DataImportNone");
        if (skipped > 0)
        {
            message += "\r\n\r\n" + Strings.Format("DataImportSkipped", skipped);
        }

        if (failures.Count > 0)
        {
            message += "\r\n\r\n" + Strings.Format(
                "DataImportFailures",
                failures.Count,
                DescribeFailures(failures));
        }

        if (imageWarnings.Count > 0)
        {
            message += "\r\n\r\n" + DescribeFailures(imageWarnings);
        }

        PaperNoticeDialog.Show(
            owner!,
            Strings.Get(imported > 0 ? "DataImportSuccessTitle" : "DataTransferFailureTitle"),
            message,
            forceTopmost: true);
    }

    private static string DescribeFailures(IReadOnlyList<string> failures)
    {
        var shown = string.Join("\r\n", failures.Take(5));
        return failures.Count > 5 ? shown + "\r\n…" : shown;
    }

    private static string FallbackTodoText(string? content)
    {
        var text = (content ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length > 2000 ? text[..2000] : text;
    }
}