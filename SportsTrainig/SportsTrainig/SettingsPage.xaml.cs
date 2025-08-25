// Module Name: SettingsPage
// Author: Kye Franken 
// Date Created: 20 / 06 / 2025
// Date Modified: 11 / 08 / 2025
// Description: Manages user preferences such as theme, units, notifications, company selection, and provides navigation to other settings.
// Fixes: adds missing using for Preferences, standardizes SelectedCompany key, corrects picker index logic, uses SessionManager on logout.

using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using SportsTraining.Services;
using System;

namespace SportsTraining.Pages
{
    public partial class SettingsPage : ContentPage
    {
        // Keys for storing preferences
        const string NotificationsKey = "NotificationsEnabled";
        const string UnitsKey = "SelectedUnits";
        const string ThemeKey = "UserPreferredTheme";
        const string CompanyKey = "SelectedCompany";

        private readonly string[] companyItems = new[] { "Normal", "ETPA" };
        private readonly string[] unitItems = new[]
        {
            "Metric (kg, km)",
            "Imperial (lbs, miles)",
            "US Customary",
        };

        public SettingsPage()
        {
            InitializeComponent();

            // Theme
            string savedTheme = Preferences.Get(ThemeKey, "Light");
            bool isDark = savedTheme == "Dark";
            ThemeToggleSwitch.IsToggled = isDark;
            Application.Current.UserAppTheme = isDark ? AppTheme.Dark : AppTheme.Light;

            // Notifications
            bool savedNotifications = Preferences.Get(NotificationsKey, true);
            NotificationsSwitch.IsToggled = savedNotifications;

            // Company picker
            CompanyPicker.ItemsSource = companyItems;
            string savedCompany = Preferences.Get(CompanyKey, "Normal");
            int index = Array.IndexOf(companyItems, savedCompany);
            CompanyPicker.SelectedIndex = index >= 0 ? index : 0;
            ApplyCompanyTheme(savedCompany);
            LogoImage.IsVisible = savedCompany == "ETPA";

            // Units picker
            UnitsPicker.ItemsSource = unitItems;
            int savedIndex = Preferences.Get(UnitsKey, 0);
            UnitsPicker.SelectedIndex = (savedIndex >= 0 && savedIndex < unitItems.Length) ? savedIndex : 0;
        }

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

        private void OnNotificationsToggled(object sender, ToggledEventArgs e)
        {
            Preferences.Set(NotificationsKey, e.Value);
        }

        private void OnCompanyPickerSelectedIndexChanged(object sender, EventArgs e)
        {
            if (CompanyPicker.SelectedIndex == -1)
                return;

            string selectedCompany = companyItems[CompanyPicker.SelectedIndex];
            Preferences.Set(CompanyKey, selectedCompany);

            // Show or hide company-specific logo
            LogoImage.IsVisible = selectedCompany == "ETPA";
            ApplyCompanyTheme(selectedCompany);
        }

        private void ApplyCompanyTheme(string company)
        {
            if (company == "ETPA")
                LogoImage.Source = "etpa_logo.png";
            else
                LogoImage.Source = null;
        }

        private void OnUnitsPickerSelectedIndexChanged(object sender, EventArgs e)
        {
            if (UnitsPicker.SelectedIndex == -1) return;
            Preferences.Set(UnitsKey, UnitsPicker.SelectedIndex);
        }

        private void OnAppSettingsClicked(object sender, EventArgs e)
        {
            DisplayAlert("App Settings", "Go to app settings page.", "OK");
        }

        private void OnOfflineModeToggled(object sender, ToggledEventArgs e)
        {
            bool isOffline = e.Value;
            _ = isOffline; // TODO: Implement offline mode functionality if needed
        }

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

        private async void OnReportIssueClicked(object sender, EventArgs e)
        {
            await DisplayAlert("Report Issue", "Open issue reporting page.", "OK");
        }

        // Logout via SessionManager for consistency
        private async void OnLogoutClicked(object sender, EventArgs e)
        {
            SessionManager.ClearCookie();
            await Shell.Current.GoToAsync("//LoginPage");
        }

        public static bool AreNotificationsEnabled() =>
            Preferences.Get(NotificationsKey, true);
        private async void OnDiarySettingsClicked(object sender, EventArgs e)
        {
            string currentEmail = Preferences.Get("UserEmail", "");

            string message = string.IsNullOrEmpty(currentEmail)
                ? "No user email is currently stored for diary entries."
                : $"Current diary email: {currentEmail}";

            bool changeEmail = await DisplayAlert("Diary Settings", message,
                "Change Email", "Cancel");

            if (changeEmail)
            {
                string newEmail = await DisplayPromptAsync("Diary Email",
                    "Enter email address for diary entries:",
                    placeholder: currentEmail);

                if (!string.IsNullOrEmpty(newEmail))
                {
                    Preferences.Set("UserEmail", newEmail);
                    await DisplayAlert("Success", $"Diary email updated to: {newEmail}", "OK");
                }
            }
        }

        private async void OnViewDiaryHistoryClicked(object sender, EventArgs e)
        {
            string userEmail = Preferences.Get("UserEmail", "");

            if (string.IsNullOrEmpty(userEmail))
            {
                await DisplayAlert("No User Set", "Please set your email address first.", "OK");
                return;
            }

            // Could navigate to a diary history page or show recent entries
            await DisplayAlert("Diary History", "Diary history feature coming soon!", "OK");
        }
        private void OnDiaryAutoPromptToggled(object sender, ToggledEventArgs e)
        {
            bool autoPrompt = e.Value;
            Preferences.Set("DiaryAutoPrompt", autoPrompt);
        }
    }
}
