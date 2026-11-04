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
    // ---------- Alt + left-button drag anywhere on the paper ----------
    // Reuses the upstream title-bar drag gesture verbatim: threshold-armed, tether
    // aware, and ending in the native caption-drag loop, so the feel is identical to
    // dragging by the title bar (smooth, cross-monitor, works unfocused). The right-
    // button variant with manual tracking + mouse capture was removed: capturing on
    // the window root deadlocked right-click interactions for seconds.

    private void InitializeAltDragGesture(FrameworkElement surface)
    {
        // Maximize/minimize transitions must reconcile the immersive header extent.
        StateChanged += (_, _) => RefreshManualTopBarHiddenForWindowState();

        surface.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left ||
                (Keyboard.Modifiers & ModifierKeys.Alt) == 0 ||
                _paper.IsCollapsed ||
                !_paper.IsVisible)
            {
                return;
            }

            // Route straight into the upstream title-bar drag pipeline: it arms its own
            // session, captures on the drag source, and hands off to the native
            // caption drag once the movement threshold is crossed.
            BeginTitleBarDragGesture(surface, e);
            e.Handled = true;
        };
    }

    // ---------- Blank-body double-click maximize ----------
    // The editor's TextView covers the whole body, so "blank" cannot be detected by hit
    // source. A double-click is blank when it lands outside the TextView bounds or
    // below the last rendered visual line; word selection keeps everything else.
    // Alt-modified clicks belong to the drag gesture, never to maximize toggling.

    private void InitializeBlankDoubleClickMaximize(FrameworkElement body)
    {
        body.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2 || e.Handled ||
                (Keyboard.Modifiers & ModifierKeys.Alt) != 0)
            {
                return;
            }

            if (!IsBlankNoteBodyHit(e))
            {
                return;
            }

            ToggleWindowStateForBlankDoubleClick();
            e.Handled = true;
        };
    }

    private bool IsBlankNoteBodyHit(MouseButtonEventArgs e)
    {
        var box = _noteBox;
        if (box == null)
        {
            // Todo papers keep their native behavior.
            return false;
        }

        var view = box.TextArea?.TextView;
        if (view == null)
        {
            return false;
        }

        var p = e.GetPosition(view);
        if (p.X < 0 || p.Y < 0 || p.X > view.ActualWidth || p.Y > view.ActualHeight)
        {
            // Outside the text viewport entirely (content padding / margins).
            return true;
        }

        var lines = view.VisualLines;
        if (lines == null || lines.Count == 0)
        {
            return true;
        }

        var last = lines[lines.Count - 1];
        return p.Y > last.VisualTop + last.Height + 2;
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
