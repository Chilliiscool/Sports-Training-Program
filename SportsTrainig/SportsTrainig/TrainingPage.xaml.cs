// Module Name: TrainingPage
// Author: Kye Franken 
// Date Created: 20 / 06 / 2025
// Date Modified: 11 / 08 / 2025
// Description: Displays a training session in a WebView using a session URL and cookie for authentication.
// Forces the "first link" by coercing the query param i=0; retries once; falls back to JSON summary.

using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using SportsTraining.Services;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SportsTraining.Pages
{
    [QueryProperty(nameof(Url), "url")]
    public partial class TrainingPage : ContentPage, INotifyPropertyChanged
    {
        private string _url;

        public string Url
        {
            get => WebUtility.UrlDecode(_url);
            set
            {
                var decoded = WebUtility.UrlDecode(value);
                _url = ForceFirstIndexAndNormalize(decoded);
                LoadSessionDetails();
            }
        }

        public TrainingPage()
        {
            InitializeComponent();
        }

        private static string ForceFirstIndexAndNormalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;

            int q = raw.IndexOf('?');
            string path = q < 0 ? raw : raw.Substring(0, q);
            string query = q < 0 ? "" : raw.Substring(q + 1);

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

            return q < 0 ? $"{path}?{string.Join("&", parts)}" : $"{path}?{string.Join("&", parts)}";
        }

        private async void LoadSessionDetails()
        {
            Debug.WriteLine($"[TrainingPage] Loading session with URL: {_url}");
            try
            {
                string cookie = SessionManager.GetCookie() ?? "";

                if (string.IsNullOrEmpty(cookie))
                {
                    await DisplayAlert("Error", "No cookie found. Please login.", "OK");
                    await Shell.Current.GoToAsync("//LoginPage");
                    return;
                }

                if (string.IsNullOrEmpty(_url))
                {
                    await DisplayAlert("Error", "Session URL is missing.", "OK");
                    return;
                }

                // Attempt 1
                string htmlContent = await VisualCoachingService.GetRawSessionHtml(cookie, _url);

                // Retry once
                if (string.IsNullOrWhiteSpace(htmlContent))
                {
                    Debug.WriteLine("[TrainingPage] Empty HTML, retrying once...");
                    htmlContent = await VisualCoachingService.GetRawSessionHtml(cookie, _url);
                }

                // Fallback to Summary2 JSON if still blank
                if (string.IsNullOrWhiteSpace(htmlContent))
                {
                    Debug.WriteLine("[TrainingPage] HTML still blank after retry. Falling back to Summary2.");
                    var summary = await VisualCoachingService.GetSessionSummary(cookie, _url);
                    if (summary != null)
                    {
                        var sb = new StringBuilder();
                        var title = WebUtility.HtmlEncode(summary.SessionTitle ?? "Training Session");
                        sb.Append("<html><head><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"></head><body style='font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;padding:12px;'>");
                        sb.Append($"<h1 style='font-size:22px;margin:0 0 12px 0;'>{title}</h1>");

                        var bodyHtml = summary.HtmlSummary;
                        if (string.IsNullOrWhiteSpace(bodyHtml) && !string.IsNullOrWhiteSpace(summary.Description))
                            bodyHtml = $"<p>{WebUtility.HtmlEncode(summary.Description)}</p>";

                        if (string.IsNullOrWhiteSpace(bodyHtml))
                            bodyHtml = "<p>No details available for this session.</p>";

                        sb.Append(bodyHtml);
                        sb.Append("</body></html>");

                        TitleLabel.Text = title;
                        SessionWebView.Source = new HtmlWebViewSource { Html = sb.ToString() };
                        return;
                    }

                    await DisplayAlert("Session Expired", "Your session has expired. Please log in again.", "OK");
                    SessionManager.ClearCookie();
                    await Shell.Current.GoToAsync("//LoginPage");
                    return;
                }

                TitleLabel.Text = "Training Session";
                SessionWebView.Source = new HtmlWebViewSource { Html = htmlContent };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TrainingPage] Error loading session: {ex.Message}");
                await DisplayAlert("Error", $"Failed to load session: {ex.Message}", "OK");
            }
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            string savedCompany = Preferences.Get("SelectedCompany", "Normal");
            LogoImage.IsVisible = savedCompany == "ETPA";
        }
    }
}
