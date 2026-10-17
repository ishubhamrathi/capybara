using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CapyBara.Models;

public sealed class BrowserTab
{
    public WebView2 View { get; } = new() { Visibility = Visibility.Collapsed };

    public Border Container { get; } = new()
    {
        Height = 34,
        CornerRadius = new CornerRadius(7),
        Padding = new Thickness(3, 0, 3, 0),
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0))
    };

    public Grid TabContent { get; } = new() { ColumnSpacing = 0 };

    public Button SelectButton { get; } = new()
    {
        Height = 32,
        MinWidth = 0,
        Padding = new Thickness(2, 0, 1, 0),
        BorderThickness = new Thickness(0),
        Background = new SolidColorBrush(Colors.Transparent),
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Center
    };

    public Button CloseButton { get; } = new()
    {
        Content = "\uE711",
        FontFamily = new FontFamily("Segoe Fluent Icons"),
        FontSize = 10,
        Width = 20,
        Height = 28,
        MinWidth = 0,
        Padding = new Thickness(0),
        BorderThickness = new Thickness(0),
        Background = new SolidColorBrush(Colors.Transparent),
        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 164, 168, 176)),
        CornerRadius = new CornerRadius(5),
        VerticalAlignment = VerticalAlignment.Center
    };

    public Image FaviconImage { get; } = new()
    {
        Width = 16,
        Height = 16,
        Stretch = Stretch.Uniform,
        VerticalAlignment = VerticalAlignment.Center
    };

    public TextBlock TitleText { get; } = new()
    {
        Text = "New Tab",
        FontSize = 13,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 164, 168, 176))
    };

    public string Title { get; set; } = "New Tab";
    public bool IsNewTabPage { get; set; }

    public BrowserTab()
    {
        var selectContent = new Grid { ColumnSpacing = 7 };
        selectContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        selectContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        selectContent.Children.Add(FaviconImage);
        Grid.SetColumn(TitleText, 1);
        selectContent.Children.Add(TitleText);
        SelectButton.Content = selectContent;

        TabContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        TabContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(SelectButton, 0);
        Grid.SetColumn(CloseButton, 1);
        TabContent.Children.Add(SelectButton);
        TabContent.Children.Add(CloseButton);
        Container.Child = TabContent;
        ToolTipService.SetToolTip(SelectButton, Title);
    }

    public void SetWidth(double width, double selectWidth, double closeWidth)
    {
        Container.Width = width;
        SelectButton.Width = selectWidth;
        CloseButton.Width = closeWidth;
        SelectButton.Padding = selectWidth < 34
            ? new Thickness(0)
            : new Thickness(5, 0, 2, 0);
        TitleText.Visibility = selectWidth < 42 ? Visibility.Collapsed : Visibility.Visible;
    }
}
