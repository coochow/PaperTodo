namespace PaperTodo;

public sealed partial class AppController
{
    // Custom patch (dev branch): apply the completed-to-bottom policy to existing
    // items once, when the user enables the switch or restores todo defaults.
    // Ongoing completions are handled by TodoRules.ApplyDoneTransitionOrdering,
    // which inserts a newly completed item at the top of the completed group.
    internal void ReorderCompletedTodosToBottom()
    {
        if (!State.AutoMoveCompletedTodosToBottom)
        {
            return;
        }

        var changed = false;
        foreach (var paper in State.Papers)
        {
            if (paper.Type != PaperTypes.Todo)
            {
                continue;
            }

            if (TodoRules.ApplyCompletedOrdering(paper.Items, enabled: true))
            {
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        MarkDirty();
        foreach (var window in _windows.Values)
        {
            window.RefreshTodoRowsForExternalMutation();
        }
    }
}