using System;
using System.Collections.Generic;
using System.Linq;

namespace PaperTodo;

// === Custom patch (dev branch): Alt+O collapse/restore expanded papers ===
// First press: every visible, expanded paper folds into its capsule (capsules that
// were already collapsed stay exactly where they are — nothing is hidden). Second
// press: the papers that were folded by the first press expand back. The snapshot
// only remembers ids of papers folded BY THIS COMMAND, so manual expansions or
// collapses in between are respected on restore.
public sealed partial class AppController
{
    private HashSet<string>? _collapseExpandedPapersSnapshot;

    internal void ToggleCollapseExpandedPapers()
    {
        if (_collapseExpandedPapersSnapshot is { } snapshot)
        {
            RestoreCollapsedPapers(snapshot);
            return;
        }

        CollapseExpandedPapersNow();
    }

    private void CollapseExpandedPapersNow()
    {
        var toCollapse = State.Papers
            .Where(paper =>
                paper.IsVisible &&
                !paper.IsCollapsed &&
                CanPaperDisplayAsCapsule(paper))
            .Select(paper => paper.Id)
            .ToList();
        if (toCollapse.Count == 0)
        {
            return;
        }

        _collapseExpandedPapersSnapshot = toCollapse.ToHashSet(StringComparer.Ordinal);
        foreach (var id in toCollapse)
        {
            if (_windows.TryGetValue(id, out var window) &&
                !window.IsClosed)
            {
                window.SetCollapsedState(true);
            }
        }

        RefreshTrayMenu();
    }

    private void RestoreCollapsedPapers(HashSet<string> snapshot)
    {
        _collapseExpandedPapersSnapshot = null;
        foreach (var paper in State.Papers)
        {
            if (!snapshot.Contains(paper.Id))
            {
                continue;
            }

            // The paper may have been deleted or manually hidden meanwhile; respect that.
            if (!paper.IsVisible || !paper.IsCollapsed)
            {
                continue;
            }

            if (_windows.TryGetValue(paper.Id, out var window) && !window.IsClosed)
            {
                window.OpenCapsuleForEditing();
            }
        }

        RefreshTrayMenu();
    }
}
