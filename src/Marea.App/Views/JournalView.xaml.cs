using System.Windows.Controls;
using Marea.Core;

namespace Marea.App.Views;

/// <summary>Append-only activity journal, rendered in the current language.</summary>
public partial class JournalView : UserControl, IRefreshable
{
    /// <summary>Creates and loads the page.</summary>
    public JournalView()
    {
        InitializeComponent();
        Refresh();
    }

    /// <inheritdoc/>
    public void Refresh() =>
        Grid.ItemsSource = App.Engine.Ledger.Journal(1000).Select(j => new
        {
            When = j.AtUtc.ToLocalTime().ToString("g"),
            Action = (j.Ok ? "" : "⚠ ") + Loc.Render(j.Action),
            File = Fmt.ShortPath(j.Path),
            Size = j.Bytes > 0 ? Fmt.Gb(j.Bytes) : "",
            Detail = Fmt.Source(j.Detail),
        }).ToList();
}
