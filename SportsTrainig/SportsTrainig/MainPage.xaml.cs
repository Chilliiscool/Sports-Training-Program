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
        public ObservableCollection<ProgramSession> TodayPrograms { get; set; } = new();
        private bool isLoading = false;

        public MainPage()
        {
            InitializeComponent();
            ProgramsListView.ItemsSource = TodayPrograms;
        }

        protected override async void OnAppearing()
        {
            Debug.WriteLine("[Debug Test] MainPage OnAppearing called.");

            base.OnAppearing();

            LogoImage.IsVisible = Preferences.Get("SelectedCompany", "Normal") == "ETPA";

            if (!SessionManager.IsLoggedIn)
            {
                await Shell.Current.GoToAsync("//LoginPage");
                return;
            }

            await LoadUserProgramsAsync();
        }

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
                isLoading = false;
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

                        if (seenKeys.Add(key)) // returns true if not already in set
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
                Debug.WriteLine("[MainPage] Session expired, clearing cookie.");
                await DisplayAlert("Session Expired", "Please log in again.", "OK");
                SessionManager.ClearCookie();
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

        private async void ProgramsListView_ItemSelected(object sender, SelectedItemChangedEventArgs e)
        {
            if (e.SelectedItem is ProgramSession selected)
            {
                ProgramsListView.SelectedItem = null;

                var encodedUrl = Uri.EscapeDataString(selected.Url);
                await Shell.Current.GoToAsync($"{nameof(TrainingPage)}?url={encodedUrl}");
            }
        }

        private void ShowProgramList()
        {
            LoadingIndicator.IsVisible = false;
            LoadingIndicator.IsRunning = false;
            ProgramsListView.IsVisible = true;
        }

        // ** Pull-to-Refresh handler **
        private async void ProgramsListView_Refreshing(object sender, EventArgs e)
        {
            await LoadUserProgramsAsync();
        }
    }
}
