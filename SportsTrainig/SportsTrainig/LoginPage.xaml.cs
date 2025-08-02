using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using SportsTraining.Services;

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

        protected override void OnAppearing()
        {
            base.OnAppearing();
            SetPasswordIcon(); // ✅ Now runs after the visual tree is built
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            Application.Current.RequestedThemeChanged -= OnThemeChanged;
        }

        private void OnThemeChanged(object sender, AppThemeChangedEventArgs e)
        {
            SetPasswordIcon(); // Refresh icon if theme changes while app is running
        }

        private void OnPasswordToggleClicked(object sender, EventArgs e)
        {
            isPasswordVisible = !isPasswordVisible;
            passwordEntry.IsPassword = !isPasswordVisible;
            SetPasswordIcon(); // Refresh icon on toggle
        }

        private void SetPasswordIcon()
        {
            bool isDarkMode = Application.Current?.RequestedTheme == AppTheme.Dark;

            string iconName;

            if (isPasswordVisible)
                iconName = isDarkMode ? "eye_open_light.png" : "eye_open_dark.png";
            else
                iconName = isDarkMode ? "eye_closed_light.png" : "eye_closed_dark.png";

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
                    Preferences.Set("VCP_Cookie", cookie);
                    await Shell.Current.GoToAsync("//MainPage");
                }
                else
                {
                    statusLabel.Text = "Login failed. Please check your email and password.";
                }
            }
            catch (Exception ex)
            {
                statusLabel.Text = "An error occurred. Please try again.";
                System.Diagnostics.Debug.WriteLine($"Login error: {ex.Message}");
            }
        }
    }
}
