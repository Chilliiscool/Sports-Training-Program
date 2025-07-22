using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using Microsoft.Maui.Dispatching;
using System.Collections.ObjectModel;
using SportsTraining.Services;
using SportsTraining.Models;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using System.Linq;

namespace SportsTraining.Pages
{
    public partial class MainPage : ContentPage
    {
        public ObservableCollection<ProgramSession> TodayPrograms { get; set; } = new ObservableCollection<ProgramSession>();
        private bool isLoading = false;

        public MainPage()
        {
            InitializeComponent();
            ProgramsListView.ItemsSource = TodayPrograms;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            LogoImage.IsVisible = Preferences.Get("SelectedCompany", "Normal") == "ETPA";
            _ = LoadUserProgramsAsync();
        }

        private async Task LoadUserProgramsAsync()
        {
            if (isLoading) return;
            isLoading = true;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                TodayPrograms.Clear();
                LoadingIndicator.IsVisible = true;
                LoadingIndicator.IsRunning = true;
                ProgramsListView.IsVisible = false;
            });

            string cookie = Preferences.Get("VCP_Cookie", string.Empty);

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
                var jsonResponse = await VisualCoachingService.GetRawSessionsJson(cookie, today);

                var sessions = new List<VisualCoachingService.ProgramSessionBrief>();

                try
                {
                    sessions = Newtonsoft.Json.JsonConvert.DeserializeObject<List<VisualCoachingService.ProgramSessionBrief>>(jsonResponse)
                               ?? new List<VisualCoachingService.ProgramSessionBrief>();
                }
                catch
                {
                    try
                    {
                        var jobject = JObject.Parse(jsonResponse);
                        if (jobject["sessions"] != null)
{
    sessions = jobject["sessions"]?.ToObject<List<VisualCoachingService.ProgramSessionBrief>>() ?? new List<VisualCoachingService.ProgramSessionBrief>();
}
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"JSON parsing failed: {ex.Message}");
                    }
                }

                var seenKeys = new HashSet<string>();

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    TodayPrograms.Clear();

                    foreach (var brief in sessions)
                    {
                        string key = $"{brief.SessionTitle}_{brief.Url}";

                        if (!seenKeys.Contains(key))
                        {
                            seenKeys.Add(key);

                            TodayPrograms.Add(new ProgramSession
                            {
                                SessionTitle = brief.SessionTitle,
                                Url = brief.Url
                            });
                        }
                    }

                    ShowProgramList();
                });
            }
            catch (Exception ex)
            {
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
                });
            }
        }

        private void ProgramsListView_ItemSelected(object sender, SelectedItemChangedEventArgs e)
        {
            if (e.SelectedItem is ProgramSession selected)
            {
                ProgramsListView.SelectedItem = null;
                Debug.WriteLine($"Selected session: {selected.SessionTitle}, URL: {selected.Url}");
            }
        }

        private void ShowProgramList()
        {
            LoadingIndicator.IsVisible = false;
            LoadingIndicator.IsRunning = false;
            ProgramsListView.IsVisible = true;
        }
    }
}
