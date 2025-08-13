// Module Name: MainPage
// Author: Kye Franken 
// Date Created: 19 / 06 / 2025
// Date Modified: 13 / 08 / 2025
// Description: Displays today's workout programs fetched from the Visual Coaching API,
// groups by ClientGroup then sorts by ClientName, and navigates to the Training tab with a URL.

using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Storage;
using SportsTraining.Services;
using SportsTraining.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq; // <-- Needed for GroupBy/OrderBy/FirstOrDefault
using System.Threading.Tasks;

namespace SportsTraining.Pages
{
    // Helper grouping class for ListView grouping
    public class Grouping<TKey, TItem> : ObservableCollection<TItem>
    {
        public TKey Key { get; }
        public Grouping(TKey key, IEnumerable<TItem> items) : base(items)
        {
            Key = key;
        }
    }

    public partial class MainPage : ContentPage
    {
        // Flat collection (optional to keep around if needed elsewhere)
        public ObservableCollection<ProgramSession> TodayPrograms { get; set; } = new();

        // Grouped collection bound to the ListView
        public ObservableCollection<Grouping<string, ProgramSession>> GroupedPrograms { get; set; } = new();

        private bool isLoading = false;

        public MainPage()
        {
            InitializeComponent();

            // Bind the grouped collection
            ProgramsListView.ItemsSource = GroupedPrograms;
        }

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

            await LoadUserProgramsAsync();
        }

        private async Task LoadUserProgramsAsync()
        {
            if (isLoading) return;
            isLoading = true;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                TodayPrograms.Clear();
                GroupedPrograms.Clear();
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

                var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var temp = new List<ProgramSession>();

                foreach (var brief in sessions)
                {
                    // Build a unique key including client to avoid dupes
                    string key = $"{brief.SessionTitle}_{brief.Url}_{brief.ClientName}_{brief.ClientGroup}";
                    if (!seenKeys.Add(key)) continue;

                    temp.Add(new ProgramSession
                    {
                        SessionTitle = brief.SessionTitle,
                        Url = brief.Url,
                        ClientName = brief.ClientName,
                        ClientGroup = string.IsNullOrWhiteSpace(brief.ClientGroup) ? "Ungrouped" : brief.ClientGroup
                    });
                }

                // Sort items by ClientName within each group, and group by ClientGroup
                var grouped = temp
                    .GroupBy(p => p.ClientGroup)
                    .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(g =>
                        new Grouping<string, ProgramSession>(
                            g.Key,
                            g.OrderBy(p => p.ClientName, StringComparer.OrdinalIgnoreCase)
                        ));

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    TodayPrograms.Clear();
                    foreach (var item in temp) TodayPrograms.Add(item);

                    GroupedPrograms.Clear();
                    foreach (var grp in grouped) GroupedPrograms.Add(grp);

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
                    ProgramsListView.IsRefreshing = false;
                });
            }
        }

        // Selection handler (kept your PM->AM link logic)
        private async void ProgramsListView_ItemSelected(object sender, SelectedItemChangedEventArgs e)
        {
            if (e.SelectedItem is ProgramSession selected)
            {
                // Clear selection highlight
                ((ListView)sender).SelectedItem = null;

                Debug.WriteLine($"Selected session URL before check: {selected.Url}");

                // If PM clicked, use AM link instead (when available)
                string finalUrl = selected.Url;
                if (selected.SessionTitle?.Contains("PM", StringComparison.OrdinalIgnoreCase) == true)
                {
                    var amSession = TodayPrograms
                        .FirstOrDefault(p => p.SessionTitle?.Contains("AM", StringComparison.OrdinalIgnoreCase) == true
                                             && p.ClientName?.Equals(selected.ClientName, StringComparison.OrdinalIgnoreCase) == true);
                    if (amSession != null)
                    {
                        finalUrl = amSession.Url;
                        Debug.WriteLine($"PM session clicked — using AM URL: {finalUrl}");
                    }
                }

                var encodedUrl = Uri.EscapeDataString(finalUrl);

                // Jump to Training tab with the URL (no back button)
                await Shell.Current.GoToAsync($"//{nameof(TrainingPage)}?url={encodedUrl}");
            }
        }

        private void ShowProgramList()
        {
            LoadingIndicator.IsVisible = false;
            LoadingIndicator.IsRunning = false;
            ProgramsListView.IsVisible = true;
        }

        private async void ProgramsListView_Refreshing(object sender, EventArgs e)
        {
            await LoadUserProgramsAsync();
        }
    }
}
