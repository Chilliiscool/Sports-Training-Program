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
        }

        private void OnPasswordToggleClicked(object sender, EventArgs e)
        {
            isPasswordVisible = !isPasswordVisible;
            passwordEntry.IsPassword = !isPasswordVisible;
            passwordToggleBtn.Source = isPasswordVisible ? "eye_open.png" : "eye_closed.png";
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

            statusLabel.Text = "";

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
    }
}
