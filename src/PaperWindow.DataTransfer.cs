using System;
using System.Windows;
using Microsoft.Win32;

namespace PaperTodo;

// Custom patch (dev branch): right-click export of one paper as a shareable Markdown
// file. Notes keep their body and copy referenced images into a sibling .assets
// directory; todos become a GFM task list.
public sealed partial class PaperWindow
{
    internal void ExportPaperAsMarkdown()
    {
        try
        {
            CommitPendingEditsForSave();
        }
        catch
        {
            // Export the last committed content if the editor refuses to commit.
        }

        var title = string.IsNullOrWhiteSpace(_paper.Title)
            ? PaperTitles.EffectiveTitle(_paper, 1)
            : _paper.Title;
        var dialog = new SaveFileDialog
        {
            Title = Strings.Get("DataExportPaperDialogTitle"),
            Filter = Strings.Get("DataMarkdownFileDialogFilter"),
            FileName = PaperTransferFormat.SanitizeFileName(title, "paper") + ".md",
            DefaultExt = ".md",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var summary = PaperMarkdownExportService.ExportPaperToFile(
                _paper,
                _controller.ImageStore,
                dialog.FileName);
            var message = Strings.Format("DataExportPaperSuccess", dialog.FileName);
            if (summary.Warnings.Count > 0)
            {
                message += "\r\n\r\n" + Strings.Format("DataExportWarnings", summary.Warnings.Count);
            }

            PaperNoticeDialog.Show(
                this,
                Strings.Get("DataExportSuccessTitle"),
                message,
                forceTopmost: true);
        }
        catch (Exception ex)
        {
            PaperNoticeDialog.Show(
                this,
                Strings.Get("DataTransferFailureTitle"),
                Strings.Format("DataExportFailureMessage", ex.Message),
                forceTopmost: true);
        }
    }
}