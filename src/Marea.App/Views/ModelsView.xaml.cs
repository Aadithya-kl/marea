using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Marea.Core;

namespace Marea.App.Views;

/// <summary>Every model in the managed folders, read from its own header: format, precision,
/// parameters, size, residency and whether loading it could run code.</summary>
public partial class ModelsView : UserControl, IRefreshable
{
    private sealed record Row(ModelEntry Model, string Name, string Kind, Brush KindBrush, string Format, string Precision,
        string Params, long ParamCount, string Size, long Bytes, string State, string Safety, int SafetyRank,
        Geometry SafetyIcon, Brush SafetyBrush);

    private bool _ready;

    /// <summary>Creates and loads the page.</summary>
    public ModelsView()
    {
        InitializeComponent();
        Filter.ItemsSource = new[] { "all", "attention", "gguf", "safetensors", "pickle", "evaporated" }
            .Select(k => Loc.T("models.filter_" + k)).ToList();
        _ready = true;
        Filter.SelectedIndex = 0;
    }

    /// <inheritdoc/>
    public void Refresh()
    {
        if (!_ready)
        {
            return;
        }
        var all = App.Engine.Models();
        var attention = all.Count(m => m.Safety >= SafetyVerdict.Suspicious);
        AttentionChip.Visibility = attention > 0 ? Visibility.Visible : Visibility.Collapsed;
        AttentionText.Text = Loc.T(attention == 1 ? "models.attention_one" : "models.attention", attention);
        TotalText.Text = Loc.T("models.total", all.Count, Fmt.Gb(all.Sum(m => m.Size)));
        IEnumerable<ModelEntry> models = Filter.SelectedIndex switch
        {
            1 => all.Where(m => m.Safety >= SafetyVerdict.Suspicious),
            2 => all.Where(m => m.Formats.Contains("GGUF")),
            3 => all.Where(m => m.Formats.Contains("safetensors")),
            4 => all.Where(m => m.Formats.Any(f => f is "PyTorch" or "TorchScript" or "pickle")),
            5 => all.Where(m => m.State != ModelResidency.OnDisk),
            _ => all,
        };
        var q = Search.Text.Trim();
        if (q.Length > 0)
        {
            models = models.Where(m => m.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                       || (m.Architecture?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                                       || m.Precisions.Any(p => p.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }
        var rows = models.Select(ToRow).ToList();
        Grid.ItemsSource = rows;
        EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        OnSelection(this, null!);
    }

    private Row ToRow(ModelEntry m)
    {
        var meta = m.Meta.All(x => x is null);
        var kind = Loc.T("kind." + m.Kind.ToString().ToLowerInvariant())
                   + (m.Architecture is { } a ? " · " + a : "") + " · " + Fmt.RelativeFolder(m.Files[0]);
        return new Row(m, m.Name, kind, (Brush)FindResource(m.Kind == ModelKind.Ollama ? "DerivableBrush" : "SeaAccentBrush"),
            m.Formats.Count > 0 ? string.Join(" + ", m.Formats) : "—",
            m.Precisions.Count > 0 ? string.Join(", ", m.Precisions) : "—",
            Fmt.Params(m.Parameters), m.Parameters, Fmt.Gb(m.Size), m.Size,
            Loc.T("mstate." + m.State.ToString().ToLowerInvariant()),
            meta ? Loc.T("safety.pending") : Fmt.Safety(m.Safety), (int)m.Safety,
            (Geometry)FindResource(Fmt.SafetyIcon(m.Safety)), (Brush)FindResource(Fmt.SafetyBrush(m.Safety)));
    }

    private void OnFilter(object sender, SelectionChangedEventArgs e) => Refresh();

    private void OnSearch(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Refresh();
    }

    /// <summary>Shows what the header says, the safety verdict in plain words and the files.</summary>
    private void OnSelection(object sender, SelectionChangedEventArgs e)
    {
        if (Grid.SelectedItem is not Row row)
        {
            DetailBadge.Background = (Brush)FindResource("SeaAccentBrush");
            DetailIcon.Data = (Geometry)FindResource("Icon.Cube");
            DetailTitle.Text = Loc.T("models.select_title");
            DetailText.Text = Loc.T("models.select_hint");
            DetailFiles.Text = "";
            OpenButton.IsEnabled = false;
            return;
        }
        var m = row.Model;
        DetailBadge.Background = row.SafetyBrush;
        DetailIcon.Data = row.SafetyIcon;
        DetailTitle.Text = $"{m.Name}  ·  {row.Safety}";
        var text = Loc.T("safety_help." + Fmt.SafetyKey(m.Safety));
        if (m.Findings.Count > 0)
        {
            text += "\n" + Loc.T("models.findings") + " " + string.Join(", ", m.Findings.Take(6));
        }
        DetailText.Text = text;
        DetailFiles.Text = string.Join("\n", m.Files.Zip(m.Meta).Take(8).Select(p =>
            $"{Fmt.Gb(p.First.Size),9}  {p.Second?.Format ?? "?",-12} {Fmt.Residency(p.First.Residency),-12} {Fmt.ShortPath(p.First.Path)}"))
            + (m.Files.Count > 8 ? "\n" + Loc.T("models.more_files", m.Files.Count - 8) : "");
        OpenButton.IsEnabled = System.IO.Directory.Exists(m.Folder);
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is Row row && System.IO.Directory.Exists(row.Model.Folder))
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { row.Model.Folder }, UseShellExecute = false });
        }
    }
}
