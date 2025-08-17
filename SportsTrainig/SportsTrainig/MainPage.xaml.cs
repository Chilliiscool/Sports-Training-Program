// Module Name: MainPage
// Author: Kye Franken 
// Date Created: 19 / 06 / 2025
// Date Modified: 16 / 08 / 2025
// Description: Displays today's programs, grouped. Carries correct week/day and ad=DateStart
//              into TrainingPage so content + dates line up. Normalises week/ad; keeps API day.

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
using System.Reflection;
using System.Globalization;

namespace SportsTraining.Pages
{
    public class Grouping<TKey, TItem> : ObservableCollection<TItem>
    {
        public TKey Key { get; }
        public Grouping(TKey key, IEnumerable<TItem> items) : base(items) => Key = key;
    }

    public partial class MainPage : ContentPage
    {
        public ObservableCollection<ProgramSession> TodayPrograms { get; set; } = new();
        public ObservableCollection<Grouping<string, ProgramSession>> GroupedPrograms { get; set; } = new();

        private bool isLoading = false;

        public MainPage()
        {
            InitializeComponent();
            ProgramsListView.ItemsSource = GroupedPrograms;
        }

        protected override async void OnAppearing()
        {
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
                GroupedPrograms.Clear();
                LoadingIndicator.IsVisible = true;
                LoadingIndicator.IsRunning = true;
                ProgramsListView.IsVisible = false;
            });

            string cookie = SessionManager.GetCookie();
            if (string.IsNullOrEmpty(cookie))
            {
                await DisplayAlert("Login Required", "Please login to view your program.", "OK");
                ShowProgramList();
                isLoading = false;
                return;
            }

            try
            {
                string today = DateTime.Today.ToString("yyyy-MM-dd");
                var sessions = await VisualCoachingService.GetSessionsForDate(cookie, today);

                var bestByPersonAndName = new Dictionary<string, ProgramSession>(StringComparer.OrdinalIgnoreCase);

                foreach (var brief in sessions)
                {
                    string clientName = GetPropString(brief, "ClientName");
                    string name = FirstNonEmpty(
                        GetPropString(brief, "Name"),
                        GetPropString(brief, "PName"),
                        GetPropString(brief, "SessionTitle")
                    );
                    string clientGroup = FirstNonEmpty(GetPropString(brief, "ClientGroup"), "Ungrouped");
                    string sessionTitle = GetPropString(brief, "SessionTitle");
                    string urlRaw = GetPropString(brief, "Url");

                    // From API (strings so we can normalise)
                    string dateStartRaw = GetPropString(brief, "DateStart"); // should be yyyy-MM-dd
                    string weekRaw = GetPropString(brief, "Week");      // could be 0-based
                    string dayRaw = GetPropString(brief, "Day");       // use as-is

                    if (string.IsNullOrWhiteSpace(clientName) || string.IsNullOrWhiteSpace(urlRaw)) continue;

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        var pid = GetPropString(brief, "PId");
                        var uid = GetPropString(brief, "ClientUserId");
                        name = FirstNonEmpty(pid, $"{weekRaw}-{dayRaw}-{uid}", "(unknown)");
                    }

                    // ---- Normalise week/ad BEFORE passing forward; keep API 'day' as-is ----
                    string weekNorm = weekRaw;
                    if (int.TryParse(weekRaw, out var w))
                    {
                        if (w < 1) w = w + 1; // 0-based -> 1-based
                        weekNorm = w.ToString(CultureInfo.InvariantCulture);
                    }

                    string adNorm = NormalizeDateToYMD(dateStartRaw);
                    string dayNorm = dayRaw; // no rotation — trust API

                    // ---- Build URL and inject normalised week/day/ad ----
                    string normalized = NormalizeUrlToFirstIndex(urlRaw);
                    normalized = AppendOrReplaceQuery(normalized, "week", weekNorm);
                    normalized = AppendOrReplaceQuery(normalized, "day", dayNorm);
                    string normalizedWithAd = AppendOrReplaceQuery(normalized, "ad", adNorm);

                    var candidate = new ProgramSession
                    {
                        SessionTitle = sessionTitle,
                        Url = normalizedWithAd,
                        ClientName = clientName,
                        ClientGroup = string.IsNullOrWhiteSpace(clientGroup) ? "Ungrouped" : clientGroup
                    };

                    string key = $"{clientName}||{name}";
                    int candidateSession = GetSessionIndexFromUrl(normalized);

                    if (!bestByPersonAndName.TryGetValue(key, out var existing))
                    {
                        bestByPersonAndName[key] = candidate;
                    }
                    else
                    {
                        int existingSession = GetSessionIndexFromUrl(existing.Url ?? "");
                        if (candidateSession < existingSession)
                            bestByPersonAndName[key] = candidate;
                    }
                }

                var items = bestByPersonAndName.Values.ToList();

                var grouped = items
                    .GroupBy(p => p.ClientGroup ?? "Ungrouped")
                    .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new Grouping<string, ProgramSession>(
                        g.Key,
                        g.OrderBy(p => p.ClientName ?? "", StringComparer.OrdinalIgnoreCase)));

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    TodayPrograms.Clear();
                    foreach (var it in items) TodayPrograms.Add(it);

                    GroupedPrograms.Clear();
                    foreach (var grp in grouped) GroupedPrograms.Add(grp);

                    ShowProgramList();
                });
            }
            catch (UnauthorizedAccessException)
            {
                await DisplayAlert("Session Expired", "Please log in again.", "OK");
                SessionManager.ClearCookie();
                await Shell.Current.GoToAsync("//LoginPage");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainPage] Error: {ex.Message}");
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

        // Tap -> go to TrainingPage with url + anchorDate (from ad=)
        private async void ProgramsListView_ItemSelected(object sender, SelectedItemChangedEventArgs e)
        {
            if (e.SelectedItem is not ProgramSession selected) return;
            ProgramsListView.SelectedItem = null;

            string withFirst = NormalizeUrlToFirstIndex(selected.Url ?? "");
            string ad = GetQueryValue(withFirst, "ad");
            string finalUrl = RemoveQueryKey(withFirst, "ad");

            var encodedUrl = Uri.EscapeDataString(finalUrl);
            var encodedAnchor = Uri.EscapeDataString(ad ?? "");
            await Shell.Current.GoToAsync($"//{nameof(TrainingPage)}?url={encodedUrl}&anchorDate={encodedAnchor}");
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

        private static string NormalizeDateToYMD(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";
            if (DateTime.TryParseExact(input, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                       DateTimeStyles.None, out var dt))
                return dt.ToString("yyyy-MM-dd");
            if (DateTime.TryParse(input, CultureInfo.InvariantCulture,
                                  DateTimeStyles.AssumeLocal, out dt))
                return dt.ToString("yyyy-MM-dd");
            return input; // last resort: pass through
        }

        private static int GetSessionIndexFromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return int.MaxValue;
            int q = url.IndexOf('?', StringComparison.Ordinal);
            string query = q >= 0 ? url[(q + 1)..] : "";
            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                var key = part[..eq];
                var val = part[(eq + 1)..];
                if (key.Equals("session", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, out var s))
                    return s;
            }
            return int.MaxValue;
        }

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

        private static string NormalizeUrlToFirstIndex(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;
            int qIndex = raw.IndexOf('?');
            string path = qIndex < 0 ? raw : raw[..qIndex];
            string query = qIndex < 0 ? "" : raw[(qIndex + 1)..];

            var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in query.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int eq = p.IndexOf('=');
                if (eq <= 0) continue;
                kv[p[..eq]] = p[(eq + 1)..];
            }

            kv["session"] = "0";
            kv["i"] = "0";
            if (!kv.ContainsKey("format")) kv["format"] = "Tablet";
            if (!kv.ContainsKey("version")) kv["version"] = "2";

            string[] order = { "week", "day", "session", "i", "format", "version", "ad" };
            var keys = kv.Keys
                .OrderBy(k => Array.IndexOf(order, k) is int idx && idx >= 0 ? idx : int.MaxValue)
                .ThenBy(k => k, StringComparer.OrdinalIgnoreCase);

            return $"{path}?{string.Join("&", keys.Select(k => $"{k}={kv[k]}"))}";
        }

        private static string AppendOrReplaceQuery(string url, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;
            int q = url.IndexOf('?');
            string path = q < 0 ? url : url[..q];
            var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (q >= 0)
            {
                foreach (var p in url[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    int eq = p.IndexOf('=');
                    if (eq <= 0) continue;
                    kv[p[..eq]] = p[(eq + 1)..];
                }
            }
            if (!string.IsNullOrWhiteSpace(value)) kv[key] = value;
            var rebuilt = string.Join("&", kv.Select(k => $"{k.Key}={k.Value}"));
            return $"{path}?{rebuilt}";
        }

        private static string GetQueryValue(string url, string key)
        {
            int q = url.IndexOf('?'); if (q < 0) return "";
            foreach (var p in url[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int eq = p.IndexOf('=');
                if (eq <= 0) continue;
                if (p[..eq].Equals(key, StringComparison.OrdinalIgnoreCase)) return p[(eq + 1)..];
            }
            return "";
        }

        private static string RemoveQueryKey(string url, string key)
        {
            int q = url.IndexOf('?'); if (q < 0) return url;
            string path = url[..q];
            var parts = url[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            parts = parts.Where(p => !(p.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))).ToList();
            return parts.Count == 0 ? path : $"{path}?{string.Join("&", parts)}";
        }
    }
}
