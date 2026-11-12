using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace PaperTodo;

// === Custom patch bundle (dev branch) ===
// All user-requested customizations live in this partial class so upstream merges
// stay mechanical. Upstream files only carry short, anchored hook lines.
public sealed partial class PaperWindow
{
    // ---------- Preview-mode plain-left drag ----------
    // A plain left press on a read-only note body either drags the window (press +
    // move > threshold hands off to the native caption drag) or enters the editor
    // (still release calls the original caret-placement path). The gesture is fully
    // self-contained: three temporary handlers on the editor, removed on the first
    // terminal event. The preview-press interception lives inside Note.cs's
    // noteMouseDown (it runs with handledEventsToo and would otherwise steal the
    // press before any shell-level handler).

    internal bool ShouldPreviewPressStartWindowDrag(MarkdownTextBox box, MouseButtonEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0 ||
            _paper.IsCollapsed ||
            !_paper.IsVisible)
        {
            return false;
        }

        // Links keep their open behavior: a press on a link never drags.
        var textViewPoint = e.GetPosition(box.TextArea.TextView);
        return !box.TryGetOpenableLinkFromTextViewPoint(textViewPoint, out _);
    }

    // Self-contained preview-press gesture: arm on press, hand off to the native
    // caption drag when movement crosses the threshold, and on a still release fall
    // back to the caller-provided enter-editor action (caret placement at the press
    // point). All handlers are attached to the editor itself and removed when the
    // gesture ends, so no capture or session ever outlives the press.
    private const double PreviewDragThreshold = 6.0;

    internal void BeginPreviewPressDragOrClick(
        MarkdownTextBox box,
        MouseButtonEventArgs e,
        Action enterEditorAction)
    {
        var start = e.GetPosition(this);
        var ended = false;
        MouseButtonEventHandler? upHandler = null;
        MouseEventHandler? moveHandler = null;
        MouseEventHandler? captureLostHandler = null;

        void End(bool dragged)
        {
            if (ended)
            {
                return;
            }

            ended = true;
            box.PreviewMouseLeftButtonUp -= upHandler;
            box.PreviewMouseMove -= moveHandler;
            box.LostMouseCapture -= captureLostHandler;
            if (ReferenceEquals(Mouse.Captured, box))
            {
                box.ReleaseMouseCapture();
            }

            if (!dragged)
            {
                enterEditorAction();
            }
        }

        upHandler = (_, up) =>
        {
            End(dragged: false);
            up.Handled = true;
        };
        moveHandler = (_, move) =>
        {
            if (move.LeftButton != MouseButtonState.Pressed)
            {
                End(dragged: true);
                return;
            }

            var p = move.GetPosition(this);
            if (Math.Abs(p.X - start.X) < PreviewDragThreshold &&
                Math.Abs(p.Y - start.Y) < PreviewDragThreshold)
            {
                return;
            }

            End(dragged: true);
            DetachExperimentalAttachmentBeforeUserDrag();
            WindowNative.BeginWindowCaptionDrag(this);
            move.Handled = true;
        };
        captureLostHandler = (_, _) => End(dragged: true);

        box.PreviewMouseLeftButtonUp += upHandler;
        box.PreviewMouseMove += moveHandler;
        box.LostMouseCapture += captureLostHandler;
        box.CaptureMouse();
    }

    // ---------- Todo blank-area window drag (v3) ----------
    // Notes drag the window from their read-only preview body. Todo papers have no
    // preview state, but the todo BODY ScrollViewer owns a real blank zone: the
    // viewport area below/around the _todoPanel (few items). Rows, checkboxes and the
    // "+" append bar all live INSIDE _todoPanel and are excluded structurally — the
    // gesture mounts on the ScrollViewer itself, so the top bar, capsule and every
    // other window surface are outside its blast radius by construction.
    internal bool ShouldTodoBlankPressStartWindowDrag(DependencyObject? source)
    {
        if (_paper.IsCollapsed || !_paper.IsVisible)
        {
            return false;
        }
        if (source == null || IsDescendantOfTodoBody(source))
        {
            return false; // inside the panel: rows / checkboxes / append bar keep their semantics
        }
        return !IsScrollBarInteractionSource(source, this);
    }

    private bool IsDescendantOfTodoBody(DependencyObject? source)
    {
        var current = source;
        while (current != null)
        {
            if (ReferenceEquals(current, _todoPanel))
            {
                return true;
            }
            current = GetSafeParent(current);
        }
        return false;
    }

    internal void BeginTodoBlankPressDragOrClear(
        UIElement host,
        MouseButtonEventArgs e)
    {
        var start = e.GetPosition(this);
        var ended = false;
        MouseButtonEventHandler? upHandler = null;
        MouseEventHandler? moveHandler = null;
        MouseEventHandler? captureLostHandler = null;

        void End(bool dragged)
        {
            if (ended)
            {
                return;
            }

            ended = true;
            host.PreviewMouseLeftButtonUp -= upHandler;
            host.PreviewMouseMove -= moveHandler;
            host.LostMouseCapture -= captureLostHandler;
            if (ReferenceEquals(Mouse.Captured, host))
            {
                host.ReleaseMouseCapture();
            }

            if (!dragged)
            {
                ClearTodoSelection();
            }
        }

        upHandler = (_, up) =>
        {
            End(dragged: false);
            up.Handled = true;
        };
        moveHandler = (_, move) =>
        {
            if (move.LeftButton != MouseButtonState.Pressed)
            {
                End(dragged: true);
                return;
            }

            var p = move.GetPosition(this);
            if (Math.Abs(p.X - start.X) < PreviewDragThreshold &&
                Math.Abs(p.Y - start.Y) < PreviewDragThreshold)
            {
                return;
            }

            End(dragged: true);
            DetachExperimentalAttachmentBeforeUserDrag();
            WindowNative.BeginWindowCaptionDrag(this);
            move.Handled = true;
        };
        captureLostHandler = (_, _) => End(dragged: true);

        host.PreviewMouseLeftButtonUp += upHandler;
        host.PreviewMouseMove += moveHandler;
        host.LostMouseCapture += captureLostHandler;
        host.CaptureMouse();
    }

    private void InitializeDragGestures(FrameworkElement surface)
    {
        // Maximize/minimize transitions must reconcile the immersive header extent.
        StateChanged += (_, _) => RefreshManualTopBarHiddenForWindowState();

        // Alt+left drag was removed: preview-mode plain-left drag covers the main
        // flow, and edit-mode papers drag by the (possibly hidden-then-revealed) top
        // bar. One gesture, one code path.
        _ = surface;
    }

    // ---------- Maximize toggle: F11 ----------
    // The blank-body double-click entry was removed: it structurally competed with
    // the preview-press drag interception and the click-to-enter-editor path (all
    // three share the same left-button press), and every patch layered on top risked
    // regressing the verified drag behavior. F11 is keyboard-routed, immune to all
    // mouse-gesture interference, and available in any paper state.

    private void InitializeBlankDoubleClickMaximize(FrameworkElement body)
    {
        // Retained as the F11 hook site (no double-click handling). The key handler
        // lives in OnWindowPreviewKeyDown; see ToggleWindowStateForBlankDoubleClick.
        _ = body;
    }

    private void ToggleWindowStateForBlankDoubleClick()
    {
        if (_paper.IsCollapsed || !_paper.IsVisible)
        {
            return;
        }

        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    // Maximized papers keep the immersive hidden-header mode: the collapse stays and
    // the chrome is nudged up by the extent (handled in the focus-presentation partial).
    internal void RefreshManualTopBarHiddenForWindowState()
    {
        RefreshManualTopBarHiddenPresentation(animate: false);
    }

    // ---------- Temporary table view (editor context menu) ----------
    // The caret anchors the search for a GFM pipe-table block; the transform swaps it
    // for an aligned code block through a single document edit so Ctrl+Z (an editor
    // command) restores the raw table. The entry lives in the EDITOR menu — beside
    // the edit commands it belongs with — and is enabled when the caret is in a table.

    internal bool IsTableViewAvailableAtCaret(MarkdownTextBox box)
    {
        if (_paper.IsCollapsed)
        {
            return false;
        }

        var caret = Math.Clamp(box.CaretIndex, 0, Math.Max(0, box.Text.Length));
        return MarkdownTablePaste.TryBuildTableBlockView(box.Text, caret).HasValue;
    }

    private void ToggleNoteTableView()
    {
        var box = _noteBox;
        if (box == null)
        {
            return;
        }

        var caret = Math.Clamp(box.CaretIndex, 0, Math.Max(0, box.Text.Length));
        var plan = MarkdownTablePaste.TryBuildTableBlockView(box.Text, caret);
        if (plan is not { } block)
        {
            return;
        }

        box.Select(block.Start, block.Length);
        box.SelectedText = block.Replacement;
        // Single undo step restores the raw pipe table.
        box.Select(block.Start + block.Replacement.Length, 0);
    }

}
