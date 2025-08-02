using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using Microsoft.Maui.Dispatching;
using System.Collections.ObjectModel;
using SportsTraining.Services;
using SportsTraining.Models;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Threading.Tasks;

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
                string today = System.DateTime.Today.ToString("yyyy-MM-dd");
                var jsonResponse = await VisualCoachingService.GetRawSessionsJson(cookie, today);

                var sessions = new System.Collections.Generic.List<VisualCoachingService.ProgramSessionBrief>();

                try
                {
                    sessions = Newtonsoft.Json.JsonConvert.DeserializeObject<System.Collections.Generic.List<VisualCoachingService.ProgramSessionBrief>>(jsonResponse)
                               ?? new System.Collections.Generic.List<VisualCoachingService.ProgramSessionBrief>();
                }
                catch
                {
                    try
                    {
                        var jobject = JObject.Parse(jsonResponse);
                        if (jobject["sessions"] != null)
                        {
                            sessions = jobject["sessions"]?.ToObject<System.Collections.Generic.List<VisualCoachingService.ProgramSessionBrief>>() ?? new System.Collections.Generic.List<VisualCoachingService.ProgramSessionBrief>();
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.WriteLine($"JSON parsing failed: {ex.Message}");
                    }
                }

                var seenKeys = new System.Collections.Generic.HashSet<string>();

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
            catch (System.Exception ex)
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

        private async void ProgramsListView_ItemSelected(object sender, SelectedItemChangedEventArgs e)
        {
            if (e.SelectedItem is ProgramSession selected)
            {
                ProgramsListView.SelectedItem = null;

                var encodedUrl = Uri.EscapeDataString(selected.Url);
                await Shell.Current.GoToAsync($"{nameof(TrainingPage)}?url={encodedUrl}");
 
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
