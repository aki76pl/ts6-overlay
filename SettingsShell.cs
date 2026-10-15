using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using static TS6Overlay.L;

namespace TS6Overlay;

public sealed partial class SettingsWindow
{
    void BuildSettingsShell()
    {
        Width = 1380; Height = 860; MinWidth = 1000; MinHeight = 640;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        var root = new Grid { Margin = new Thickness(20) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(214) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(310) });

        var sidebar = new DockPanel { Margin = new Thickness(0, 0, 18, 0) };
        var brand = new StackPanel { Margin = new Thickness(12, 8, 0, 26) };
        brand.Children.Add(new TextBlock { Text = "TS6 / OVERLAY", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Fg });
        brand.Children.Add(new TextBlock { Text = "CONTROL CENTER", FontSize = 10, Foreground = AccentB, Margin = new Thickness(0, 7, 0, 0) });
        DockPanel.SetDock(brand, Dock.Top); sidebar.Children.Add(brand);
        var footer = new StackPanel { Margin = new Thickness(12, 16, 0, 8) };
        footer.Children.Add(new TextBlock { Text = "TS6 Overlay / " + Updater.Current, Foreground = Dim, FontSize = 11 });
        footer.Children.Add(new TextBlock { Text = "AKI76PL", Foreground = AccentB, FontSize = 11, Margin = new Thickness(0, 5, 0, 0) });
        DockPanel.SetDock(footer, Dock.Bottom); sidebar.Children.Add(footer);

        var nav = new StackPanel();
        var buttons = new List<Button>();
        string[] icons = ["\uE790", "\uE771", "\uE713", "\uE7F4", "\uE765", "\uE716", "\uE714", "\uE774", "\uE7FC", "\uE895"];
        var groups = new Dictionary<int, string> { [0] = "PERSONALIZACJA", [2] = "STEROWANIE", [6] = "POŁĄCZENIA", [9] = "SYSTEM" };
        for (int i = 0; i < _tabs.Items.Count; i++)
        {
            if (groups.TryGetValue(i, out var group))
                nav.Children.Add(new TextBlock { Text = T(group), Foreground = Dim, FontSize = 10, Margin = new Thickness(12, 16, 0, 8) });
            int index = i;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextBlock { Text = icons[i], FontFamily = new FontFamily("Segoe MDL2 Assets"), Width = 30, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = ((TabItem)_tabs.Items[i]).Header.ToString(), VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Content = row, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(13, 12, 10, 12), Margin = new Thickness(0, 2, 0, 2), BorderThickness = new Thickness(1), Background = Brushes.Transparent, Foreground = Dim };
            button.Click += (_, _) => SelectTab(index);
            buttons.Add(button); nav.Children.Add(button);
        }
        sidebar.Children.Add(new ScrollViewer { Content = nav, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        root.Children.Add(sidebar);

        var center = new DockPanel { Margin = new Thickness(0, 4, 16, 0) };
        Grid.SetColumn(center, 1); root.Children.Add(center);
        var heading = new StackPanel { Margin = new Thickness(8, 0, 8, 20) };
        heading.Children.Add(new TextBlock { Text = T("Ustawienia"), Foreground = Dim, FontSize = 12 });
        var title = new TextBlock { FontSize = 30, FontWeight = FontWeights.Bold, Foreground = Fg, Margin = new Thickness(0, 5, 0, 7) };
        heading.Children.Add(title);
        heading.Children.Add(new TextBlock { Text = T("Dostosuj nakładkę do swojego stylu gry."), Foreground = Dim, TextWrapping = TextWrapping.Wrap });
        DockPanel.SetDock(heading, Dock.Top); center.Children.Add(heading);

        // Keep the original TabControl as the selection and content owner. Existing
        // shortcuts and every settings callback continue to use the same indices.
        _tabs.Template = (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="TabControl">
              <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="16" ClipToBounds="True">
                <ContentPresenter x:Name="PART_SelectedContentHost" ContentSource="SelectedContent" Margin="1"/>
              </Border>
            </ControlTemplate>
            """);
        center.Children.Add(_tabs);

        var previewPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        previewPanel.Children.Add(new TextBlock { Text = T("PODGLĄD NA ŻYWO"), Foreground = AccentB, FontSize = 11, FontWeight = FontWeights.SemiBold });
        previewPanel.Children.Add(new TextBlock { Text = T("Twój overlay"), Foreground = Fg, FontSize = 23, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 8) });
        previewPanel.Children.Add(new TextBlock { Text = T("Sprawdź wygląd na przykładowych danych."), Foreground = Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 22) });
        previewPanel.Children.Add(new Border
        {
            Background = new LinearGradientBrush(Color.FromRgb(24, 39, 65), Color.FromRgb(10, 17, 30), 45),
            BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16), Padding = new Thickness(18), MinHeight = 280,
            Child = new Viewbox { Child = _preview, StretchDirection = StretchDirection.DownOnly, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left }
        });
        previewPanel.Children.Add(new Border { Background = Card, CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Margin = new Thickness(0, 16, 0, 0), Child = new TextBlock { Text = T("Zmiany zapisują się automatycznie.\n\nCtrl + przeciągnięcie — przesuń nakładkę\nCtrl + Shift + O — pokaż / ukryj"), Foreground = Dim, TextWrapping = TextWrapping.Wrap, FontSize = 12, LineHeight = 21 } });
        var previewScroll = new ScrollViewer { Content = previewPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(previewScroll, 2); root.Children.Add(previewScroll);
        void UpdateNavigation()
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                bool selected = i == _tabs.SelectedIndex;
                buttons[i].Background = selected ? InputBg : Brushes.Transparent;
                buttons[i].Foreground = selected ? AccentB : Dim;
                buttons[i].BorderBrush = selected ? Line : Brushes.Transparent;
                buttons[i].FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            }
            title.Text = (_tabs.SelectedItem as TabItem)?.Header.ToString() ?? "";
            if (SystemParameters.ClientAreaAnimation)
            {
                _tabs.BeginAnimation(OpacityProperty, new DoubleAnimation(.35, 1, TimeSpan.FromMilliseconds(220)));
                var transform = new TranslateTransform(); _tabs.RenderTransform = transform;
                transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
        }
        _tabs.SelectionChanged += (_, e) => { if (e.Source == _tabs) UpdateNavigation(); };
        _tabs.SelectedIndex = 0; UpdateNavigation();
        Content = root;
        InstallPolishedControls();
    }

    void InstallPolishedControls()
    {
        var style = new Style(typeof(Button), (Style)Resources[typeof(Button)]);
        style.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
              <Border x:Name="Surface" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="9" Padding="{TemplateBinding Padding}">
                <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center" RecognizesAccessKey="True"/>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Surface" Property="Opacity" Value="0.8"/></Trigger>
                <Trigger Property="IsPressed" Value="True"><Setter TargetName="Surface" Property="Opacity" Value="0.55"/></Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Surface" Property="BorderBrush" Value="#79A8FF"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter TargetName="Surface" Property="Opacity" Value="0.4"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
            """)));
        style.Setters.Add(new Setter(Control.MinHeightProperty, 34.0));
        Resources[typeof(Button)] = style;
        var combo = new Style(typeof(ComboBox));
        combo.Setters.Add(new Setter(Control.ForegroundProperty, Fg));
        combo.Setters.Add(new Setter(Control.BackgroundProperty, InputBg));
        combo.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ComboBox">
              <Grid>
                <Border Background="#1C2940" BorderBrush="#2B3B55" BorderThickness="1" CornerRadius="8"/>
                <ContentPresenter Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" Margin="12,7,30,7" VerticalAlignment="Center" IsHitTestVisible="False"/>
                <ToggleButton IsChecked="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}" Focusable="False" Background="Transparent" BorderThickness="0">
                  <ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="Transparent"><TextBlock Text="⌄" Foreground="#79A8FF" HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,12,0"/></Border></ControlTemplate></ToggleButton.Template>
                </ToggleButton>
                <Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" AllowsTransparency="True" Focusable="False" PopupAnimation="None">
                  <Border Background="#1C2940" BorderBrush="#4A6285" BorderThickness="1" CornerRadius="8" Padding="6" MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}" MaxHeight="320">
                    <ScrollViewer CanContentScroll="True"><ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/></ScrollViewer>
                  </Border>
                </Popup>
              </Grid>
            </ControlTemplate>
            """)));
        combo.Setters.Add(new Setter(Control.MinHeightProperty, 30.0));
        combo.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 4, 8, 4)));
        Resources[typeof(ComboBox)] = combo;
        var check = new Style(typeof(CheckBox));
        check.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(5, 3, 0, 3)));
        check.Setters.Add(new Setter(ContentControl.ContentTemplateProperty, (DataTemplate)XamlReader.Parse("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><TextBlock Text="{Binding}" TextWrapping="Wrap"/></DataTemplate>
            """)));
        Resources[typeof(CheckBox)] = check;
    }
}
