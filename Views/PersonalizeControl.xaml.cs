using Microsoft.UI.Xaml.Shapes;
using System;
using LinqqXrayVPN.Services;

namespace LinqqXrayVPN.Views
{
    public sealed partial class PersonalizeControl
    {
        public PersonalizeViewModel ViewModel { get; set; } = null!;
        public LocalizationService Loc => LocalizationService.Instance;
        public PersonalizeControl()
        {
            this.InitializeComponent();

            Loaded += (s, e) =>
            {
                if (ViewModel != null)
                    ViewModel.CurrentXamlRoot = this.XamlRoot;
            };
        }

        private async void ExportPresetButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var exportDir = await ViewModel.ExportPresetAsync();
                ExportSuccessInfoBar.Severity = InfoBarSeverity.Success;
                ExportSuccessInfoBar.Title = Loc.GetString("set9.10");
                ExportSuccessInfoBar.Message = $"({Loc.GetString("set9.11")} {exportDir})";
                ExportSuccessInfoBar.IsOpen = true;
            }
            catch (Exception ex)
            {
                ExportSuccessInfoBar.Severity = InfoBarSeverity.Error;
                ExportSuccessInfoBar.Title = Loc.GetString("set9.12");
                ExportSuccessInfoBar.Message = ex.Message;
                ExportSuccessInfoBar.IsOpen = true;
            }
        }
    }
}
