// Module Name: TrainingPage
// Author: Kye Franken
// Date Created: 20 / 06 / 2025
// Date Modified: 29 / 08 / 2025
// Description: Loads the programs and sessions from Visual Coaching
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
        private const string ViewModeKey = "ProgramViewMode";
        private const double ParagraphColWidth = 220;
        private const int MinWeeks = 1;
        private const int MaxWeeks = 14;
        private const string UserEmailKey = "UserEmail";
        private string _currentSessionHtml = "";

        // Monday-start model days
        private static readonly (string Label, int DayIdx)[] DaysVc =
        {
            ("Mon",0),("Tue",1),("Wed",2),("Thu",3),("Fri",4),("Sat",5),("Sun",6)
        };

        private string _url = "";
        private string _absoluteSessionUrl = "";
        private int _week = 0;
        private int _dayVc = 0;
        private DateTime? _anchorDate;
        private DateTime? _selectedDate;
        private int _entryWeekForProgram = 0;
        private DateTime _programWeek0Monday;
        private int? _programId;
        private int? _programWeeks;
        private readonly Dictionary<int, int> _weeksCache = new();
        private ToolkitMediaElement? _currentMediaElement;

        // Flag to check if enough data is ready
        private bool Ready => _anchorDate.HasValue && !string.IsNullOrWhiteSpace(_url);

        // Constructor
        public TrainingPage()
        {
            InitializeComponent();
            MessagingCenter.Subscribe<AppShell, bool>(this, "ShowDatesPanelChanged", (_, show) =>
            {
                DatesSection.IsVisible = show;
            });
        }

        // URL property (sets program and triggers reload)
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

                ParseWeekAndDayVcFromUrl(_url);
                _entryWeekForProgram = _week;

                if (_anchorDate == null)
                    TryLoadAnchorFromUrl(_url);

                if (_anchorDate != null)
                {
                    ComputeProgramBaselineMonday();
                    _selectedDate = _programWeek0Monday.AddDays(7 * _week + _dayVc);
                    Debug.WriteLine($"[Url setter] Set _selectedDate to: {_selectedDate:yyyy-MM-dd dddd} (week={_week}, dayVc={_dayVc})");
                }

                if (Ready)
                    _ = LoadAndRenderAsync();
            }
        }

        // Anchor date property
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

        // Lifecycle: when page is shown
        protected override void OnAppearing()
        {
            base.OnAppearing();
            LogoImage.IsVisible = Preferences.Get("SelectedCompany", "Normal") == "ETPA";
            DatesSection.IsVisible = Preferences.Get(ShowDatesPrefKey, true);
        }

        // Lifecycle: when page is hidden
        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            MessagingCenter.Unsubscribe<AppShell, bool>(this, "ShowDatesPanelChanged");
            CleanupCurrentMedia();
        }

        // Build week tabs navigation
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

        // Build day list navigation
        private void BuildDaysListUI()
        {
            DaysListLayout.Children.Clear();
            var weekMonday = WeekMonday(SelectedDate);

            foreach (var (label, dayOffset) in DaysVc)
            {
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

                int capturedDayOffset = dayOffset;
                var tap = new TapGestureRecognizer();
                tap.Tapped += async (_, __) =>
                {
                    Debug.WriteLine($"[Day Tap] Label: {label}, dayOffset: {capturedDayOffset}");

                    _dayVc = capturedDayOffset;
                    _selectedDate = weekMonday.AddDays(capturedDayOffset);
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

        // Ensure total program weeks
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

        // Render day table
        private bool RenderProgramDayTable(string html)
        {
            var blockCols = ExtractProgramColumns(html);
            if (blockCols.Count == 0) return false;
            SessionStack.Children.Add(BuildDayTableTransposed(blockCols));
            return true;
        }

        // Helper class for text/link parsing
        private class TextPart
        {
            public string Text { get; set; } = "";
            public bool IsLinkedProgram { get; set; }
            public string ProgramId { get; set; } = "";
            public bool IsYouTubeLink { get; set; }
            public string YouTubeUrl { get; set; } = "";
        }

        // Split HTML text into parts (plain, program links, YouTube)
        private List<TextPart> SplitTextWithAllLinks(string html)
        {
            var parts = new List<TextPart>();
            var allMatches = new List<(Match match, string type)>();

            var linkedProgramMatches = Regex.Matches(html,
                @"<a[^>]*href=""[^""]*(?:#program/|/Program/Session/)(\d+)""[^>]*class=""[^""]*linkedProgram[^""]*""[^>]*>(.*?)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match match in linkedProgramMatches)
            {
                allMatches.Add((match, "linkedProgram"));
            }

            var youtubeMatches = Regex.Matches(html,
                @"<a[^>]*href=""(https://(?:www\.)youtube\.com/watch\?v=([^""&]+))[^""]*""[^>]*>(.*?)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match match in youtubeMatches)
            {
                allMatches.Add((match, "youtube"));
            }

            allMatches.Sort((a, b) => a.match.Index.CompareTo(b.match.Index));

            if (allMatches.Count == 0)
            {
                parts.Add(new TextPart { Text = html, IsLinkedProgram = false, IsYouTubeLink = false });
                return parts;
            }

            int lastIndex = 0;
            foreach (var (match, type) in allMatches)
            {
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
                    string youtubeUrl = match.Groups[1].Value;
                    string videoId = match.Groups[2].Value;
                    string linkText = match.Groups[3].Value;

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

        // Aggressively clean link/button text
        private static string CleanButtonText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "Program";

            text = text.Replace("?", "")
                      .Replace("？", "")
                      .Replace("¿", "")
                      .Replace("؟", "")
                      .Replace("；", "")
                      .Replace("¡", "");

            while (text.Contains("(") && text.Contains(")"))
            {
                text = Regex.Replace(text, @"\([^)]*\)", "");
            }

            text = text.Replace("(", "").Replace(")", "");
            text = text.Replace("Linked Program", "", StringComparison.OrdinalIgnoreCase);
            text = text.Replace("YouTube", "", StringComparison.OrdinalIgnoreCase);
            text = text.Replace("Video", "", StringComparison.OrdinalIgnoreCase);
            text = text.Replace("Watch", "", StringComparison.OrdinalIgnoreCase);

            text = Regex.Replace(text, @"^[:\-\.\s]+|[:\-\.\s]+$", "");
            text = Regex.Replace(text, @"\s+", " ");
            text = Regex.Replace(text, @"[^\w\s\-]", "");

            text = text.Trim();

            if (string.IsNullOrWhiteSpace(text) || text.Length < 2)
                return "Video";

            return text;
        }

        // Create content (labels, buttons) with linked programs and YouTube
        private View CreateContentWithLinkedPrograms(string text)
        {
            var linkedProgramMatches = Regex.Matches(text,
                @"<a[^>]*href=""[^""]*(?:#program/|/Program/Session/)(\d+)""[^>]*class=""[^""]*linkedProgram[^""]*""[^>]*>(.*?)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            var youtubeMatches = Regex.Matches(text,
                @"<a[^>]*href=""(https://(?:www\.)?youtube\.com/watch\?v=([^""&]+))[^""]*""[^>]*>(.*?)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (linkedProgramMatches.Count == 0 && youtubeMatches.Count == 0)
            {
                return new Label
                {
                    Text = WebUtility.HtmlDecode(StripTags(text)),
                    FontSize = 14,
                    LineBreakMode = LineBreakMode.WordWrap,
                    WidthRequest = ParagraphColWidth
                };
            }

            var mainStack = new VerticalStackLayout { Spacing = 5 };
            var parts = SplitTextWithAllLinks(text);

            foreach (var part in parts)
            {
                if (part.IsLinkedProgram)
                {
                    var linkContainer = new VerticalStackLayout { Spacing = 10 };

                    var linkButton = new Button
                    {
                        Text = part.Text,
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
                            linkButton.Text = $"Hide {part.Text}";
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
                            linkButton.Text = part.Text;
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
                    var youtubeButton = new Button
                    {
                        Text = $"▶ {part.Text}",
                        FontSize = 14,
                        BackgroundColor = Color.FromArgb("#FF0000"),
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

        // Open a YouTube link, fallback to webview if needed
        private async Task OpenYouTubeLinkAsync(string youtubeUrl, string title)
        {
            try
            {
                await Browser.OpenAsync(youtubeUrl, BrowserLaunchMode.SystemPreferred);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[YouTube] Error opening link: {ex.Message}");

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

        // Extract YouTube video id from url
        private string ExtractYouTubeVideoId(string url)
        {
            var match = Regex.Match(url, @"(?:youtube\.com/watch\?v=|youtu\.be/)([^&\n?#]+)");
            return match.Success ? match.Groups[1].Value : "";
        }

        // Show YouTube in an embedded webview
        private async Task ShowYouTubeInWebViewAsync(string videoId, string title)
        {
            try
            {
                string embedUrl = $"https://www.youtube.com/embed/{videoId}?autoplay=1&modestbranding=1&rel=0";

                var webView = new WebView
                {
                    Source = embedUrl,
                    HorizontalOptions = LayoutOptions.FillAndExpand,
                    VerticalOptions = LayoutOptions.FillAndExpand
                };

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


        // Handle linked program click navigation
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

                (int targetWeek, int targetDay) = await FindFirstContentWeekDayAsync(cookie, programId);

                DateTime targetDate;
                if (targetDay == 0)
                {
                    DateTime mondayWeek0 = _programWeek0Monday;
                    targetDate = mondayWeek0.AddDays(targetWeek * 7);
                    Debug.WriteLine($"[LinkedProgram] Using day 0 logic - target date: {targetDate:yyyy-MM-dd}");
                }
                else
                {
                    DateTime mondayWeek0 = _programWeek0Monday;
                    int daysToAdd = (targetWeek * 7) + (targetDay - 1);
                    targetDate = mondayWeek0.AddDays(daysToAdd);
                    Debug.WriteLine($"[LinkedProgram] Using standard logic - target date: {targetDate:yyyy-MM-dd}");
                }

                string linkedUrl = $"/Application/Program/Session/{programId}?week={targetWeek}&day={targetDay}&session=0&i=0&format=Tablet&version=2";

                Debug.WriteLine($"[LinkedProgram] Navigating to: {linkedUrl}");
                Debug.WriteLine($"[LinkedProgram] Program: {programName} (ID: {programId}), Week: {targetWeek}, Day: {targetDay}");
                Debug.WriteLine($"[LinkedProgram] Target date: {targetDate:yyyy-MM-dd ddd}");

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

        // Extract columns from program HTML
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
                                .Select(m => m.Groups[1].Value)
                                .Where(s => !string.IsNullOrWhiteSpace(StripTags(s)))
                                .ToList();

                if (cols.Count > 0)
                    results.Add(cols);
            }

            return results;
        }

        // Build transposed table for a day
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

                    var content = CreateContentWithLinkedPrograms(text);

                    Grid.SetRow(content, r);
                    Grid.SetColumn(content, c);
                    grid.Children.Add(content);
                }
            }

            return new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = grid };
        }

        // Internal classes for exercises
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

        // Render exercises from html
        private bool RenderExercisesFromHtml(string html)
        {
            var items = ExtractExercises(html);
            if (items.Count == 0) return false;

            foreach (var ex in items)
                SessionStack.Children.Add(BuildExerciseRow(ex));

            return true;
        }

        // Extract exercise data from html
        private static List<ExerciseItem> ExtractExercises(string html)
        {
            var list = new List<ExerciseItem>();
            if (string.IsNullOrWhiteSpace(html)) return list;

            foreach (Match ex in Regex.Matches(html,
                @"<div[^>]*class=['""]exercise['""][^>]*>(?<inner>.*?)</div>\s*</div>\s*",
                RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                string inner = ex.Groups["inner"].Value;

                var nameRaw = ExtractFirst(inner, @"<h3[^>]*>(.*?)</h3>");
                string name = WebUtility.HtmlDecode(StripTags(nameRaw ?? "")).Trim();

                string notes = "";
                var notesRaw = ExtractFirst(inner, @"<div[^>]*class=['""]notes['""][^>]*>(.*?)</div>");
                if (!string.IsNullOrWhiteSpace(notesRaw))
                {
                    var p = ExtractFirst(notesRaw, @"<p[^>]*>(.*?)</p>");
                    notes = Sanitize(WebUtility.HtmlDecode(StripTags(p ?? "")));
                }

                var detailLinkMatch = Regex.Match(inner, @"href=""(/Application/Exercise/Details/(\d+)\?[^""]*)"">", RegexOptions.IgnoreCase);
                string detailUrl = detailLinkMatch.Success ? detailLinkMatch.Groups[1].Value : "";

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

        // Build UI row for an exercise
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

            var rightTop = new Grid();
            rightTop.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
            rightTop.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new Label { Text = ex.Name, FontAttributes = FontAttributes.Bold, FontSize = 16 };
            rightTop.Add(title, 0, 0);

            var setsGrid = BuildSetsGrid(ex.Sets);
            rightTop.Add(setsGrid, 1, 0);

            row.Add(rightTop, 1, 0);

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

        // Build grid for exercise sets
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

        // Info button tapped handler
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

        // Show exercise details modal popup
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

        // Helper to create info section in popup
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

        // Extract description from exercise html
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

        // Extract instructions from exercise html
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

        // Extract muscle groups from exercise html
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

        // Strip html and clean text
        private string CleanHtmlText(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";

            string text = Regex.Replace(html, "<.*?>", " ");
            text = System.Net.WebUtility.HtmlDecode(text);
            text = Regex.Replace(text, @"\s+", " ").Trim();

            return text;
        }

        // Remove all HTML tags from a string
        private static string StripTags(string s) => Regex.Replace(s ?? "", "<.*?>", string.Empty);

        // Extract the first regex match result from HTML
        private static string? ExtractFirst(string html, string pattern)
            => Regex.Match(html ?? "", pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase) is var m && m.Success ? m.Groups[1].Value : null;

        // Clean and normalize whitespace from text
        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            var t = s.Replace('\u00A0', ' ');
            t = Regex.Replace(t, "[\u200B\u200C\u200D\uFEFF]", "");
            t = Regex.Replace(t, @"\s+", " ");
            return t.Trim();
        }

        // Normalize exercise ID to a 5-digit format
        private static string NormalizeExerciseId(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var digits = new string(raw.Where(char.IsDigit).ToArray());
            if (string.IsNullOrEmpty(digits)) return "";
            if (digits.Length > 5) digits = digits[^5..];
            return digits.PadLeft(5, '0');
        }

        // Build exercise image URL
        private static string GetExerciseImageUrl(string id5)
            => $"https://cloud.visualcoaching2.com/Application/Exercise/Image/{id5}";

        // Build exercise video URL
        private static string GetExerciseVideoUrl(string id5)
            => $"https://cloud.visualcoaching2.com/VCP/Images/Exercises/{id5}.mp4";

        // Try extracting an exercise ID from HTML
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

        // Build absolute URL from a relative path
        private static string BuildAbsoluteUrl(string? maybeRelative)
        {
            if (string.IsNullOrWhiteSpace(maybeRelative)) return "";
            if (Uri.TryCreate(maybeRelative, UriKind.Absolute, out _)) return maybeRelative;
            return $"https://cloud.visualcoaching2.com{(maybeRelative!.StartsWith("/") ? "" : "/")}{maybeRelative}";
        }

        // Parse week/day values from a program URL
        private void ParseWeekAndDayVcFromUrl(string raw)
        {
            _week = GetQueryInt(raw, "week", 0);

            int dayRaw = GetQueryInt(raw, "day", 0);

            // Convert API day (1-7) to internal representation (0-6)
            if (dayRaw >= 1 && dayRaw <= 7)
            {
                _dayVc = dayRaw - 1;
            }
            else if (dayRaw >= 0 && dayRaw <= 6)
            {
                _dayVc = dayRaw;
            }
            else
            {
                _dayVc = 0;
            }

            Debug.WriteLine($"[ParseWeekAndDayVc] dayRaw from URL: {dayRaw}, _dayVc (internal): {_dayVc}");

            if (_anchorDate.HasValue)
            {
                _selectedDate = _programWeek0Monday.AddDays(7 * _week + _dayVc);
                Debug.WriteLine($"[ParseWeekAndDayVc] Updated _selectedDate to: {_selectedDate:yyyy-MM-dd dddd}");
            }
        }

        // Add/replace week and day query values in a URL
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

            Upsert("week", week.ToString(CultureInfo.InvariantCulture));

            // Convert 0–6 (Mon–Sun) internal to 1–7 API values
            int apiDay = Math.Clamp(dayVc, 0, 6) + 1;
            Upsert("day", apiDay.ToString(CultureInfo.InvariantCulture));

            Upsert("session", "0");
            Upsert("i", "0");
            if (!parts.Any(p => p.StartsWith("format=", StringComparison.OrdinalIgnoreCase))) parts.Add("format=Tablet");
            if (!parts.Any(p => p.StartsWith("version=", StringComparison.OrdinalIgnoreCase))) parts.Add("version=2");

            return $"{path}?{string.Join("&", parts)}";
        }

        // Get an integer query value from a URL, or fallback
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

        // Normalize URL, forcing index and required params
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

        // Replace a query parameter in a URL
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

        // Get the Monday date for the week containing a given date
        private static DateTime WeekMonday(DateTime d)
        {
            int diff = ((7 + (int)d.DayOfWeek - (int)DayOfWeek.Monday) % 7);
            return d.Date.AddDays(-diff);
        }

        // Compute the program's baseline Monday
        private void ComputeProgramBaselineMonday()
        {
            var ad = _anchorDate!.Value;
            var adMon = WeekMonday(ad);
            _programWeek0Monday = adMon.AddDays(-7 * _entryWeekForProgram);
        }

        // Get the currently selected date
        private DateTime SelectedDate
            => _selectedDate ?? _programWeek0Monday.AddDays(7 * _week + Math.Clamp(_dayVc, 0, 6));

        // Get the current selected week index
        private int SelectedWeekIndex
        {
            get
            {
                var monday = WeekMonday(SelectedDate);
                var days = (monday - _programWeek0Monday).TotalDays;
                return Math.Max(0, (int)(days / 7.0));
            }
        }

        // Get display week number (1-based)
        private int DisplayWeek
        {
            get
            {
                var span = WeekMonday(SelectedDate) - _programWeek0Monday;
                int w = (int)(span.TotalDays / 7.0);
                return Math.Max(0, w) + 1;
            }
        }

        // Try loading anchor date from a program URL
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

        // Try parsing a date string in yyyy-MM-dd format
        private static bool TryParseYMD(string? s, out DateTime dt)
        {
            dt = default;
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                return true;
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt);
        }

        // Get a query parameter value from a URL
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

        // Parse a program ID from a program URL
        private static int? ParseProgramId(string url)
        {
            var m = Regex.Match(url ?? "", @"/Program/Session/(\d+)", RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var id)) return id;
            return null;
        }

        // Safely clean up current media element
        private void CleanupCurrentMedia()
        {
            if (_currentMediaElement != null)
            {
                try
                {
                    _currentMediaElement.Source = "";
                    _currentMediaElement = null;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Media] Cleanup warning: {ex.Message}");
                }
            }
        }

        // Download a video behind-auth to a temp file
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

        // Show a video in the lightbox overlay
        private async Task ShowVideoOverlayAsync(string videoUrl, string? title = null)
        {
            CleanupCurrentMedia();

            if (!Uri.TryCreate(videoUrl, UriKind.Absolute, out var _))
                videoUrl = $"https://cloud.visualcoaching2.com{(videoUrl.StartsWith("/") ? "" : "/")}{videoUrl}";

            var media = new ToolkitMediaElement
            {
                ShouldAutoPlay = true,
                ShouldShowPlaybackControls = true,
                Aspect = Aspect.AspectFit,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill
            };

            _currentMediaElement = media;
            bool playing = false;

            try
            {
                media.Source = videoUrl;
                playing = true;
                Debug.WriteLine($"[Video] Using direct URL: {videoUrl}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Video] Direct URL failed: {ex.Message}");
                playing = false;
            }

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

            LightboxTitle.Text = string.IsNullOrWhiteSpace(title) ? "Video" : title;
            LightboxContent.Content = media;
            LightboxOverlay.IsVisible = true;
        }

        // Show an image in the lightbox overlay
        private void ShowImageOverlay(string imageUrl, string? title = null)
        {
            try
            {
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

        // Hide the lightbox overlay
        private void HideLightbox()
        {
            try
            {
                CleanupCurrentMedia();
                LightboxContent.Content = null;
                LightboxOverlay.IsVisible = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Lightbox] Hide error: {ex.Message}");
                LightboxOverlay.IsVisible = false;
            }
        }

        // Event handler: close button in lightbox
        private void OnLightboxCloseClicked(object sender, EventArgs e) => HideLightbox();

        // Event handler: tapping background hides lightbox
        private void OnLightboxBackgroundTapped(object sender, TappedEventArgs e) => HideLightbox();

        // Build compact exercise row with thumbnail, title, sets, and actions
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

            // Thumbnail for exercise
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

                var imageTap = new TapGestureRecognizer();
                imageTap.Tapped += (_, __) => ShowImageOverlay(ex.ImageUrl, ex.Name);
                image.GestureRecognizers.Add(imageTap);

                thumb = image;
            }
            row.Add(thumb, 0, 0);
            Grid.SetRowSpan(thumb, 3);

            // Title and sets
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

            if (ex.Sets.Count > 0)
            {
                var compactSetsGrid = BuildCompactSetsGrid(ex.Sets);
                rightTop.Add(compactSetsGrid, 1, 0);
            }

            row.Add(rightTop, 1, 0);

            // Action buttons row
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
                    Source = "video.png",
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

            if (!string.IsNullOrWhiteSpace(ex.DetailUrl))
            {
                var infoBtn = new ImageButton
                {
                    Source = "info.png",
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

            // Notes text
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


        // Build compact grid for sets shown inline in the UI
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

        // Build compact table for displaying day content
        private View BuildCompactDayTable(IList<List<string>> blocksAsCols)
        {
            var mainStack = new VerticalStackLayout { Spacing = 8 };

            for (int colIdx = 0; colIdx < blocksAsCols.Count; colIdx++)
            {
                var column = blocksAsCols[colIdx];
                if (column.Count == 0) continue;

                var colStack = new VerticalStackLayout { Spacing = 4 };

                foreach (var item in column)
                {
                    if (string.IsNullOrWhiteSpace(StripTags(item))) continue;

                    var content = CreateContentWithLinkedPrograms(item);
                    colStack.Children.Add(content);
                }

                if (colStack.Children.Count > 0)
                {
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

        // Check if program is a "Strength" type by inspecting its HTML
        private async Task<bool> IsStrengthProgramAsync(string cookie, string programId)
        {
            try
            {
                string testUrl = $"/Application/Program/Session/{programId}?week=0&day=0&session=0&i=0&format=Tablet&version=2";
                string html = await VisualCoachingService.GetRawSessionHtml(cookie, testUrl);

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

        // Find the first week/day that has content for a given program
        private async Task<(int week, int day)> FindFirstContentWeekDayAsync(string cookie, string programId)
        {
            bool foundContent = false;
            int targetWeek = 0;
            int targetDay = 1; // Default to day 1 for non-strength programs

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

            if (isStrengthProgram)
            {
                Debug.WriteLine($"[LinkedProgram] Searching strength program {programId} starting from day 0");

                for (int week = 0; week < MaxWeeks && !foundContent; week++)
                {
                    if (await HasContentAsync(week, 0))
                    {
                        targetWeek = week;
                        targetDay = 0;
                        foundContent = true;
                        Debug.WriteLine($"[LinkedProgram] Found strength content at week {week}, day 0");
                        break;
                    }

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
                targetDay = isStrengthProgram ? 0 : 1;
            }

            return (targetWeek, targetDay);
        }


        // Fetch all sessions for a program (handles strength vs regular programs)
        public async Task<bool> FetchAllSessionsAsync(int programId, string cookie)
        {
            bool hasContent = false;

            try
            {
                bool isStrengthProgram = await IsStrengthProgramAsync(cookie, programId.ToString());
                Debug.WriteLine($"[Session Fetch] Program {programId} is {(isStrengthProgram ? "Strength" : "Regular")} type");

                if (isStrengthProgram)
                {
                    for (int week = 0; week <= 13; week++)
                    {
                        if (await HasSessionContentAsync(programId, week, 0, cookie))
                        {
                            Debug.WriteLine($"[Session Fetch] Found Strength content: Week {week}, Day 0");
                            hasContent = true;
                        }

                        for (int day = 1; day <= 7; day++)
                        {
                            if (await HasSessionContentAsync(programId, week, day, cookie))
                            {
                                Debug.WriteLine($"[Session Fetch] Found Strength content: Week {week}, Day {day}");
                                hasContent = true;
                            }
                        }
                    }
                }
                else
                {
                    for (int week = 0; week <= 13; week++)
                    {
                        for (int day = 1; day <= 7; day++)
                        {
                            if (await HasSessionContentAsync(programId, week, day, cookie))
                            {
                                Debug.WriteLine($"[Session Fetch] Found Regular content: Week {week}, Day {day}");
                                hasContent = true;
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

        // Check if a program session has any content
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

        // Load a linked program and render it inline in the UI
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

        // Fetch all sessions (legacy method, simpler version)
        public void FetchAllSessions(int programId)
        {
            if (HasWeekZero(programId))
            {
                FetchSession(programId, 0, 0);

                for (int day = 1; day <= 7; day++)
                {
                    if (HasSessionContent(programId, 0, day))
                    {
                        FetchSession(programId, 0, day);
                    }
                }
            }

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

        // Check if a program supports Week 0 sessions
        private bool HasWeekZero(int programId)
        {
            return programId == 1476483; // Incline cycling program
        }

        // Placeholder method: check if session content exists (legacy)
        private bool HasSessionContent(int programId, int week, int day)
        {
            return true;
        }

        // Fetch a single session (legacy method with logging)
        private void FetchSession(int programId, int week, int day)
        {
            string url = string.Format(
                "https://cloud.visualcoaching2.com/Application/Program/Session/{0}?week={1}&day={2}&session=0&i=0&format=Tablet&version=2",
                programId, week, day
            );

            try
            {
                Debug.WriteLine($"[VCS] Fetching session from: {url}");
                // Fetch logic would go here
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VCS] Failed to fetch session: {url}. Error: {ex.Message}");
            }
        }

        // Info class for diary data
        private class SessionDiaryInfo
        {
            public int DiaryId { get; set; }
            public int UserId { get; set; }
            public string Date { get; set; } = "";
            public string ProgramKey { get; set; } = "";

            public bool IsValid()
            {
                return DiaryId > 0 && UserId > 0 && !string.IsNullOrEmpty(Date) && !string.IsNullOrEmpty(ProgramKey);
            }
        }


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
            SessionStack.Children.Add(diarySection);
        }

        // Example of how to call this from your existing code
        private async void OnLoadDiaryClicked(object sender, EventArgs e)
        {
            string userEmail = "user@example.com"; // Replace with actual email
            string currentDate = DateTime.Today.ToString("yyyy-MM-dd");
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

        // Get current user email
        private async Task<string> GetCurrentUserEmailAsync()
        {
            string storedEmail = Preferences.Get(UserEmailKey, "");

            if (!string.IsNullOrEmpty(storedEmail))
            {
                bool useStored = await DisplayAlert("Confirm Email",
                    $"Use saved email: {storedEmail}?", "Yes", "Use Different Email");

                if (useStored)
                    return storedEmail;
            }

            string newEmail = await DisplayPromptAsync("User Email",
                "Enter your email address for diary entry:",
                placeholder: storedEmail);

            if (!string.IsNullOrEmpty(newEmail))
            {
                Preferences.Set(UserEmailKey, newEmail);
                return newEmail;
            }

            return storedEmail;
        }

        // Method to clear stored email
        public static void ClearStoredUserEmail()
        {
            Preferences.Remove(UserEmailKey);
        }

        // Updated LoadAndRenderAsync method
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

                await EnsureProgramWeeksAsync(cookie);
                BuildWeekTabsUI();
                BuildDaysListUI();

                string html = await VisualCoachingService.GetRawSessionHtml(cookie, _url);

                // Store the HTML for diary extraction
                _currentSessionHtml = html ?? "";

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

                // Add diary functionality after successful rendering
                if (rendered)
                {
                    AddDiaryButtonForSession();
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

        // Load diary form method
        private async Task LoadDiaryForm(VerticalStackLayout parentContainer, string cookie,
            UserInfo userInfo, string diaryType)
        {
            try
            {
                int diaryId = diaryType.ToLower() == "wellness" ? userInfo.WellnessDiaryId : userInfo.PerformanceDiaryId;

                if (diaryId <= 0)
                {
                    Debug.WriteLine($"[Diary] No {diaryType} diary found for user");

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

        // Show post session diary async
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

        // Show diary form async
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

        // Load diary for user
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

        // Add diary button for session
        private void AddDiaryButtonForSession()
        {
            // Check if diary button already exists
            if (SessionStack.Children.Any(c => c is VerticalStackLayout vs &&
                vs.Children.Any(child => child is Button btn && btn.Text.Contains("Diary"))))
                return;

            if (string.IsNullOrEmpty(_currentSessionHtml)) return;

            var diariesInSession = ExtractSessionDiaryInfoFromHtml(_currentSessionHtml);

            if (diariesInSession.Count == 0)
            {
                Debug.WriteLine("[Diary] No diary information found in session HTML");
                return;
            }

            Debug.WriteLine($"[Diary] Found {diariesInSession.Count} diary entries in session");

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

            // Add diary buttons for each diary found
            foreach (var diaryInfo in diariesInSession)
            {
                var diaryButton = new Button
                {
                    Text = $"Fill Diary (ID: {diaryInfo.DiaryId})",
                    BackgroundColor = Color.FromArgb("#4CAF50"),
                    TextColor = Colors.White,
                    CornerRadius = 8,
                    Padding = new Thickness(20, 10),
                    HorizontalOptions = LayoutOptions.Center,
                    Margin = new Thickness(0, 5)
                };

                var capturedDiaryInfo = diaryInfo;
                diaryButton.Clicked += async (s, e) => await OnSessionDiaryButtonClicked(capturedDiaryInfo);

                diarySection.Children.Add(diaryButton);
            }

            SessionStack.Children.Add(diarySection);
        }

        // On diary button clicked
        private async void OnDiaryButtonClicked(object sender, EventArgs e)
        {
            try
            {
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

                string userEmail = Preferences.Get("UserEmail", "");

                if (string.IsNullOrEmpty(userEmail))
                {
                    userEmail = await DisplayPromptAsync("User Email",
                        "Enter your email address for diary entries:",
                        placeholder: "user@example.com");

                    if (string.IsNullOrEmpty(userEmail))
                    {
                        await DisplayAlert("Email Required", "Email is required for diary entries.", "OK");
                        return;
                    }

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

        // Load diary for current session
        private async Task LoadDiaryForCurrentSession(string cookie, string userEmail)
        {
            try
            {
                var userInfo = await VisualCoachingService.GetUserInfo(cookie, userEmail);
                if (userInfo == null)
                {
                    await DisplayAlert("Error", $"Could not find user information for {userEmail}", "OK");
                    return;
                }

                string currentDate = SelectedDate.ToString("yyyy-MM-dd");
                int programId = _programId ?? 0;
                int week = _week;
                int day = _dayVc + 1;

                Debug.WriteLine($"[Diary] Loading diary for {userInfo.DisplayName}");
                Debug.WriteLine($"[Diary] Session: {currentDate}, Program: {programId}, Week: {week}, Day: {day}");

                var diaryContainer = new VerticalStackLayout
                {
                    Spacing = 15,
                    Margin = new Thickness(0, 10),
                    BackgroundColor = Color.FromArgb("#F8F9FA"),
                    Padding = new Thickness(15)
                };

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

        // Load specific diary
        private async Task LoadSpecificDiary(VerticalStackLayout parentContainer, string cookie,
            UserInfo userInfo, string diaryType, int diaryId, string date, int programId, int week, int day)
        {
            try
            {
                Debug.WriteLine($"[Diary] Loading {diaryType} diary (ID: {diaryId})");

                var existingData = await VisualCoachingService.GetDiaryData(
                    cookie, diaryId, date, userInfo.UserId, programId, week, day, 0, 0);

                var diaryForm = CreateBasicDiaryForm(diaryType, diaryId);
                diaryForm.Date = date;
                diaryForm.UserId = userInfo.UserId;
                diaryForm.ProgramKey = $"{programId}:{week:000}:{day:000}:000:000:000";

                if (!string.IsNullOrEmpty(existingData))
                {
                    PopulateFormWithExistingData(diaryForm, existingData);
                }

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

        // Extract session diary info from HTML
        private List<SessionDiaryInfo> ExtractSessionDiaryInfoFromHtml(string html)
        {
            var diaryList = new List<SessionDiaryInfo>();

            if (string.IsNullOrWhiteSpace(html)) return diaryList;

            var diaryMatches = Regex.Matches(html,
                @"<a[^>]*class=""[^""]*btn btn-primary[^""]*""[^>]*target=""customDiary""[^>]*data-id=""([^""]+)""[^>]*>Diary</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match match in diaryMatches)
            {
                try
                {
                    string dataId = match.Groups[1].Value;
                    Debug.WriteLine($"[Diary] Found diary data-id: {dataId}");

                    var parts = dataId.Split('&');
                    var diaryInfo = new SessionDiaryInfo();

                    // First part is the diary ID
                    if (parts.Length > 0 && int.TryParse(parts[0], out int diaryId))
                    {
                        diaryInfo.DiaryId = diaryId;
                    }

                    // Parse the remaining key-value pairs
                    for (int i = 1; i < parts.Length; i++)
                    {
                        var keyValue = parts[i].Split('=', 2);
                        if (keyValue.Length != 2) continue;

                        switch (keyValue[0].ToLower())
                        {
                            case "date":
                                diaryInfo.Date = keyValue[1];
                                break;
                            case "userid":
                                if (int.TryParse(keyValue[1], out var userId))
                                    diaryInfo.UserId = userId;
                                break;
                            case "programkey":
                                diaryInfo.ProgramKey = keyValue[1];
                                break;
                        }
                    }

                    if (diaryInfo.IsValid())
                    {
                        diaryList.Add(diaryInfo);
                        Debug.WriteLine($"[Diary] Extracted: DiaryId={diaryInfo.DiaryId}, UserId={diaryInfo.UserId}, Date={diaryInfo.Date}, ProgramKey={diaryInfo.ProgramKey}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Diary] Error parsing diary info: {ex.Message}");
                }
            }

            return diaryList;
        }

        // On session diary button clicked
        private async Task OnSessionDiaryButtonClicked(SessionDiaryInfo diaryInfo)
        {
            try
            {
                string cookie = SessionManager.GetCookie();
                if (string.IsNullOrEmpty(cookie))
                {
                    await DisplayAlert("Error", "Please log in again.", "OK");
                    return;
                }

                Debug.WriteLine($"[Diary] Loading diary with extracted info:");
                Debug.WriteLine($"[Diary] DiaryId: {diaryInfo.DiaryId}");
                Debug.WriteLine($"[Diary] UserId: {diaryInfo.UserId}");
                Debug.WriteLine($"[Diary] Date: {diaryInfo.Date}");
                Debug.WriteLine($"[Diary] ProgramKey: {diaryInfo.ProgramKey}");

                await LoadDiaryWithExtractedInfo(cookie, diaryInfo);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Diary] Error: {ex.Message}");
                await DisplayAlert("Error", "Failed to load diary form.", "OK");
            }
        }

        // Load diary with extracted info
        private async Task LoadDiaryWithExtractedInfo(string cookie, SessionDiaryInfo diaryInfo)
        {
            try
            {
                var keyParts = diaryInfo.ProgramKey.Split(':');
                if (keyParts.Length >= 6)
                {
                    int programId = int.Parse(keyParts[0]);
                    int week = int.Parse(keyParts[1]);
                    int day = int.Parse(keyParts[2]);
                    int session = int.Parse(keyParts[3]);
                    int i = int.Parse(keyParts[4]);

                    var existingData = await VisualCoachingService.GetDiaryData(
                        cookie, diaryInfo.DiaryId, diaryInfo.Date, diaryInfo.UserId,
                        programId, week, day, session, i);

                    await ShowDiaryFormFromExtractedData(diaryInfo, existingData, cookie);
                }
                else
                {
                    await DisplayAlert("Error", "Invalid program key format.", "OK");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Diary] Error loading diary: {ex.Message}");
                await DisplayAlert("Error", "Failed to load diary.", "OK");
            }
        }

        // Show diary form from extracted data
        private async Task ShowDiaryFormFromExtractedData(SessionDiaryInfo diaryInfo, string existingData, string cookie)
        {
            try
            {
                var diaryForm = new DiaryForm
                {
                    DiaryId = diaryInfo.DiaryId,
                    Title = "Session Diary",
                    Type = "performance",
                    Date = diaryInfo.Date,
                    UserId = diaryInfo.UserId,
                    ProgramKey = diaryInfo.ProgramKey,
                    Fields = new List<DiaryField>
            {
                new DiaryField { Name = "rpe", Label = "Rate of Perceived Exertion (1-10)", Type = "rating", MinValue = 1, MaxValue = 10, Required = true },
                new DiaryField { Name = "duration", Label = "Session Duration (minutes)", Type = "number", MinValue = 0, MaxValue = 300 },
                new DiaryField { Name = "completed", Label = "Session Completed", Type = "dropdown", Options = new List<string> { "Yes", "Partially", "No" }, Required = true },
                new DiaryField { Name = "notes", Label = "Session Notes", Type = "textarea", Placeholder = "How did the session feel? Any issues or achievements?" }
            }
                };

                if (!string.IsNullOrEmpty(existingData))
                {
                    PopulateFormWithExistingData(diaryForm, existingData);
                }

                var diaryContainer = new VerticalStackLayout
                {
                    Spacing = 15,
                    Margin = new Thickness(0, 10),
                    BackgroundColor = Color.FromArgb("#F8F9FA"),
                    Padding = new Thickness(15)
                };

                diaryContainer.Children.Add(new Label
                {
                    Text = "Session Diary Entry",
                    FontAttributes = FontAttributes.Bold,
                    FontSize = 18,
                    HorizontalOptions = LayoutOptions.Center
                });

                diaryContainer.Children.Add(new Label
                {
                    Text = $"Date: {diaryInfo.Date} • Diary ID: {diaryInfo.DiaryId}",
                    FontSize = 14,
                    HorizontalOptions = LayoutOptions.Center,
                    TextColor = Color.FromArgb("#666666"),
                    Margin = new Thickness(0, 0, 0, 10)
                });

                var formUI = CreateDiaryFormUI(diaryForm, cookie);
                diaryContainer.Children.Add(formUI);

                SessionStack.Children.Add(diaryContainer);

                if (SessionStack.Parent is ScrollView scrollView)
                {
                    await scrollView.ScrollToAsync(diaryContainer, ScrollToPosition.Start, true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Diary] Error showing diary form: {ex.Message}");
                await DisplayAlert("Error", "Failed to display diary form.", "OK");
            }
        }
    }
}