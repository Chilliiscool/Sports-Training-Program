// Module Name: LoginPage
// Author: Kye Franken 
// Date Created: 11 / 07 / 2025
// Date Modified: 06 / 08 / 2025
// Description: Handles user login using email and password. Displays theme-aware password toggle icons,
// validates input, and saves login session cookie for use across the app.

using Microsoft.Maui.Controls;
using SportsTraining.Services;
using System;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SportsTraining.Pages
{
    public partial class LoginPage : ContentPage
    {
        // Tracks whether the password is currently visible
        bool isPasswordVisible = false;

        public LoginPage()
        {
            InitializeComponent();
            // Subscribe to theme changes to update icon dynamically
            Application.Current.RequestedThemeChanged += OnThemeChanged;
        }

        // Runs each time the page appears
        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // If already logged in, skip login and go to MainPage
            if (SessionManager.IsLoggedIn)
            {
                await Shell.Current.GoToAsync("//MainPage");
                return;
            }

            SetPasswordIcon();
        }

        // Runs when the page disappears
        protected override void OnDisappearing()
        {
            base.OnDisappearing();

            // Unsubscribe from theme changes
            Application.Current.RequestedThemeChanged -= OnThemeChanged;

            // Clear input fields and status
            emailEntry.Text = "";
            passwordEntry.Text = "";
            statusLabel.Text = "";
        }

        // Updates the password visibility icon when the app theme changes
        private void OnThemeChanged(object sender, AppThemeChangedEventArgs e)
        {
            SetPasswordIcon();
        }

        // Toggles password visibility and updates icon
        private void OnPasswordToggleClicked(object sender, EventArgs e)
        {
            isPasswordVisible = !isPasswordVisible;
            passwordEntry.IsPassword = !isPasswordVisible;
            SetPasswordIcon();
        }

        // Sets the password toggle icon based on theme and visibility
        private void SetPasswordIcon()
        {
            bool isDarkMode = Application.Current?.RequestedTheme == AppTheme.Dark;

            string iconName = (isPasswordVisible, isDarkMode) switch
            {
                (true, true) => "eye_open_light.png",
                (true, false) => "eye_open_dark.png",
                (false, true) => "eye_closed_light.png",
                _ => "eye_closed_dark.png"
            };

            passwordToggleBtn.Source = ImageSource.FromFile(iconName);
        }

        // Validates that the entered email is in a correct format
        private bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            var emailPattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";
            return Regex.IsMatch(email, emailPattern, RegexOptions.IgnoreCase);
        }

        // Handles login button click event
        private async void OnLoginClicked(object sender, EventArgs e)
        {
            statusLabel.Text = "";  // Clear previous status messages

            string email = emailEntry.Text?.Trim() ?? "";
            string password = passwordEntry.Text ?? "";

            // Check for empty inputs
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                statusLabel.Text = "Please enter both email and password.";
                return;
            }

            // Validate email format
            if (!IsValidEmail(email))
            {
                statusLabel.Text = "Please enter a valid email address.";
                return;
            }

            loginButton.IsEnabled = false;  // Prevent double submissions

            try
            {
                // Attempt login via Visual Coaching API
                string cookie = await VisualCoachingService.LoginAndGetCookie(email, password);

                if (!string.IsNullOrEmpty(cookie))
                {
                    // Save cookie for use across the app
                    SessionManager.SaveCookie(cookie);

                    Debug.WriteLine($"[Login] Cookie saved: {cookie}");
                    Debug.WriteLine($"[Login] Cookie from SessionManager: {SessionManager.GetCookie()}");

                    // Navigate to main page
                    await Shell.Current.GoToAsync("//MainPage");
                }
                else
                {
                    await DisplayAlert("Login Failed", "Invalid credentials or no cookie returned.", "OK");
                }
            }
            catch (Exception ex)
            {
                statusLabel.Text = "An error occurred. Please try again.";
                Debug.WriteLine($"[Login] Error: {ex.Message}");
            }
            finally
            {
                loginButton.IsEnabled = true;  // Re-enable login button
            }
        }
    }
}
