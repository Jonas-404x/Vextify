using System.Windows;
using Microsoft.Win32;
using Vextify.Core;

namespace Vextify.Views;

/// <summary>Shared by every place that accepts dropped or browsed mod files.</summary>
public static class ImportHelper
{
    public static void HandleDrop(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) Import(files);
    }

    public static void Browse()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Mods auswählen",
            Filter = "Mods (*.js;*.css;*.zip)|*.js;*.css;*.zip|Alle Dateien|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog() == true) Import(dlg.FileNames);
    }

    private static void Import(IEnumerable<string> paths)
    {
        var (added, problems) = Guardian.Instance.Mods.Import(paths);
        if (added > 0) Guardian.Instance.PendingChanges = true;
        Guardian.Instance.Refresh();

        var main = App.Instance.MainWin;
        if (problems.Count > 0)
            main?.ShowToast(problems[0] + (problems.Count > 1 ? $" (+{problems.Count - 1} weitere)" : ""), warning: true);
        else if (added > 0)
            main?.ShowToast(added == 1 ? "1 Mod hinzugefügt – schalte ihn unter „Mods“ ein" : $"{added} Mods hinzugefügt – schalte sie unter „Mods“ ein");
        else
            main?.ShowToast("Nichts hinzugefügt", warning: true);
    }
}
