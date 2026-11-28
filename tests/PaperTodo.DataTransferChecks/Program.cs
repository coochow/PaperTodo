using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PaperTodo;

internal static class Program
{
    private static int _failures;

    [STAThread]
    private static int Main()
    {
        Check("Note metadata round-trips through the invisible comment", CheckNoteRoundtrip);
        Check("Todo checklist keeps order and done state", CheckTodoRoundtrip);
        Check("Plain Markdown imports as a note body", CheckPlainMarkdown);
        Check("Malformed PaperTodo metadata is rejected", CheckMalformedMetadata);
        Check("File names survive Windows-invalid characters", CheckFileNames);
        Check("ZIP export and import round-trip both paper types", CheckZipRoundtrip);
        Check("Single-paper export round-trips", CheckSingleExport);
        Check("Note images export and re-import into a fresh store", CheckImageRoundtrip);
        Check("Loose Markdown file falls back to its file name", CheckLooseMarkdownFile);
        Check("Completed todos land at the top of the completed group", CheckCompletedTodoOrdering);
        Check("Imported todo bodies follow the display policy", CheckImportedTodoOrdering);
        Console.WriteLine(_failures == 0
            ? "All data transfer checks passed."
            : $"{_failures} check(s) failed.");
        return _failures == 0 ? 0 : 1;
    }

    private static void CheckNoteRoundtrip()
    {
        var paper = new PaperData
        {
            Type = PaperTypes.Note,
            Title = "周会 --> 纪要",
            Content = "## A\r\nbody\r\n"
        };
        var text = PaperTransferFormat.ComposePaperFile(paper, paper.Content);
        var kind = PaperTransferFormat.ParsePaperFile(text, out var metadata, out var body);
        Require(kind == PaperTransferFileKind.PaperTodo, "metadata comment not recognized");
        Require(metadata != null, "metadata missing");
        Equal("周会 --> 纪要", metadata!.Title, "title");
        Equal(paper.Content, body, "body");
        Require(metadata.Type == PaperTypes.Note, "type");
    }

    private static void CheckTodoRoundtrip()
    {
        var paper = new PaperData { Type = PaperTypes.Todo, Title = "本周" };
        paper.Items.Add(new PaperItem { Text = "B", Done = true, Order = 2 });
        paper.Items.Add(new PaperItem { Text = "A", Done = false, Order = 0 });
        paper.Items.Add(new PaperItem { Text = "C", Done = false, Order = 1 });
        var body = PaperTransferFormat.FormatTodoBody(paper);
        Equal("- [ ] A\r\n- [ ] C\r\n- [x] B", body, "todo body");

        var items = PaperTransferFormat.ParseTodoBody(body);
        Equal(3, items.Count, "item count");
        Equal("A", items[0].Text, "first text");
        Require(!items[0].Done, "first done");
        Equal("B", items[2].Text, "last text");
        Require(items[2].Done, "last done");
    }

    private static void CheckPlainMarkdown()
    {
        var kind = PaperTransferFormat.ParsePaperFile("# hello\r\nworld", out var metadata, out var body);
        Require(kind == PaperTransferFileKind.None, "plain markdown misidentified");
        Require(metadata == null, "plain markdown gained metadata");
        Equal("# hello\r\nworld", body, "body");
    }

    private static void CheckMalformedMetadata()
    {
        var kind = PaperTransferFormat.ParsePaperFile(
            "<!-- papertodo: not-json -->\r\nbody",
            out _,
            out _);
        Require(kind == PaperTransferFileKind.Malformed, "malformed metadata not rejected");
    }

    private static void CheckFileNames()
    {
        var name = PaperTransferFormat.SanitizeFileName("a/b:c*?\"d", "fallback");
        Require(name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0, "invalid character kept");
        Equal("_CON", PaperTransferFormat.SanitizeFileName("CON", "fallback"), "reserved name");
        var link = PaperTransferFormat.SafeLinkComponent("我的 标题 (1)", "paper");
        Require(!link.Contains(' ') && !link.Contains('(') && !link.Contains(')'), "unsafe link component");
    }

    private static void CheckZipRoundtrip()
    {
        var temp = Path.Combine(Path.GetTempPath(), "PaperTodoCheck-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var zipPath = Path.Combine(temp, "backup.zip");
            var note = new PaperData
            {
                Type = PaperTypes.Note,
                Title = "Note 1",
                Content = "hello\r\nworld"
            };
            var todo = new PaperData { Type = PaperTypes.Todo, Title = "Todo 1" };
            todo.Items.Add(new PaperItem { Text = "one", Done = true, Order = 0 });
            todo.Items.Add(new PaperItem { Text = "two", Done = false, Order = 1 });
            using var store = new NoteImageStore();
            var summary = PaperMarkdownExportService.ExportAllToZip(
                new List<PaperData> { note, todo },
                store,
                zipPath,
                "test");
            Equal(1, summary.Notes, "exported notes");
            Equal(1, summary.Todos, "exported todos");
            Equal(0, summary.Warnings.Count, "export warnings");

            using var batch = PaperImportBatch.Read(new[] { zipPath });
            Equal(2, batch.Sources.Count, "imported sources");
            var noteSource = batch.Sources.Single(source => source.Type == PaperTypes.Note);
            Equal("Note 1", noteSource.Title, "note title");
            Equal("hello\r\nworld", noteSource.Content, "note content");

            var todoSource = batch.Sources.Single(source => source.Type == PaperTypes.Todo);
            Equal("Todo 1", todoSource.Title, "todo title");
            Equal(2, todoSource.TodoItems.Count, "todo items");
            Equal("one", todoSource.TodoItems[0].Text, "todo first");
            Require(todoSource.TodoItems[0].Done, "todo first done");
        }
        finally
        {
            try
            {
                Directory.Delete(temp, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static void CheckSingleExport()
    {
        var temp = Path.Combine(Path.GetTempPath(), "PaperTodoCheck-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var paper = new PaperData
            {
                Type = PaperTypes.Note,
                Title = "分享",
                Content = "body"
            };
            using var store = new NoteImageStore();
            var path = Path.Combine(temp, "share.md");
            PaperMarkdownExportService.ExportPaperToFile(paper, store, path);
            var kind = PaperTransferFormat.ParsePaperFile(File.ReadAllText(path), out var metadata, out var body);
            Require(kind == PaperTransferFileKind.PaperTodo, "single export metadata");
            Equal("分享", metadata!.Title, "single export title");
            Equal("body", body, "single export body");

            using var batch = PaperImportBatch.Read(new[] { path });
            Equal(1, batch.Sources.Count, "single file source");
            Equal("分享", batch.Sources[0].Title, "single file title");
            Equal("body", batch.Sources[0].Content, "single file content");
        }
        finally
        {
            try
            {
                Directory.Delete(temp, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static void CheckImageRoundtrip()
    {
        var temp = Path.Combine(Path.GetTempPath(), "PaperTodoCheck-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var sourceStorePath = Path.Combine(temp, "source.lmdb");
            const string noteId = "note-1";
            string internalReference;
            using (var sourceStore = new NoteImageStore(sourceStorePath))
            {
                sourceStore.Load();
                var bitmap = BitmapSource.Create(
                    2,
                    2,
                    96,
                    96,
                    PixelFormats.Bgra32,
                    null,
                    new byte[16],
                    8);
                var asset = sourceStore.ImportBitmapSource(noteId, bitmap);
                internalReference = MarkdownImageReferences.CreateReference(asset.Id);
            }

            var note = new PaperData
            {
                Id = noteId,
                Type = PaperTypes.Note,
                Title = "图",
                Content = "before\r\n" + internalReference
            };
            var zipPath = Path.Combine(temp, "image.zip");
            using (var exportStore = new NoteImageStore(sourceStorePath))
            {
                exportStore.Load();
                var summary = PaperMarkdownExportService.ExportAllToZip(
                    new List<PaperData> { note },
                    exportStore,
                    zipPath,
                    "test");
                Equal(0, summary.Warnings.Count, "image export warnings");
            }

            using var batch = PaperImportBatch.Read(new[] { zipPath });
            var source = batch.Sources.Single(item => item.Type == PaperTypes.Note);
            Require(source.Content.Contains(".assets/", StringComparison.Ordinal), "relative image link");

            var targetStorePath = Path.Combine(temp, "target.lmdb");
            using (var targetStore = new NoteImageStore(targetStorePath))
            {
                targetStore.Load();
                var warnings = new List<string>();
                var imported = PaperMarkdownImageImporter.ImportForNote(
                    targetStore,
                    "note-2",
                    source.Content,
                    source.MarkdownDirectory,
                    warnings);
                Equal(0, warnings.Count, "image import warnings");
                var imageIds = MarkdownImageReferences
                    .Enumerate(imported)
                    .Select(reference => reference.ImageId)
                    .ToList();
                Equal(1, imageIds.Count, "imported image references");
                Require(targetStore.TryGetAsset(imageIds[0], out _), "image blob stored");
            }
        }
        finally
        {
            try
            {
                Directory.Delete(temp, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static void CheckLooseMarkdownFile()
    {
        var temp = Path.Combine(Path.GetTempPath(), "PaperTodoCheck-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var path = Path.Combine(temp, "朋友分享.md");
            File.WriteAllText(path, "# hello\r\n正文");
            using var batch = PaperImportBatch.Read(new[] { path });
            Equal(1, batch.Sources.Count, "loose file source");
            Equal("朋友分享", batch.Sources[0].Title, "loose file title");
            Equal("# hello\r\n正文", batch.Sources[0].Content, "loose file content");
        }
        finally
        {
            try
            {
                Directory.Delete(temp, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static void CheckCompletedTodoOrdering()
    {
        var activeOne = new PaperItem { Text = "active-1", Order = 0 };
        var activeTwo = new PaperItem { Text = "active-2", Order = 1 };
        var doneOld = new PaperItem { Text = "done-old", Done = true, Order = 2 };
        var doneOlder = new PaperItem { Text = "done-older", Done = true, Order = 3 };
        var items = new List<PaperItem> { activeOne, activeTwo, doneOld, doneOlder };

        activeOne.Done = true;
        Require(
            TodoRules.ApplyDoneTransitionOrdering(
                items,
                new[] { activeOne.Id },
                done: true,
                enabled: true),
            "completion was not applied");
        Equal(
            "active-2|active-1|done-old|done-older",
            string.Join("|", items.OrderBy(item => item.Order).Select(item => item.Text)),
            "newly completed item must top the completed group");

        doneOld.Done = false;
        Require(
            TodoRules.ApplyDoneTransitionOrdering(
                items,
                new[] { doneOld.Id },
                done: false,
                enabled: true),
            "restore was not applied");
        Equal(
            "active-2|done-old|active-1|done-older",
            string.Join("|", items.OrderBy(item => item.Order).Select(item => item.Text)),
            "restored item must return to the end of the active group");

        var mixed = new List<PaperItem>
        {
            new() { Text = "a", Order = 0 },
            new() { Text = "done-1", Done = true, Order = 1 },
            new() { Text = "b", Order = 2 },
            new() { Text = "done-2", Done = true, Order = 3 }
        };
        Require(
            TodoRules.ApplyCompletedOrdering(mixed, enabled: true),
            "existing completed items were not moved");
        Equal(
            "a|b|done-1|done-2",
            string.Join("|", mixed.OrderBy(item => item.Order).Select(item => item.Text)),
            "turning the policy on must put completed items last");
    }

    private static void CheckImportedTodoOrdering()
    {
        var parsed = PaperTransferFormat.ParseTodoBody(
            "- [x] done-1\r\n- [ ] active-1\r\n- [x] done-2\r\n- [ ] active-2");
        var paper = new PaperData { Type = PaperTypes.Todo };
        var order = 0;
        foreach (var item in parsed)
        {
            paper.Items.Add(new PaperItem
            {
                Text = item.Text,
                Done = item.Done,
                Order = order++
            });
        }

        TodoRules.ApplyCompletedOrdering(paper.Items, enabled: true);
        Equal(
            "active-1|active-2|done-1|done-2",
            string.Join("|", paper.Items.OrderBy(item => item.Order).Select(item => item.Text)),
            "imported todo display order");
    }

    private static void Check(string name, Action action)
    {
        try
        {
            action();
            Console.WriteLine("PASS " + name);
        }
        catch (Exception ex)
        {
            _failures++;
            Console.WriteLine("FAIL " + name + ": " + ex.Message);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{message}: expected {expected}, actual {actual}");
        }
    }
}