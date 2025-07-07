using System;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Maui.Storage;

namespace SportsTraining.Pages
{
    public partial class LoginPage : ContentPage
    {
        public LoginPage()
        {
            InitializeComponent();
        }

        private async void OnLoginClicked(object sender, EventArgs e)
        {
            ErrorLabel.IsVisible = false;

            var loginRequest = new
            {
                Email = EmailEntry.Text,
                Password = PasswordEntry.Text
            };

            using var client = new HttpClient();

            try
            {
                var response = await client.PostAsJsonAsync("https://cloud.visualcoaching2.com/Account/LogOn", loginRequest);

                if (response.IsSuccessStatusCode)
                {
                    var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>();

                    if (loginResponse != null && !string.IsNullOrEmpty(loginResponse.Token))
                    {
                        // Save token securely on device
                        await SecureStorage.Default.SetAsync("auth_token", loginResponse.Token);

                        // Navigate to MainPage after successful login
                        await Navigation.PushAsync(new MainPage());
                    }
                    else
                    {
                        ErrorLabel.Text = "Login failed: Invalid server response.";
                        ErrorLabel.IsVisible = true;
                    }
                }
                else
                {
                    ErrorLabel.Text = "Login failed: Incorrect email or password.";
                    ErrorLabel.IsVisible = true;
                }
            }
            catch (Exception ex)
            {
                ErrorLabel.Text = $"Error: {ex.Message}";
                ErrorLabel.IsVisible = true;
            }
        }
    }

    public class LoginResponse
    {
        public string Token { get; set; }
    }
}
