// Module Name: TrainingPage
// Author: Kye Franken
// Date Created: 20 / 06 / 2025
// Date Modified: 15 / 08 / 2025
// Description: Loads Visual Coaching HTML for a session and renders ONLY a top summary matrix
//              with AM and PM across for Planned. The “Got” row is hidden (since all content
//              we need is shown in Planned). All other HTML below (paragraphs/tables/etc.)
//              is NOT rendered. Includes robust fallbacks and sanitising so “PM OFF” etc. show.

using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using SportsTraining.Services;
using System;
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
        private string _absoluteSessionUrl; // full https URL for the session

        public string Url
        {
            get => WebUtility.UrlDecode(_url);
            set
            {
                var decoded = WebUtility.UrlDecode(value ?? "");
                _url = ForceFirstIndexAndNormalize(decoded); // keep i=0/Tablet/2; do not force AM/PM
                _absoluteSessionUrl = BuildAbsoluteUrl(_url);
                Debug.WriteLine($"[TrainingPage] Set Url -> {_url}");
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

        // --- UI events ---
        private async void OnRefreshClicked(object sender, EventArgs e) => await LoadAndRenderAsync();

        private async void OnOpenInBrowserClicked(object sender, EventArgs e)
        {
            try { await Launcher.OpenAsync(new Uri(_absoluteSessionUrl)); }
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

                // attempt #1: raw HTML
                string html = await VisualCoachingService.GetRawSessionHtml(cookie, _url);

                // retry once if empty
                if (string.IsNullOrWhiteSpace(html))
                {
                    Debug.WriteLine("[TrainingPage] Empty HTML, retry 1…");
                    html = await VisualCoachingService.GetRawSessionHtml(cookie, _url);
                }

                // fallback: Summary2 ? synthesize minimal HTML
                if (string.IsNullOrWhiteSpace(html))
                {
                    var summary = await VisualCoachingService.GetSessionSummary(cookie, _url);
                    if (summary == null)
                    {
                        // Could be expired cookie; prompt relogin
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

                // Final visible fallback (no more blank screens)
                if (string.IsNullOrWhiteSpace(html))
                {
                    RenderNoContentFallback();
                    return;
                }

                RenderHtmlAsNative(html);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TrainingPage] Error: {ex.Message}");
                await DisplayAlert("Error", $"Failed to load session: {ex.Message}", "OK");
                RenderNoContentFallback(); // show something instead of blank
            }
        }

        private void RenderNoContentFallback()
        {
            SessionStack.Children.Clear();
            TitleLabel.Text = "Training Session";

            SessionStack.Children.Add(new Label
            {
                Text = "No content was returned for this session.\nTry pulling to refresh, or open it in the browser.",
                FontSize = 16
            });

            var openBtn = new Button { Text = "Open in Browser", Margin = new Thickness(0, 12, 0, 0) };
            openBtn.Clicked += OnOpenInBrowserClicked;
            SessionStack.Children.Add(openBtn);
        }

        private void RenderHtmlAsNative(string html)
        {
            // Clear previous content
            SessionStack.Children.Clear();

            // Title (from first <h1>)
            var title = ExtractFirst(html, @"<h1[^>]*>(.*?)</h1>");
            TitleLabel.Text = !string.IsNullOrWhiteSpace(title)
                ? WebUtility.HtmlDecode(StripTags(title).Trim())
                : "Training Session";

            // ---- AM/PM matrix (Planned only) ----
            var (amPlan, pmPlan) = ExtractAmPmPlans(html);

            // Always show the matrix; use em-dashes if nothing found
            SessionStack.Children.Add(BuildAmPmMatrixPlannedOnly(
                string.IsNullOrWhiteSpace(amPlan) ? "—" : amPlan,
                string.IsNullOrWhiteSpace(pmPlan) ? "—" : pmPlan
            ));

            // STOP HERE — do NOT render the rest of the HTML (tables/paragraphs)
            // per user request to remove the content under the table.
        }

        // --- AM/PM extraction for PLANNED (multi-line preserved) ---

        // Returns (amPlanned, pmPlanned) with multi-line content preserved and “AM/PM” removed from the text.
        private static (string amPlan, string pmPlan) ExtractAmPmPlans(string html)
        {
            string am = "";
            string pm = "";

            foreach (Match block in Regex.Matches(
                         html ?? "",
                         @"<div[^>]*class=['""]weekly-no-background['""][^>]*>.*?<div[^>]*class=['""]text_element['""][^>]*>(?<content>.*?)</div>.*?</div>",
                         RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                var inner = block.Groups["content"].Value;

                // collect ALL <p> lines inside this text_element
                var lines = Regex.Matches(inner, @"<p[^>]*>(.*?)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase)
                                 .Cast<Match>()
                                 .Select(m => WebUtility.HtmlDecode(StripTags(m.Groups[1].Value)).Trim())
                                 .Where(s => !string.IsNullOrWhiteSpace(s))
                                 .Select(Sanitize) // handle NBSP / zero?width chars
                                 .ToList();

                if (lines.Count == 0) continue;

                string header = lines[0]; // e.g. "AM Swim" or "PM OFF"
                bool isAm = header.StartsWith("AM", StringComparison.OrdinalIgnoreCase);
                bool isPm = header.StartsWith("PM", StringComparison.OrdinalIgnoreCase);

                // remove the AM/PM prefix from the header text ("AM Swim" -> "Swim", "PM OFF" -> "OFF")
                string headerWithoutTag = CleanupPlannedText(header, isAm ? "AM" : "PM");

                // join remaining lines, if any
                string rest = lines.Count > 1 ? string.Join("\n", lines.Skip(1)) : "";

                string combined = string.IsNullOrWhiteSpace(rest) ? headerWithoutTag
                                                                  : $"{headerWithoutTag} — {rest}";

                if (isAm) am = combined;
                if (isPm) pm = combined;
            }

            return (am, pm);
        }

        // Remove leading "AM"/"PM" + any punctuation/spaces that follow.
        private static string CleanupPlannedText(string text, string prefix)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            text = Sanitize(text).Trim();
            var m = Regex.Match(text, @"^(?i)" + prefix + @"\s*[:\-–]?\s*(.*)$");
            return m.Success ? m.Groups[1].Value.Trim() : text;
        }

        private View BuildAmPmMatrixPlannedOnly(string amPlan, string pmPlan)
        {
            // Grid: 3 columns, 2 rows:
            // Row 0:   "" |   AM   |   PM
            // Row 1: Planned | amPlan | pmPlan
            var grid = new Grid
            {
                ColumnSpacing = 10,
                RowSpacing = 6,
                Padding = new Thickness(10),
                Margin = new Thickness(0, 4, 0, 10),
                BackgroundColor = Application.Current?.RequestedTheme == AppTheme.Dark
                                  ? new Microsoft.Maui.Graphics.Color(0.12f, 0.12f, 0.12f)
                                  : new Microsoft.Maui.Graphics.Color(0.96f, 0.96f, 0.96f)
            };

            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto)); // labels column
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star)); // AM
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star)); // PM

            for (int i = 0; i < 2; i++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            // Header row
            AddHeader(grid, 0, 1, "AM");
            AddHeader(grid, 0, 2, "PM");

            // Planned row (only row we show)
            AddHeader(grid, 1, 0, "Planned");
            AddCell(grid, 1, 1, amPlan);
            AddCell(grid, 1, 2, pmPlan);

            return new VerticalStackLayout
            {
                Spacing = 4,
                Children =
                {
                    new Label { Text = "Today’s Sessions", FontSize = 18, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0,4,0,0) },
                    grid
                }
            };
        }

        private static void AddHeader(Grid g, int r, int c, string text)
        {
            var lbl = new Label
            {
                Text = text,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold
            };
            Grid.SetRow(lbl, r);
            Grid.SetColumn(lbl, c);
            g.Children.Add(lbl);
        }

        private static void AddCell(Grid g, int r, int c, string text)
        {
            var lbl = new Label
            {
                Text = WebUtility.HtmlDecode(text),
                FontSize = 14,
                LineBreakMode = LineBreakMode.WordWrap
            };
            Grid.SetRow(lbl, r);
            Grid.SetColumn(lbl, c);
            g.Children.Add(lbl);
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

        // Keep i=0 and add format/version if missing (don’t force AM/PM here)
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

        // Remove zero-width chars (200B–200D, FEFF), NBSP, and collapse whitespace.
        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            string t = s.Replace('\u00A0', ' ');
            t = Regex.Replace(t, "[\u200B\u200C\u200D\uFEFF]", ""); // zero?width chars
            t = Regex.Replace(t, @"\s+", " ");
            return t.Trim();
        }
    }
}
