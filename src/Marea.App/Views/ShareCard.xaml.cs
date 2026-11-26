using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Marea.Core;

namespace Marea.App.Views;

/// <summary>A 1200×630 image (the size social networks preview) with the user's results. It shows
/// numbers only: never paths, file or model names.</summary>
public partial class ShareCard : UserControl
{
    private ShareCard()
    {
        InitializeComponent();
    }

    /// <summary>What the card says. <paramref name="freedBytes"/> is what left the disk (a batch or
    /// everything evaporated); when it is 0 the card shows what could leave instead.</summary>
    public sealed record Numbers(long FreedBytes, long FreeableBytes, long TotalBytes, int Models, long InstantBytes);

    /// <summary>Current totals of the whole ledger.</summary>
    public static Numbers Current()
    {
        var e = App.Engine;
        var files = e.Ledger.Files();
        var plan = e.Plan(0);
        return new Numbers(
            files.Where(f => f.Residency == Residency.Evaporated).Sum(f => f.Size),
            plan.Sum(p => p.File.Size),
            files.Sum(f => f.Size),
            e.Models().Count,
            plan.Where(p => p.Instant).Sum(p => p.File.Size));
    }

    /// <summary>Renders the card, saves it under Pictures\Marea, copies it to the clipboard and shows
    /// it in Explorer. Returns the saved path.</summary>
    public static string SaveAndCopy(Numbers n)
    {
        var card = new ShareCard();
        card.Fill(n);
        card.Measure(new Size(1200, 630));
        card.Arrange(new Rect(0, 0, 1200, 630));
        card.UpdateLayout();
        var bmp = new RenderTargetBitmap(1200, 630, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(card);
        bmp.Freeze();
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Marea");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"marea-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        using (var fs = File.Create(path))
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            enc.Save(fs);
        }
        try
        {
            Clipboard.SetImage(bmp);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Clipboard busy: the file is saved anyway.
        }
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false });
        return path;
    }

    private void Fill(Numbers n)
    {
        Headline.Text = n.FreedBytes > 0 ? Loc.T("share.freed", Fmt.Gb(n.FreedBytes)) : Loc.T("share.freeable", Fmt.Gb(n.FreeableBytes));
        Subline.Text = Loc.T("share.subline", Fmt.Gb(n.TotalBytes));
        Stat1.Text = n.Models.ToString();
        Stat1Label.Text = Loc.T("share.models");
        Stat2.Text = Fmt.Gb(n.InstantBytes);
        Stat2Label.Text = Loc.T("share.instant");
        Stat3.Text = "0";
        Stat3Label.Text = Loc.T("share.deleted");
        Footer.Text = Loc.T("share.footer");
    }
}
