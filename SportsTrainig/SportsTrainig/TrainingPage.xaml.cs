// Module Name: TrainingPage
// Author: Kye Franken
// Date Created: 20 / 06 / 2025
// Date Modified: 14 / 08 / 2025
// Description: Loads Visual Coaching HTML for a session, parses key bits,
//              converts <table> elements to native Grid, and appends a Diary button
//              under every table. No WebView is used.

using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using SportsTraining.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SportsTraining.Pages
{
    [QueryProperty(nameof(Url), "url")]
    public partial class TrainingPage : ContentPage, INotifyPropertyChanged
    {
        private string _url;
        private string _absoluteSessionUrl;      // full https URL for the session
        private List<string> _diaryLinks = new(); // any "Diary" links found in the page (if any)

        public string Url
        {
            get => WebUtility.UrlDecode(_url);
            set
            {
                var decoded = WebUtility.UrlDecode(value ?? "");
                _url = ForceFirstIndexAndNormalize(decoded);
                _absoluteSessionUrl = BuildAbsoluteUrl(_url);
                _ = LoadAndRenderAsync();
            }
        }

        public TrainingPage()
        {
            InitializeComponent();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            string savedCompany = Preferences.Get("SelectedCompany", "Normal");
            LogoImage.IsVisible = savedCompany == "ETPA";
        }

        // --- Navigation & UI events ---
        private async void OnRefreshClicked(object sender, EventArgs e) => await LoadAndRenderAsync();

        private async void OnOpenInBrowserClicked(object sender, EventArgs e)
        {
            try { await Launcher.OpenAsync(new Uri(_absoluteSessionUrl)); }
            catch { /* ignore */ }
        }

        private async void OnDiaryClicked(object sender, EventArgs e)
        {
            try
            {
                var btn = (Button)sender;
                var link = btn.CommandParameter as string;

                // Prefer the table-specific link if available,
                // otherwise try any page-level diary link, then fallback to session URL.
                string target =
                    (!string.IsNullOrWhiteSpace(link) ? link :
                     _diaryLinks.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))) ?? _absoluteSessionUrl;

                await Launcher.OpenAsync(new Uri(BuildAbsoluteUrl(target)));
            }
            catch { /* ignore */ }
        }

        // --- Core Loading/Rendering ---
        private async Task LoadAndRenderAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_url))
                {
                    await DisplayAlert("Error", "Session URL is missing.", "OK");
                    return;
                }

                string cookie = SessionManager.GetCookie() ?? "";
                if (string.IsNullOrEmpty(cookie))
                {
                    await DisplayAlert("Error", "No cookie found. Please login.", "OK");
                    await Shell.Current.GoToAsync("//LoginPage");
                    return;
                }

                SetLoading(true);

                // attempt #1
                string html = await VisualCoachingService.GetRawSessionHtml(cookie, _url);

                // retry once
                if (string.IsNullOrWhiteSpace(html))
                {
                    Debug.WriteLine("[TrainingPage] Empty HTML, retry 1…");
                    html = await VisualCoachingService.GetRawSessionHtml(cookie, _url);
                }

                // fallback: Summary2
                if (string.IsNullOrWhiteSpace(html))
                {
                    var summary = await VisualCoachingService.GetSessionSummary(cookie, _url);
                    if (summary == null)
                    {
                        await DisplayAlert("Session Expired", "Your session has expired. Please log in again.", "OK");
                        SessionManager.ClearCookie();
                        await Shell.Current.GoToAsync("//LoginPage");
                        return;
                    }

                    var body = summary.HtmlSummary;
                    if (string.IsNullOrWhiteSpace(body) && !string.IsNullOrWhiteSpace(summary.Description))
                        body = $"<p>{WebUtility.HtmlEncode(summary.Description)}</p>";

                    html = $"<h1>{WebUtility.HtmlEncode(summary.SessionTitle ?? "Training Session")}</h1>{body}";
                }

                RenderHtmlAsNative(html);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TrainingPage] Error: {ex.Message}");
                await DisplayAlert("Error", $"Failed to load session: {ex.Message}", "OK");
            }
            finally
            {
                SetLoading(false);
            }
        }

        private void RenderHtmlAsNative(string html)
        {
            // Clear previous content
            SessionStack.Children.Clear();
            _diaryLinks.Clear();

            // Title
            var title = ExtractFirst(html, @"<h1[^>]*>(.*?)</h1>");
            TitleLabel.Text = !string.IsNullOrWhiteSpace(title)
                ? WebUtility.HtmlDecode(StripTags(title).Trim())
                : "Training Session";

            // Capture any "Diary" anchors on the page for fallback usage
            foreach (Match m in Regex.Matches(html, @"<a[^>]*href=""(?<href>[^""]+)""[^>]*>(?<txt>.*?)</a>",
                                               RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                var txt = StripTags(m.Groups["txt"].Value).Trim();
                var href = m.Groups["href"].Value.Trim();
                if (txt.Contains("Diary", StringComparison.OrdinalIgnoreCase) ||
                    href.Contains("Diary", StringComparison.OrdinalIgnoreCase))
                {
                    _diaryLinks.Add(href);
                }
            }

            // Paragraph intro (optional — grab first few <p>)
            var introParas = Regex.Matches(html, @"<p[^>]*>(.*?)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase)
                                  .Cast<Match>().Select(m => StripTags(m.Groups[1].Value).Trim())
                                  .Where(t => !string.IsNullOrWhiteSpace(t))
                                  .Take(3)
                                  .ToList();
            if (introParas.Count > 0)
            {
                var intro = new VerticalStackLayout { Spacing = 6 };
                foreach (var p in introParas)
                    intro.Children.Add(new Label { Text = WebUtility.HtmlDecode(p), FontSize = 15 });
                SessionStack.Children.Add(intro);
            }

            // Convert each <table>…</table> to a Grid + Diary button
            var tables = Regex.Matches(html, @"<table[^>]*>(.*?)</table>", RegexOptions.Singleline | RegexOptions.IgnoreCase)
                              .Cast<Match>()
                              .Select(m => m.Value)
                              .ToList();

            if (tables.Count == 0)
            {
                // If no tables, show the remaining paragraphs as a simple list
                var allParas = Regex.Matches(html, @"<p[^>]*>(.*?)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase)
                                    .Cast<Match>().Select(m => StripTags(m.Groups[1].Value).Trim())
                                    .Where(t => !string.IsNullOrWhiteSpace(t))
                                    .ToList();

                foreach (var p in allParas)
                    SessionStack.Children.Add(new Label { Text = WebUtility.HtmlDecode(p), FontSize = 16 });

                return;
            }

            int tableIndex = 0;
            foreach (var tableHtml in tables)
            {
                // Try to find a table-local "Diary" link (anchor nearby within same block)
                string? localDiaryLink = FindNearestDiaryLink(tableHtml);

                var grid = BuildGridFromHtmlTable(tableHtml);
                SessionStack.Children.Add(grid);

                var diaryBtn = new Button
                {
                    Text = "Diary",
                    Margin = new Thickness(0, 8, 0, 24),
                    CommandParameter = localDiaryLink
                };
                diaryBtn.Clicked += OnDiaryClicked;

                SessionStack.Children.Add(diaryBtn);

                tableIndex++;
            }
        }

        // --- Helpers: parsing & UI build ---
        private static string StripTags(string s) => Regex.Replace(s ?? "", "<.*?>", string.Empty);

        private static string? ExtractFirst(string html, string pattern)
        {
            var m = Regex.Match(html ?? "", pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string BuildAbsoluteUrl(string maybeRelative)
        {
            if (string.IsNullOrWhiteSpace(maybeRelative)) return "";
            if (Uri.TryCreate(maybeRelative, UriKind.Absolute, out _)) return maybeRelative;
            return $"https://cloud.visualcoaching2.com{(maybeRelative.StartsWith("/") ? "" : "/")}{maybeRelative}";
        }

        private static string ForceFirstIndexAndNormalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;

            int q = raw.IndexOf('?');
            string path = q < 0 ? raw : raw[..q];
            string query = q < 0 ? "" : raw[(q + 1)..];

            var parts = query
                .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            bool hasI = false;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].StartsWith("i=", StringComparison.OrdinalIgnoreCase))
                {
                    parts[i] = "i=0";
                    hasI = true;
                }
            }
            if (!hasI) parts.Add("i=0");

            if (!parts.Any(p => p.StartsWith("format=", StringComparison.OrdinalIgnoreCase)))
                parts.Add("format=Tablet");
            if (!parts.Any(p => p.StartsWith("version=", StringComparison.OrdinalIgnoreCase)))
                parts.Add("version=2");

            return $"{path}?{string.Join("&", parts)}";
        }

        private Grid BuildGridFromHtmlTable(string tableHtml)
        {
            // Parse rows/cells very simply (HTML is assumed clean from VC)
            var rowMatches = Regex.Matches(tableHtml, @"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

            var grid = new Grid
            {
                ColumnSpacing = 8,
                RowSpacing = 6,
                Padding = new Thickness(10),
                BackgroundColor = Application.Current?.RequestedTheme == AppTheme.Dark
                                  ? new Microsoft.Maui.Graphics.Color(0.12f, 0.12f, 0.12f)
                                  : new Microsoft.Maui.Graphics.Color(0.96f, 0.96f, 0.96f)
            };

            int rowIndex = 0;
            int maxCols = 0;
            List<List<string>> rows = new();

            foreach (Match row in rowMatches)
            {
                var cells = Regex.Matches(row.Groups[1].Value, @"<(td|th)[^>]*>(.*?)</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)
                                 .Cast<Match>()
                                 .Select(m => WebUtility.HtmlDecode(StripTags(m.Groups[2].Value).Trim()))
                                 .ToList();
                if (cells.Count > 0)
                {
                    rows.Add(cells);
                    maxCols = Math.Max(maxCols, cells.Count);
                }
            }

            // Define columns
            for (int c = 0; c < maxCols; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

            // Add rows + labels
            foreach (var cells in rows)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                for (int c = 0; c < maxCols; c++)
                {
                    string text = c < cells.Count ? cells[c] : "";
                    var lbl = new Label
                    {
                        Text = text,
                        FontSize = 14,
                        LineBreakMode = LineBreakMode.WordWrap
                    };

                    // treat first row as header if came from <th>
                    if (rowIndex == 0)
                    {
                        lbl.FontAttributes = FontAttributes.Bold;
                    }

                    Grid.SetRow(lbl, rowIndex);
                    Grid.SetColumn(lbl, c);
                    grid.Children.Add(lbl);
                }
                rowIndex++;
            }

            return grid;
        }

        private static string? FindNearestDiaryLink(string htmlBlock)
        {
            // Look for an <a> with "Diary" in the text or href inside the same block
            foreach (Match m in Regex.Matches(htmlBlock, @"<a[^>]*href=""(?<href>[^""]+)""[^>]*>(?<txt>.*?)</a>",
                                              RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                var txt = StripTags(m.Groups["txt"].Value).Trim();
                var href = m.Groups["href"].Value.Trim();
                if (txt.Contains("Diary", StringComparison.OrdinalIgnoreCase) ||
                    href.Contains("Diary", StringComparison.OrdinalIgnoreCase))
                {
                    return href;
                }
            }
            return null;
        }

        private void SetLoading(bool on)
        {
            LoadingIndicator.IsVisible = on;
            LoadingIndicator.IsRunning = on;
        }
    }
}
