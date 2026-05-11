using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Runtime.InteropServices;
using Windows.Graphics;
using LinqqXrayVPN.Helpers;
using LinqqXrayVPN.Models;
using LinqqXrayVPN.Services;

namespace LinqqXrayVPN.Views
{
    public sealed partial class CustomRulesWindow
    {
        private const int GWLP_HWNDPARENT = -8;

        [DllImport("User32.dll", CharSet = CharSet.Auto, EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("User32.dll", CharSet = CharSet.Auto, EntryPoint = "SetWindowLong")]
        private static extern IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private readonly Window _owner;
        private readonly StackPanel _rulesPanel;

        public CustomRulesViewModel ViewModel { get; }
        public LocalizationService Loc => LocalizationService.Instance;

        public CustomRulesWindow(Window owner, CustomRulesViewModel viewModel)
        {
            ViewModel = viewModel;
            this.InitializeComponent();

            _owner = owner;
            _rulesPanel = RulesPanel;

            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var scale = DpiHelper.GetWindowScale(hWnd);
            AppWindow.Resize(new SizeInt32(
                (int)Math.Round(620 * scale),
                (int)Math.Round(460 * scale)));

            AppWindow.Title = ViewModel.Loc.GetString("set4.1");
            AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;

            var presenter = OverlappedPresenter.CreateForDialog();
            SetWindowOwner(owner);
            presenter.IsModal = true;
            AppWindow.SetPresenter(presenter);
            AppWindow.Show();

            ViewModel.GetXamlRoot = () => Content?.XamlRoot;

            ViewModel.Rules.CollectionChanged += OnRulesChanged;

            ViewModel.ShowAddOrEditDialogRequested += OnShowAddOrEditDialogRequested;
            ViewModel.CloseRequested += OnCloseRequested;

            RefreshRulesList();

            _ = ViewModel.LoadAsync();

            this.Closed += OnClosed;
        }

        private void OnRulesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            DispatcherQueue.TryEnqueue(RefreshRulesList);
        }

        private void RefreshRulesList()
        {
            _rulesPanel.Children.Clear();

            foreach (var rule in ViewModel.Rules)
            {
                var item = CreateRuleItem(rule);
                _rulesPanel.Children.Add(item);
            }
        }

        private FrameworkElement CreateRuleItem(CustomRoutingRule rule)
        {
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },     
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto },      
                    new ColumnDefinition { Width = GridLength.Auto }       
                },
                ColumnSpacing = 12,
                Padding = new Thickness(4, 8, 4, 8)
            };

    
            var badgeGrid = new Grid { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(badgeGrid, 0);

            if (rule.DomainVisibility == Visibility.Visible)
            {
                var domainBorder = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Background = (Brush)Application.Current.Resources["SystemFillColorAttentionBackgroundBrush"],
                    Padding = new Thickness(8, 2, 8, 2)
                };
                domainBorder.Child = new TextBlock
                {
                    Text = ViewModel.Domain,
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
                };
                badgeGrid.Children.Add(domainBorder);
            }
            else
            {
                var ipBorder = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Background = (Brush)Application.Current.Resources["SystemFillColorSuccessBackgroundBrush"],
                    Padding = new Thickness(8, 2, 8, 2)
                };
                ipBorder.Child = new TextBlock
                {
                    Text = ViewModel.Ip,
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"]
                };
                badgeGrid.Children.Add(ipBorder);
            }
            grid.Children.Add(badgeGrid);

            var matchText = new TextBlock
            {
                Text = rule.Match,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(matchText, 1);
            grid.Children.Add(matchText);

            var outboundText = new TextBlock
            {
                Text = rule.OutboundTag,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(outboundText, 2);
            grid.Children.Add(outboundText);

            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2
            };
            Grid.SetColumn(buttonsPanel, 3);

            var editBtn = new Button
            {
                Padding = new Thickness(6),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
            };
            ToolTipService.SetToolTip(editBtn, ViewModel.Redact);
            editBtn.Content = new FontIcon { Glyph = "\uE70F", FontSize = 14 };
            editBtn.Click += (s, e) => ViewModel.EditRuleCommand.Execute(rule);
            buttonsPanel.Children.Add(editBtn);

            var deleteBtn = new Button
            {
                Padding = new Thickness(6),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
            };
            ToolTipService.SetToolTip(deleteBtn, ViewModel.Dell);
            deleteBtn.Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 };
            deleteBtn.Click += (s, e) => ViewModel.DeleteRuleCommand.Execute(rule);
            buttonsPanel.Children.Add(deleteBtn);

            grid.Children.Add(buttonsPanel);

            var border = new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                Margin = new Thickness(0, 2, 0, 2)
            };
            border.Child = grid;

            return border;
        }

        private void OnClosed(object sender, WindowEventArgs args)
        {
            ViewModel.Rules.CollectionChanged -= OnRulesChanged;
            ViewModel.ShowAddOrEditDialogRequested -= OnShowAddOrEditDialogRequested;
            ViewModel.CloseRequested -= OnCloseRequested;
            _owner.Activate();
        }

        private void OnCloseRequested(object? sender, EventArgs e) => Close();

        private async void OnShowAddOrEditDialogRequested(object? sender, CustomRoutingRule? existing)
        {
            var dialog = new AddRuleDialog(existing) { XamlRoot = Content.XamlRoot };
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary || dialog.Result is null) return;

            if (existing is null)
                ViewModel.AddNewRule(dialog.Result);
            else
                ViewModel.ReplaceRule(existing, dialog.Result);
        }

        private void UpdateGeoButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.UpdateGeoDataCommand.Execute(null);
        }

        private void SetWindowOwner(Window owner)
        {
            var ownerHwnd = WinRT.Interop.WindowNative.GetWindowHandle(owner);
            var ownedHwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);

            if (IntPtr.Size == 8)
                SetWindowLongPtr(ownedHwnd, GWLP_HWNDPARENT, ownerHwnd);
            else
                SetWindowLong(ownedHwnd, GWLP_HWNDPARENT, ownerHwnd);
        }
    }
}