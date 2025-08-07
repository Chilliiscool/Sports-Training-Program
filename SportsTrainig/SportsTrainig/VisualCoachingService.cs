// Module Name: VisualCoachingService
// Author: Kye Franken 
// Date Created: 23 / 07 / 2025
// Date Modified: 06 / 08 / 2025
// Description: Provides methods to interact with the Visual Coaching API including login, 
// fetching sessions for a date, session summaries, and raw session HTML content. 
// Manages cookies and handles session expiration.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using Newtonsoft.Json;

namespace SportsTraining.Services
{
    public static class VisualCoachingService
    {
        // Shared CookieContainer and HttpClient with cookie handling enabled
        private static readonly CookieContainer cookieContainer = new();
        private static readonly HttpClientHandler handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = cookieContainer,
        };
        private static readonly HttpClient client = new HttpClient(handler);

        // Model for login response JSON
        public class LoginResponse
        {
            public string UserId { get; set; }
            public string Name { get; set; }
            public string Cookie { get; set; }
        }

        // Model for detailed session info returned by API
        public class ProgramSessionDetail
        {
            public string SessionTitle { get; set; }
            public string HtmlSummary { get; set; }
            public string SessionName { get; set; }
            public string Description { get; set; }
        }

        // Model for brief session info returned by API
        public class ProgramSessionBrief
        {
            [JsonProperty("Url")]
            public string Url { get; set; }

            [JsonProperty("SessionTitle")]
            public string SessionTitle { get; set; }
        }

        // Login method: sends credentials and extracts cookie from response JSON or headers
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

                // Try parse cookie from JSON response
                var loginResult = JsonConvert.DeserializeObject<LoginResponse>(responseContent);
                if (!string.IsNullOrEmpty(loginResult?.Cookie))
                {
                    Uri baseUri = new Uri("https://cloud.visualcoaching2.com");
                    cookieContainer.SetCookies(baseUri, $".VCPCOOKIES={loginResult.Cookie}");
                    Preferences.Set("VCP_Cookie", loginResult.Cookie);
                    Debug.WriteLine($"Login cookie from JSON: {loginResult.Cookie}");
                    return loginResult.Cookie;
                }

                // Try parse cookie from response headers if JSON did not contain it
                if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
                {
                    foreach (var cookie in cookies)
                    {
                        if (cookie.StartsWith(".VCPCOOKIES"))
                        {
                            var cookieValue = cookie.Split(';')[0].Split('=')[1];
                            Uri baseUri = new Uri("https://cloud.visualcoaching2.com");
                            cookieContainer.SetCookies(baseUri, $".VCPCOOKIES={cookieValue}");
                            Preferences.Set("VCP_Cookie", cookieValue);
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

        // Retrieves a list of sessions for a given date, handling unauthorized responses
        public static async Task<List<ProgramSessionBrief>> GetSessionsForDate(string cookie, string date)
        {
            try
            {
                // URL for sessions list with date and parameters
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
                    // Try deserialize directly as list
                    var sessions = JsonConvert.DeserializeObject<List<ProgramSessionBrief>>(json);
                    if (sessions != null)
                        return sessions;
                }
                catch
                {
                    // Fallback: parse JSON object and extract 'sessions' token
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

        // Retrieves detailed session summary JSON by parsing the session URL to get keys
        public static async Task<ProgramSessionDetail?> GetSessionSummary(string cookie, string sessionUrl)
        {
            try
            {
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

        // Retrieves raw HTML content for a given session URL, handling session expiration
        public static async Task<string> GetRawSessionHtml(string cookie, string sessionUrl)
        {
            try
            {
                string baseUrl = "https://cloud.visualcoaching2.com";
                string fullUrl = baseUrl + sessionUrl;

                Debug.WriteLine($"Fetching session HTML from: {fullUrl}");
                Debug.WriteLine($"Using cookie: {cookie}");

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

                var content = await response.Content.ReadAsStringAsync();

                // Optional debug: output start of content for verification
                Debug.WriteLine($"Response content starts with: {content.Substring(0, Math.Min(200, content.Length))}");

                return content;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetRawSessionHtml error: {ex.Message}");
                return string.Empty;
            }
        }

    }
}
