using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("PaperTodo.EdgeTitleChecks")]
[assembly: InternalsVisibleTo("PaperTodo.LifecycleChecks")]
[assembly: InternalsVisibleTo("PaperTodo.PluginApiChecks")]
[assembly: InternalsVisibleTo("PaperTodo.ThreadingChecks")]
[assembly: InternalsVisibleTo("PaperTodo.SettingsApiChecks")]
[assembly: InternalsVisibleTo("PaperTodo.PersistenceChecks")]
[assembly: InternalsVisibleTo("PaperTodo.MarkdownEditingChecks")]
[assembly: InternalsVisibleTo("PaperTodo.WindowCloseActivationChecks")]
[assembly: InternalsVisibleTo("PaperTodo.MicaChecks")]
// Custom patch (dev branch): Markdown transfer checks.
[assembly: InternalsVisibleTo("PaperTodo.DataTransferChecks")]
