// Module Name: MainPage
// Author: Kye Franken 
// Date Created: 19 / 06 / 2025
// Date Modified: 11 / 08 / 2025
// Description: Displays today's workout programs fetched from the Visual Coaching API,
// handles user session validation, and supports navigation to detailed training pages.
// Slightly softer handling around unauthorized to pair with PM-session fixes.

using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Storage;
using SportsTraining.Services;
using SportsTraining.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SportsTraining.Pages
{
    public partial class MainPage : ContentPage
    {
        // Observable collection bound to the ListView to display workout sessions
        public ObservableCollection<ProgramSession> TodayPrograms { get; set; } = new();

        // Flag to prevent overlapping load operations
        private bool isLoading = false;

        public MainPage()
        {
            InitializeComponent();

            // Bind the ObservableCollection to the ListView
            ProgramsListView.ItemsSource = TodayPrograms;
        }

        // Triggered when page appears, ensures user is logged in and loads programs
        protected override async void OnAppearing()
        {
            Debug.WriteLine("[Debug Test] MainPage OnAppearing called.");

            base.OnAppearing();

            // Show company logo if selected company is ETPA
            LogoImage.IsVisible = Preferences.Get("SelectedCompany", "Normal") == "ETPA";

            // Redirect to login if user is not authenticated
            if (!SessionManager.IsLoggedIn)
            {
                await Shell.Current.GoToAsync("//LoginPage");
                return;
            }

            // Load today's workout sessions asynchronously
            await LoadUserProgramsAsync();
        }

        // Loads the workout sessions from the API and updates the UI
        private async Task LoadUserProgramsAsync()
        {
            if (isLoading) return;
            isLoading = true;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                TodayPrograms.Clear();
                LoadingIndicator.IsVisible = true;
                LoadingIndicator.IsRunning = true;
                ProgramsListView.IsVisible = false;
            });

            string cookie = SessionManager.GetCookie();
            Debug.WriteLine($"[MainPage] Cookie on OnAppearing: {cookie}");

            if (string.IsNullOrEmpty(cookie))
            {
                Debug.WriteLine("[MainPage] No cookie found. Prompt login.");
                await DisplayAlert("Login Required", "Please login to view your program.", "OK");
                ShowProgramList();
                isLoading = false;  // Reset flag here before return
                return;
            }

            try
            {
                string today = DateTime.Today.ToString("yyyy-MM-dd");
                var sessions = await VisualCoachingService.GetSessionsForDate(cookie, today);

                var seenKeys = new HashSet<string>();

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    TodayPrograms.Clear();

                    foreach (var brief in sessions)
                    {
                        string key = $"{brief.SessionTitle}_{brief.Url}";

                        if (seenKeys.Add(key)) // Only add if not already present
                        {
                            TodayPrograms.Add(new ProgramSession
                            {
                                SessionTitle = brief.SessionTitle,
                                Url = brief.Url
                            });
                        }
                    }

                    ShowProgramList();
                });
            }
            catch (UnauthorizedAccessException)
            {
                Debug.WriteLine("[MainPage] Session expired.");
                // Clear cookie and push to Login
                SessionManager.ClearCookie();
                await DisplayAlert("Session Expired", "Please log in again.", "OK");
                await Shell.Current.GoToAsync("//LoginPage");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainPage] Error loading sessions: {ex.Message}");
                await DisplayAlert("Error", $"Failed to load program: {ex.Message}", "OK");
                ShowProgramList();
            }
            finally
            {
                isLoading = false;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    LoadingIndicator.IsVisible = false;
                    LoadingIndicator.IsRunning = false;
                    ProgramsListView.IsVisible = true;
                    ProgramsListView.IsRefreshing = false;  // Stop Pull-to-Refresh animation
                });
            }
        }

        // Handles selection of a program and navigates to the training page with URL parameter
        private async void ProgramsListView_ItemSelected(object sender, SelectedItemChangedEventArgs e)
        {
            if (e.SelectedItem is ProgramSession selected)
            {
                ProgramsListView.SelectedItem = null;

                Debug.WriteLine($"Selected session URL: {selected.Url}");

                var encodedUrl = Uri.EscapeDataString(selected.Url);
                await Shell.Current.GoToAsync($"{nameof(TrainingPage)}?url={encodedUrl}");
            }
        }

        // Helper method to toggle visibility after loading is complete
        private void ShowProgramList()
        {
            LoadingIndicator.IsVisible = false;
            LoadingIndicator.IsRunning = false;
            ProgramsListView.IsVisible = true;
        }

        // Pull-to-refresh handler to reload the programs list
        private async void ProgramsListView_Refreshing(object sender, EventArgs e)
        {
            await LoadUserProgramsAsync();
        }
    }
}
