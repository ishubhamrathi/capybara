using CapyBara.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Controls.Primitives;
using CapyBara.Services;

namespace CapyBara.Controls;

public sealed partial class BrowserToolbar : UserControl
{
    private readonly List<BrowserTab> _tabs = [];
    private readonly Dictionary<string, RadioMenuFlyoutItem> _searchEngineMenuItems = new(StringComparer.OrdinalIgnoreCase);
    private BrowserTab? _activeTab;

    public event RoutedEventHandler? HomeRequested;
    public event RoutedEventHandler? BackRequested;
    public event RoutedEventHandler? ForwardRequested;
    public event RoutedEventHandler? ReloadRequested;
    public event RoutedEventHandler? NewTabRequested;
    public event RoutedEventHandler? MinimizeRequested;
    public event RoutedEventHandler? MaximizeRequested;
    public event RoutedEventHandler? CloseRequested;
    public event Action<string>? AddressSuggestionRequested;
    public event Action<string>? AddressQuerySubmitted;
    public event Action<string>? AddressSearchEngineChanged;
    public event RoutedEventHandler? AddressGotFocus;
    public event RoutedEventHandler? AddressLostFocus;

    public UIElement DragArea => DragContainer;
    public string AddressText
    {
        get => AddressBar.Text;
        set => AddressBar.Text = value;
    }

    public bool IsAddressFocused
    {
        get
        {
            TextBox? editor = FindDescendant<TextBox>(AddressBar);
            return editor is not null && editor.FocusState != FocusState.Unfocused;
        }
    }

    public bool IsAddressSuggestionListOpen => AddressBar.IsSuggestionListOpen;

    public BrowserToolbar()
    {
        InitializeComponent();
        foreach (SearchEngineSettings engine in SearchEngineSettings.AvailableEngines)
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = engine.Name,
                Tag = engine.Id,
                GroupName = "AddressBarSearchEngines",
                IsChecked = string.Equals(engine.Id, SearchEngineSettings.Current.Id, StringComparison.OrdinalIgnoreCase)
            };
            try
            {
                item.Icon = new BitmapIcon
                {
                    UriSource = new Uri(engine.IconUrl),
                    ShowAsMonochrome = false
                };
            }
            catch (Exception)
            {
                item.Icon = new SymbolIcon(Symbol.Find);
            }
            item.Click += SearchEngineMenuItem_Click;
            SearchEngineMenu.Items.Add(item);
            _searchEngineMenuItems[engine.Id] = item;
        }
    }

    public void AddTab(BrowserTab tab)
    {
        tab.SelectButton.Style = (Style)RootGrid.Resources["TabButtonStyle"];
        tab.Container.PointerEntered += (_, _) =>
        {
            if (tab != _activeTab)
                tab.Container.Background = ThemeBrush("TabHoverBackground");
        };
        tab.Container.PointerExited += (_, _) =>
            SetTabActive(tab, tab == _activeTab);
        tab.CloseButton.PointerEntered += (_, _) =>
            tab.CloseButton.Background = ThemeBrush("ToolbarHover");
        tab.CloseButton.PointerExited += (_, _) =>
            tab.CloseButton.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));

        TabStrip.Children.Add(tab.Container);
        _tabs.Add(tab);
        UpdateTabWidths();
    }

    public void RemoveTab(BrowserTab tab)
    {
        _tabs.Remove(tab);
        TabStrip.Children.Remove(tab.Container);
        if (_activeTab == tab)
            _activeTab = null;
        UpdateTabWidths();
    }

    public void SetTabActive(BrowserTab tab, bool active)
    {
        if (active)
            _activeTab = tab;

        tab.Container.Background = ThemeBrush(active ? "TabActiveBackground" : "TabInactiveBackground");
        tab.TitleText.Foreground = ThemeBrush(active ? "PrimaryText" : "SecondaryText");
    }

    public void SetNavigationState(bool canGoBack, bool canGoForward, bool hasActiveTab)
    {
        BackButton.IsEnabled = canGoBack;
        ForwardButton.IsEnabled = canGoForward;
        ReloadButton.IsEnabled = hasActiveTab;
    }

    public void SetMaximized(bool maximized)
    {
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        ToolTipService.SetToolTip(MaximizeButton, maximized ? "Restore" : "Maximize");
    }

    public void FocusAddressBar()
    {
        AddressBar.Focus(FocusState.Programmatic);
        if (FindDescendant<TextBox>(AddressBar) is { } textBox)
            textBox.SelectAll();
    }

    public void SetSearchEngine(SearchEngineSettings engine)
    {
        foreach ((string id, RadioMenuFlyoutItem item) in _searchEngineMenuItems)
            item.IsChecked = string.Equals(id, engine.Id, StringComparison.OrdinalIgnoreCase);

        SearchEngineFallbackIcon.Visibility = Visibility.Visible;
        SearchEngineIcon.Visibility = Visibility.Collapsed;
        try
        {
            SearchEngineIcon.Source = new BitmapImage(new Uri(engine.IconUrl));
            SearchEngineIcon.Visibility = Visibility.Visible;
            SearchEngineFallbackIcon.Visibility = Visibility.Collapsed;
        }
        catch (Exception)
        {
            SearchEngineIcon.Source = null;
        }
    }

    public void SetAddressSuggestions(IEnumerable<SearchSuggestion> suggestions)
    {
        List<SearchSuggestion> items = suggestions.ToList();
        AddressBar.ItemsSource = items;
        AddressBar.IsSuggestionListOpen = items.Count > 0;
    }

    public void ClearAddressSuggestions()
    {
        AddressBar.ItemsSource = null;
        AddressBar.IsSuggestionListOpen = false;
    }

    private SolidColorBrush ThemeBrush(string key) =>
        (SolidColorBrush)RootGrid.Resources[key];

    private void TabContainer_SizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateTabWidths();

    private void UpdateTabWidths()
    {
        if (_tabs.Count == 0)
            return;

        double spacing = TabStrip.Spacing;
        double tabAreaWidth = Math.Max(0, TabContainer.ActualWidth - NewTabButton.Width - TabContainer.ColumnSpacing);
        double tabWidth = Math.Min(
            180,
            Math.Max(0, (tabAreaWidth - spacing * (_tabs.Count - 1)) / _tabs.Count));
        double contentWidth = Math.Max(0, tabWidth - 6);
        double closeWidth = Math.Min(26, contentWidth * 0.36);
        double selectWidth = Math.Max(0, contentWidth - closeWidth);

        foreach (BrowserTab tab in _tabs)
            tab.SetWidth(tabWidth, selectWidth, closeWidth);

        TabViewport.Width = _tabs.Count * tabWidth + Math.Max(0, _tabs.Count - 1) * spacing;
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e) => HomeRequested?.Invoke(sender, e);
    private void BackButton_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(sender, e);
    private void ForwardButton_Click(object sender, RoutedEventArgs e) => ForwardRequested?.Invoke(sender, e);
    private void ReloadButton_Click(object sender, RoutedEventArgs e) => ReloadRequested?.Invoke(sender, e);
    private void NewTabButton_Click(object sender, RoutedEventArgs e) => NewTabRequested?.Invoke(sender, e);
    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => MinimizeRequested?.Invoke(sender, e);
    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => MaximizeRequested?.Invoke(sender, e);
    private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(sender, e);
    private void AddressBar_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            AddressSuggestionRequested?.Invoke(sender.Text);
    }

    private void AddressBar_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        string query = args.ChosenSuggestion is SearchSuggestion suggestion
            ? suggestion.Text
            : args.QueryText;
        AddressQuerySubmitted?.Invoke(query);
    }

    private void SearchEngineMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioMenuFlyoutItem { Tag: string engineId })
            AddressSearchEngineChanged?.Invoke(engineId);
    }

    private void AddressBar_GotFocus(object sender, RoutedEventArgs e) => AddressGotFocus?.Invoke(sender, e);
    private void AddressBar_LostFocus(object sender, RoutedEventArgs e) => AddressLostFocus?.Invoke(sender, e);

    private void SearchEngineIcon_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        SearchEngineIcon.Visibility = Visibility.Collapsed;
        SearchEngineFallbackIcon.Visibility = Visibility.Visible;
    }

    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                return match;

            T? descendant = FindDescendant<T>(child);
            if (descendant is not null)
                return descendant;
        }

        return null;
    }
}
