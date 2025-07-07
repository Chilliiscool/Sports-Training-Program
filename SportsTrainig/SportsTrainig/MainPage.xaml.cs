using Microsoft.Maui.Controls;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Maui.Storage;
using SportsTraining.Models;

namespace SportsTraining.Pages
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
            LoadUserPrograms();
        }

        private async void LoadUserPrograms()
        {
            try
            {
                // Get the stored token
                string token = await SecureStorage.Default.GetAsync("auth_token");

                if (string.IsNullOrEmpty(token))
                {
                    await DisplayAlert("Error", "You must log in first.", "OK");
                    await Navigation.PopAsync(); // Go back to login
                    return;
                }

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                // ✅ Replace with actual user ID or let API return current user’s data
                string userId = "currentUserId";  // Placeholder
                var programs = await client.GetFromJsonAsync<List<WorkoutProgram>>(
                    $"https://cloud.visualcoaching2.com/api/users/{userId}/programs"
                );

                // Set to ListView
                ProgramsListView.ItemsSource = programs ?? new List<WorkoutProgram>();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Could not load programs: {ex.Message}", "OK");
            }
        }
    }
}

