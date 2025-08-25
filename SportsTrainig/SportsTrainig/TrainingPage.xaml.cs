// Module Name: TrainingPage
// Author: Kye Franken
// Date Created: 20 / 06 / 2025
// Date Modified: 21 / 08 / 2025
// Description: Native rendering for BOTH program styles with Monday-start weeks.
//   • Table: original AM/PM planned matrix (blocks as columns, <p> rows).
//   • Exercises: weights view parsed from <div class="exercise"> blocks.
//   • View toggle logic keeps both working; Auto prefers Exercises when present.
//   • Images/videos use an in-page lightbox overlay (no popup).
//   • Fixed Media3 compatibility issues with custom player handling.
//   • Added info button for exercise details.
//   • FIXED: Day indexing - Monday=1, Sunday=7 for API
//   • UPDATED: Clean button text without question mark symbols
//   • ADDED: YouTube link support with embedded player and browser fallback

// Lightbox uses Toolkit MediaElement for video with safer handling
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Layouts;
using Microsoft.Maui.Storage;
using Newtonsoft.Json;
using SportsTraining.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static SportsTraining.Services.VisualCoachingService;
using ToolkitMediaElement = CommunityToolkit.Maui.Views.MediaElement;

namespace SportsTraining.Pages
{
    [QueryProperty(nameof(Url), "url")]
    [QueryProperty(nameof(AnchorDate), "anchorDate")]
    public partial class TrainingPage : ContentPage, INotifyPropertyChanged
    {
        private const string ShowDatesPrefKey = "ShowDatesPanel";
        private const string ViewModeKey = "ProgramViewMode"; // Auto | Exercises | Table
        private const double ParagraphColWidth = 220;
        private const int MinWeeks = 1;
        private const int MaxWeeks = 14;
        private const string UserEmailKey = "UserEmail";
        // Monday-start model: 0..6 = Mon..Sun (internal representation)
        private static readonly (string Label, int DayIdx)[] DaysVc =
        {
            ("Mon",0),("Tue",1),("Wed",2),("Thu",3),("Fri",4),("Sat",5),("Sun",6)
        };

        private string _url = "";
        private string _absoluteSessionUrl = "";

        private int _week = 0;           // 0-based week from URL (server)
        private int _dayVc = 0;          // 0..6 (Mon..Sun) - internal representation
        private DateTime? _anchorDate;   // Program DateStart yyyy-MM-dd (ad)
        private DateTime? _selectedDate; // exact date for current view (seeded from 'ad')

        private int _entryWeekForProgram = 0;
        private DateTime _programWeek0Monday; // baseline Monday for Week 0

        private int? _programId;
        private int? _programWeeks;
        private readonly Dictionary<int, int> _weeksCache = new();

        // Media handling for safer video playback
        private ToolkitMediaElement? _currentMediaElement;

        private bool Ready => _anchorDate.HasValue && !string.IsNullOrWhiteSpace(_url);

        public TrainingPage()
        {
            InitializeComponent();

            // Optional Shell toggle to show/hide dates panel
            MessagingCenter.Subscribe<AppShell, bool>(this, "ShowDatesPanelChanged", (_, show) =>
            {
                DatesSection.IsVisible = show;
            });
        }

        // ----------------------- Query props -----------------------
        public string Url
        {
            get => WebUtility.UrlDecode(_url);
            set
            {
                var decoded = WebUtility.UrlDecode(value ?? "");
                _url = ForceFirstIndexAndNormalize(decoded);
                _absoluteSessionUrl = BuildAbsoluteUrl(_url);

                var newPid = ParseProgramId(_url);
                if (newPid != _programId)
                {
                    _programId = newPid;
                    _selectedDate = null;
                    _programWeeks = null;
                    if (_programId is int cachedPid && _weeksCache.TryGetValue(cachedPid, out var w))
                        _programWeeks = w;
                }

                // Parse week and day from URL BEFORE setting up dates
                ParseWeekAndDayVcFromUrl(_url);
                _entryWeekForProgram = _week;

                if (_anchorDate == null)
                    TryLoadAnchorFromUrl(_url);

                // Set selected date based on the day from the URL
                if (_anchorDate != null)
                {
                    ComputeProgramBaselineMonday();
                    // Calculate the selected date based on week and day from URL
                    _selectedDate = _programWeek0Monday.AddDays(7 * _week + _dayVc);
                    Debug.WriteLine($"[Url setter] Set _selectedDate to: {_selectedDate:yyyy-MM-dd dddd} (week={_week}, dayVc={_dayVc})");
                }

                if (Ready)
                    _ = LoadAndRenderAsync();
            }
        }

        public string AnchorDate
        {
            get => _anchorDate?.ToString("yyyy-MM-dd");
            set
            {
                if (TryParseYMD(value, out var dt))
                {
                    _anchorDate = dt.Date;
                    _selectedDate ??= _anchorDate;
                    ComputeProgramBaselineMonday();

                    if (!string.IsNullOrWhiteSpace(_url))
                        _ = LoadAndRenderAsync();
                }
            }
        }

        // ----------------------- Lifecycle -----------------------
        protected override void OnAppearing()
        {
            base.OnAppearing();
            LogoImage.IsVisible = Preferences.Get("SelectedCompany", "Normal") == "ETPA";
            DatesSection.IsVisible = Preferences.Get(ShowDatesPrefKey, true);
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            MessagingCenter.Unsubscribe<AppShell, bool>(this, "ShowDatesPanelChanged");

            // Clean up any active media
            CleanupCurrentMedia();
        }

        // ----------------------- Weeks / Days UI -----------------------
        private void BuildWeekTabsUI()
        {
            WeekTabsLayout.Children.Clear();
            int totalWeeks = _programWeeks ?? 1;
            int activeWeek = SelectedWeekIndex;

            for (int wk = 0; wk < totalWeeks; wk++)
            {
                bool active = wk == activeWeek;
                var btn = new Button
                {
                    Text = $"Wk {wk + 1}",
                    Padding = new Thickness(12, 6),
                    CornerRadius = 14,
                    FontSize = 14,
                    BackgroundColor = active
                        ? new Microsoft.Maui.Graphics.Color(0.25f, 0.43f, 0.96f)
                        : new Microsoft.Maui.Graphics.Color(0f, 0f, 0f, 0.08f),
                    TextColor = Microsoft.Maui.Graphics.Colors.White
                };
                int captured = wk;
                btn.Clicked += async (_, __) =>
                {
                    _week = captured;
                    if (_dayVc < 0 || _dayVc > 6) _dayVc = 0;
                    _selectedDate = _programWeek0Monday.AddDays(7 * _week + _dayVc);

                    _url = WithWeekDayVc(_url, _week, _dayVc);
                    _absoluteSessionUrl = BuildAbsoluteUrl(_url);
                    await LoadAndRenderAsync();
                    HeaderWeekDateLabel.Text = $"Week {DisplayWeek} · {SelectedDate:ddd dd/MM/yyyy}";
                };
                WeekTabsLayout.Children.Add(btn);
            }
        }

        private void BuildDaysListUI()
        {
            DaysListLayout.Children.Clear();

            var weekMonday = WeekMonday(SelectedDate);
            foreach (var (label, dayOffset) in DaysVc)
            {
                // dayOffset is 0-based (0=Mon, 1=Tue, ..., 6=Sun) for AddDays
                var date = weekMonday.AddDays(dayOffset);
                bool isSelected = SelectedDate.Date == date.Date;

                var row = new Grid { Padding = new Thickness(10, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
                row.BackgroundColor = isSelected
                    ? new Microsoft.Maui.Graphics.Color(0f, 0f, 0f, 0.08f)
                    : Microsoft.Maui.Graphics.Colors.Transparent;

                row.Add(new Label { Text = label, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 0, 10, 0) }, 0, 0);
                row.Add(new Label { Text = date.ToString("dd/MM"), Opacity = 0.8 }, 1, 0);

                // FIXED: Use dayOffset directly (0-6) internally
                int capturedDayOffset = dayOffset; // Keep 0-based (0=Mon, 6=Sun)
                var tap = new TapGestureRecognizer();
                tap.Tapped += async (_, __) =>
                {
                    Debug.WriteLine($"[Day Tap] Label: {label}, dayOffset: {capturedDayOffset}");

                    _dayVc = capturedDayOffset; // Store as 0-based (0=Mon, 6=Sun)
                    _selectedDate = weekMonday.AddDays(capturedDayOffset); // Use 0-based offset for date calc
                    _week = SelectedWeekIndex;

                    Debug.WriteLine($"[Day Tap] _dayVc (0-based): {_dayVc}, _selectedDate: {_selectedDate:yyyy-MM-dd ddd}");

                    _url = WithWeekDayVc(_url, _week, _dayVc);
                    _absoluteSessionUrl = BuildAbsoluteUrl(_url);

                    Debug.WriteLine($"[Day Tap] URL: {_url}");

                    await LoadAndRenderAsync();
                    HeaderWeekDateLabel.Text = $"Week {DisplayWeek} · {SelectedDate:ddd dd/MM/yyyy}";
                };
                row.GestureRecognizers.Add(tap);
                DaysListLayout.Children.Add(row);
            }
        }

        // ----------------------- Program length detection -----------------------
        private async Task EnsureProgramWeeksAsync(string cookie)
        {
            if (_programWeeks != null) return;

            if (_programId is int pid && _weeksCache.TryGetValue(pid, out var cached))
            {
                _programWeeks = cached;
                return;
            }

            int counted = 0;
            int testDay = (_dayVc >= 0 && _dayVc <= 6) ? _dayVc : 0;

            for (int w = 0; w < MaxWeeks; w++)
            {
                var u = WithWeekDayVc(_url, w, testDay);
                string h = await VisualCoachingService.GetRawSessionHtml(cookie, u);

                if (!string.IsNullOrWhiteSpace(h)) counted = w + 1; else break;
            }

            _programWeeks = Math.Clamp(counted, MinWeeks, MaxWeeks);
            if (_programId is int pid2) _weeksCache[pid2] = _programWeeks.Value;
        }

        // ======================================================
        //                     VIEW RENDERERS
        // ======================================================

        // -------- Table (AM/PM planned matrix; transposed) --------
        private bool RenderProgramDayTable(string html)
        {
            var blockCols = ExtractProgramColumns(html);
            if (blockCols.Count == 0) return false;
            SessionStack.Children.Add(BuildDayTableTransposed(blockCols));
            return true;
        }

        // ======================================================
        //                 LINKED PROGRAMS SUPPORT
        // ======================================================

        // Enhanced TextPart class for both linked programs and YouTube links
        private class TextPart
        {
            public string Text { get; set; } = "";
            public bool IsLinkedProgram { get; set; }
            public string ProgramId { get; set; } = "";
            public bool IsYouTubeLink { get; set; }
            public string YouTubeUrl { get; set; } = "";
        }

        // Updated SplitTextWithAllLinks method - fix YouTube link text cleaning
        private List<TextPart> SplitTextWithAllLinks(string html)
        {
            var parts = new List<TextPart>();

            // Combined regex to match both linked programs and YouTube links
            var allMatches = new List<(Match match, string type)>();

            // Find linked programs
            var linkedProgramMatches = Regex.Matches(html,
                @"<a[^>]*href=""[^""]*(?:#program/|/Program/Session/)(\d+)""[^>]*class=""[^""]*linkedProgram[^""]*""[^>]*>(.*?)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match match in linkedProgramMatches)
            {
                allMatches.Add((match, "linkedProgram"));
            }

            // Find YouTube links
            var youtubeMatches = Regex.Matches(html,
                @"<a[^>]*href=""(https://(?:www\.)youtube\.com/watch\?v=([^""&]+))[^""]*""[^>]*>(.*?)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match match in youtubeMatches)
            {
                allMatches.Add((match, "youtube"));
            }

            // Sort matches by position
            allMatches.Sort((a, b) => a.match.Index.CompareTo(b.match.Index));

            if (allMatches.Count == 0)
            {
                parts.Add(new TextPart { Text = html, IsLinkedProgram = false, IsYouTubeLink = false });
                return parts;
            }

            int lastIndex = 0;

            foreach (var (match, type) in allMatches)
            {
                // Add text before the link
                if (match.Index > lastIndex)
                {
                    string beforeText = html.Substring(lastIndex, match.Index - lastIndex);
                    if (!string.IsNullOrWhiteSpace(StripTags(beforeText)))
                    {
                        parts.Add(new TextPart { Text = beforeText, IsLinkedProgram = false, IsYouTubeLink = false });
                    }
                }

                if (type == "linkedProgram")
                {
                    // Handle linked program (existing logic)
                    string programId = match.Groups[1].Value;
                    string linkText = match.Groups[2].Value;

                    string cleanLinkText = CleanButtonText(WebUtility.HtmlDecode(StripTags(linkText)));

                    parts.Add(new TextPart
                    {
                        Text = cleanLinkText,
                        IsLinkedProgram = true,
                        ProgramId = programId,
                        IsYouTubeLink = false
                    });
                }
                else if (type == "youtube")
                {
                    // Handle YouTube link - APPLY THE SAME CLEANING
                    string youtubeUrl = match.Groups[1].Value;
                    string videoId = match.Groups[2].Value;
                    string linkText = match.Groups[3].Value;

                    // FIXED: Apply the same aggressive cleaning to YouTube link text
                    string cleanLinkText = CleanButtonText(WebUtility.HtmlDecode(StripTags(linkText)));

                    if (string.IsNullOrWhiteSpace(cleanLinkText))
                    {
                        cleanLinkText = "YouTube Video";
                    }

                    parts.Add(new TextPart
                    {
                        Text = cleanLinkText,
                        IsLinkedProgram = false,
                        IsYouTubeLink = true,
                        YouTubeUrl = youtubeUrl
                    });
                }

                lastIndex = match.Index + match.Length;
            }

            // Add remaining text after the last link
            if (lastIndex < html.Length)
            {
                string afterText = html.Substring(lastIndex);
                if (!string.IsNullOrWhiteSpace(StripTags(afterText)))
                {
                    parts.Add(new TextPart { Text = afterText, IsLinkedProgram = false, IsYouTubeLink = false });
                }
            }

            return parts;
        }

        // Enhanced CleanButtonText method with even more aggressive cleaning
        private static string CleanButtonText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "Program";

            // AGGRESSIVELY remove ALL types of question marks (including Unicode variants)
            text = text.Replace("?", "")           // Regular question mark
                      .Replace("？", "")           // Full-width question mark (Unicode)
                      .Replace("¿", "")            // Inverted question mark
                      .Replace("؟", "")            // Arabic question mark
                      .Replace("；", "")           // Any other question-like characters
                      .Replace("¡", "");           // Inverted exclamation (sometimes mistaken)

            // Remove ALL parentheses and their content (multiple passes to handle nested)
            while (text.Contains("(") && text.Contains(")"))
            {
                text = Regex.Replace(text, @"\([^)]*\)", "");
            }

            // Remove any leftover standalone parentheses
            text = text.Replace("(", "").Replace(")", "");

            // Remove "Linked Program" text
            text = text.Replace("Linked Program", "", StringComparison.OrdinalIgnoreCase);

            // Remove "YouTube" if it's redundant (since we add the play button)
            text = text.Replace("YouTube", "", StringComparison.OrdinalIgnoreCase);

            // Remove common video-related words that might be redundant
            text = text.Replace("Video", "", StringComparison.OrdinalIgnoreCase);
            text = text.Replace("Watch", "", StringComparison.OrdinalIgnoreCase);

            // Remove leading/trailing colons, dashes, periods, and whitespace
            text = Regex.Replace(text, @"^[:\-\.\s]+|[:\-\.\s]+$", "");

            // Clean up multiple spaces and normalize whitespace
            text = Regex.Replace(text, @"\s+", " ");

            // Remove any remaining special characters that might cause issues
            text = Regex.Replace(text, @"[^\w\s\-]", "");

            text = text.Trim();

            // If nothing meaningful left, use appropriate default
            if (string.IsNullOrWhiteSpace(text) || text.Length < 2)
                return "Video";

            return text;
        }

        // Updated CreateContentWithLinkedPrograms method with better YouTube button text
        private View CreateContentWithLinkedPrograms(string text)
        {
            // Check for both linked programs and YouTube links
            var linkedProgramMatches = Regex.Matches(text,
                @"<a[^>]*href=""[^""]*(?:#program/|/Program/Session/)(\d+)""[^>]*class=""[^""]*linkedProgram[^""]*""[^>]*>(.*?)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            var youtubeMatches = Regex.Matches(text,
                @"<a[^>]*href=""(https://(?:www\.)?youtube\.com/watch\?v=([^""&]+))[^""]*""[^>]*>(.*?)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (linkedProgramMatches.Count == 0 && youtubeMatches.Count == 0)
            {
                // No special links, return simple label
                return new Label
                {
                    Text = WebUtility.HtmlDecode(StripTags(text)),
                    FontSize = 14,
                    LineBreakMode = LineBreakMode.WordWrap,
                    WidthRequest = ParagraphColWidth
                };
            }

            // Has special links, create interactive content
            var mainStack = new VerticalStackLayout { Spacing = 5 };

            // Process the text to handle both types of links
            var parts = SplitTextWithAllLinks(text);

            foreach (var part in parts)
            {
                if (part.IsLinkedProgram)
                {
                    // Handle linked programs (existing functionality)
                    var linkContainer = new VerticalStackLayout { Spacing = 10 };

                    var linkButton = new Button
                    {
                        Text = part.Text, // Already cleaned in SplitTextWithAllLinks
                        FontSize = 14,
                        BackgroundColor = Color.FromArgb("#0088FF"),
                        TextColor = Colors.White,
                        CornerRadius = 6,
                        Padding = new Thickness(12, 6),
                        HorizontalOptions = LayoutOptions.Start,
                        Margin = new Thickness(0, 2)
                    };

                    var expandedContainer = new VerticalStackLayout
                    {
                        Spacing = 5,
                        IsVisible = false,
                        Margin = new Thickness(20, 10, 0, 10),
                        BackgroundColor = Application.Current?.RequestedTheme == AppTheme.Dark
                            ? new Microsoft.Maui.Graphics.Color(0.08f, 0.08f, 0.08f)
                            : new Microsoft.Maui.Graphics.Color(0.95f, 0.95f, 0.95f),
                        Padding = new Thickness(10)
                    };

                    bool isExpanded = false;
                    string programId = part.ProgramId;

                    linkButton.Clicked += async (s, e) =>
                    {
                        if (!isExpanded)
                        {
                            linkButton.Text = $"Hide {part.Text}"; // Use cleaned text
                            linkButton.BackgroundColor = Color.FromArgb("#0066CC");
                            expandedContainer.Children.Clear();
                            expandedContainer.Children.Add(new Label
                            {
                                Text = "Loading...",
                                FontAttributes = FontAttributes.Italic,
                                Opacity = 0.7
                            });
                            expandedContainer.IsVisible = true;
                            await LoadLinkedProgramInlineAsync(programId, expandedContainer);
                            isExpanded = true;
                        }
                        else
                        {
                            linkButton.Text = part.Text; // Use cleaned text
                            linkButton.BackgroundColor = Color.FromArgb("#0088FF");
                            expandedContainer.IsVisible = false;
                            expandedContainer.Children.Clear();
                            isExpanded = false;
                        }
                    };

                    linkContainer.Children.Add(linkButton);
                    linkContainer.Children.Add(expandedContainer);
                    mainStack.Children.Add(linkContainer);
                }
                else if (part.IsYouTubeLink)
                {
                    // Handle YouTube links - CLEAN BUTTON TEXT
                    var youtubeButton = new Button
                    {
                        Text = $"▶ {part.Text}", // part.Text is now cleaned by CleanButtonText
                        FontSize = 14,
                        BackgroundColor = Color.FromArgb("#FF0000"), // YouTube red
                        TextColor = Colors.White,
                        CornerRadius = 6,
                        Padding = new Thickness(12, 6),
                        HorizontalOptions = LayoutOptions.Start,
                        Margin = new Thickness(0, 2)
                    };

                    string youtubeUrl = part.YouTubeUrl;
                    youtubeButton.Clicked += async (s, e) =>
                    {
                        await OpenYouTubeLinkAsync(youtubeUrl, part.Text);
                    };

                    mainStack.Children.Add(youtubeButton);
                }
                else if (!string.IsNullOrWhiteSpace(part.Text))
                {
                    var label = new Label
                    {
                        Text = WebUtility.HtmlDecode(StripTags(part.Text)),
                        FontSize = 14,
                        LineBreakMode = LineBreakMode.WordWrap
                    };
                    mainStack.Children.Add(label);
                }
            }

            return new Frame
            {
                Content = mainStack,
                BackgroundColor = Colors.Transparent,
                BorderColor = Colors.Transparent,
                Padding = 0,
                WidthRequest = ParagraphColWidth
            };
        }



        // YouTube link handling methods
        private async Task OpenYouTubeLinkAsync(string youtubeUrl, string title)
        {
            try
            {
                // Option 1: Open in system browser (most reliable)
                await Browser.OpenAsync(youtubeUrl, BrowserLaunchMode.SystemPreferred);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[YouTube] Error opening link: {ex.Message}");

                // Fallback: Try to extract video ID and show in a web view
                var videoId = ExtractYouTubeVideoId(youtubeUrl);
                if (!string.IsNullOrEmpty(videoId))
                {
                    await ShowYouTubeInWebViewAsync(videoId, title);
                }
                else
                {
                    await DisplayAlert("Error", "Unable to open YouTube video.", "OK");
                }
            }
        }

        // Extract YouTube video ID from URL
        private string ExtractYouTubeVideoId(string url)
        {
            var match = Regex.Match(url, @"(?:youtube\.com/watch\?v=|youtu\.be/)([^&\n?#]+)");
            return match.Success ? match.Groups[1].Value : "";
        }

        // Alternative: Show YouTube video in a web view overlay
        private async Task ShowYouTubeInWebViewAsync(string videoId, string title)
        {
            try
            {
                // Create embedded YouTube player URL
                string embedUrl = $"https://www.youtube.com/embed/{videoId}?autoplay=1&modestbranding=1&rel=0";

                var webView = new WebView
                {
                    Source = embedUrl,
                    HorizontalOptions = LayoutOptions.FillAndExpand,
                    VerticalOptions = LayoutOptions.FillAndExpand
                };

                // Show in lightbox overlay (reuse existing lightbox)
                LightboxTitle.Text = title ?? "YouTube Video";
                LightboxContent.Content = webView;
                LightboxOverlay.IsVisible = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[YouTube] WebView error: {ex.Message}");
                await DisplayAlert("Error", "Unable to display YouTube video.", "OK");
            }
        }

        // Updated OnLinkedProgramClicked method - navigate directly without displaying extra info
        private async Task OnLinkedProgramClicked(string programId, string programName)
        {
            try
            {
                string cookie = SessionManager.GetCookie();
                if (string.IsNullOrEmpty(cookie))
                {
                    await DisplayAlert("Error", "Please log in again.", "OK");
                    return;
                }

                Debug.WriteLine($"[LinkedProgram] Opening linked program: {programName} (ID: {programId})");

                // Find the first day with actual content, considering program type
                (int targetWeek, int targetDay) = await FindFirstContentWeekDayAsync(cookie, programId);

                // Calculate the target date based on CURRENT PROGRAM'S DATE CONTEXT
                DateTime targetDate;
                if (targetDay == 0)
                {
                    // For day 0 (strength programs), use the Monday of the target week
                    DateTime mondayWeek0 = _programWeek0Monday;
                    targetDate = mondayWeek0.AddDays(targetWeek * 7); // Day 0 = Monday of that week
                    Debug.WriteLine($"[LinkedProgram] Using day 0 logic - target date: {targetDate:yyyy-MM-dd}");
                }
                else
                {
                    // For regular days 1-7, use standard calculation
                    DateTime mondayWeek0 = _programWeek0Monday;
                    int daysToAdd = (targetWeek * 7) + (targetDay - 1); // targetDay 1-7 becomes 0-6 offset
                    targetDate = mondayWeek0.AddDays(daysToAdd);
                    Debug.WriteLine($"[LinkedProgram] Using standard logic - target date: {targetDate:yyyy-MM-dd}");
                }

                // Build URL for the linked program
                string linkedUrl = $"/Application/Program/Session/{programId}?week={targetWeek}&day={targetDay}&session=0&i=0&format=Tablet&version=2";

                Debug.WriteLine($"[LinkedProgram] Navigating to: {linkedUrl}");
                Debug.WriteLine($"[LinkedProgram] Program: {programName} (ID: {programId}), Week: {targetWeek}, Day: {targetDay}");
                Debug.WriteLine($"[LinkedProgram] Target date: {targetDate:yyyy-MM-dd ddd}");

                // Navigate to the linked program directly
                var encodedUrl = Uri.EscapeDataString(linkedUrl);
                var encodedAnchor = Uri.EscapeDataString(targetDate.ToString("yyyy-MM-dd"));
                await Shell.Current.GoToAsync($"//{nameof(TrainingPage)}?url={encodedUrl}&anchorDate={encodedAnchor}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TrainingPage] Error opening linked program: {ex.Message}");
                await DisplayAlert("Error", $"Could not open {programName} program.", "OK");
            }
        }

        private static List<List<string>> ExtractProgramColumns(string html)
        {
            var results = new List<List<string>>();

            foreach (Match block in Regex.Matches(
                         html ?? "",
                         @"<div[^>]*class=['""]weekly-no-background['""][^>]*>.*?<div[^>]*class=['""]text_element['""][^>]*>(?<content>.*?)</div>.*?</div>",
                         RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                var inner = block.Groups["content"].Value;

                var cols = Regex.Matches(inner, @"<p[^>]*>(.*?)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase)
                                .Cast<Match>()
                                .Select(m => m.Groups[1].Value) // Keep HTML for linked program detection
                                .Where(s => !string.IsNullOrWhiteSpace(StripTags(s)))
                                .ToList();

                if (cols.Count > 0)
                    results.Add(cols);
            }

            return results;
        }

        private View BuildDayTableTransposed(IList<List<string>> blocksAsCols)
        {
            int colCount = blocksAsCols.Count;
            int rowCount = blocksAsCols.Max(b => b.Count);

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

            for (int r = 0; r < rowCount; r++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < colCount; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ParagraphColWidth, GridUnitType.Absolute) });

            for (int r = 0; r < rowCount; r++)
            {
                for (int c = 0; c < colCount; c++)
                {
                    var colList = blocksAsCols[c];
                    string text = (r < colList.Count) ? colList[r] : "";

                    // Check for linked programs and create appropriate content
                    var content = CreateContentWithLinkedPrograms(text);

                    Grid.SetRow(content, r);
                    Grid.SetColumn(content, c);
                    grid.Children.Add(content);
                }
            }

            return new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = grid };
        }

        // -------- Exercises (weights) --------
        private sealed class ExSet { public string Reps = "", Sign = "", Value = "", Unit = "", Extra = ""; }
        private sealed class ExerciseItem
        {
            public string Name { get; init; } = "";
            public string ImageUrl { get; init; } = "";
            public string VideoUrl { get; init; } = "";
            public string ExerciseId { get; init; } = "";
            public string DetailUrl { get; init; } = "";
            public List<ExSet> Sets { get; init; } = new();
            public string Notes { get; init; } = "";
        }

        private bool RenderExercisesFromHtml(string html)
        {
            var items = ExtractExercises(html);
            if (items.Count == 0) return false;

            foreach (var ex in items)
                SessionStack.Children.Add(BuildExerciseRow(ex));

            return true;
        }

        private static List<ExerciseItem> ExtractExercises(string html)
        {
            var list = new List<ExerciseItem>();
            if (string.IsNullOrWhiteSpace(html)) return list;

            foreach (Match ex in Regex.Matches(html,
                @"<div[^>]*class=['""]exercise['""][^>]*>(?<inner>.*?)</div>\s*</div>\s*",
                RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                string inner = ex.Groups["inner"].Value;

                // Name
                var nameRaw = ExtractFirst(inner, @"<h3[^>]*>(.*?)</h3>");
                string name = WebUtility.HtmlDecode(StripTags(nameRaw ?? "")).Trim();

                // Notes
                string notes = "";
                var notesRaw = ExtractFirst(inner, @"<div[^>]*class=['""]notes['""][^>]*>(.*?)</div>");
                if (!string.IsNullOrWhiteSpace(notesRaw))
                {
                    var p = ExtractFirst(notesRaw, @"<p[^>]*>(.*?)</p>");
                    notes = Sanitize(WebUtility.HtmlDecode(StripTags(p ?? "")));
                }

                // Extract exercise detail URL for info button
                var detailLinkMatch = Regex.Match(inner, @"href=""(/Application/Exercise/Details/(\d+)\?[^""]*)"">", RegexOptions.IgnoreCase);
                string detailUrl = detailLinkMatch.Success ? detailLinkMatch.Groups[1].Value : "";

                // Sets
                var sets = new List<ExSet>();
                foreach (Match s in Regex.Matches(inner,
                    @"<div[^>]*class=['""]prescribed-fields\s+result-col['""][^>]*>.*?<div[^>]*class=['""]fields\s+prescribed-details['""][^>]*>(?<hdr>.*?)</div>(?<extra>.*?)</div>",
                    RegexOptions.Singleline | RegexOptions.IgnoreCase))
                {
                    string hdr = s.Groups["hdr"].Value;
                    string extraBlock = s.Groups["extra"].Value;

                    string reps = StripTags(ExtractFirst(hdr, @"<span[^>]*class=['""]reps_prescribed['""][^>]*>(.*?)</span>") ?? "").Trim();
                    string sign = StripTags(ExtractFirst(hdr, @"<span[^>]*class=['""]pres_sign['""][^>]*>(.*?)</span>") ?? "").Trim();
                    string value = StripTags(ExtractFirst(hdr, @"<span[^>]*class=['""]result_prescribed['""][^>]*>(.*?)</span>") ?? "").Trim();
                    string unit = StripTags(ExtractFirst(hdr, @"<span[^>]*class=['""]unitb_prescribed['""][^>]*>(.*?)</span>") ?? "").Trim();
                    string extra = StripTags(ExtractFirst(extraBlock, @"<div[^>]*class=['""]fields['""][^>]*>(.*?)</div>") ?? "").Trim();

                    reps = Sanitize(reps);
                    sign = Sanitize(sign);
                    value = Sanitize(value);
                    unit = Sanitize(unit);
                    extra = Sanitize(extra);

                    if (!(string.IsNullOrWhiteSpace(reps) && string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(unit)))
                        sets.Add(new ExSet { Reps = reps, Sign = sign, Value = value, Unit = unit, Extra = extra });
                }

                // Derive media by Exercise ID (zero-padded 5 digits)
                var exId = TryExtractExerciseId(inner);
                string id5 = NormalizeExerciseId(exId);

                string imageUrl = !string.IsNullOrWhiteSpace(id5)
                    ? GetExerciseImageUrl(id5)
                    : BuildAbsoluteUrl(ExtractFirst(inner, @"<div[^>]*class=['""]ex-media['""][^>]*>.*?<img[^>]*src=['""](?<src>[^'""]+)['""]"));

                string videoUrl = !string.IsNullOrWhiteSpace(id5) ? GetExerciseVideoUrl(id5) : "";

                list.Add(new ExerciseItem
                {
                    Name = string.IsNullOrWhiteSpace(name) ? "(Untitled exercise)" : name,
                    ImageUrl = imageUrl ?? "",
                    VideoUrl = videoUrl,
                    ExerciseId = id5,
                    DetailUrl = detailUrl,
                    Sets = sets,
                    Notes = notes
                });
            }
            return list;
        }

        private View BuildExerciseRow(ExerciseItem ex)
        {
            var row = new Grid
            {
                Padding = new Thickness(10),
                Margin = new Thickness(0, 6),
                BackgroundColor = Application.Current?.RequestedTheme == AppTheme.Dark
                    ? new Microsoft.Maui.Graphics.Color(0.12f, 0.12f, 0.12f)
                    : new Microsoft.Maui.Graphics.Color(0.97f, 0.97f, 0.97f)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Thumbnail
            View thumb = new Grid { WidthRequest = 72, HeightRequest = 56, Margin = new Thickness(0, 2, 10, 2) };
            if (!string.IsNullOrWhiteSpace(ex.ImageUrl))
            {
                thumb = new Image
                {
                    Source = ImageSource.FromUri(new Uri(ex.ImageUrl)),
                    Aspect = Aspect.AspectFit,
                    WidthRequest = 72,
                    HeightRequest = 56,
                    Margin = new Thickness(0, 2, 10, 2)
                };
            }
            row.Add(thumb, 0, 0);
            Grid.SetRowSpan(thumb, 3);

            // Title + sets
            var rightTop = new Grid();
            rightTop.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
            rightTop.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new Label { Text = ex.Name, FontAttributes = FontAttributes.Bold, FontSize = 16 };
            rightTop.Add(title, 0, 0);

            var setsGrid = BuildSetsGrid(ex.Sets);
            rightTop.Add(setsGrid, 1, 0);

            row.Add(rightTop, 1, 0);

            // Actions - Clean button text without question marks
            var actions = new HorizontalStackLayout { Spacing = 10, Margin = new Thickness(0, 6, 0, 0) };

            if (!string.IsNullOrWhiteSpace(ex.VideoUrl))
            {
                var videoBtn = new Button { Text = "Play Video", Padding = new Thickness(10, 6), CornerRadius = 10 };
                videoBtn.Clicked += async (_, __) =>
                {
                    var url = !string.IsNullOrWhiteSpace(ex.ExerciseId) ? GetExerciseVideoUrl(ex.ExerciseId) : ex.VideoUrl;
                    await ShowVideoOverlayAsync(url, ex.Name);
                };
                actions.Add(videoBtn);
            }

            if (!string.IsNullOrWhiteSpace(ex.ExerciseId))
            {
                var imageBtn = new Button { Text = "Image", Padding = new Thickness(10, 6), CornerRadius = 10 };
                imageBtn.Clicked += (_, __) => ShowImageOverlay(GetExerciseImageUrl(ex.ExerciseId), ex.Name);
                actions.Add(imageBtn);
            }

            // Info button
            if (!string.IsNullOrWhiteSpace(ex.DetailUrl))
            {
                var infoBtn = new Button { Text = "Info", Padding = new Thickness(10, 6), CornerRadius = 10 };
                infoBtn.Clicked += async (_, __) => await OnInfoTapped(ex.DetailUrl, ex.Name, ex.ExerciseId);
                actions.Add(infoBtn);
            }

            if (!string.IsNullOrWhiteSpace(ex.ExerciseId))
            {
                var idTag = new Label
                {
                    Text = $"ID: {ex.ExerciseId}",
                    FontSize = 12,
                    Opacity = 0.7,
                    VerticalTextAlignment = TextAlignment.Center
                };
                actions.Add(idTag);
            }

            if (actions.Children.Count > 0)
                row.Add(actions, 1, 1);

            if (!string.IsNullOrWhiteSpace(ex.Notes))
            {
                var notes = new Label { Text = ex.Notes, FontSize = 13, Opacity = 0.85, Margin = new Thickness(0, 6, 0, 0) };
                row.Add(notes, 1, 2);
            }

            return row;
        }

        private static Grid BuildSetsGrid(IList<ExSet> sets)
        {
            var grid = new Grid
            {
                RowSpacing = 2,
                ColumnSpacing = 6,
                Padding = new Thickness(8, 6),
                BackgroundColor = new Microsoft.Maui.Graphics.Color(1f, 0.95f, 0.8f),
                MinimumWidthRequest = 120
            };

            if (sets.Count == 0)
            {
                grid.Children.Add(new Label { Text = "—", FontSize = 13 });
                return grid;
            }

            static string Line(ExSet s)
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(s.Reps)) parts.Add(s.Reps);
                var rv = $"{s.Sign}{s.Value}".Trim();
                if (!string.IsNullOrWhiteSpace(rv)) parts.Add(rv);
                if (!string.IsNullOrWhiteSpace(s.Unit)) parts.Add(s.Unit);
                return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
            }

            for (int i = 0; i < sets.Count; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var lineRow = new Grid();
                lineRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                lineRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                lineRow.Add(new Label { Text = Line(sets[i]), FontSize = 13 }, 0, 0);

                if (!string.IsNullOrWhiteSpace(sets[i].Extra))
                    lineRow.Add(new Label { Text = sets[i].Extra, FontSize = 12, Opacity = 0.7, Margin = new Thickness(6, 0, 0, 0) }, 1, 0);

                grid.Add(lineRow, 0, i);
            }
            return grid;
        }

        // ======================================================
        //                  INFO BUTTON FUNCTIONALITY
        // ======================================================

        // Info button handler
        private async Task OnInfoTapped(string detailUrl, string exerciseName, string exerciseId)
        {
            try
            {
                string cookie = SessionManager.GetCookie();
                if (string.IsNullOrEmpty(cookie))
                {
                    await DisplayAlert("Error", "Please log in again.", "OK");
                    return;
                }

                var detailHtml = await VisualCoachingService.GetRawSessionHtml(cookie, detailUrl);

                if (string.IsNullOrEmpty(detailHtml))
                {
                    await DisplayAlert("Error", "Could not load exercise details.", "OK");
                    return;
                }

                await ShowExerciseDetailsPopup(detailHtml, exerciseName, exerciseId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TrainingPage] Error loading exercise details: {ex.Message}");
                await DisplayAlert("Error", "Failed to load exercise details.", "OK");
            }
        }

        // Show exercise details modal
        private async Task ShowExerciseDetailsPopup(string detailHtml, string exerciseName, string exerciseId)
        {
            string description = ExtractExerciseDescription(detailHtml);
            string instructions = ExtractExerciseInstructions(detailHtml);
            string muscles = ExtractMuscleGroups(detailHtml);

            var popup = new ContentPage
            {
                Title = exerciseName,
                Content = new ScrollView
                {
                    Content = new VerticalStackLayout
                    {
                        Padding = 20,
                        Spacing = 15,
                        Children =
                        {
                            new Label
                            {
                                Text = exerciseName,
                                FontSize = 22,
                                FontAttributes = FontAttributes.Bold,
                                HorizontalOptions = LayoutOptions.Center
                            },

                            new Label
                            {
                                Text = $"Exercise ID: {exerciseId}",
                                FontSize = 12,
                                TextColor = Color.FromArgb("#666666"),
                                HorizontalOptions = LayoutOptions.Center
                            },

                            CreateInfoSection("Description", description),
                            CreateInfoSection("Instructions", instructions),
                            CreateInfoSection("Target Muscles", muscles),

                            new Button
                            {
                                Text = "Close",
                                BackgroundColor = Color.FromArgb("#2196F3"),
                                TextColor = Colors.White,
                                HorizontalOptions = LayoutOptions.Center,
                                WidthRequest = 120,
                                Margin = new Thickness(0, 20, 0, 0)
                            }
                        }
                    }
                }
            };

            var contentStack = (VerticalStackLayout)((ScrollView)popup.Content).Content;
            var closeButton = (Button)contentStack.Children.Last();
            closeButton.Clicked += async (s, e) => await Navigation.PopModalAsync();

            await Navigation.PushModalAsync(popup);
        }

        private VerticalStackLayout CreateInfoSection(string title, string content)
        {
            var section = new VerticalStackLayout { Spacing = 5 };

            if (!string.IsNullOrEmpty(content))
            {
                section.Children.Add(new Label
                {
                    Text = title,
                    FontSize = 16,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#333333")
                });

                section.Children.Add(new Label
                {
                    Text = content,
                    FontSize = 14,
                    TextColor = Color.FromArgb("#666666"),
                    LineBreakMode = LineBreakMode.WordWrap
                });
            }

            return section;
        }

        // HTML parsing helpers for exercise details
        private string ExtractExerciseDescription(string html)
        {
            if (string.IsNullOrEmpty(html)) return "No description available.";

            var patterns = new[]
            {
                @"<div[^>]*class=""[^""]*description[^""]*""[^>]*>(.*?)</div>",
                @"<p[^>]*class=""[^""]*description[^""]*""[^>]*>(.*?)</p>",
                @"<div[^>]*class=""[^""]*exercise-description[^""]*""[^>]*>(.*?)</div>",
                @"<div[^>]*id=""[^""]*description[^""]*""[^>]*>(.*?)</div>"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(html, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return CleanHtmlText(match.Groups[1].Value);
                }
            }

            var paragraphs = Regex.Matches(html, @"<p[^>]*>(.*?)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            foreach (Match p in paragraphs)
            {
                string text = CleanHtmlText(p.Groups[1].Value);
                if (!string.IsNullOrEmpty(text) && text.Length > 20)
                {
                    return text;
                }
            }

            return "No description available.";
        }

        private string ExtractExerciseInstructions(string html)
        {
            if (string.IsNullOrEmpty(html)) return "No instructions available.";

            var patterns = new[]
            {
                @"<div[^>]*class=""[^""]*instructions[^""]*""[^>]*>(.*?)</div>",
                @"<div[^>]*class=""[^""]*steps[^""]*""[^>]*>(.*?)</div>",
                @"<ol[^>]*>(.*?)</ol>",
                @"<ul[^>]*>(.*?)</ul>",
                @"<div[^>]*id=""[^""]*instructions[^""]*""[^>]*>(.*?)</div>"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(html, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return CleanHtmlText(match.Groups[1].Value);
                }
            }

            return "No instructions available.";
        }

        private string ExtractMuscleGroups(string html)
        {
            if (string.IsNullOrEmpty(html)) return "Muscle information not available.";

            var patterns = new[]
            {
                @"<div[^>]*class=""[^""]*muscles[^""]*""[^>]*>(.*?)</div>",
                @"<div[^>]*class=""[^""]*muscle-groups[^""]*""[^>]*>(.*?)</div>",
                @"<span[^>]*class=""[^""]*muscle[^""]*""[^>]*>(.*?)</span>",
                @"<div[^>]*id=""[^""]*muscles[^""]*""[^>]*>(.*?)</div>"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(html, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return CleanHtmlText(match.Groups[1].Value);
                }
            }

            return "Muscle information not available.";
        }

        private string CleanHtmlText(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";

            string text = Regex.Replace(html, "<.*?>", " ");
            text = System.Net.WebUtility.HtmlDecode(text);
            text = Regex.Replace(text, @"\s+", " ").Trim();

            return text;
        }

        // ======================================================
        //                  Helpers / parsing / dates
        // ======================================================
        private static string StripTags(string s) => Regex.Replace(s ?? "", "<.*?>", string.Empty);
        private static string? ExtractFirst(string html, string pattern)
            => Regex.Match(html ?? "", pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase) is var m && m.Success ? m.Groups[1].Value : null;

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            var t = s.Replace('\u00A0', ' ');
            t = Regex.Replace(t, "[\u200B\u200C\u200D\uFEFF]", "");
            t = Regex.Replace(t, @"\s+", " ");
            return t.Trim();
        }

        // --- Exercise media helpers (zero-padded ids) ---
        private static string NormalizeExerciseId(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var digits = new string(raw.Where(char.IsDigit).ToArray());
            if (string.IsNullOrEmpty(digits)) return "";
            if (digits.Length > 5) digits = digits[^5..];
            return digits.PadLeft(5, '0');
        }

        private static string GetExerciseImageUrl(string id5)
            => $"https://cloud.visualcoaching2.com/Application/Exercise/Image/{id5}";

        private static string GetExerciseVideoUrl(string id5)
            => $"https://cloud.visualcoaching2.com/VCP/Images/Exercises/{id5}.mp4";

        private static string TryExtractExerciseId(string innerHtml)
        {
            var m = Regex.Match(innerHtml, @"/Application/Exercise/Details/(\d+)", RegexOptions.IgnoreCase);
            if (m.Success) return NormalizeExerciseId(m.Groups[1].Value);

            m = Regex.Match(innerHtml, @"/Application/Exercise/Image/(\d+)", RegexOptions.IgnoreCase);
            if (m.Success) return NormalizeExerciseId(m.Groups[1].Value);

            m = Regex.Match(innerHtml, @"/VCP/Images/Exercises/(\d+)\.mp4", RegexOptions.IgnoreCase);
            if (m.Success) return NormalizeExerciseId(m.Groups[1].Value);

            m = Regex.Match(innerHtml, @"\b(\d{4,6})\b");
            if (m.Success) return NormalizeExerciseId(m.Groups[1].Value);

            return "";
        }

        private static string BuildAbsoluteUrl(string? maybeRelative)
        {
            if (string.IsNullOrWhiteSpace(maybeRelative)) return "";
            if (Uri.TryCreate(maybeRelative, UriKind.Absolute, out _)) return maybeRelative;
            return $"https://cloud.visualcoaching2.com{(maybeRelative!.StartsWith("/") ? "" : "/")}{maybeRelative}";
        }

        // --- URL week/day handling (accept 0..6 OR 1..7; store as 0..6 Mon..Sun internally) ---
        private void ParseWeekAndDayVcFromUrl(string raw)
        {
            _week = GetQueryInt(raw, "week", 0);

            int dayRaw = GetQueryInt(raw, "day", 0);

            // Convert API day (1-7) to internal representation (0-6)
            if (dayRaw >= 1 && dayRaw <= 7)
            {
                _dayVc = dayRaw - 1; // 1..7 -> 0..6 (Mon=0, Sun=6)
            }
            else if (dayRaw >= 0 && dayRaw <= 6)
            {
                _dayVc = dayRaw; // Already 0..6
            }
            else
            {
                _dayVc = 0; // default Monday
            }

            Debug.WriteLine($"[ParseWeekAndDayVc] dayRaw from URL: {dayRaw}, _dayVc (internal): {_dayVc}");

            // Update selected date based on the parsed day
            if (_anchorDate.HasValue)
            {
                _selectedDate = _programWeek0Monday.AddDays(7 * _week + _dayVc);
                Debug.WriteLine($"[ParseWeekAndDayVc] Updated _selectedDate to: {_selectedDate:yyyy-MM-dd dddd}");
            }
        }

        // FIXED: Convert internal 0-6 to API 1-7
        private static string WithWeekDayVc(string raw, int week, int dayVc)
        {
            int qIdx = raw.IndexOf('?');
            string path = qIdx < 0 ? raw : raw[..qIdx];
            string query = qIdx < 0 ? "" : raw[(qIdx + 1)..];

            var parts = query.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

            void Upsert(string k, string v)
            {
                int i = parts.FindIndex(p => p.StartsWith(k + "=", StringComparison.OrdinalIgnoreCase));
                if (i >= 0) parts[i] = $"{k}={v}";
                else parts.Add($"{k}={v}");
            }

            Upsert("week", week.ToString(CultureInfo.InvariantCulture)); // 0-based week

            // FIXED: Convert internal 0-6 (Mon-Sun) to API 1-7 (Mon=1, Sun=7)
            int apiDay = Math.Clamp(dayVc, 0, 6) + 1; // 0->1, 1->2, ..., 6->7
            Upsert("day", apiDay.ToString(CultureInfo.InvariantCulture)); // 1..7 for API

            Upsert("session", "0");
            Upsert("i", "0");
            if (!parts.Any(p => p.StartsWith("format=", StringComparison.OrdinalIgnoreCase))) parts.Add("format=Tablet");
            if (!parts.Any(p => p.StartsWith("version=", StringComparison.OrdinalIgnoreCase))) parts.Add("version=2");

            return $"{path}?{string.Join("&", parts)}";
        }

        private static int GetQueryInt(string url, string key, int fallback)
        {
            int q = url.IndexOf('?');
            string query = q >= 0 ? url[(q + 1)..] : "";
            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                if (part[..eq].Equals(key, StringComparison.OrdinalIgnoreCase) && int.TryParse(part[(eq + 1)..], out var n))
                    return n;
            }
            return fallback;
        }

        private static string ForceFirstIndexAndNormalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;
            int q = raw.IndexOf('?');
            string path = q < 0 ? raw : raw[..q];
            string query = q < 0 ? "" : raw[(q + 1)..];

            var parts = query.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
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
            if (!parts.Any(p => p.StartsWith("format=", StringComparison.OrdinalIgnoreCase))) parts.Add("format=Tablet");
            if (!parts.Any(p => p.StartsWith("version=", StringComparison.OrdinalIgnoreCase))) parts.Add("version=2");

            return $"{path}?{string.Join("&", parts)}";
        }

        private static string ReplaceQuery(string url, string key, string value)
        {
            int q = url.IndexOf('?');
            string path = q < 0 ? url : url[..q];
            var kvs = new List<string>();
            if (q >= 0)
            {
                foreach (var p in url[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    int eq = p.IndexOf('=');
                    if (eq <= 0) continue;
                    var k = p[..eq];
                    if (!k.Equals(key, StringComparison.OrdinalIgnoreCase))
                        kvs.Add(p);
                }
            }
            kvs.Add($"{key}={value}");
            return $"{path}?{string.Join("&", kvs)}";
        }

        // Monday-start calendar math
        private static DateTime WeekMonday(DateTime d)
        {
            int diff = ((7 + (int)d.DayOfWeek - (int)DayOfWeek.Monday) % 7);
            return d.Date.AddDays(-diff);
        }

        private void ComputeProgramBaselineMonday()
        {
            var ad = _anchorDate!.Value;
            var adMon = WeekMonday(ad);
            _programWeek0Monday = adMon.AddDays(-7 * _entryWeekForProgram);
        }

        private DateTime SelectedDate
            => _selectedDate ?? _programWeek0Monday.AddDays(7 * _week + Math.Clamp(_dayVc, 0, 6));

        private int SelectedWeekIndex
        {
            get
            {
                var monday = WeekMonday(SelectedDate);
                var days = (monday - _programWeek0Monday).TotalDays;
                return Math.Max(0, (int)(days / 7.0));
            }
        }

        private int DisplayWeek
        {
            get
            {
                var span = WeekMonday(SelectedDate) - _programWeek0Monday;
                int w = (int)(span.TotalDays / 7.0); // zero-based
                return Math.Max(0, w) + 1;           // display 1-based
            }
        }

        private void TryLoadAnchorFromUrl(string rawUrl)
        {
            var ad = GetQueryValue(rawUrl, "ad");
            if (TryParseYMD(ad, out var dt))
            {
                _anchorDate = dt.Date;
                _selectedDate ??= _anchorDate;
                ComputeProgramBaselineMonday();
            }
        }

        private static bool TryParseYMD(string? s, out DateTime dt)
        {
            dt = default;
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                return true;
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt);
        }

        private static string GetQueryValue(string url, string key)
        {
            int q = url.IndexOf('?'); if (q < 0) return "";
            foreach (var p in url[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int eq = p.IndexOf('=');
                if (eq <= 0) continue;
                if (p[..eq].Equals(key, StringComparison.OrdinalIgnoreCase)) return WebUtility.UrlDecode(p[(eq + 1)..]);
            }
            return "";
        }

        private static int? ParseProgramId(string url)
        {
            var m = Regex.Match(url ?? "", @"/Program/Session/(\d+)", RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var id)) return id;
            return null;
        }

        // ======================================================
        //                 Image & Video (lightbox) - IMPROVED
        // ======================================================

        // Helper: safely clean up current media element
        private void CleanupCurrentMedia()
        {
            if (_currentMediaElement != null)
            {
                try
                {
                    // Don't call Stop() or Pause() - just detach source
                    _currentMediaElement.Source = "";
                    _currentMediaElement = null;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Media] Cleanup warning: {ex.Message}");
                }
            }
        }

        // Helper: download behind-auth video to a temp file using the saved cookie
        private static async Task<string?> DownloadVideoToTempAsync(string url)
        {
            try
            {
                var cookie = SessionManager.GetCookie() ?? "";
                using var handler = new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false };
                using var http = new HttpClient(handler);

                if (!string.IsNullOrEmpty(cookie))
                    http.DefaultRequestHeaders.Add("Cookie", $".VCPCOOKIES={cookie}");
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; SportsTrainingApp/1.0)");

                var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (!resp.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[Video] HTTP {resp.StatusCode} for {url}");
                    return null;
                }

                var ext = Path.GetExtension(url);
                if (string.IsNullOrWhiteSpace(ext)) ext = ".mp4";
                var tempPath = Path.Combine(FileSystem.CacheDirectory, $"vcvideo_{Guid.NewGuid():N}{ext}");

                await using var src = await resp.Content.ReadAsStreamAsync();
                await using var fs = File.OpenWrite(tempPath);
                await src.CopyToAsync(fs);

                return tempPath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Video] Download error: {ex.Message}");
                return null;
            }
        }

        private async Task ShowVideoOverlayAsync(string videoUrl, string? title = null)
        {
            // Clean up any existing media first
            CleanupCurrentMedia();

            // Build absolute URL if needed
            if (!Uri.TryCreate(videoUrl, UriKind.Absolute, out var _))
                videoUrl = $"https://cloud.visualcoaching2.com{(videoUrl.StartsWith("/") ? "" : "/")}{videoUrl}";

            // Create the player with safer initialization
            var media = new ToolkitMediaElement
            {
                ShouldAutoPlay = true,
                ShouldShowPlaybackControls = true,
                Aspect = Aspect.AspectFit,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill
            };

            // Store reference for cleanup
            _currentMediaElement = media;

            bool playing = false;

            try
            {
                // 1) Try direct URL first (works if the file is publicly accessible)
                media.Source = videoUrl;
                playing = true;
                Debug.WriteLine($"[Video] Using direct URL: {videoUrl}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Video] Direct URL failed: {ex.Message}");
                playing = false;
            }

            // 2) Fallback: download with authentication cookie
            if (!playing)
            {
                try
                {
                    var localPath = await DownloadVideoToTempAsync(videoUrl);
                    if (localPath != null && File.Exists(localPath))
                    {
                        media.Source = localPath;
                        playing = true;
                        Debug.WriteLine($"[Video] Using downloaded file: {localPath}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Video] Download fallback failed: {ex.Message}");
                }
            }

            if (!playing)
            {
                CleanupCurrentMedia();
                await DisplayAlert("Video Error", "Unable to load the video.", "OK");
                return;
            }

            // Show the lightbox
            LightboxTitle.Text = string.IsNullOrWhiteSpace(title) ? "Video" : title;
            LightboxContent.Content = media;
            LightboxOverlay.IsVisible = true;
        }

        private void ShowImageOverlay(string imageUrl, string? title = null)
        {
            try
            {
                // Clean up any existing media
                CleanupCurrentMedia();

                var img = new Image
                {
                    Source = ImageSource.FromUri(new Uri(imageUrl)),
                    Aspect = Aspect.AspectFit,
                    HorizontalOptions = LayoutOptions.Fill,
                    VerticalOptions = LayoutOptions.Fill
                };

                LightboxTitle.Text = string.IsNullOrWhiteSpace(title) ? "Image" : title;
                LightboxContent.Content = img;
                LightboxOverlay.IsVisible = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Image] Error: {ex.Message}");
                DisplayAlert("Error", "Unable to load image.", "OK");
            }
        }

        private void HideLightbox()
        {
            try
            {
                // Clean up media safely
                CleanupCurrentMedia();

                // Clear content and hide overlay
                LightboxContent.Content = null;
                LightboxOverlay.IsVisible = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Lightbox] Hide error: {ex.Message}");
                // Force hide anyway
                LightboxOverlay.IsVisible = false;
            }
        }

        // Event handlers for lightbox
        private void OnLightboxCloseClicked(object sender, EventArgs e) => HideLightbox();
        private void OnLightboxBackgroundTapped(object sender, TappedEventArgs e) => HideLightbox();

        // ======================================================
        //                 LINKED PROGRAMS SUPPORT - CLEAN BUTTONS
        // ======================================================

        // Compact exercise row for inline display with clean button text
        private View BuildCompactExerciseRow(ExerciseItem ex)
        {
            var row = new Grid
            {
                Padding = new Thickness(8),
                Margin = new Thickness(0, 4),
                BackgroundColor = Application.Current?.RequestedTheme == AppTheme.Dark
                    ? new Microsoft.Maui.Graphics.Color(0.15f, 0.15f, 0.15f)
                    : new Microsoft.Maui.Graphics.Color(0.98f, 0.98f, 0.98f)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Smaller thumbnail for compact view - make it tappable for image overlay
            View thumb = new Grid { WidthRequest = 48, HeightRequest = 36, Margin = new Thickness(0, 2, 8, 2) };
            if (!string.IsNullOrWhiteSpace(ex.ImageUrl))
            {
                var image = new Image
                {
                    Source = ImageSource.FromUri(new Uri(ex.ImageUrl)),
                    Aspect = Aspect.AspectFit,
                    WidthRequest = 48,
                    HeightRequest = 36,
                    Margin = new Thickness(0, 2, 8, 2)
                };

                // Make thumbnail tappable to show full image
                var imageTap = new TapGestureRecognizer();
                imageTap.Tapped += (_, __) => ShowImageOverlay(ex.ImageUrl, ex.Name);
                image.GestureRecognizers.Add(imageTap);

                thumb = image;
            }
            row.Add(thumb, 0, 0);
            Grid.SetRowSpan(thumb, 3);

            // Title + sets in more compact layout
            var rightTop = new Grid();
            rightTop.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
            rightTop.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new Label
            {
                Text = ex.Name,
                FontAttributes = FontAttributes.Bold,
                FontSize = 14
            };
            rightTop.Add(title, 0, 0);

            // Compact sets display in the top right
            if (ex.Sets.Count > 0)
            {
                var compactSetsGrid = BuildCompactSetsGrid(ex.Sets);
                rightTop.Add(compactSetsGrid, 1, 0);
            }

            row.Add(rightTop, 1, 0);

            // Compact actions row with clean button text
            var actions = new FlexLayout
            {
                Direction = FlexDirection.Row,
                Wrap = FlexWrap.Wrap,
                JustifyContent = FlexJustify.Start,
                AlignItems = FlexAlignItems.Start,
                Margin = new Thickness(0, 4, 0, 0)
            };

            if (!string.IsNullOrWhiteSpace(ex.VideoUrl))
            {
                var videoBtn = new ImageButton
                {
                    Source = "video.png",   // put video.png in Resources/Images
                    HeightRequest = 28,
                    WidthRequest = 28,
                    BackgroundColor = Colors.Transparent,
                    Margin = new Thickness(0, 0, 4, 4)
                };
                videoBtn.Clicked += async (_, __) =>
                {
                    var url = !string.IsNullOrWhiteSpace(ex.ExerciseId) ? GetExerciseVideoUrl(ex.ExerciseId) : ex.VideoUrl;
                    await ShowVideoOverlayAsync(url, ex.Name);
                };
                actions.Add(videoBtn);
            }

            // Info button for details
            if (!string.IsNullOrWhiteSpace(ex.DetailUrl))
            {
                var infoBtn = new ImageButton
                {
                    Source = "info.png",   // put info.png in Resources/Images
                    HeightRequest = 28,
                    WidthRequest = 28,
                    BackgroundColor = Colors.Transparent,
                    Margin = new Thickness(0, 0, 4, 4)
                };

                infoBtn.Clicked += async (_, __) =>
                    await OnInfoTapped(ex.DetailUrl, ex.Name, ex.ExerciseId);

                actions.Add(infoBtn);
            }

            if (actions.Children.Count > 0)
                row.Add(actions, 1, 1);

            // Notes if available
            if (!string.IsNullOrWhiteSpace(ex.Notes))
            {
                var notes = new Label
                {
                    Text = ex.Notes,
                    FontSize = 12,
                    Opacity = 0.8,
                    Margin = new Thickness(0, 4, 0, 0),
                    LineBreakMode = LineBreakMode.WordWrap
                };
                row.Add(notes, 1, 2);
            }

            return row;
        }

        // Compact sets grid for inline display
        private static Grid BuildCompactSetsGrid(IList<ExSet> sets)
        {
            var grid = new Grid
            {
                RowSpacing = 1,
                ColumnSpacing = 4,
                Padding = new Thickness(6, 4),
                BackgroundColor = new Microsoft.Maui.Graphics.Color(1f, 0.95f, 0.8f),
                MinimumWidthRequest = 80
            };

            if (sets.Count == 0)
            {
                grid.Children.Add(new Label { Text = "—", FontSize = 11 });
                return grid;
            }

            static string Line(ExSet s)
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(s.Reps)) parts.Add(s.Reps);
                var rv = $"{s.Sign}{s.Value}".Trim();
                if (!string.IsNullOrWhiteSpace(rv)) parts.Add(rv);
                if (!string.IsNullOrWhiteSpace(s.Unit)) parts.Add(s.Unit);
                return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
            }

            // Show first few sets, or condense if many
            int displaySets = Math.Min(sets.Count, 3);
            for (int i = 0; i < displaySets; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var label = new Label
                {
                    Text = Line(sets[i]),
                    FontSize = 11
                };

                grid.Add(label, 0, i);
            }

            // If more sets exist, show indicator
            if (sets.Count > 3)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.Add(new Label
                {
                    Text = $"+ {sets.Count - 3} more",
                    FontSize = 10,
                    Opacity = 0.7
                }, 0, displaySets);
            }

            return grid;
        }

        // Compact table for inline display
        private View BuildCompactDayTable(IList<List<string>> blocksAsCols)
        {
            var mainStack = new VerticalStackLayout { Spacing = 8 };

            for (int colIdx = 0; colIdx < blocksAsCols.Count; colIdx++)
            {
                var column = blocksAsCols[colIdx];
                if (column.Count == 0) continue;

                // Create a compact representation of each column
                var colStack = new VerticalStackLayout { Spacing = 4 };

                foreach (var item in column)
                {
                    if (string.IsNullOrWhiteSpace(StripTags(item))) continue;

                    var content = CreateContentWithLinkedPrograms(item);
                    colStack.Children.Add(content);
                }

                if (colStack.Children.Count > 0)
                {
                    // Add a subtle separator between columns
                    if (mainStack.Children.Count > 0)
                    {
                        mainStack.Children.Add(new BoxView
                        {
                            HeightRequest = 1,
                            BackgroundColor = Colors.LightGray,
                            Margin = new Thickness(0, 5)
                        });
                    }
                    mainStack.Children.Add(colStack);
                }
            }

            return mainStack;
        }

        // ======================================================
        //                 STRENGTH PROGRAM SUPPORT
        // ======================================================

        // Method to detect if a program is a "Strength" type
        private async Task<bool> IsStrengthProgramAsync(string cookie, string programId)
        {
            try
            {
                // Try to get program metadata - using heuristic approach by checking day 0 content
                string testUrl = $"/Application/Program/Session/{programId}?week=0&day=0&session=0&i=0&format=Tablet&version=2";
                string html = await VisualCoachingService.GetRawSessionHtml(cookie, testUrl);

                // If day 0 has substantial content, it's likely a strength program
                if (!string.IsNullOrWhiteSpace(html) &&
                    (html.Contains("class=\"exercise\"", StringComparison.OrdinalIgnoreCase) ||
                     html.Contains("weekly-no-background", StringComparison.OrdinalIgnoreCase) ||
                     html.Contains("strength", StringComparison.OrdinalIgnoreCase)))
                {
                    Debug.WriteLine($"[Program Detection] Program {programId} appears to be a Strength program (has day 0 content)");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Program Detection] Error checking if program {programId} is Strength type: {ex.Message}");
                return false;
            }
        }

        // Updated method to find first content with Strength program support
        private async Task<(int week, int day)> FindFirstContentWeekDayAsync(string cookie, string programId)
        {
            bool foundContent = false;
            int targetWeek = 0;
            int targetDay = 1; // Default to day 1 for non-strength programs

            // Check if this is a strength program that might start from day 0
            bool isStrengthProgram = await IsStrengthProgramAsync(cookie, programId);

            async Task<bool> HasContentAsync(int week, int day)
            {
                string testUrl = $"/Application/Program/Session/{programId}?week={week}&day={day}&session=0&i=0&format=Tablet&version=2";
                string html = await VisualCoachingService.GetRawSessionHtml(cookie, testUrl);

                return !string.IsNullOrWhiteSpace(html) &&
                       !html.Contains("No program content", StringComparison.OrdinalIgnoreCase) &&
                       (html.Contains("class=\"exercise\"", StringComparison.OrdinalIgnoreCase) ||
                        html.Contains("weekly-no-background", StringComparison.OrdinalIgnoreCase) ||
                        html.Contains("<h1", StringComparison.OrdinalIgnoreCase));
            }

            // Search strategy depends on program type
            if (isStrengthProgram)
            {
                Debug.WriteLine($"[LinkedProgram] Searching strength program {programId} starting from day 0");

                // For strength programs, start from day 0
                for (int week = 0; week < MaxWeeks && !foundContent; week++)
                {
                    // Check day 0 first for strength programs
                    if (await HasContentAsync(week, 0))
                    {
                        targetWeek = week;
                        targetDay = 0;
                        foundContent = true;
                        Debug.WriteLine($"[LinkedProgram] Found strength content at week {week}, day 0");
                        break;
                    }

                    // Then check days 1-7
                    for (int day = 1; day <= 7 && !foundContent; day++)
                    {
                        if (await HasContentAsync(week, day))
                        {
                            targetWeek = week;
                            targetDay = day;
                            foundContent = true;
                            Debug.WriteLine($"[LinkedProgram] Found strength content at week {week}, day {day}");
                            break;
                        }
                    }
                }
            }
            else
            {
                Debug.WriteLine($"[LinkedProgram] Searching regular program {programId} starting from day 1");

                // For regular programs, search days 1-7 only
                for (int week = 0; week < MaxWeeks && !foundContent; week++)
                {
                    for (int day = 1; day <= 7 && !foundContent; day++)
                    {
                        if (await HasContentAsync(week, day))
                        {
                            targetWeek = week;
                            targetDay = day;
                            foundContent = true;
                            Debug.WriteLine($"[LinkedProgram] Found regular content at week {week}, day {day}");
                            break;
                        }
                    }
                }
            }

            if (!foundContent)
            {
                Debug.WriteLine($"[LinkedProgram] No content found for program {programId}, using defaults");
                targetWeek = 0;
                targetDay = isStrengthProgram ? 0 : 1; // Use day 0 for strength, day 1 for others
            }

            return (targetWeek, targetDay);
        }

        // Enhanced FetchAllSessions method to handle Strength programs properly
        public async Task<bool> FetchAllSessionsAsync(int programId, string cookie)
        {
            bool hasContent = false;

            try
            {
                // Check if it's a strength program
                bool isStrengthProgram = await IsStrengthProgramAsync(cookie, programId.ToString());

                Debug.WriteLine($"[Session Fetch] Program {programId} is {(isStrengthProgram ? "Strength" : "Regular")} type");

                if (isStrengthProgram)
                {
                    // For strength programs, check day 0 for each week first
                    for (int week = 0; week <= 13; week++)
                    {
                        if (await HasSessionContentAsync(programId, week, 0, cookie))
                        {
                            Debug.WriteLine($"[Session Fetch] Found Strength content: Week {week}, Day 0");
                            hasContent = true;
                            // Process this session...
                        }

                        // Then check regular days 1-7
                        for (int day = 1; day <= 7; day++)
                        {
                            if (await HasSessionContentAsync(programId, week, day, cookie))
                            {
                                Debug.WriteLine($"[Session Fetch] Found Strength content: Week {week}, Day {day}");
                                hasContent = true;
                                // Process this session...
                            }
                        }
                    }
                }
                else
                {
                    // For regular programs, only check days 1-7
                    for (int week = 0; week <= 13; week++)
                    {
                        for (int day = 1; day <= 7; day++)
                        {
                            if (await HasSessionContentAsync(programId, week, day, cookie))
                            {
                                Debug.WriteLine($"[Session Fetch] Found Regular content: Week {week}, Day {day}");
                                hasContent = true;
                                // Process this session...
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Session Fetch] Error fetching sessions for program {programId}: {ex.Message}");
            }

            return hasContent;
        }

        // Helper method to check if a session has content
        private async Task<bool> HasSessionContentAsync(int programId, int week, int day, string cookie)
        {
            try
            {
                string url = $"/Application/Program/Session/{programId}?week={week}&day={day}&session=0&i=0&format=Tablet&version=2";
                string html = await VisualCoachingService.GetRawSessionHtml(cookie, url);

                bool hasContent = !string.IsNullOrWhiteSpace(html) &&
                                 !html.Contains("No program content", StringComparison.OrdinalIgnoreCase) &&
                                 (html.Contains("class=\"exercise\"", StringComparison.OrdinalIgnoreCase) ||
                                  html.Contains("weekly-no-background", StringComparison.OrdinalIgnoreCase) ||
                                  html.Contains("<h1", StringComparison.OrdinalIgnoreCase));

                if (hasContent)
                {
                    Debug.WriteLine($"[Content Check] Program {programId} Week {week} Day {day}: HAS CONTENT");
                }

                return hasContent;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Content Check] Error checking program {programId} Week {week} Day {day}: {ex.Message}");
                return false;
            }
        }

        // Updated LoadLinkedProgramInlineAsync with clean header (no symbols or ID)
        private async Task LoadLinkedProgramInlineAsync(string programId, VerticalStackLayout container)
        {
            try
            {
                Debug.WriteLine($"[LinkedProgram] Loading inline content for program ID: {programId}");

                string cookie = SessionManager.GetCookie();
                if (string.IsNullOrEmpty(cookie))
                {
                    container.Children.Clear();
                    container.Children.Add(new Label { Text = "Please log in again.", TextColor = Colors.Red });
                    return;
                }

                // Find first content considering program type (might be day 0 for Strength)
                (int week, int day) = await FindFirstContentWeekDayAsync(cookie, programId);

                Debug.WriteLine($"[LinkedProgram] Found content at week {week}, day {day} for program {programId}");

                string url = $"/Application/Program/Session/{programId}?week={week}&day={day}&session=0&i=0&format=Tablet&version=2";
                Debug.WriteLine($"[LinkedProgram] Fetching URL: {url}");

                string html = await VisualCoachingService.GetRawSessionHtml(cookie, url);

                container.Children.Clear();

                if (string.IsNullOrWhiteSpace(html))
                {
                    Debug.WriteLine($"[LinkedProgram] No HTML content returned for program {programId}");
                    container.Children.Add(new Label
                    {
                        Text = "No content available.",
                        FontAttributes = FontAttributes.Italic,
                        Opacity = 0.7
                    });
                    return;
                }

                Debug.WriteLine($"[LinkedProgram] HTML length: {html.Length} characters");

                // Try to render exercises first, then table format (no header with IDs)
                var exercises = ExtractExercises(html);
                Debug.WriteLine($"[LinkedProgram] Found {exercises.Count} exercises");

                if (exercises.Count > 0)
                {
                    foreach (var ex in exercises)
                    {
                        Debug.WriteLine($"[LinkedProgram] Adding exercise: {ex.Name}");
                        var compactExercise = BuildCompactExerciseRow(ex);
                        container.Children.Add(compactExercise);
                    }
                }
                else
                {
                    // Try table format
                    var blockCols = ExtractProgramColumns(html);
                    Debug.WriteLine($"[LinkedProgram] Found {blockCols.Count} table columns");

                    if (blockCols.Count > 0)
                    {
                        var tableView = BuildCompactDayTable(blockCols);
                        container.Children.Add(tableView);
                    }
                    else
                    {
                        Debug.WriteLine($"[LinkedProgram] No exercises or table content found");
                        container.Children.Add(new Label
                        {
                            Text = "Content format not recognized for inline display.",
                            FontAttributes = FontAttributes.Italic,
                            Opacity = 0.7,
                            FontSize = 12
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LinkedProgram] Inline load error for program {programId}: {ex.Message}");

                container.Children.Clear();
                container.Children.Add(new Label
                {
                    Text = $"Error loading program content: {ex.Message}",
                    TextColor = Colors.Red,
                    FontAttributes = FontAttributes.Italic,
                    FontSize = 12,
                    LineBreakMode = LineBreakMode.WordWrap
                });
            }
        }

        // Legacy methods for compatibility
        public void FetchAllSessions(int programId)
        {
            // Check for Week 0, Day 0 first (for programs like incline cycling)
            if (HasWeekZero(programId))
            {
                FetchSession(programId, 0, 0); // Week 0, Day 0

                // Then check for other days in Week 0
                for (int day = 1; day <= 7; day++)
                {
                    if (HasSessionContent(programId, 0, day))
                    {
                        FetchSession(programId, 0, day);
                    }
                }
            }

            // Continue with regular weeks 1-13, days 1-7
            for (int week = 1; week <= 13; week++)
            {
                for (int day = 1; day <= 7; day++)
                {
                    if (HasSessionContent(programId, week, day))
                    {
                        FetchSession(programId, week, day);
                    }
                }
            }
        }

        private bool HasWeekZero(int programId)
        {
            // Check if this program type has a Week 0
            // This would need to be determined by program metadata
            return programId == 1476483; // Incline cycling program
        }

        private bool HasSessionContent(int programId, int week, int day)
        {
            // This method should check if session content exists
            // You'll need to implement this based on your existing logic
            // For now, return true to attempt fetching
            return true;
        }

        private void FetchSession(int programId, int week, int day)
        {
            string url = string.Format(
                "https://cloud.visualcoaching2.com/Application/Program/Session/{0}?week={1}&day={2}&session=0&i=0&format=Tablet&version=2",
                programId, week, day
            );

            // Add error handling for server issues
            try
            {
                // Your existing fetch logic here
                Debug.WriteLine($"[VCS] Fetching session from: {url}");

                // Call your existing GetRawSessionHtml or similar method
                // GetRawSessionHtml(url);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VCS] Failed to fetch session: {url}. Error: {ex.Message}");
                // Maybe retry or queue for later
            }
        }
        // Example usage in TrainingPage.xaml.cs or a new DiaryPage

        // Add this method to your TrainingPage class to load diary data
        private async Task LoadDiaryDataAsync(string userEmail, string date, int programId, int week, int day)
        {
            try
            {
                string cookie = SessionManager.GetCookie();
                if (string.IsNullOrEmpty(cookie))
                {
                    await DisplayAlert("Error", "Please log in again.", "OK");
                    return;
                }

                Debug.WriteLine($"[Diary] Loading diary for {userEmail} on {date}");

                // Method 1: Get diary data by email (recommended for simplicity)
                var performanceDiaryJson = await VisualCoachingService.GetDiaryDataByEmail(
                    cookie, userEmail, date, programId, week, day, "performance");

                var wellnessDiaryJson = await VisualCoachingService.GetDiaryDataByEmail(
                    cookie, userEmail, date, programId, week, day, "wellness");

                // Method 2: Manual approach (if you need more control)
                // var userInfo = await VisualCoachingService.GetUserInfo(cookie, userEmail);
                // if (userInfo != null)
                // {
                //     var performanceDiaryJson = await VisualCoachingService.GetDiaryData(
                //         cookie, userInfo.PerformanceDiaryId, date, userInfo.UserId, 
                //         programId, week, day);
                // }

                // Parse and display the diary data
                if (!string.IsNullOrEmpty(performanceDiaryJson))
                {
                    var performanceEntry = VisualCoachingService.ParseDiaryData(performanceDiaryJson);
                    if (performanceEntry != null)
                    {
                        DisplayDiaryData("Performance Diary", performanceEntry);
                    }
                }

                if (!string.IsNullOrEmpty(wellnessDiaryJson))
                {
                    var wellnessEntry = VisualCoachingService.ParseDiaryData(wellnessDiaryJson);
                    if (wellnessEntry != null)
                    {
                        DisplayDiaryData("Wellness Diary", wellnessEntry);
                    }
                }

                if (string.IsNullOrEmpty(performanceDiaryJson) && string.IsNullOrEmpty(wellnessDiaryJson))
                {
                    Debug.WriteLine("[Diary] No diary data found");
                    // You might want to show a message to the user
                }
            }
            catch (UnauthorizedAccessException)
            {
                await DisplayAlert("Session Expired", "Please log in again.", "OK");
                SessionManager.ClearCookie();
                await Shell.Current.GoToAsync("//LoginPage");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Diary] Error loading diary data: {ex.Message}");
                await DisplayAlert("Error", "Failed to load diary data.", "OK");
            }
        }

        // Helper method to display diary data in your UI
        private void DisplayDiaryData(string diaryType, DiaryEntry entry)
        {
            Debug.WriteLine($"[Diary] {diaryType} for {entry.Date}:");

            // Create UI elements to display the diary data
            var diarySection = new VerticalStackLayout { Spacing = 8, Margin = new Thickness(0, 10) };

            // Add a header
            diarySection.Children.Add(new Label
            {
                Text = $"{diaryType} - {entry.Date}",
                FontAttributes = FontAttributes.Bold,
                FontSize = 16,
                BackgroundColor = Color.FromArgb("#E3F2FD"),
                Padding = new Thickness(10, 6)
            });

            // Display each field in the diary
            foreach (var field in entry.Fields)
            {
                Debug.WriteLine($"[Diary] {field.Key}: {field.Value}");

                // Create a row for each field
                var fieldRow = new Grid
                {
                    ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) }
            },
                    Padding = new Thickness(10, 4),
                    BackgroundColor = Color.FromArgb("#F5F5F5")
                };

                // Field name
                fieldRow.Add(new Label
                {
                    Text = field.Key,
                    FontAttributes = FontAttributes.Bold,
                    VerticalTextAlignment = TextAlignment.Center
                }, 0, 0);

                // Field value
                string valueText = field.Value?.ToString() ?? "N/A";
                fieldRow.Add(new Label
                {
                    Text = valueText,
                    VerticalTextAlignment = TextAlignment.Center
                }, 1, 0);

                diarySection.Children.Add(fieldRow);
            }

            // Add the diary section to your main content area
            // Assuming you have a main StackLayout called SessionStack
            SessionStack.Children.Add(diarySection);
        }

        // Example of how to call this from your existing code
        // You might add this to your LoadAndRenderAsync method or create a separate button
        private async void OnLoadDiaryClicked(object sender, EventArgs e)
        {
            // You'll need to determine the user's email somehow
            // This could come from user input, session data, or program context
            string userEmail = "user@example.com"; // Replace with actual email

            // Use current date and program context
            string currentDate = DateTime.Today.ToString("yyyy-MM-dd");

            // Extract program info from your current context
            int programId = _programId.HasValue ? _programId.Value : 0;
            int week = _week;
            int day = _dayVc + 1; // Convert from 0-based to 1-based

            await LoadDiaryDataAsync(userEmail, currentDate, programId, week, day);
        }

        // Create number input with validation
        private View CreateNumberInput(DiaryField field)
        {
            var entry = new Entry
            {
                Placeholder = field.Placeholder,
                Keyboard = Keyboard.Numeric,
                Text = field.Value?.ToString() ?? ""
            };

            if (field.MinValue.HasValue || field.MaxValue.HasValue)
            {
                var validationLabel = new Label
                {
                    Text = $"Range: {field.MinValue ?? 0} - {field.MaxValue ?? 999}",
                    FontSize = 12,
                    TextColor = Color.FromArgb("#666666"),
                    Margin = new Thickness(0, 2, 0, 0)
                };

                return new VerticalStackLayout
                {
                    Children = { entry, validationLabel }
                };
            }

            return entry;
        }

        // Create dropdown input
        private View CreateDropdownInput(DiaryField field)
        {
            var picker = new Picker
            {
                Title = $"Select {field.Label}",
                ItemsSource = field.Options
            };

            // Set selected value if exists
            if (field.Value != null)
            {
                var selectedValue = field.Value.ToString();
                var index = field.Options.FindIndex(o => o.Equals(selectedValue, StringComparison.OrdinalIgnoreCase));
                if (index >= 0) picker.SelectedIndex = index;
            }

            return picker;
        }

        // Create text area input
        private View CreateTextAreaInput(DiaryField field)
        {
            var editor = new Editor
            {
                Placeholder = field.Placeholder,
                Text = field.Value?.ToString() ?? "",
                HeightRequest = 80,
                BackgroundColor = Color.FromArgb("#F8F8F8")
            };

            return editor;
        }

        // Create text input
        private View CreateTextInput(DiaryField field)
        {
            return new Entry
            {
                Placeholder = field.Placeholder,
                Text = field.Value?.ToString() ?? ""
            };
        }

        // Create rating input (1-10 scale with buttons)
        private View CreateRatingInput(DiaryField field)
        {
            int minValue = field.MinValue ?? 1;
            int maxValue = field.MaxValue ?? 10;
            int currentValue = 0;

            if (field.Value != null && int.TryParse(field.Value.ToString(), out var val))
            {
                currentValue = val;
            }

            var ratingContainer = new HorizontalStackLayout
            {
                Spacing = 5,
                HorizontalOptions = LayoutOptions.Center
            };

            var selectedLabel = new Label
            {
                Text = currentValue > 0 ? currentValue.ToString() : "Not selected",
                FontAttributes = FontAttributes.Bold,
                FontSize = 16,
                TextColor = Color.FromArgb("#2196F3"),
                HorizontalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };

            var buttonContainer = new HorizontalStackLayout { Spacing = 2 };

            var ratingButtons = new List<Button>();

            for (int i = minValue; i <= maxValue; i++)
            {
                int capturedValue = i;
                var button = new Button
                {
                    Text = i.ToString(),
                    WidthRequest = 35,
                    HeightRequest = 35,
                    CornerRadius = 17,
                    FontSize = 12,
                    Padding = new Thickness(0),
                    BackgroundColor = currentValue == i ? Color.FromArgb("#2196F3") : Color.FromArgb("#E0E0E0"),
                    TextColor = currentValue == i ? Colors.White : Color.FromArgb("#666666")
                };

                button.Clicked += (s, e) =>
                {
                    // Update all buttons
                    foreach (var btn in ratingButtons)
                    {
                        var btnValue = int.Parse(btn.Text);
                        btn.BackgroundColor = btnValue == capturedValue ? Color.FromArgb("#2196F3") : Color.FromArgb("#E0E0E0");
                        btn.TextColor = btnValue == capturedValue ? Colors.White : Color.FromArgb("#666666");
                    }

                    selectedLabel.Text = capturedValue.ToString();
                    field.Value = capturedValue;
                };

                ratingButtons.Add(button);
                buttonContainer.Children.Add(button);
            }

            var mainContainer = new VerticalStackLayout { Spacing = 8 };
            mainContainer.Children.Add(selectedLabel);
            mainContainer.Children.Add(buttonContainer);

            return mainContainer;
        }

        // Submit diary form
        private async Task SubmitDiaryForm(DiaryForm form, Dictionary<string, View> fieldControls,
            string cookie, Button submitButton)
        {
            try
            {
                submitButton.IsEnabled = false;
                submitButton.Text = "Saving...";

                // Collect form data
                var formData = new Dictionary<string, object>();

                foreach (var field in form.Fields)
                {
                    if (!fieldControls.TryGetValue(field.Name, out var control))
                        continue;

                    object? value = null;

                    // Extract value based on control type
                    switch (control)
                    {
                        case Entry entry:
                            value = entry.Text;
                            // Convert to number if it's a number field
                            if (field.Type.ToLower() == "number" && !string.IsNullOrEmpty(entry.Text))
                            {
                                if (double.TryParse(entry.Text, out var numValue))
                                    value = numValue;
                            }
                            break;

                        case Editor editor:
                            value = editor.Text;
                            break;

                        case Picker picker:
                            if (picker.SelectedIndex >= 0 && picker.SelectedIndex < field.Options.Count)
                                value = field.Options[picker.SelectedIndex];
                            break;

                        case VerticalStackLayout stackLayout when field.Type.ToLower() == "number":
                            // For number inputs with validation (has Entry as first child)
                            if (stackLayout.Children.FirstOrDefault() is Entry numberEntry)
                            {
                                if (double.TryParse(numberEntry.Text, out var numValue))
                                    value = numValue;
                                else
                                    value = numberEntry.Text;
                            }
                            break;

                        case VerticalStackLayout stackLayout when field.Type.ToLower() == "rating":
                            // For rating controls, the value is stored in the field
                            value = field.Value;
                            break;

                        case VerticalStackLayout stackLayout:
                            // For other VerticalStackLayout controls, try to get value from field or first Entry child
                            if (field.Value != null)
                                value = field.Value;
                            else if (stackLayout.Children.FirstOrDefault() is Entry firstEntry)
                                value = firstEntry.Text;
                            break;
                    }

                    // Validate required fields
                    if (field.Required && (value == null || string.IsNullOrWhiteSpace(value.ToString())))
                    {
                        await DisplayAlert("Validation Error", $"{field.Label} is required.", "OK");
                        submitButton.IsEnabled = true;
                        submitButton.Text = $"Save {form.Title}";
                        return;
                    }

                    // Validate number ranges
                    if (field.Type.ToLower() == "number" && value != null)
                    {
                        if (double.TryParse(value.ToString(), out var numVal))
                        {
                            if (field.MinValue.HasValue && numVal < field.MinValue.Value)
                            {
                                await DisplayAlert("Validation Error",
                                    $"{field.Label} must be at least {field.MinValue.Value}.", "OK");
                                submitButton.IsEnabled = true;
                                submitButton.Text = $"Save {form.Title}";
                                return;
                            }

                            if (field.MaxValue.HasValue && numVal > field.MaxValue.Value)
                            {
                                await DisplayAlert("Validation Error",
                                    $"{field.Label} must be no more than {field.MaxValue.Value}.", "OK");
                                submitButton.IsEnabled = true;
                                submitButton.Text = $"Save {form.Title}";
                                return;
                            }
                        }
                    }

                    if (value != null)
                        formData[field.Name] = value;
                }

                // Submit the data
                bool success = await VisualCoachingService.SubmitDiaryData(
                    cookie, form.DiaryId, form.UserId, form.Date,
                    _programId.HasValue ? _programId.Value : 0, _week, _dayVc + 1, formData);

                if (success)
                {
                    submitButton.Text = "✓ Saved";
                    submitButton.BackgroundColor = Color.FromArgb("#4CAF50");

                    await DisplayAlert("Success", $"{form.Title} saved successfully!", "OK");

                    // Optionally disable form after successful submission
                    foreach (var control in fieldControls.Values)
                    {
                        if (control is Entry entry) entry.IsEnabled = false;
                        else if (control is Editor editor) editor.IsEnabled = false;
                        else if (control is Picker picker) picker.IsEnabled = false;
                    }
                }
                else
                {
                    await DisplayAlert("Error", $"Failed to save {form.Title}. Please try again.", "OK");
                    submitButton.IsEnabled = true;
                    submitButton.Text = $"Save {form.Title}";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Diary] Submit error: {ex.Message}");
                await DisplayAlert("Error", $"An error occurred while saving: {ex.Message}", "OK");

                submitButton.IsEnabled = true;
                submitButton.Text = $"Save {form.Title}";
            }
        }

        // Helper method to check if user has already filled diary for this session
        private async Task<bool> HasExistingDiaryEntry(string userEmail, string diaryType)
        {
            try
            {
                string cookie = SessionManager.GetCookie();
                if (string.IsNullOrEmpty(cookie)) return false;

                var userInfo = await VisualCoachingService.GetUserInfo(cookie, userEmail);
                if (userInfo == null) return false;

                string currentDate = SelectedDate.ToString("yyyy-MM-dd");
                int programId = _programId.HasValue ? _programId.Value : 0;
                int week = _week;
                int day = _dayVc + 1;

                var existingData = await VisualCoachingService.GetDiaryDataByEmail(
                    cookie, userEmail, currentDate, programId, week, day, diaryType);

                return !string.IsNullOrEmpty(existingData);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Diary] Error checking existing diary entry: {ex.Message}");
                return false;
            }
        }

        // Method to show diary status (completed/pending)
        private async Task ShowDiaryStatusAsync(string userEmail)
        {
            try
            {
                bool hasPerformanceDiary = await HasExistingDiaryEntry(userEmail, "performance");
                bool hasWellnessDiary = await HasExistingDiaryEntry(userEmail, "wellness");

                var statusContainer = new VerticalStackLayout
                {
                    Spacing = 5,
                    Margin = new Thickness(0, 10),
                    BackgroundColor = Color.FromArgb("#FFF3E0"),
                    Padding = new Thickness(10)
                };

                statusContainer.Children.Add(new Label
                {
                    Text = "Diary Status",
                    FontAttributes = FontAttributes.Bold,
                    FontSize = 14
                });

                if (hasPerformanceDiary || hasWellnessDiary)
                {
                    string status = "";
                    if (hasPerformanceDiary) status += "Performance Diary: ✓ Completed\n";
                    if (hasWellnessDiary) status += "Wellness Diary: ✓ Completed";

                    statusContainer.Children.Add(new Label
                    {
                        Text = status.TrimEnd('\n'),
                        TextColor = Color.FromArgb("#4CAF50"),
                        FontSize = 13
                    });

                    // Add button to view/edit existing entries
                    var viewButton = new Button
                    {
                        Text = "View/Edit Diary Entries",
                        BackgroundColor = Color.FromArgb("#FF9800"),
                        TextColor = Colors.White,
                        CornerRadius = 6,
                        Margin = new Thickness(0, 5, 0, 0)
                    };

                    viewButton.Clicked += async (s, e) => await ShowPostSessionDiaryAsync(userEmail);
                    statusContainer.Children.Add(viewButton);
                }
                else
                {
                    statusContainer.Children.Add(new Label
                    {
                        Text = "No diary entries found for this session.",
                        TextColor = Color.FromArgb("#FF9800"),
                        FontSize = 13
                    });
                }

                SessionStack.Children.Add(statusContainer);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Diary] Error showing diary status: {ex.Message}");
            }
        }

        // Method to automatically prompt for diary if session is from today
        private async Task AutoPromptDiaryIfNeeded()
        {
            try
            {
                // Only prompt if the session date is today or recent
                var sessionDate = SelectedDate.Date;
                var today = DateTime.Today;
                var daysDifference = (today - sessionDate).TotalDays;

                // Prompt for diary if session is from today or yesterday
                if (daysDifference >= 0 && daysDifference <= 1)
                {
                    string userEmail = await GetCurrentUserEmailAsync();
                    if (!string.IsNullOrEmpty(userEmail))
                    {
                        bool hasAnyDiary = await HasExistingDiaryEntry(userEmail, "performance") ||
                                          await HasExistingDiaryEntry(userEmail, "wellness");

                        if (!hasAnyDiary)
                        {
                            bool shouldFillDiary = await DisplayAlert(
                                "Diary Entry",
                                $"Would you like to fill out your diary for this session from {sessionDate:MMM dd}?",
                                "Yes", "Later");

                            if (shouldFillDiary)
                            {
                                await ShowPostSessionDiaryAsync(userEmail);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Diary] Error in AutoPromptDiaryIfNeeded: {ex.Message}");
            }
        }

        // Create basic diary form as fallback
        private DiaryForm CreateBasicDiaryForm(string diaryType, int diaryId)
        {
            var form = new DiaryForm
            {
                DiaryId = diaryId,
                Title = $"{diaryType.Substring(0, 1).ToUpper()}{diaryType.Substring(1)} Diary",
                Type = diaryType
            };

            if (diaryType.ToLower() == "performance")
            {
                form.Fields = new List<DiaryField>
        {
            new DiaryField { Name = "rpe", Label = "Rate of Perceived Exertion (1-10)", Type = "rating", MinValue = 1, MaxValue = 10, Required = true },
            new DiaryField { Name = "duration", Label = "Session Duration (minutes)", Type = "number", MinValue = 0, MaxValue = 300 },
            new DiaryField { Name = "completed", Label = "Session Completed", Type = "dropdown", Options = new List<string> { "Yes", "Partially", "No" }, Required = true },
            new DiaryField { Name = "notes", Label = "Performance Notes", Type = "textarea", Placeholder = "How did the session feel? Any issues or achievements?" }
        };
            }
            else // wellness
            {
                form.Fields = new List<DiaryField>
        {
            new DiaryField { Name = "energy", Label = "Energy Level (1-10)", Type = "rating", MinValue = 1, MaxValue = 10, Required = true },
            new DiaryField { Name = "sleep_quality", Label = "Sleep Quality (1-10)", Type = "rating", MinValue = 1, MaxValue = 10 },
            new DiaryField { Name = "mood", Label = "Mood", Type = "dropdown", Options = new List<string> { "Excellent", "Good", "Average", "Poor", "Very Poor" } },
            new DiaryField { Name = "stress_level", Label = "Stress Level (1-10)", Type = "rating", MinValue = 1, MaxValue = 10 },
            new DiaryField { Name = "wellness_notes", Label = "Wellness Notes", Type = "textarea", Placeholder = "Any wellness concerns or observations?" }
        };
            }

            return form;
        }

        // Populate form with existing diary data
        private void PopulateFormWithExistingData(DiaryForm form, string existingDataJson)
        {
            try
            {
                var existingData = JsonConvert.DeserializeObject<Dictionary<string, object>>(existingDataJson);
                if (existingData == null) return;

                foreach (var field in form.Fields)
                {
                    if (existingData.TryGetValue(field.Name, out var value))
                    {
                        field.Value = value;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Diary] Error populating form with existing data: {ex.Message}");
            }
        }

        // Create UI for diary form
        private View CreateDiaryFormUI(DiaryForm form, string cookie)
        {
            var formContainer = new VerticalStackLayout { Spacing = 10 };

            // Form title
            formContainer.Children.Add(new Label
            {
                Text = form.Title,
                FontAttributes = FontAttributes.Bold,
                FontSize = 16,
                Margin = new Thickness(0, 0, 0, 10),
                BackgroundColor = Color.FromArgb("#2196F3"),
                TextColor = Colors.White,
                Padding = new Thickness(10, 6)
            });

            var fieldControls = new Dictionary<string, View>();

            // Create form fields
            foreach (var field in form.Fields)
            {
                var fieldContainer = new VerticalStackLayout { Spacing = 5 };

                // Field label
                var label = new Label
                {
                    Text = field.Required ? $"{field.Label} *" : field.Label,
                    FontAttributes = field.Required ? FontAttributes.Bold : FontAttributes.None,
                    FontSize = 14
                };
                fieldContainer.Children.Add(label);

                // Create appropriate input control
                View inputControl = field.Type.ToLower() switch
                {
                    "number" => CreateNumberInput(field),
                    "dropdown" => CreateDropdownInput(field),
                    "textarea" => CreateTextAreaInput(field),
                    "rating" => CreateRatingInput(field),
                    _ => CreateTextInput(field)
                };

                fieldControls[field.Name] = inputControl;
                fieldContainer.Children.Add(inputControl);
                formContainer.Children.Add(fieldContainer);
            }

            // Submit button
            var submitButton = new Button
            {
                Text = $"Save {form.Title}",
                BackgroundColor = Color.FromArgb("#4CAF50"),
                TextColor = Colors.White,
                CornerRadius = 6,
                Margin = new Thickness(0, 15, 0, 0)
            };

            submitButton.Clicked += async (s, e) =>
            {
                await SubmitDiaryForm(form, fieldControls, cookie, submitButton);
            };

            formContainer.Children.Add(submitButton);

            return new Frame
            {
                Content = formContainer,
                BackgroundColor = Colors.White,
                BorderColor = Color.FromArgb("#E0E0E0"),
                CornerRadius = 8,
                Padding = new Thickness(15),
                Margin = new Thickness(0, 10)
            };
        }
        private async Task<string> GetCurrentUserEmailAsync()
        {
            // First, try to get stored email
            string storedEmail = Preferences.Get(UserEmailKey, "");

            if (!string.IsNullOrEmpty(storedEmail))
            {
                // Ask if they want to use the stored email
                bool useStored = await DisplayAlert("Confirm Email",
                    $"Use saved email: {storedEmail}?", "Yes", "Use Different Email");

                if (useStored)
                    return storedEmail;
            }

            // Get new email from user
            string newEmail = await DisplayPromptAsync("User Email",
                "Enter your email address for diary entry:",
                placeholder: storedEmail);

            if (!string.IsNullOrEmpty(newEmail))
            {
                // Store for future use
                Preferences.Set(UserEmailKey, newEmail);
                return newEmail;
            }

            return storedEmail; // Fallback to stored email
        }

        // Method to clear stored email (maybe add to settings page)
        public static void ClearStoredUserEmail()
        {
            Preferences.Remove(UserEmailKey);
        }
        private async Task LoadAndRenderAsync()
        {
            if (!Ready) return;
            try
            {
                var cookie = SessionManager.GetCookie();
                if (string.IsNullOrEmpty(cookie))
                {
                    await Shell.Current.GoToAsync("//LoginPage");
                    return;
                }

                // Your existing session loading code...
                await EnsureProgramWeeksAsync(cookie);
                BuildWeekTabsUI();
                BuildDaysListUI();
                string html = await VisualCoachingService.GetRawSessionHtml(cookie, _url);

                // Clear old content and render session
                SessionStack.Children.Clear();
                SessionStack.Children.Add(new Label
                {
                    Text = TitleLabel.Text,
                    FontAttributes = FontAttributes.Bold,
                    FontSize = 18,
                    Margin = new Thickness(0, 4, 0, 0)
                });

                HeaderWeekDateLabel.Text = $"Week {DisplayWeek} · {SelectedDate:ddd dd/MM/yyyy}";

                // Your existing rendering logic...
                var mode = Preferences.Get(ViewModeKey, "Auto");
                bool hasExercisesMarkup = Regex.IsMatch(html ?? "", @"class=['""]exercise['""]", RegexOptions.IgnoreCase);

                bool rendered = false;
                if (mode == "Exercises")
                {
                    rendered = RenderExercisesFromHtml(html) || RenderProgramDayTable(html);
                }
                else if (mode == "Table")
                {
                    rendered = RenderProgramDayTable(html) || RenderExercisesFromHtml(html);
                }
                else // Auto
                {
                    rendered = (hasExercisesMarkup && RenderExercisesFromHtml(html))
                               || RenderProgramDayTable(html)
                               || RenderExercisesFromHtml(html);
                }

                if (!rendered)
                    SessionStack.Children.Add(new Label { Text = "No program content.", FontSize = 14 });

                // ✅ ADD THESE LINES HERE:
                if (rendered) // if session content was successfully rendered
                {
                    AddDiaryButtonForSession(); // Add the diary button
                }
            }
            catch (UnauthorizedAccessException)
            {
                await DisplayAlert("Session Expired", "Please log in again.", "OK");
                SessionManager.ClearCookie();
                await Shell.Current.GoToAsync("//LoginPage");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TrainingPage] Load error: {ex.Message}");
                SessionStack.Children.Clear();
                SessionStack.Children.Add(new Label { Text = "Unable to load content.", FontSize = 14 });
            }
        }

        private async Task LoadDiaryForm(VerticalStackLayout parentContainer, string cookie,
    UserInfo userInfo, string diaryType)
        {
            try
            {
                int diaryId = diaryType.ToLower() == "wellness" ? userInfo.WellnessDiaryId : userInfo.PerformanceDiaryId;

                if (diaryId <= 0)
                {
                    Debug.WriteLine($"[Diary] No {diaryType} diary found for user");

                    // Add a message to the UI instead of silently skipping
                    parentContainer.Children.Add(new Label
                    {
                        Text = $"{diaryType.Substring(0, 1).ToUpper()}{diaryType.Substring(1)} diary not available for this user.",
                        TextColor = Color.FromArgb("#FF9800"),
                        FontAttributes = FontAttributes.Italic,
                        Margin = new Thickness(0, 5),
                        Padding = new Thickness(10),
                        BackgroundColor = Color.FromArgb("#FFF3E0")
                    });

                    return;
                }

                // Rest of your existing diary form loading code...
                string currentDate = SelectedDate.ToString("yyyy-MM-dd");
                int programId = _programId.HasValue ? _programId.Value : 0;
                int week = _week;
                int day = _dayVc + 1;

                var existingData = await VisualCoachingService.GetDiaryData(
                    cookie, diaryId, currentDate, userInfo.UserId, programId, week, day);

                var diaryForm = CreateBasicDiaryForm(diaryType, diaryId);
                diaryForm.Date = currentDate;
                diaryForm.UserId = userInfo.UserId;
                diaryForm.ProgramKey = $"{programId}:{week:000}:{day:000}:000:000";

                if (!string.IsNullOrEmpty(existingData))
                {
                    PopulateFormWithExistingData(diaryForm, existingData);
                }

                var formContainer = CreateDiaryFormUI(diaryForm, cookie);
                parentContainer.Children.Add(formContainer);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Diary] Error loading {diaryType} diary form: {ex.Message}");

                parentContainer.Children.Add(new Label
                {
                    Text = $"Error loading {diaryType} diary: {ex.Message}",
                    TextColor = Colors.Red,
                    FontAttributes = FontAttributes.Italic,
                    Margin = new Thickness(0, 5)
                });
            }
        }

        // Also update your ShowPostSessionDiaryAsync method to handle missing wellness diary gracefully
        private async Task ShowPostSessionDiaryAsync(string userEmail)
        {
            try
            {
                string cookie = SessionManager.GetCookie();
                if (string.IsNullOrEmpty(cookie))
                {
                    await DisplayAlert("Error", "Please log in again.", "OK");
                    return;
                }

                var userInfo = await VisualCoachingService.GetUserInfo(cookie, userEmail);
                if (userInfo == null)
                {
                    await DisplayAlert("Error", $"Could not find user information for {userEmail}", "OK");
                    return;
                }

                Debug.WriteLine($"[Diary] Loading diary for {userInfo.DisplayName}");
                Debug.WriteLine($"[Diary] Performance Diary ID: {userInfo.PerformanceDiaryId}");
                Debug.WriteLine($"[Diary] Wellness Diary ID: {userInfo.WellnessDiaryId}");

                var diarySection = new VerticalStackLayout
                {
                    Spacing = 15,
                    Margin = new Thickness(0, 10),
                    BackgroundColor = Color.FromArgb("#F5F5F5"),
                    Padding = new Thickness(15)
                };

                diarySection.Children.Add(new Label
                {
                    Text = $"Post-Session Diary - {userInfo.DisplayName}",
                    FontAttributes = FontAttributes.Bold,
                    FontSize = 18,
                    HorizontalOptions = LayoutOptions.Center,
                    TextColor = Color.FromArgb("#333333")
                });

                // Always try to load performance diary
                await LoadDiaryForm(diarySection, cookie, userInfo, "performance");

                // Only load wellness diary if it exists
                if (userInfo.WellnessDiaryId > 0)
                {
                    await LoadDiaryForm(diarySection, cookie, userInfo, "wellness");
                }
                else
                {
                    Debug.WriteLine("[Diary] Wellness diary not configured for this user");
                }

                SessionStack.Children.Add(diarySection);

                if (SessionStack.Parent is ScrollView scrollView)
                {
                    await scrollView.ScrollToAsync(diarySection, ScrollToPosition.Start, true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Diary] Error in ShowPostSessionDiaryAsync: {ex.Message}");
                await DisplayAlert("Error", "Failed to load diary forms.", "OK");
            }
        }
        
        private async Task ShowDiaryFormAsync(string userEmail)
        {
            try
            {
                string cookie = SessionManager.GetCookie();
                if (string.IsNullOrEmpty(cookie))
                {
                    await DisplayAlert("Error", "Please log in again.", "OK");
                    return;
                }

                var userInfo = await VisualCoachingService.GetUserInfo(cookie, userEmail);
                if (userInfo == null)
                {
                    await DisplayAlert("Error", $"User not found: {userEmail}", "OK");
                    return;
                }

                await LoadDiaryForUser(userInfo, cookie);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Diary] ShowDiaryFormAsync error: {ex.Message}");
                await DisplayAlert("Error", "Failed to load diary.", "OK");
            }
        }
        private async Task LoadDiaryForUser(UserInfo userInfo, string cookie)
        {
            string currentDate = SelectedDate.ToString("yyyy-MM-dd");
            int programId = _programId ?? 0;
            int week = _week;
            int day = _dayVc + 1; // Convert 0-based to 1-based for API

            var diaryContainer = new VerticalStackLayout
            {
                Spacing = 15,
                Margin = new Thickness(0, 10),
                BackgroundColor = Color.FromArgb("#F8F9FA"),
                Padding = new Thickness(15)
            };

            // Header
            diaryContainer.Children.Add(new Label
            {
                Text = $"📝 Diary Entry - {userInfo.DisplayName}",
                FontAttributes = FontAttributes.Bold,
                FontSize = 18,
                HorizontalOptions = LayoutOptions.Center
            });

            diaryContainer.Children.Add(new Label
            {
                Text = $"Session: {currentDate} • Week {DisplayWeek}, Day {day}",
                FontSize = 14,
                HorizontalOptions = LayoutOptions.Center,
                TextColor = Color.FromArgb("#666666"),
                Margin = new Thickness(0, 0, 0, 10)
            });

            // Load diaries
            if (userInfo.PerformanceDiaryId > 0)
            {
                await LoadDiaryForm(diaryContainer, cookie, userInfo, "performance");
            }

            if (userInfo.WellnessDiaryId > 0)
            {
                await LoadDiaryForm(diaryContainer, cookie, userInfo, "wellness");
            }

            if (userInfo.PerformanceDiaryId == 0 && userInfo.WellnessDiaryId == 0)
            {
                diaryContainer.Children.Add(new Label
                {
                    Text = "No diaries configured for this user.",
                    TextColor = Color.FromArgb("#FF9800"),
                    FontAttributes = FontAttributes.Italic,
                    HorizontalOptions = LayoutOptions.Center
                });
            }

            SessionStack.Children.Add(diaryContainer);

            // Scroll to diary
            if (SessionStack.Parent is ScrollView scrollView)
            {
                await scrollView.ScrollToAsync(diaryContainer, ScrollToPosition.Start, true);
            }
        }
        private void AddDiaryButtonForSession()
        {
            // Check if diary button already exists
            if (SessionStack.Children.Any(c => c is VerticalStackLayout vs &&
                vs.Children.Any(child => child is Button btn && btn.Text.Contains("Diary"))))
                return;

            var diarySection = new VerticalStackLayout
            {
                Spacing = 10,
                Margin = new Thickness(0, 20, 0, 10),
                BackgroundColor = Color.FromArgb("#E8F5E8"),
                Padding = new Thickness(15, 10)
            };

            diarySection.Children.Add(new Label
            {
                Text = "Complete Your Session Diary",
                FontAttributes = FontAttributes.Bold,
                FontSize = 16,
                HorizontalOptions = LayoutOptions.Center
            });

            var diaryButton = new Button
            {
                Text = "📝 Fill Session Diary",
                BackgroundColor = Color.FromArgb("#4CAF50"),
                TextColor = Colors.White,
                CornerRadius = 8,
                Padding = new Thickness(20, 10),
                HorizontalOptions = LayoutOptions.Center
            };

            diaryButton.Clicked += OnDiaryButtonClicked;
            diarySection.Children.Add(diaryButton);
            SessionStack.Children.Add(diarySection);
        }

        private async void OnDiaryButtonClicked(object sender, EventArgs e)
        {
            try
            {
                // Use the existing cookie from login
                string cookie = SessionManager.GetCookie();
                if (string.IsNullOrEmpty(cookie))
                {
                    await DisplayAlert("Error", "Please log in again.", "OK");
                    return;
                }

                // Hide the diary button section after clicking
                if (sender is Button button && button.Parent is VerticalStackLayout parent)
                {
                    parent.IsVisible = false;
                }

                // Get the user's email from stored preferences (set during login or settings)
                string userEmail = Preferences.Get("UserEmail", "");

                if (string.IsNullOrEmpty(userEmail))
                {
                    // Prompt once for email and store it
                    userEmail = await DisplayPromptAsync("User Email",
                        "Enter your email address for diary entries:",
                        placeholder: "user@example.com");

                    if (string.IsNullOrEmpty(userEmail))
                    {
                        await DisplayAlert("Email Required", "Email is required for diary entries.", "OK");
                        return;
                    }

                    // Store for future use
                    Preferences.Set("UserEmail", userEmail);
                }

                await LoadDiaryForCurrentSession(cookie, userEmail);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Diary] Error: {ex.Message}");
                await DisplayAlert("Error", "Failed to load diary form.", "OK");
            }
        }

        private async Task LoadDiaryForCurrentSession(string cookie, string userEmail)
        {
            try
            {
                // Get user info using the login cookie
                var userInfo = await VisualCoachingService.GetUserInfo(cookie, userEmail);
                if (userInfo == null)
                {
                    await DisplayAlert("Error", $"Could not find user information for {userEmail}", "OK");
                    return;
                }

                // Use current session context
                string currentDate = SelectedDate.ToString("yyyy-MM-dd");
                int programId = _programId ?? 0;
                int week = _week;
                int day = _dayVc + 1; // Convert 0-based to 1-based for API

                Debug.WriteLine($"[Diary] Loading diary for {userInfo.DisplayName}");
                Debug.WriteLine($"[Diary] Session: {currentDate}, Program: {programId}, Week: {week}, Day: {day}");

                var diaryContainer = new VerticalStackLayout
                {
                    Spacing = 15,
                    Margin = new Thickness(0, 10),
                    BackgroundColor = Color.FromArgb("#F8F9FA"),
                    Padding = new Thickness(15)
                };

                // Header
                diaryContainer.Children.Add(new Label
                {
                    Text = $"📝 Diary Entry - {userInfo.DisplayName}",
                    FontAttributes = FontAttributes.Bold,
                    FontSize = 18,
                    HorizontalOptions = LayoutOptions.Center
                });

                diaryContainer.Children.Add(new Label
                {
                    Text = $"Session: {currentDate} • Week {DisplayWeek}, Day {day}",
                    FontSize = 14,
                    HorizontalOptions = LayoutOptions.Center,
                    TextColor = Color.FromArgb("#666666"),
                    Margin = new Thickness(0, 0, 0, 10)
                });

                // Load Performance Diary (if available)
                if (userInfo.PerformanceDiaryId > 0)
                {
                    await LoadSpecificDiary(diaryContainer, cookie, userInfo, "Performance",
                        userInfo.PerformanceDiaryId, currentDate, programId, week, day);
                }

                // Load Wellness Diary (if available)
                if (userInfo.WellnessDiaryId > 0)
                {
                    await LoadSpecificDiary(diaryContainer, cookie, userInfo, "Wellness",
                        userInfo.WellnessDiaryId, currentDate, programId, week, day);
                }

                // Show message if no diaries available
                if (userInfo.PerformanceDiaryId == 0 && userInfo.WellnessDiaryId == 0)
                {
                    diaryContainer.Children.Add(new Label
                    {
                        Text = "No diaries configured for this user.",
                        TextColor = Color.FromArgb("#FF9800"),
                        FontAttributes = FontAttributes.Italic,
                        HorizontalOptions = LayoutOptions.Center
                    });
                }

                SessionStack.Children.Add(diaryContainer);

                // Scroll to diary section
                if (SessionStack.Parent is ScrollView scrollView)
                {
                    await scrollView.ScrollToAsync(diaryContainer, ScrollToPosition.Start, true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Diary] Error loading diary: {ex.Message}");
                await DisplayAlert("Error", "Failed to load diary forms.", "OK");
            }
        }

        private async Task LoadSpecificDiary(VerticalStackLayout parentContainer, string cookie,
            UserInfo userInfo, string diaryType, int diaryId, string date, int programId, int week, int day)
        {
            try
            {
                Debug.WriteLine($"[Diary] Loading {diaryType} diary (ID: {diaryId})");

                // Check for existing diary data using the cookie
                var existingData = await VisualCoachingService.GetDiaryData(
                    cookie, diaryId, date, userInfo.UserId, programId, week, day);

                // Create basic diary form
                var diaryForm = CreateBasicDiaryForm(diaryType, diaryId);
                diaryForm.Date = date;
                diaryForm.UserId = userInfo.UserId;
                diaryForm.ProgramKey = $"{programId}:{week:000}:{day:000}:000:000";

                // Populate with existing data if available
                if (!string.IsNullOrEmpty(existingData))
                {
                    PopulateFormWithExistingData(diaryForm, existingData);
                }

                // Create and add the form UI
                var formUI = CreateDiaryFormUI(diaryForm, cookie);
                parentContainer.Children.Add(formUI);

                Debug.WriteLine($"[Diary] {diaryType} diary form created successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Diary] Error loading {diaryType} diary: {ex.Message}");

                parentContainer.Children.Add(new Label
                {
                    Text = $"Error loading {diaryType} diary: {ex.Message}",
                    TextColor = Colors.Red,
                    FontSize = 12,
                    Margin = new Thickness(0, 5)
                });
            }
        }

    }
}