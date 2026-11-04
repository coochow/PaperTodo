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

    // ---------- Top-bar opacity quick slider (fixed slot) ----------
    // Sits in the fixed button row (between the ellipsis and close buttons) instead of
    // the pinned action area, so the responsive layout pass never hides it. Tuning goes
    // through the settings pipeline, which refreshes every paper window live.

    private Popup? _topBarOpacityPopup;
    private CheckBox? _topBarOpacityEnabledToggle;
    private Slider? _topBarOpacitySlider;
    private TextBlock? _topBarOpacityPercentText;

    internal Button CreateTopBarOpacityButton()
    {
        var button = IconButton("◐", Strings.Get("ToolTipOpacityQuickSlider"));
        button.Width = 23;
        button.FontSize = AppTypography.Scale(14);
        button.Click += (_, _) => ToggleTopBarOpacityPopup(button);
        return button;
    }

    private void ToggleTopBarOpacityPopup(Button anchor)
    {
        if (_topBarOpacityPopup is { IsOpen: true })
        {
            _topBarOpacityPopup.IsOpen = false;
            return;
        }

        _topBarOpacityPopup ??= BuildTopBarOpacityPopup();
        RefreshTopBarOpacityPopupValues();
        _topBarOpacityPopup.PlacementTarget = anchor;
        _topBarOpacityPopup.Placement = PlacementMode.Bottom;
        _topBarOpacityPopup.IsOpen = true;
    }

    private Popup BuildTopBarOpacityPopup()
    {
        var state = _controller.State;

        var enabledToggle = _topBarOpacityEnabledToggle = new CheckBox
        {
            Content = Strings.Get("LabsEnablePersistentPaperOpacity"),
            IsChecked = state.ExperimentalPersistentPaperOpacity,
            Margin = new Thickness(0, 0, 0, 6),
            Foreground = TextBrush
        };
        enabledToggle.Click += (_, _) =>
            _controller.SetExperimentalPersistentPaperOpacityFromPaper(
                enabledToggle.IsChecked == true);

        var slider = _topBarOpacitySlider = new Slider
        {
            Minimum = ExperimentalOpacityLevels.Minimum,
            Maximum = ExperimentalOpacityLevels.Maximum,
            Value = state.ExperimentalPersistentPaperOpacityLevel,
            TickFrequency = ExperimentalOpacityLevels.Step,
            IsSnapToTickEnabled = true,
            Width = 150,
            IsMoveToPointEnabled = true
        };
        slider.ValueChanged += (_, e) =>
        {
            if (_topBarOpacityPopup?.IsOpen != true)
            {
                return;
            }

            _controller.SetExperimentalPersistentPaperOpacityLevelFromPaper(e.NewValue);
            if (_topBarOpacityPercentText != null)
            {
                _topBarOpacityPercentText.Text = $"{Math.Round(e.NewValue * 100):0}%";
            }
        };

        var percentText = _topBarOpacityPercentText = new TextBlock
        {
            Text = $"{Math.Round(state.ExperimentalPersistentPaperOpacityLevel * 100):0}%",
            FontSize = AppTypography.Scale(12),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = TextBrush
        };

        var panel = new StackPanel
        {
            Margin = new Thickness(10, 8, 10, 10),
            MinWidth = 170
        };
        panel.Children.Add(enabledToggle);
        panel.Children.Add(slider);
        panel.Children.Add(percentText);

        // Reuse the live chrome brushes so the popup follows the current skin without
        // resource lookup (a Popup lives outside the window's visual tree).
        var border = new Border
        {
            Child = panel,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(2),
            Background = _paperChrome.Background,
            BorderBrush = _paperChrome.BorderBrush
        };

        return new Popup
        {
            Child = border,
            StaysOpen = false,
            AllowsTransparency = true
        };
    }

    private void RefreshTopBarOpacityPopupValues()
    {
        if (_topBarOpacityEnabledToggle == null || _topBarOpacitySlider == null)
        {
            return;
        }

        var state = _controller.State;
        _topBarOpacityEnabledToggle.IsChecked = state.ExperimentalPersistentPaperOpacity;
        _topBarOpacitySlider.Value = state.ExperimentalPersistentPaperOpacityLevel;
        if (_topBarOpacityPercentText != null)
        {
            _topBarOpacityPercentText.Text =
                $"{Math.Round(state.ExperimentalPersistentPaperOpacityLevel * 100):0}%";
        }
    }
}
