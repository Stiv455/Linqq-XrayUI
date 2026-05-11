using LinqqXrayVPN.Helpers;
using LinqqXrayVPN.Models;
using LinqqXrayVPN.Services;

namespace LinqqXrayVPN.Views
{
    public sealed partial class AddRuleDialog
    {
        public CustomRoutingRule? Result { get; private set; }
        public LocalizationService Loc => LocalizationService.Instance;
        public AddRuleDialog() : this(null) { }

        public AddRuleDialog(CustomRoutingRule? existing)
        {
            this.InitializeComponent();
            this.RequestedTheme = ThemeHelper.ActualTheme;

            if (existing != null)
            {
                Title             = Loc.GetString("set9.1");
                PrimaryButtonText = Loc.GetString("set9.2");

                TypeComboBox.SelectedIndex     = existing.Type == "ip" ? 1 : 0;
                MatchTextBox.Text              = existing.Match;
                OutboundComboBox.SelectedIndex = existing.OutboundTag switch
                {
                    "direct" => 1,
                    "block"  => 2,
                    _        => 0,   // proxy
                };
            }

            this.PrimaryButtonClick += OnPrimaryClick;
        }

        private void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            var match = MatchTextBox.Text?.Trim() ?? "";
            if (match.Length == 0)
            {
                ErrorText.Visibility = Visibility.Visible;
                args.Cancel = true;
                return;
            }

            var typeTag     = (TypeComboBox.SelectedItem     as ComboBoxItem)?.Tag as string ?? "domain";
            var outboundTag = (OutboundComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "proxy";

            Result = new CustomRoutingRule
            {
                Type        = typeTag,
                Match       = match,
                OutboundTag = outboundTag,
                IsEnabled   = true,
            };
        }
    }
}
