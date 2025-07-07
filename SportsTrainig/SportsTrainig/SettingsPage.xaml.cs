using Microsoft.Maui.Controls;
using System;

namespace SportsTraining.Pages
{
    public partial class SettingsPage : ContentPage
    {
        const string NotificationsKey = "NotificationsEnabled";
        const string UnitsKey = "SelectedUnits";
        public SettingsPage()
        {
            InitializeComponent();  // Loads your XAML content
            ThemeToggleSwitch.IsToggled = Application.Current.RequestedTheme == AppTheme.Dark;
            bool savedNotifications = Preferences.Get(NotificationsKey, true);
            NotificationsSwitch.IsToggled = savedNotifications;
            UnitsPicker.ItemsSource = new string[]
            {
                "Metric (kg, km)",
                "Imperial (lbs, miles)",
                "US Customary",
            };
            int savedIndex = Preferences.Get(UnitsKey, 0);
            UnitsPicker.SelectedIndex = savedIndex;
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
        
        private void OnNotificationsToggled(object sender, ToggledEventArgs e)
        {
            bool notificationsEnabled = e.Value;

            // Save preference persistently
            Preferences.Set(NotificationsKey, notificationsEnabled);
        }
        private void OnUnitsPickerSelectedIndexChanged(object sender, EventArgs e)
        {
            if (UnitsPicker.SelectedIndex == -1)
                return;

            int selectedIndex = UnitsPicker.SelectedIndex;

            // Save selected index persistently
            Preferences.Set(UnitsKey, selectedIndex);

            string selectedUnit = UnitsPicker.Items[selectedIndex];
         

            // TODO: Add logic to apply unit changes throughout your app
        }

        private void OnAppSettingsClicked(object sender, EventArgs e)
        {
            DisplayAlert("App Settings", "Go to app settings page.", "OK");
        }
        private void OnOfflineModeToggled(object sender, ToggledEventArgs e)
        {
            bool isOffline = e.Value;
            string status = isOffline ? "enabled" : "disabled";
        }

        private void OnThemeToggleToggled(object sender, ToggledEventArgs e)
        {
            bool isDark = e.Value;

            // Change app theme
            Application.Current.UserAppTheme = isDark ? AppTheme.Dark : AppTheme.Light;

            // Save the setting using Preferences
            Preferences.Set("UserPreferredTheme", isDark ? "Dark" : "Light");
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
        public static bool AreNotificationsEnabled() =>
            Preferences.Get(NotificationsKey, true);




    }
}
