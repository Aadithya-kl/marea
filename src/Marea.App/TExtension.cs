using System.Windows.Markup;
using Marea.Core;

namespace Marea.App;

/// <summary>XAML markup extension for translated text: <c>Text="{l:T ov.title}"</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension(string key) : MarkupExtension
{
    /// <summary>Translation key.</summary>
    public string Key { get; set; } = key;

    /// <inheritdoc/>
    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.T(Key);
}
