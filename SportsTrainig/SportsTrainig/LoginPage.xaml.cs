using Microsoft.Maui.Controls;
using SportsTraining.Services;
using System;
using System.Diagnostics;

namespace SportsTraining.Pages
{
    public partial class LoginPage : ContentPage
    {
        bool isPasswordVisible = false;

        public LoginPage()
        {
            InitializeComponent();
            Application.Current.RequestedThemeChanged += OnThemeChanged;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            if (SessionManager.IsLoggedIn)
            {
                await Shell.Current.GoToAsync("//MainPage");
                return;
            }

            SetPasswordIcon();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            Application.Current.RequestedThemeChanged -= OnThemeChanged;

            emailEntry.Text = "";
            passwordEntry.Text = "";
            statusLabel.Text = "";
        }

        private void OnThemeChanged(object sender, AppThemeChangedEventArgs e)
        {
            SetPasswordIcon();
        }

        private void OnPasswordToggleClicked(object sender, EventArgs e)
        {
            isPasswordVisible = !isPasswordVisible;
            passwordEntry.IsPassword = !isPasswordVisible;
            SetPasswordIcon();
        }

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

        private async void OnLoginClicked(object sender, EventArgs e)
        {
            string email = emailEntry.Text?.Trim() ?? "";
            string password = passwordEntry.Text ?? "";

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            {
                statusLabel.Text = "Please enter both email and password.";
                return;
            }

            try
            {
                string cookie = await VisualCoachingService.LoginAndGetCookie(email, password);

                if (!string.IsNullOrEmpty(cookie))
                {
                    SessionManager.SaveCookie(cookie);
                    Debug.WriteLine($"[Login] Cookie saved: {cookie}");
                    Debug.WriteLine($"[Login] Cookie from SessionManager: {SessionManager.GetCookie()}");

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
                Debug.WriteLine($"[Login] Error: {ex.Message}");  // <-- Add this line
            }
        }
    }
}
