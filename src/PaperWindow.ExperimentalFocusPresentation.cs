using System;
using System.Windows;
using System.Windows.Controls;

namespace PaperTodo;

public sealed partial class PaperWindow
{
    private bool _experimentalFocusPresentationInitialized;
    private bool _inactiveTitleBarPresentationAttached;
    private bool _manualTopBarHiddenPresentationAttached;

    internal void UpdateExperimentalFocusPresentationSettings()
    {
        InitializeExperimentalFocusPresentation();
        RefreshExperimentalFocusPresentation();
        RefreshManualTopBarHiddenPresentation(animate: false);
    }

    internal void RestoreExperimentalInactiveTitleBarPresentation()
    {
        if (!_inactiveTitleBarPresentationAttached)
        {
            return;
        }

        _paperChrome.SetHeaderOpacity(1, 0);
        _paperChrome.SetHeaderExtent(0);
        _windowHost.LayoutUpdated -= OnInactiveTitleBarLayoutUpdated;
        _inactiveTitleBarPresentationAttached = false;
        _topBarHost!.IsHitTestVisible = true;
    }

    // ---- Manual immersive top-bar hiding (paper context menu toggle) ----
    // Reuses the chrome header-collapse pipeline built for the inactive-title-bar
    // labs feature: the shell keeps its original layout (row 0 reserved) while the
    // background surface shrinks, so the body never reflows and restoring is lossless.

    internal void ToggleManualTopBarHidden()
    {
        _paper.TopBarHidden = !_paper.TopBarHidden;
        _controller.MarkDirty();
        RefreshManualTopBarHiddenPresentation();
    }

    internal void RefreshManualTopBarHiddenPresentation(bool animate = true)
    {
        if (!_isShellBuilt || _topBarHost == null)
        {
            return;
        }

        var hidden = _paper.TopBarHidden && !_paper.IsCollapsed;
        // Collapsed capsules have no top bar; keep the flag but never apply chrome
        // changes while the paper is not expanded.
        if (_paper.IsCollapsed)
        {
            hidden = false;
        }

        _topBarHost.IsHitTestVisible = !hidden;
        if (hidden)
        {
            if (!_manualTopBarHiddenPresentationAttached)
            {
                _manualTopBarHiddenPresentationAttached = true;
                _windowHost.LayoutUpdated += OnManualTopBarHiddenLayoutUpdated;
            }

            UpdateManualTopBarHiddenExtent();
            _paperChrome.SetHeaderOpacity(
                0,
                animate && _controller.State.EnableAnimations
                    ? AnimationTiming.ScaleMilliseconds(ExperimentalOpacityTransitionMilliseconds) : 0);
        }
        else
        {
            if (_manualTopBarHiddenPresentationAttached)
            {
                _manualTopBarHiddenPresentationAttached = false;
                _windowHost.LayoutUpdated -= OnManualTopBarHiddenLayoutUpdated;
            }

            // The labs-driven inactive-title-bar path may still want the header faded;
            // only restore when neither feature requests a hidden header.
            var labsWantsHidden =
                _controller.State.ExperimentalHideInactiveTitleBar &&
                !IsMouseOver && !IsActive;
            if (!labsWantsHidden)
            {
                _paperChrome.SetHeaderOpacity(
                    1,
                    animate && _controller.State.EnableAnimations
                        ? AnimationTiming.ScaleMilliseconds(ExperimentalOpacityTransitionMilliseconds) : 0);
                _paperChrome.SetHeaderExtent(0);
            }
        }
    }

    private void OnManualTopBarHiddenLayoutUpdated(object? sender, EventArgs e)
    {
        if (!_paper.TopBarHidden || _paper.IsCollapsed)
        {
            RefreshManualTopBarHiddenPresentation(animate: false);
            return;
        }

        UpdateManualTopBarHiddenExtent();
    }

    private void UpdateManualTopBarHiddenExtent()
    {
        if (!_manualTopBarHiddenPresentationAttached || _topBarHost == null ||
            _shell.RowDefinitions.Count == 0 || _shell.RowDefinitions[0].ActualHeight <= 0)
        {
            return;
        }

        _paperChrome.SetHeaderExtent(_shell.RowDefinitions[0].ActualHeight);
    }

    private void InitializeExperimentalFocusPresentation()
    {
        if (_experimentalFocusPresentationInitialized || !_isShellBuilt)
        {
            return;
        }

        _experimentalFocusPresentationInitialized = true;
        _topBarHost!.SetBinding(OpacityProperty, new System.Windows.Data.Binding
        {
            Source = _paperChrome,
            Path = new PropertyPath(PaperChromeBorder.HeaderOpacityProperty)
        });
        // Hover reveals optional action buttons; the whole title bar follows focus.
        MouseEnter += (_, _) => RefreshExperimentalFocusPresentation();
        MouseLeave += (_, _) => RefreshExperimentalFocusPresentation();
        IsVisibleChanged += (_, _) => RefreshExperimentalFocusPresentation(animate: false);
    }

    private void RefreshExperimentalFocusPresentation(bool animate = true)
    {
        if (!_isShellBuilt)
        {
            return;
        }

        var interactionReveal =
            IsActive ||
            IsBuiltInFindOpen ||
            HasOpenOwnedContextMenu() ||
            _titleBarDragSession != null ||
            _todoDrag?.IsDragging == true ||
            _topBarDrag?.IsDragging == true;

        var eligible = CanFadeInactiveTitleBar();
        SetInactiveTitleBarHidden(
            _controller.State.ExperimentalHideInactiveTitleBar && !interactionReveal && eligible,
            animate && eligible);

        if (_topBarActionButtonsHost != null)
        {
            var hideButtons =
                _controller.State.ExperimentalHideInactiveTopBarButtons &&
                !(interactionReveal || IsMouseOver);
            _topBarActionButtonsHost.IsHitTestVisible = !hideButtons;
            SetExperimentalVisualOpacity(
                _topBarActionButtonsHost,
                hideButtons ? 0.0 : 1.0,
                animate);
        }
    }

    private bool CanFadeInactiveTitleBar() =>
        AllowsTransparency &&
        (_paperChrome is not SkinBorder skin || !PaperSkins.IsDecorated(skin.Skin)) &&
        IsVisible &&
        !_paper.IsCollapsed &&
        WindowState == WindowState.Normal &&
        !_isSnappedPresentation &&
        !IsPaperFormTransitioning;

    private void SetInactiveTitleBarHidden(bool hidden, bool animate)
    {
        if (_topBarHost == null)
        {
            return;
        }

        _topBarHost.IsHitTestVisible = !hidden;
        if (!hidden && !_inactiveTitleBarPresentationAttached)
        {
            return;
        }

        if (!hidden && (!animate || !_controller.State.EnableAnimations))
        {
            RestoreExperimentalInactiveTitleBarPresentation();
            return;
        }

        if (!_inactiveTitleBarPresentationAttached)
        {
            _inactiveTitleBarPresentationAttached = true;
            _windowHost.LayoutUpdated += OnInactiveTitleBarLayoutUpdated;
        }

        UpdateInactiveTitleBarExtent();
        _paperChrome.SetHeaderOpacity(
            hidden ? 0 : 1,
            animate && _controller.State.EnableAnimations
                ? AnimationTiming.ScaleMilliseconds(ExperimentalOpacityTransitionMilliseconds) : 0,
            hidden ? null : RestoreExperimentalInactiveTitleBarPresentation);
    }

    private void OnInactiveTitleBarLayoutUpdated(object? sender, EventArgs e)
    {
        // Typography and DPI changes can change the title row's extent. The shell
        // retains its original layout; only the background surface gets shorter.
        if (!CanFadeInactiveTitleBar())
        {
            RestoreExperimentalInactiveTitleBarPresentation();
            return;
        }
        UpdateInactiveTitleBarExtent();
    }

    private void UpdateInactiveTitleBarExtent()
    {
        if (!_inactiveTitleBarPresentationAttached || _topBarHost == null ||
            _shell.RowDefinitions.Count == 0 || _shell.RowDefinitions[0].ActualHeight <= 0)
        {
            return;
        }

        _paperChrome.SetHeaderExtent(_shell.RowDefinitions[0].ActualHeight);
    }
}
