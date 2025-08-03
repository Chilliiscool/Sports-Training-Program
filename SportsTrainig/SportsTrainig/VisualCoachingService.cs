using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using Newtonsoft.Json;

namespace SportsTraining.Services
{
    public static class VisualCoachingService
    {
        private static readonly HttpClient client = new();

        private static void SetupHttpClientHeaders(string cookie)
        {
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("Cookie", $".VCPCOOKIES={cookie}");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; MyApp/1.0)");
        }

        public class LoginResponse
        {
            public string UserId { get; set; }
            public string Name { get; set; }
            public string Cookie { get; set; }
        }

        public class ProgramSessionDetail
        {
            public string SessionTitle { get; set; }
            public string HtmlSummary { get; set; }
            public string SessionName { get; set; }
            public string Description { get; set; }
        }

        public class ProgramSessionBrief
        {
            [JsonProperty("Url")]
            public string Url { get; set; }

            [JsonProperty("SessionTitle")]
            public string SessionTitle { get; set; }
        }

        public static async Task<string?> LoginAndGetCookie(string email, string password)
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("user", email),
                new KeyValuePair<string, string>("password", password)
            });

            try
            {
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; MyApp/1.0)");

                var response = await client.PostAsync("https://cloud.visualcoaching2.com/api/2/Account/Logon", content);
                var responseContent = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Login Response: {responseContent}");

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine("Login failed with status code: " + response.StatusCode);
                    return null;
                }

                var loginResult = JsonConvert.DeserializeObject<LoginResponse>(responseContent);
                if (!string.IsNullOrEmpty(loginResult?.Cookie))
                {
                    Debug.WriteLine($"Login cookie from JSON: {loginResult.Cookie}");
                    return loginResult.Cookie;
                }

                if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
                {
                    foreach (var cookie in cookies)
                    {
                        if (cookie.StartsWith(".VCPCOOKIES"))
                        {
                            var cookieValue = cookie.Split(';')[0].Split('=')[1];
                            Debug.WriteLine($"Login cookie from header: {cookieValue}");
                            return cookieValue;
                        }
                    }
                }

                Debug.WriteLine("No cookie found in login response.");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Login error: {ex.Message}");
                return null;
            }
        }

        public static async Task<List<ProgramSessionBrief>> GetSessionsForDate(string cookie, string date)
        {
            try
            {
                SetupHttpClientHeaders(cookie);

                string url = $"https://cloud.visualcoaching2.com/Application/Program/?date={date}&current=true&version=2&today=true&format=Tablet&json=true&requireSortFilters=true&client=";

                var response = await client.GetAsync(url);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    Debug.WriteLine("Session expired detected in GetSessionsForDate.");
                    SessionManager.ClearCookie();
                    throw new UnauthorizedAccessException("Session expired, please login again.");
                }

                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Sessions JSON: {json}");

                try
                {
                    var sessions = JsonConvert.DeserializeObject<List<ProgramSessionBrief>>(json);
                    if (sessions != null)
                        return sessions;
                }
                catch
                {
                    var jobject = Newtonsoft.Json.Linq.JObject.Parse(json);
                    var sessionsToken = jobject["sessions"];

                    if (sessionsToken != null)
                    {
                        return sessionsToken.ToObject<List<ProgramSessionBrief>>() ?? new List<ProgramSessionBrief>();
                    }
                }

                return new List<ProgramSessionBrief>();
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetSessionsForDate error: {ex.Message}");
                return new List<ProgramSessionBrief>();
            }
        }

        public static async Task<ProgramSessionDetail?> GetSessionSummary(string cookie, string sessionUrl)
        {
            try
            {
                SetupHttpClientHeaders(cookie);

                var match = Regex.Match(sessionUrl, @"/Session/(\d+)\?week=(\d+)&day=(\d+)&session=(\d+)&i=(\d+)");
                if (!match.Success)
                {
                    Debug.WriteLine("Could not parse session URL");
                    return null;
                }

                string sessionId = match.Groups[1].Value;
                string week = match.Groups[2].Value;
                string day = match.Groups[3].Value;
                string session = match.Groups[4].Value;
                string i = match.Groups[5].Value;

                string key = $"{week}:{day}:{session}:{i}";
                string apiUrl = $"https://cloud.visualcoaching2.com/api/2/Program/Summary2/{sessionId}?key={key}";

                var response = await client.GetAsync(apiUrl);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    Debug.WriteLine("Session expired detected in GetSessionSummary.");
                    SessionManager.ClearCookie();
                    throw new UnauthorizedAccessException("Session expired, please login again.");
                }

                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Session summary JSON: {json}");

                var sessionDetail = JsonConvert.DeserializeObject<ProgramSessionDetail>(json);
                return sessionDetail;
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetSessionSummary error: {ex.Message}");
                return null;
            }
        }

        public static async Task<string> GetRawSessionHtml(string cookie, string sessionUrl)
        {
            try
            {
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Add("Cookie", $".VCPCOOKIES={cookie}");
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; MyApp/1.0)");

                string baseUrl = "https://cloud.visualcoaching2.com";
                string fullUrl = baseUrl + sessionUrl;

                var response = await client.GetAsync(fullUrl);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    Debug.WriteLine("Session expired detected in GetRawSessionHtml.");
                    SessionManager.ClearCookie();
                    return string.Empty;
                }

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"GetRawSessionHtml failed with status code: {response.StatusCode}");
                    return string.Empty;
                }

                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetRawSessionHtml error: {ex.Message}");
                return string.Empty;
            }
        }
    }
}
