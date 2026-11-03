using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PaperTodo;

// === Custom patch bundle (dev branch) ===
// All user-requested customizations live in this partial class so upstream merges
// stay mechanical. Upstream files only carry short, anchored hook lines.
public sealed partial class PaperWindow
{
    // ---------- Right-button long-press drag (manual tracking) ----------
    // The native caption-drag modal loop requires the LEFT button to be held, so a
    // right-button gesture must move the window manually: track screen deltas and
    // write Left/Top per move, ending when the right button is released.

    private const int RightButtonDragHoldMilliseconds = 350;
    private const double RightButtonDragThreshold = 6.0;

    private DispatcherTimer? _rightButtonDragHoldTimer;
    private Point? _rightButtonDragStartPosition;
    private bool _rightButtonDragArmed;
    private bool _rightButtonDragging;
    private Point _rightButtonDragScreenStart;
    private double _rightButtonDragWindowLeft;
    private double _rightButtonDragWindowTop;
    private double _rightButtonDragDpiScale = 1.0;

    private void InitializeRightButtonDrag(FrameworkElement surface)
    {
        surface.PreviewMouseRightButtonDown += (_, e) =>
        {
            if (_paper.IsCollapsed || !_paper.IsVisible)
            {
                return;
            }

            _rightButtonDragStartPosition = e.GetPosition(this);
            _rightButtonDragArmed = true;
            _rightButtonDragHoldTimer ??= CreateRightButtonDragHoldTimer();
            _rightButtonDragHoldTimer.Stop();
            _rightButtonDragHoldTimer.Start();
        };

        surface.PreviewMouseRightButtonUp += (_, e) =>
        {
            if (_rightButtonDragging)
            {
                EndRightButtonDrag(saveGeometry: true);
                // Swallow the release so no context menu opens right after a drag.
                e.Handled = true;
            }
            else
            {
                _rightButtonDragHoldTimer?.Stop();
                _rightButtonDragArmed = false;
                _rightButtonDragStartPosition = null;
            }
        };

        surface.PreviewMouseMove += (_, e) =>
        {
            if (_rightButtonDragging)
            {
                if (e.RightButton != MouseButtonState.Pressed)
                {
                    EndRightButtonDrag(saveGeometry: true);
                }
                else
                {
                    UpdateRightButtonDragPosition(e);
                }

                return;
            }

            if (!_rightButtonDragArmed || _rightButtonDragStartPosition is not { } start)
            {
                return;
            }

            var p = e.GetPosition(this);
            if (Math.Abs(p.X - start.X) >= RightButtonDragThreshold ||
                Math.Abs(p.Y - start.Y) >= RightButtonDragThreshold)
            {
                // Early movement cancels the hold; the click keeps its menu behavior.
                _rightButtonDragHoldTimer?.Stop();
                _rightButtonDragArmed = false;
                _rightButtonDragStartPosition = null;
            }
        };

        surface.MouseLeave += (_, _) =>
        {
            if (_rightButtonDragging)
            {
                // While dragging the pointer may leave the surface; button polling ends it.
                return;
            }

            _rightButtonDragHoldTimer?.Stop();
            _rightButtonDragArmed = false;
            _rightButtonDragStartPosition = null;
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
            if (!_rightButtonDragArmed || _rightButtonDragStartPosition is not { } start)
            {
                return;
            }

            if (Mouse.RightButton != MouseButtonState.Pressed ||
                _paper.IsCollapsed ||
                !_paper.IsVisible)
            {
                return;
            }

            _rightButtonDragArmed = false;
            BeginRightButtonDrag(start);
        };
        return timer;
    }

    private void BeginRightButtonDrag(Point windowRelativeStart)
    {
        _rightButtonDragging = true;
        _rightButtonDragWindowLeft = Left;
        _rightButtonDragWindowTop = Top;
        _rightButtonDragScreenStart = PointToScreen(windowRelativeStart);
        _rightButtonDragDpiScale = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (_rightButtonDragDpiScale <= 0 || !double.IsFinite(_rightButtonDragDpiScale))
        {
            _rightButtonDragDpiScale = 1.0;
        }

        DetachExperimentalAttachmentBeforeUserDrag();
    }

    private void UpdateRightButtonDragPosition(MouseEventArgs e)
    {
        var screenNow = PointToScreen(e.GetPosition(this));
        var dx = (screenNow.X - _rightButtonDragScreenStart.X) / _rightButtonDragDpiScale;
        var dy = (screenNow.Y - _rightButtonDragScreenStart.Y) / _rightButtonDragDpiScale;
        var targetLeft = _rightButtonDragWindowLeft + dx;
        var targetTop = _rightButtonDragWindowTop + dy;
        MoveWindowWithoutGeometrySave(() =>
        {
            Left = targetLeft;
            Top = targetTop;
        });
    }

    private void EndRightButtonDrag(bool saveGeometry)
    {
        _rightButtonDragging = false;
        _rightButtonDragStartPosition = null;
        if (saveGeometry)
        {
            SaveGeometryForCurrentPresentation();
        }
    }

    // ---------- Blank-body double-click maximize ----------
    // The editor's TextView covers the whole body, so "blank" cannot be detected by hit
    // source. A double-click is blank when it lands outside the TextView bounds or
    // below the last rendered visual line; word selection keeps everything else.

    private void InitializeBlankDoubleClickMaximize(FrameworkElement body)
    {
        body.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2 || e.Handled)
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
