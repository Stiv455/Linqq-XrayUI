using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System;
using LinqqXrayVPN.Models;
using LinqqXrayVPN.Services;

namespace LinqqXrayVPN.Views
{
    public sealed partial class ManageSubscriptionsDialog : UserControl
    {
        public LocalizationService Loc => LocalizationService.Instance;
        public ManageSubscriptionsViewModel ViewModel { get; }

        private readonly StackPanel _subscriptionsPanel;

        public ManageSubscriptionsDialog(ManageSubscriptionsViewModel vm)
        {
            ViewModel = vm;
            InitializeComponent();

            _subscriptionsPanel = SubscriptionsPanel;

            ViewModel.Subscriptions.CollectionChanged += OnSubscriptionsChanged;

            RefreshSubscriptionsList();
        }

        private void OnSubscriptionsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            DispatcherQueue.TryEnqueue(() => RefreshSubscriptionsList());
        }

        private void RefreshSubscriptionsList()
        {
            _subscriptionsPanel.Children.Clear();

            foreach (var sub in ViewModel.Subscriptions)
            {
                var item = CreateSubscriptionItem(sub);
                _subscriptionsPanel.Children.Add(item);
            }
        }

        private FrameworkElement CreateSubscriptionItem(SubscriptionEntry sub)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnSpacing = 4;

            var infoPanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(infoPanel, 0);

            infoPanel.Children.Add(new TextBlock
            {
                Text = sub.Name,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            infoPanel.Children.Add(new TextBlock
            {
                Text = sub.Url,
                FontSize = 12,
                Opacity = 0.65,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            if (!sub.HasError)
            {
                infoPanel.Children.Add(new TextBlock
                {
                    Text = sub.LastUpdatedText,
                    FontSize = 12,
                    Opacity = 0.7
                });
            }
            else
            {
                infoPanel.Children.Add(new TextBlock
                {
                    Text = sub.LastErrorText,
                    FontSize = 12,
                    Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
            }

            grid.Children.Add(infoPanel);

            var refreshBtn = CreateRefreshButton(sub);
            Grid.SetColumn(refreshBtn, 1);
            grid.Children.Add(refreshBtn);

            var deleteBtn = CreateDeleteButton(sub);
            Grid.SetColumn(deleteBtn, 2);
            grid.Children.Add(deleteBtn);

            border.Child = grid;
            return border;
        }

        private Button CreateRefreshButton(SubscriptionEntry sub)
        {
            var btn = new Button
            {
                Padding = new Thickness(6),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
            };

            ToolTipService.SetToolTip(btn, ViewModel.RefreshTooltip);

            var contentGrid = new Grid { Width = 16, Height = 16 };
            contentGrid.Children.Add(new FontIcon
            {
                Glyph = "\uE895",
                FontSize = 14
            });

            btn.Content = contentGrid;
            btn.Click += (s, e) => ViewModel.RefreshSubscriptionCommand.Execute(sub);

            return btn;
        }

        private Button CreateDeleteButton(SubscriptionEntry sub)
        {
            var btn = new Button
            {
                Padding = new Thickness(6),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
            };

            ToolTipService.SetToolTip(btn, ViewModel.DeleteTooltip);

            btn.Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 };

            var flyout = new Flyout { Placement = FlyoutPlacementMode.Bottom };

            var stack = new StackPanel { Spacing = 12, MaxWidth = 240 };

            stack.Children.Add(new TextBlock
            {
                Text = ViewModel.DeleteTitle,
                TextWrapping = TextWrapping.Wrap,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            stack.Children.Add(new TextBlock
            {
                Text = ViewModel.DeleteMessage,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Opacity = 0.7
            });

            var confirmBtn = new Button
            {
                Content = ViewModel.DeleteConfirm,
                Style = (Style)Application.Current.Resources["DangerAccentButtonStyle"],
                HorizontalAlignment = HorizontalAlignment.Right
            };

            confirmBtn.Click += (s, e) =>
            {
                flyout.Hide();
                ViewModel.DeleteSubscriptionCommand.Execute(sub);
            };

            stack.Children.Add(confirmBtn);
            flyout.Content = stack;
            btn.Flyout = flyout;

            return btn;
        }

        ~ManageSubscriptionsDialog()
        {
            if (ViewModel?.Subscriptions != null)
                ViewModel.Subscriptions.CollectionChanged -= OnSubscriptionsChanged;
        }
    }
}