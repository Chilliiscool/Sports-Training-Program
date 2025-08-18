// Module Name: TrainingPage
// Author: Kye Franken
// Date Created: 20 / 06 / 2025
// Date Modified: 18 / 08 / 2025
// Description: Native rendering for BOTH program styles with Monday-start weeks.
//   • Table: original AM/PM planned matrix (blocks as columns, <p> rows).
//   • Exercises: weights view parsed from <div class="exercise"> blocks.
//   • View toggle logic keeps both working; Auto prefers Exercises when present.
//   • Images/videos use an in-page lightbox overlay (no popup).

using CommunityToolkit.Maui.Media;           // MediaElement
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.ApplicationModel;       // Launcher (fallback)
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;                // FileSystem, Preferences
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

        // Monday-start model: 0..6 = Mon..Sun
        private static readonly (string Label, int DayIdx)[] DaysVc =
        {
            ("Mon",0),("Tue",1),("Wed",2),("Thu",3),("Fri",4),("Sat",5),("Sun",6)
        };

        private string _url = "";
        private string _absoluteSessionUrl = "";

        private int _week = 0;           // 0-based week from URL (server)
        private int _dayVc = 0;          // 0..6 (Mon..Sun)
        private DateTime? _anchorDate;   // Program DateStart yyyy-MM-dd (ad)
        private DateTime? _selectedDate; // exact date for current view (seeded from 'ad')

        private int _entryWeekForProgram = 0;
        private DateTime _programWeek0Monday; // baseline Monday for Week 0

        private int? _programId;
        private int? _programWeeks;
        private readonly Dictionary<int, int> _weeksCache = new();

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

                ParseWeekAndDayVcFromUrl(_url);
                _entryWeekForProgram = _week;

                if (_anchorDate == null)
                    TryLoadAnchorFromUrl(_url);

                if (_selectedDate == null && _anchorDate != null)
                    _selectedDate = _anchorDate;

                if (_anchorDate != null)
                    ComputeProgramBaselineMonday();

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
        }

        // ----------------------- Loader -----------------------
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

                // Build week tabs + day list (needs SelectedDate/Week)
                await EnsureProgramWeeksAsync(cookie);
                BuildWeekTabsUI();
                BuildDaysListUI();

                string html = await VisualCoachingService.GetRawSessionHtml(cookie, _url);

                // Fallback for servers expecting 1..7 indexing
                if (string.IsNullOrWhiteSpace(html))
                {
                    int altDay = (_dayVc >= 0 && _dayVc <= 6) ? (_dayVc + 1) : 1; // convert 0..6 -> 1..7
                    var altUrl = WithWeekDayVc(_url, _week, _dayVc);
                    altUrl = ReplaceQuery(altUrl, "day", altDay.ToString(CultureInfo.InvariantCulture));
                    string htmlAlt = await VisualCoachingService.GetRawSessionHtml(cookie, altUrl);
                    if (!string.IsNullOrWhiteSpace(htmlAlt))
                    {
                        html = htmlAlt;
                        _url = altUrl;
                        _absoluteSessionUrl = BuildAbsoluteUrl(_url);
                        _selectedDate ??= _programWeek0Monday.AddDays(7 * _week + Math.Clamp(_dayVc, 0, 6));
                    }
                }

                // JSON summary fallback
                if (string.IsNullOrWhiteSpace(html))
                {
                    var summary = await VisualCoachingService.GetSessionSummary(cookie, _url);
                    if (summary != null)
                    {
                        var body = summary.HtmlSummary;
                        if (string.IsNullOrWhiteSpace(body) && !string.IsNullOrWhiteSpace(summary.Description))
                            body = $"<p>{WebUtility.HtmlEncode(summary.Description)}</p>";
                        html = $"<h1>{WebUtility.HtmlEncode(summary.SessionTitle ?? "Training Session")}</h1>{body}";
                    }
                }

                // Title (from HTML <h1>)
                var h1 = ExtractFirst(html, @"<h1[^>]*>(.*?)</h1>");
                TitleLabel.Text = !string.IsNullOrWhiteSpace(h1)
                    ? WebUtility.HtmlDecode(StripTags(h1).Trim())
                    : "Training Session";

                // Header line above tabs
                HeaderWeekDateLabel.Text = $"Week {DisplayWeek} · {SelectedDate:ddd dd/MM/yyyy}";

                // Clear old content
                SessionStack.Children.Clear();

                // Show session title inside the content
                SessionStack.Children.Add(new Label
                {
                    Text = TitleLabel.Text,
                    FontAttributes = FontAttributes.Bold,
                    FontSize = 18,
                    Margin = new Thickness(0, 8, 0, 8)
                });

                // Add buttons for any <a class="linkedProgram"> (e.g., “Weights”)
                AddLinkedProgramButtons(html);

                var mode = Preferences.Get(ViewModeKey, "Auto"); // Auto | Exercises | Table
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
                        ? new Color(0.25f, 0.43f, 0.96f)
                        : new Color(0f, 0f, 0f, 0.08f),
                    TextColor = Colors.White
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
            foreach (var (label, dIdx) in DaysVc)
            {
                var date = weekMonday.AddDays(dIdx);
                bool isSelected = SelectedDate.Date == date.Date;

                var row = new Grid { Padding = new Thickness(10, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
                row.BackgroundColor = isSelected
                    ? new Color(0f, 0f, 0f, 0.08f)
                    : Colors.Transparent;

                row.Add(new Label { Text = label, FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 0, 10, 0) }, 0, 0);
                row.Add(new Label { Text = date.ToString("dd/MM"), Opacity = 0.8 }, 1, 0);

                int captured = dIdx;
                var tap = new TapGestureRecognizer();
                tap.Tapped += async (_, __) =>
                {
                    _dayVc = captured; // 0..6 (Mon..Sun)
                    _selectedDate = weekMonday.AddDays(_dayVc);
                    _week = SelectedWeekIndex;

                    _url = WithWeekDayVc(_url, _week, _dayVc);
                    _absoluteSessionUrl = BuildAbsoluteUrl(_url);

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
                                .Select(m => WebUtility.HtmlDecode(Regex.Replace(m.Groups[1].Value, "<.*?>", "")))
                                .Select(NormalizeProgramLine)
                                .Where(s => !string.IsNullOrWhiteSpace(s))
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
                                  ? new Color(0.12f, 0.12f, 0.12f)
                                  : new Color(0.96f, 0.96f, 0.96f)
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
                    var lbl = new Label
                    {
                        Text = text,
                        FontSize = 14,
                        LineBreakMode = LineBreakMode.WordWrap,
                        WidthRequest = ParagraphColWidth
                    };
                    Grid.SetRow(lbl, r);
                    Grid.SetColumn(lbl, c);
                    grid.Children.Add(lbl);
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
                    ? new Color(0.12f, 0.12f, 0.12f)
                    : new Color(0.97f, 0.97f, 0.97f)
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

            // Actions
            if (!string.IsNullOrWhiteSpace(ex.VideoUrl) || !string.IsNullOrWhiteSpace(ex.ExerciseId))
            {
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

                    var idTag = new Label
                    {
                        Text = $"ID: {ex.ExerciseId}",
                        FontSize = 12,
                        Opacity = 0.7,
                        VerticalTextAlignment = TextAlignment.Center
                    };
                    actions.Add(idTag);
                }

                row.Add(actions, 1, 1);
            }

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
                BackgroundColor = new Color(1f, 0.95f, 0.8f),
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

        private static string NormalizeProgramLine(string s)
        {
            var t = Sanitize(s);
            t = Regex.Replace(t, @"\bA\s*M\b", "AM", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\bP\s*M\b", "PM", RegexOptions.IgnoreCase);
            return t;
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

        // --- URL week/day handling (accept 0..6 OR 1..7; store as 0..6 Mon..Sun) ---
        private void ParseWeekAndDayVcFromUrl(string raw)
        {
            _week = GetQueryInt(raw, "week", 0);

            int dayRaw = GetQueryInt(raw, "day", 0);
            if (dayRaw >= 0 && dayRaw <= 6) _dayVc = dayRaw;          // 0..6 (Mon..Sun)
            else if (dayRaw >= 1 && dayRaw <= 7) _dayVc = dayRaw - 1; // 1..7 -> 0..6
            else _dayVc = 0; // default Monday
        }

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

            Upsert("week", week.ToString(CultureInfo.InvariantCulture));                   // 0-based
            Upsert("day", Math.Clamp(dayVc, 0, 6).ToString(CultureInfo.InvariantCulture)); // 0..6 Mon..Sun
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
        //                 Image & Video (lightbox)
        // ======================================================
        private void ShowVideoPopup(string videoUrl, string? title = null)
        {
            var player = new SportsTraining.Controls.VideoPlayerView
            {
                Source = videoUrl,
                AutoPlay = true,
                ShowControls = true,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill
            };

            LightboxTitle.Text = string.IsNullOrWhiteSpace(title) ? "Video" : title;
            LightboxContent.Content = player;     // your existing ContentView in the overlay
            LightboxOverlay.IsVisible = true;     // your existing overlay grid
        }

        private void HideLightbox()
        {
            LightboxContent.Content = null;       // this disposes player via handler
            LightboxOverlay.IsVisible = false;
        }


        // ------------------------------------------------------
        // Linked program buttons (e.g., “Weights”)
        // ------------------------------------------------------
        private void AddLinkedProgramButtons(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return;

            var matches = Regex.Matches(
                html,
                @"<a[^>]*class=['""]linkedProgram[^'""]*['""][^>]*href=['""](?<href>[^'""]+)['""][^>]*>(?<text>.*?)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match m in matches.Cast<Match>())
            {
                var href = WebUtility.HtmlDecode(m.Groups["href"].Value);
                var text = WebUtility.HtmlDecode(StripTags(m.Groups["text"].Value)).Trim();
                if (string.IsNullOrWhiteSpace(text)) text = "Open linked program";

                var btn = new Button
                {
                    Text = text,
                    Padding = new Thickness(10, 6),
                    CornerRadius = 10,
                    Margin = new Thickness(0, 6, 0, 0)
                };

                btn.Clicked += async (_, __) => await OpenLinkedProgramAsync(href, text);
                SessionStack.Children.Add(btn);
            }
        }

        private async Task OpenLinkedProgramAsync(string href, string linkText)
        {
            try
            {
                // Look for "...program/12345" inside href (it’s often in the fragment)
                var idMatch = Regex.Match(href ?? "", @"program/(\d+)", RegexOptions.IgnoreCase);
                if (idMatch.Success)
                {
                    string id = idMatch.Groups[1].Value;
                    int week = _week;                       // keep current week
                    int day = Math.Clamp(_dayVc, 0, 6);     // keep current day (Mon=0..Sun=6)
                    string ad = _anchorDate?.ToString("yyyy-MM-dd") ?? "";

                    // Treat that number like a Program/Session id
                    string target = $"/Application/Program/Session/{id}?week={week}&day={day}&session=0&i=0&format=Tablet&version=2&ad={ad}";

                    var encodedUrl = Uri.EscapeDataString(target);
                    var encodedAnchor = Uri.EscapeDataString(ad);
                    await Shell.Current.GoToAsync($"//{nameof(TrainingPage)}?url={encodedUrl}&anchorDate={encodedAnchor}");
                    return;
                }

                // Couldn’t parse an id ? open the absolute href in the browser
                var abs = BuildAbsoluteUrl(href);
                if (!string.IsNullOrWhiteSpace(abs))
                    await Launcher.OpenAsync(new Uri(abs));
                else
                    await DisplayAlert("Open link", "Couldn’t open the linked program.", "OK");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Training] linkedProgram open failed: " + ex.Message);
                await DisplayAlert("Open link", "Couldn’t open the linked program.", "OK");
            }
        }

        // ======================================================
        //                 Lightbox handlers (images/videos)
        // ======================================================
        private void OnLightboxCloseClicked(object sender, EventArgs e) => HideLightbox();
        private void OnLightboxBackgroundTapped(object sender, TappedEventArgs e) => HideLightbox();
    }
}
