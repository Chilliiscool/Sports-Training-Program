// Module Name: MainPage
// Author: Kye Franken 
// Date Created: 19 / 06 / 2025
// Date Modified: 15 / 08 / 2025
// Description: Displays today's workout programs fetched from the Visual Coaching API,
// groups by ClientGroup then sorts by ClientName, and navigates to the Training tab with a URL.
// This version keeps ONLY the first URL (lowest `session=`) per (ClientName, Name) and ignores others.
// Update (15/08): Re-normalizes to the FIRST/AM link immediately before navigation and
//                 hard-forces session=0 (AM) and i=0 so broken PM links are never used.

using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Storage;
using SportsTraining.Services;
using SportsTraining.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Reflection; // for reflection-based getters

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

                // Keep only the FIRST url (lowest `session=`) for each (ClientName, Name) pair.
                var bestByPersonAndName = new Dictionary<string, ProgramSession>(StringComparer.OrdinalIgnoreCase);

                foreach (var brief in sessions)
                {
                    // Read properties safely via reflection (no dynamic).
                    string clientName = GetPropString(brief, "ClientName");
                    string name = FirstNonEmpty(
                                            GetPropString(brief, "Name"),
                                            GetPropString(brief, "PName"),
                                            GetPropString(brief, "SessionTitle")   // last-resort to avoid empty keys
                                        );
                    string clientGroup = FirstNonEmpty(GetPropString(brief, "ClientGroup"), "Ungrouped");
                    string sessionTitle = GetPropString(brief, "SessionTitle");
                    string urlRaw = GetPropString(brief, "Url");

                    // If Name is still empty, build a stable fallback key using program identifiers.
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        var pid = GetPropString(brief, "PId");
                        var week = GetPropString(brief, "Week");
                        var day = GetPropString(brief, "Day");
                        var uid = GetPropString(brief, "ClientUserId");
                        name = FirstNonEmpty(pid, $"{week}-{day}-{uid}", "(unknown)");
                    }

                    if (string.IsNullOrWhiteSpace(clientName) || string.IsNullOrWhiteSpace(urlRaw))
                        continue;

                    // Normalise URL so first index is stable (session=0, i=0, format=Tablet, version=2)
                    string normalisedUrl = NormalizeUrlToFirstIndex(urlRaw);

                    var candidate = new ProgramSession
                    {
                        SessionTitle = sessionTitle,
                        Url = normalisedUrl,
                        ClientName = clientName,
                        ClientGroup = string.IsNullOrWhiteSpace(clientGroup) ? "Ungrouped" : clientGroup
                    };

                    string key = $"{clientName}||{name}";
                    int candidateSession = GetSessionIndexFromUrl(normalisedUrl);

                    if (!bestByPersonAndName.TryGetValue(key, out var existing))
                    {
                        bestByPersonAndName[key] = candidate;
                    }
                    else
                    {
                        // Keep the one with the smaller `session=` (i.e., the FIRST URL, usually AM)
                        int existingSession = GetSessionIndexFromUrl(existing.Url ?? "");
                        if (candidateSession < existingSession)
                            bestByPersonAndName[key] = candidate;
                    }
                }

                var temp = bestByPersonAndName.Values.ToList();

                // Sort items by ClientName within each group, and group by ClientGroup
                var grouped = temp
                    .GroupBy(p => p.ClientGroup ?? "Ungrouped")
                    .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(g =>
                        new Grouping<string, ProgramSession>(
                            g.Key,
                            g.OrderBy(p => p.ClientName ?? "", StringComparer.OrdinalIgnoreCase)
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

        // With the list already deduped to FIRST-URL entries, tapping just opens that URL.
        // We still normalize again here to be 100% safe.
        private async void ProgramsListView_ItemSelected(object sender, SelectedItemChangedEventArgs e)
        {
            if (e.SelectedItem is not ProgramSession selected)
                return;

            ProgramsListView.SelectedItem = null;

            // Always normalize immediately before navigating (belt and braces).
            string finalUrl = NormalizeUrlToFirstIndex(selected.Url ?? "");
            Debug.WriteLine($"[MainPage] Navigating to TrainingPage with FIRST-URL: {finalUrl}");

            var encodedUrl = Uri.EscapeDataString(finalUrl);
            await Shell.Current.GoToAsync($"//{nameof(TrainingPage)}?url={encodedUrl}");
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

        // ---------- Helpers ----------

        // Parse `session=` from query string; if missing, return int.MaxValue so any explicit session beats it.
        private static int GetSessionIndexFromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return int.MaxValue;
            int q = url.IndexOf('?', StringComparison.Ordinal);
            string query = q >= 0 ? url[(q + 1)..] : "";
            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int eq = part.IndexOf('=', StringComparison.Ordinal);
                if (eq <= 0) continue;
                var key = part[..eq];
                var val = part[(eq + 1)..];
                if (key.Equals("session", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, out var s))
                    return s;
            }
            return int.MaxValue;
        }

        // Reflection-based safe property getters (string/int) so we don't need dynamic
        private static string GetPropString(object obj, string propName)
        {
            if (obj == null || string.IsNullOrWhiteSpace(propName)) return "";
            try
            {
                var pi = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                var val = pi?.GetValue(obj);
                return val?.ToString() ?? "";
            }
            catch { return ""; }
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var v in values)
                if (!string.IsNullOrWhiteSpace(v)) return v;
            return "";
        }

        // Force session=0, i=0 and add format/version; rebuild query in a stable, predictable order
        private static string NormalizeUrlToFirstIndex(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;

            int qIndex = raw.IndexOf('?');
            string path = qIndex < 0 ? raw : raw[..qIndex];
            string query = qIndex < 0 ? "" : raw[(qIndex + 1)..];

            // parse into a small dictionary
            var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in pairs)
            {
                int eq = p.IndexOf('=');
                if (eq <= 0) continue;
                var k = p[..eq];
                var v = p[(eq + 1)..];
                kv[k] = v;
            }

            // **force the AM link**
            kv["session"] = "0";
            kv["i"] = "0";
            if (!kv.ContainsKey("format")) kv["format"] = "Tablet";
            if (!kv.ContainsKey("version")) kv["version"] = "2";

            // rebuild in a stable order
            string[] order = { "week", "day", "session", "i", "format", "version" };
            var keys = kv.Keys
                         .OrderBy(k => Array.IndexOf(order, k) is int idx && idx >= 0 ? idx : int.MaxValue)
                         .ThenBy(k => k, StringComparer.OrdinalIgnoreCase)
                         .ToList();

            var rebuilt = string.Join("&", keys.Select(k => $"{k}={kv[k]}"));
            return $"{path}?{rebuilt}";
        }
    }
}
