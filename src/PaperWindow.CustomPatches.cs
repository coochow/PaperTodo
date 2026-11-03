using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace PaperTodo;

// === Custom patch (dev branch) ===
// Right-button long-press drag: holding the right button anywhere on the paper body
// for a moment starts a native caption drag, so an immersive (top-bar-hidden) paper
// can still be moved without revealing the bar. A quick right click is untouched —
// the context menu path only sees the press if it is released before the threshold.
//
// Patch notes: self-contained partial class; touches no upstream method bodies.
// Interaction with existing right-click menus is avoided by checking the press
// duration and movement — only a still, held press becomes a drag.
public sealed partial class PaperWindow
{
    private const int RightButtonDragHoldMilliseconds = 350;
    private const double RightButtonDragThreshold = 6.0;

    private DispatcherTimer? _rightButtonDragHoldTimer;
    private Point? _rightButtonDragStartPosition;
    private bool _rightButtonDragArmed;

    private void InitializeRightButtonDrag(FrameworkElement surface)
    {
        surface.PreviewMouseRightButtonDown += (_, e) =>
        {
            _rightButtonDragStartPosition = e.GetPosition(this);
            _rightButtonDragArmed = true;
            _rightButtonDragHoldTimer ??= CreateRightButtonDragHoldTimer();
            _rightButtonDragHoldTimer.Start();
        };

        surface.PreviewMouseRightButtonUp += (_, _) =>
        {
            _rightButtonDragHoldTimer?.Stop();
            _rightButtonDragStartPosition = null;
            _rightButtonDragArmed = false;
        };

        surface.PreviewMouseMove += (_, e) =>
        {
            if (!_rightButtonDragArmed || _rightButtonDragStartPosition is not { } start)
            {
                return;
            }

            var current = e.GetPosition(this);
            if (Math.Abs(current.X - start.X) >= RightButtonDragThreshold ||
                Math.Abs(current.Y - start.Y) >= RightButtonDragThreshold)
            {
                // Movement before the hold completes cancels the gesture; the normal
                // context-menu click path stays in charge.
                _rightButtonDragHoldTimer?.Stop();
                _rightButtonDragArmed = false;
            }
        };

        // Leaving the window or losing capture also cancels silently.
        MouseLeave += (_, _) =>
        {
            _rightButtonDragHoldTimer?.Stop();
            _rightButtonDragArmed = false;
        };
    }

    private DispatcherTimer CreateRightButtonDragHoldTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(RightButtonDragHoldMilliseconds)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!_rightButtonDragArmed ||
                Mouse.RightButton != MouseButtonState.Pressed ||
                !_paper.IsVisible ||
                _paper.IsCollapsed)
            {
                return;
            }

            // A context menu opened during the hold would have cleared the armed flag
            // through its own routing; only a clean still press reaches here.
            _rightButtonDragArmed = false;
            DetachExperimentalAttachmentBeforeUserDrag();
            WindowNative.BeginWindowCaptionDrag(this);
        };
        return timer;
    }

    // === Custom patch: double-click blank body toggles maximize/restore ===
    // Double-clicking empty space in the paper body switches between normal and
    // maximized. Double-clicks that land on text (word selection) or images are left
    // to their native behaviors; only true blank hits toggle the window state.

    private void InitializeBlankDoubleClickMaximize(FrameworkElement body)
    {
        body.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2 || e.Handled)
            {
                return;
            }

            if (!IsBlankBodyHit(e.OriginalSource as DependencyObject, body))
            {
                return;
            }

            ToggleWindowStateForBlankDoubleClick();
            e.Handled = true;
        };
    }

    private static bool IsBlankBodyHit(DependencyObject? originalSource, FrameworkElement body)
    {
        // The hit routed through the body itself (or a layout-only container) means no
        // text/image element took it: a blank area.
        return originalSource == body ||
            (originalSource is Grid or Border or StackPanel { Name: "" });
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
}
