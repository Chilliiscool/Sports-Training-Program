// Module Name: SettingsPage
// Author: Kye Franken 
// Date Created: 20 / 06 / 2025
// Date Modified: 06 / 08 / 2025
// Description: Manages user preferences such as theme, units, notifications, company selection, and provides navigation to other settings.

using Microsoft.Maui.Controls;
using System;

namespace SportsTraining.Pages
{
    public partial class SettingsPage : ContentPage
    {
        // Keys for storing preferences
        const string NotificationsKey = "NotificationsEnabled";
        const string UnitsKey = "SelectedUnits";
        const string ThemeKey = "UserPreferredTheme";
        const string CompanieKey = "SelectedCompanie";

        public SettingsPage()
        {
            InitializeComponent();

            // Load saved theme preference and apply
            string savedTheme = Preferences.Get(ThemeKey, "Light");
            bool isDark = savedTheme == "Dark";
            ThemeToggleSwitch.IsToggled = isDark;
            Application.Current.UserAppTheme = isDark ? AppTheme.Dark : AppTheme.Light;

            // Load notification preference
            bool savedNotifications = Preferences.Get(NotificationsKey, true);
            NotificationsSwitch.IsToggled = savedNotifications;

            // Set up company picker options
            CompaniePicker.ItemsSource = new string[] { "Normal", "ETPA" };

            // Load saved company and select in picker
            string savedCompany = Preferences.Get("SelectedCompany", "Normal");
            int index = CompaniePicker.ItemsSource.IndexOf(savedCompany);
            CompaniePicker.SelectedIndex = index >= 0 ? index : 0;

            ApplyCompanieTheme(savedCompany);

            // Set up units picker options and load saved selection
            UnitsPicker.ItemsSource = new string[]
            {
                "Metric (kg, km)",
                "Imperial (lbs, miles)",
                "US Customary",
            };
            int savedIndex = Preferences.Get(UnitsKey, 0);
            UnitsPicker.SelectedIndex = savedIndex;
        }

        // Placeholder for account settings button click
        private void OnAccountSettingsClicked(object sender, EventArgs e)
        {
            DisplayAlert("Account Settings", "Go to account settings page.", "OK");
        }

        private void OnEditProfileClicked(object sender, EventArgs e)
        {
            DisplayAlert("Edit Profile", "Go to edit profile screen.", "OK");
        }

        private void OnChangePasswordClicked(object sender, EventArgs e)
        {
            DisplayAlert("Change Password", "Navigate to password reset.", "OK");
        }

        private void OnTrainingPreferencesClicked(object sender, EventArgs e)
        {
            DisplayAlert("Training Preferences", "Navigate to training preferences screen.", "OK");
        }

        private void OnGoalsClicked(object sender, EventArgs e)
        {
            DisplayAlert("Goals", "Go to goals screen.", "OK");
        }

        // Save notification toggle preference
        private void OnNotificationsToggled(object sender, ToggledEventArgs e)
        {
            Preferences.Set(NotificationsKey, e.Value);
        }

        // Handle company picker selection change
        private void OnCompaniePickerSelectedIndexChanged(object sender, EventArgs e)
        {
            if (CompaniePicker.SelectedIndex == -1)
                return;

            string selectedCompanie = CompaniePicker.Items[CompaniePicker.SelectedIndex];
            Preferences.Set("SelectedCompany", selectedCompanie);

            // Show or hide company-specific logo
            LogoImage.IsVisible = selectedCompanie == "ETPA";

            ApplyCompanieTheme(selectedCompanie);
        }

        // Set logo image based on selected company
        private void ApplyCompanieTheme(string companie)
        {
            if (companie == "ETPA")
            {
                LogoImage.Source = "etpa_logo.png";
            }
            else
            {
                // Optionally reset or hide logo for other companies
                LogoImage.Source = null;
            }
        }

        // Handle units picker selection change
        private void OnUnitsPickerSelectedIndexChanged(object sender, EventArgs e)
        {
            if (UnitsPicker.SelectedIndex == -1)
                return;

            int selectedIndex = UnitsPicker.SelectedIndex;
            Preferences.Set(UnitsKey, selectedIndex);

            // TODO: Implement app-wide unit update based on selectedUnit
            string selectedUnit = UnitsPicker.Items[selectedIndex];
        }

        private void OnAppSettingsClicked(object sender, EventArgs e)
        {
            DisplayAlert("App Settings", "Go to app settings page.", "OK");
        }

        private void OnOfflineModeToggled(object sender, ToggledEventArgs e)
        {
            bool isOffline = e.Value;
            string status = isOffline ? "enabled" : "disabled";
            // TODO: Implement offline mode functionality if needed
        }

        // Change app theme and save preference
        private void OnThemeToggleToggled(object sender, ToggledEventArgs e)
        {
            bool isDark = e.Value;
            Application.Current.UserAppTheme = isDark ? AppTheme.Dark : AppTheme.Light;
            Preferences.Set(ThemeKey, isDark ? "Dark" : "Light");
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

        // Logout clears the saved login cookie and returns user to login page
        private async void OnLogoutClicked(object sender, EventArgs e)
        {
            Preferences.Remove("VCP_Cookie");
            await Shell.Current.GoToAsync("//LoginPage");
        }

        // Helper method to check if notifications are enabled
        public static bool AreNotificationsEnabled() =>
            Preferences.Get(NotificationsKey, true);
    }
}
