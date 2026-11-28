namespace PaperTodo;

// === Custom patch (dev branch): one-click restore for every settings page ===
// Reuses the per-page restore methods so the fork's new-user defaults stay
// defined in exactly one place. Order follows the user manual: Labs first (its
// page is hidden outside advanced mode), then Shortcuts, General, Todo, Note,
// Visual. Each page restore saves/refreshes; the final pass keeps the settings
// window in sync.
public sealed partial class AppController
{
    internal void RestoreAllSettingsToDefaults()
    {
        RestoreLabsSettingsPageDefaults();
        RestoreShortcutSettingsPageDefaults();
        RestoreSettingsSidebarGeneralDefaults();
        RestoreSettingsSidebarTodoDefaults();
        RestoreSettingsSidebarNoteDefaults();
        RestoreVisualSettingsPageDefaults();
        SaveNow();
        RefreshSettingsWindowContent();
    }
}