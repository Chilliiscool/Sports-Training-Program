using Microsoft.Maui.Controls;
using System;

namespace SportsTraining.Pages
{
    public partial class SettingsPage : ContentPage
    {
        public SettingsPage()
        {
            InitializeComponent();  // Loads your XAML content
        }

        // 💡 This is the method your button is trying to call
        private void OnAccountSettingsClicked(object sender, EventArgs e)
        {
            DisplayAlert("Account Settings", "Go to account settings page.", "OK");
        }

        // Add more button event methods if needed:
        private void OnEditProfileClicked(object sender, EventArgs e)
        {
            DisplayAlert("Edit Profile", "Go to edit profile screen.", "OK");
        }

        private void OnChangePasswordClicked(object sender, EventArgs e)
        {
            DisplayAlert("Change Password", "Navigate to password reset.", "OK");
        }

        // ...continue for each button from your XAML
        private void OnTrainingPreferencesClicked(object sender, EventArgs e)
        {
            DisplayAlert("Training Preferences", "Navigate to training preferences screen.", "OK");
        }
        private void OnGoalsClicked(object sender, EventArgs e)
        {
            DisplayAlert("Goals", "Go to goals screen.", "OK");
        }
        private void OnUnitsClicked(object sender, EventArgs e)
        {
            DisplayAlert("Units", "Open unit selection screen.", "OK");
        }
        private void OnNotificationsClicked(object sender, EventArgs e)
        {
            DisplayAlert("Notifications", "Manage notification preferences.", "OK");
        }
        private void OnToggleFeatureClicked(object sender, EventArgs e)
        {
            DisplayAlert("Enable/Disable", "Toggled a feature.", "OK");
        }

        private void OnAppSettingsClicked(object sender, EventArgs e)
        {
            DisplayAlert("App Settings", "Go to app settings page.", "OK");
        }
        private void OnOfflineModeClicked(object sender, EventArgs e)
        {
            DisplayAlert("Offline Mode", "Offline mode toggled or selected.", "OK");
        }
        private void OnThemeToggleClicked(object sender, EventArgs e)
        {
            var currentTheme = App.Current.UserAppTheme;

            // Toggle between Light and Dark
            if (currentTheme == AppTheme.Dark)
            {
                App.Current.UserAppTheme = AppTheme.Light;
            }
            else
            {
                App.Current.UserAppTheme = AppTheme.Dark;
            }

            

            //Save it for next launch
            Preferences.Set("AppTheme", App.Current.UserAppTheme.ToString());
        }

        private void OnLanguageClicked(object sender, EventArgs e)
        {
            DisplayAlert("Language", "Open language selection.", "OK");
        }
        private void OnPrivacyClicked(object sender, EventArgs e)
        {
            DisplayAlert("Privacy", "Open privacy settings.", "OK");
        }
        private void OnSupportClicked(object sender, EventArgs e)
        {
            DisplayAlert("Support", "Open support options.", "OK");
        }
        private void OnFaqClicked(object sender, EventArgs e)
        {
            DisplayAlert("FAQ", "Open frequently asked questions.", "OK");
        }
        private void OnReportIssueClicked(object sender, EventArgs e)
        {
            DisplayAlert("Report Issue", "Open issue reporting page.", "OK");
        }




    }
}
