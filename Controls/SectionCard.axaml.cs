using Avalonia;
using Avalonia.Controls;

namespace PenDynamicsLab.Controls;

/// <summary>
/// A collapsible titled panel for the left-hand control column: bold header with an
/// optional status suffix, a chevron, and a body that folds away when collapsed.
/// </summary>
/// <remarks>
/// <para>
/// The body goes in <see cref="CardContent"/>, which callers set with the property-element
/// form: <c>&lt;SectionCard&gt;&lt;SectionCard.CardContent&gt;…&lt;/…&gt;&lt;/SectionCard&gt;</c>.
/// </para>
/// <para>
/// Do NOT mark <see cref="CardContent"/> with <c>[Content]</c> to allow the shorter child
/// syntax. That attribute also applies when AvaloniaXamlLoader loads this control's own
/// .axaml, so the card's Border chrome gets assigned to <c>CardContent</c> instead of
/// <c>Content</c>; the UserControl then has no content, measures to zero height, and the
/// card renders as nothing at all.
/// </para>
/// </remarks>
public partial class SectionCard : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<SectionCard, string>(nameof(Title), defaultValue: "");

    /// <summary>Suffix shown after the title, e.g. "(OFF)". Empty hides it.</summary>
    public static readonly StyledProperty<string> StatusProperty =
        AvaloniaProperty.Register<SectionCard, string>(nameof(Status), defaultValue: "");

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<SectionCard, bool>(nameof(IsExpanded), defaultValue: true);

    public static readonly StyledProperty<object?> CardContentProperty =
        AvaloniaProperty.Register<SectionCard, object?>(nameof(CardContent));

    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Status { get => GetValue(StatusProperty); set => SetValue(StatusProperty, value); }
    public bool IsExpanded { get => GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }

    public object? CardContent { get => GetValue(CardContentProperty); set => SetValue(CardContentProperty, value); }

    public SectionCard()
    {
        InitializeComponent();

        HeaderButton.Click += (_, _) => IsExpanded = !IsExpanded;

        PropertyChanged += (_, e) =>
        {
            if (e.Property == TitleProperty || e.Property == StatusProperty) SyncHeader();
            else if (e.Property == IsExpandedProperty) SyncExpanded();
            else if (e.Property == CardContentProperty) BodyPresenter.Content = e.NewValue;
        };

        SyncHeader();
        SyncExpanded();
    }

    private void SyncHeader()
        => TitleText.Text = Status.Length == 0 ? Title : $"{Title} {Status}";

    private void SyncExpanded()
    {
        BodyPresenter.IsVisible = IsExpanded;
        ChevronText.Text = IsExpanded ? "▲" : "▼";
    }
}
