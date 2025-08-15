// Module Name: SessionDetailPage
// Author: Kye Franken 
// Date Created: 28 / 07 / 2025
// Date Modified: 15 / 08 / 2025
// Description: Loads and displays a training session's title and content. Robust against redirects:
// forces session=0 and i=0, retries once, and falls back to JSON summary if HTML fails.

using Microsoft.Maui.Controls;
using SportsTraining.Services;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Net;
using System.Text;

namespace SportsTraining.Pages
{
    [QueryProperty(nameof(SessionUrl), "url")]
    public partial class SessionDetailPage : ContentPage
    {
        public string SessionUrl { get; set; }

        public SessionDetailPage()
        {
            InitializeComponent();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            string cookie = SessionManager.GetCookie();
            if (string.IsNullOrEmpty(cookie))
            {
                await DisplayAlert("Error", "Please log in again.", "OK");
                await Shell.Current.GoToAsync("//LoginPage");
                return;
            }

            if (string.IsNullOrEmpty(SessionUrl))
            {
                await DisplayAlert("Error", "No session URL provided.", "OK");
                return;
            }

            LoadingIndicator.IsVisible = true;
            LoadingIndicator.IsRunning = true;

            string normalized = ForceFirstIndexAndNormalize(SessionUrl);
            await LoadSessionDetailsAsync(cookie, normalized);

            LoadingIndicator.IsRunning = false;
            LoadingIndicator.IsVisible = false;
        }

        // Force session=0 & i=0 and add format/version if missing
        private static string ForceFirstIndexAndNormalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;

            int q = raw.IndexOf('?');
            string path = q < 0 ? raw : raw.Substring(0, q);
            string query = q < 0 ? "" : raw.Substring(q + 1);

            var parts = query.Split('&', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
            var dict = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var p in parts)
            {
                var kv = p.Split('=', 2);
                if (kv.Length == 2) dict[kv[0]] = kv[1];
            }

            dict["session"] = "0";
            dict["i"] = "0";
            if (!dict.ContainsKey("format")) dict["format"] = "Tablet";
            if (!dict.ContainsKey("version")) dict["version"] = "2";

            var rebuilt = string.Join("&", System.Linq.Enumerable.Select(dict, kv => $"{kv.Key}={kv.Value}"));
            return $"{path}?{rebuilt}";
        }

        private async Task LoadSessionDetailsAsync(string cookie, string url)
        {
            try
            {
                var html = await VisualCoachingService.GetRawSessionHtml(cookie, url);

                if (string.IsNullOrWhiteSpace(html))
                {
                    Debug.WriteLine("[SessionDetail] Empty HTML, retrying once...");
                    html = await VisualCoachingService.GetRawSessionHtml(cookie, url);
                }

                if (string.IsNullOrWhiteSpace(html))
                {
                    Debug.WriteLine("[SessionDetail] HTML still blank; falling back to Summary2 JSON.");
                    var summary = await VisualCoachingService.GetSessionSummary(cookie, url);

                    if (summary != null)
                    {
                        var sb = new StringBuilder();
                        var safeTitle = WebUtility.HtmlEncode(summary.SessionTitle ?? "Session");
                        sb.Append("<html><head><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"></head><body style='font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;padding:12px;'>");
                        sb.Append($"<h1 style='font-size:22px;margin:0 0 12px 0;'>{safeTitle}</h1>");

                        var bodyHtml = summary.HtmlSummary;
                        if (string.IsNullOrWhiteSpace(bodyHtml) && !string.IsNullOrWhiteSpace(summary.Description))
                            bodyHtml = $"<p>{WebUtility.HtmlEncode(summary.Description)}</p>";
                        if (string.IsNullOrWhiteSpace(bodyHtml))
                            bodyHtml = "<p>No details available for this session.</p>";

                        sb.Append(bodyHtml);
                        sb.Append("</body></html>");

                        SessionTitleLabel.Text = safeTitle;
                        SessionContentStack.Children.Clear();
                        SessionContentStack.Children.Add(new WebView { Source = new HtmlWebViewSource { Html = sb.ToString() }, HeightRequest = 1200 });
                        return;
                    }

                    await DisplayAlert("Session Expired", "Your session has expired. Please log in again.", "OK");
                    SessionManager.ClearCookie();
                    await Shell.Current.GoToAsync("//LoginPage");
                    return;
                }

                // Extract <h1>
                var titleMatch = Regex.Match(html, @"<h1[^>]*>(.*?)</h1>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (titleMatch.Success)
                    SessionTitleLabel.Text = System.Net.WebUtility.HtmlDecode(titleMatch.Groups[1].Value.Trim());
                else
                    SessionTitleLabel.Text = "Session";

                // Simple paragraph dump
                var paragraphMatches = Regex.Matches(html, @"<p[^>]*>(.*?)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                SessionContentStack.Children.Clear();

                foreach (Match pMatch in paragraphMatches)
                {
                    string text = Regex.Replace(pMatch.Groups[1].Value, "<.*?>", string.Empty);
                    text = System.Net.WebUtility.HtmlDecode(text).Trim();

                    if (!string.IsNullOrEmpty(text))
                    {
                        SessionContentStack.Children.Add(new Label
                        {
                            Text = text,
                            FontSize = 16,
                            TextColor = Microsoft.Maui.Graphics.Colors.Black,
                            HorizontalOptions = LayoutOptions.Start
                        });
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"[SessionDetail] Error loading session details: {ex.Message}");
                await DisplayAlert("Error", "Failed to load session details.", "OK");
            }
        }
    }
}
