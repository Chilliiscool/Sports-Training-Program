// Module Name: VisualCoachingService
// Author: Kye Franken 
// Date Created: 23 / 07 / 2025
// Date Modified: 11 / 08 / 2025
// Description: Provides methods to interact with the Visual Coaching API including login, 
// fetching sessions for a date, session summaries, and raw session HTML content. 
// Manages cookies and handles session expiration, with explicit cookie headers and redirect detection to avoid false sign-outs.

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

        // IMPORTANT: Do NOT follow redirects — detect 302 to /Account/Logon ourselves.
        private static readonly HttpClientHandler handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = cookieContainer,
            AllowAutoRedirect = false
        };

        private static readonly HttpClient client = new HttpClient(handler);
        private static readonly Uri BaseUri = new Uri("https://cloud.visualcoaching2.com");

        private static void ClearCookiesForBase()
        {
            var coll = cookieContainer.GetCookies(BaseUri);
            foreach (Cookie c in coll) c.Expired = true;
        }

        // Helper to set the cookie container with current cookie from SessionManager
        private static void SetCookieFromSession()
        {
            string? cookie = SessionManager.GetCookie();
            if (!string.IsNullOrEmpty(cookie))
            {
                ClearCookiesForBase();
                cookieContainer.SetCookies(BaseUri, $".VCPCOOKIES={cookie}");
                Debug.WriteLine($"[VCS] Cookie set in CookieContainer: {cookie}");
            }
        }

        // Helper to also force-add Cookie header (some endpoints are picky)
        private static void ApplyCookieHeader(string cookie)
        {
            client.DefaultRequestHeaders.Remove("Cookie");
            if (!string.IsNullOrEmpty(cookie))
                client.DefaultRequestHeaders.Add("Cookie", $".VCPCOOKIES={cookie}");

            // Always set a UA
            client.DefaultRequestHeaders.UserAgent.Clear();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; SportsTrainingApp/1.0)");
        }

        private static bool IsRedirectToLogin(HttpResponseMessage response)
        {
            if ((int)response.StatusCode == 302 ||
                response.StatusCode == HttpStatusCode.Redirect ||
                response.StatusCode == HttpStatusCode.RedirectKeepVerb ||
                response.StatusCode == HttpStatusCode.RedirectMethod)
            {
                var loc = response.Headers.Location?.ToString() ?? "";
                if (loc.Contains("/Account/Logon", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine("[VCS] Detected 302 redirect to login.");
                    return true;
                }
            }
            return false;
        }

        private static bool HtmlLooksLikeLogin(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return false;
            return html.IndexOf("Account/Logon", StringComparison.OrdinalIgnoreCase) >= 0
                || html.IndexOf("name=\"user\"", StringComparison.OrdinalIgnoreCase) >= 0
                || html.IndexOf("name=\"password\"", StringComparison.OrdinalIgnoreCase) >= 0;
        }

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
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; SportsTrainingApp/1.0)");

                var response = await client.PostAsync("https://cloud.visualcoaching2.com/api/2/Account/Logon", content);
                var responseContent = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"[VCS] Login Response: {responseContent}");

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine("[VCS] Login failed with status code: " + response.StatusCode);
                    return null;
                }

                // Try parse cookie from JSON response
                var loginResult = JsonConvert.DeserializeObject<LoginResponse>(responseContent);
                if (!string.IsNullOrEmpty(loginResult?.Cookie))
                {
                    ClearCookiesForBase();
                    cookieContainer.SetCookies(BaseUri, $".VCPCOOKIES={loginResult.Cookie}");
                    SessionManager.SaveCookie(loginResult.Cookie);
                    Debug.WriteLine($"[VCS] Login cookie from JSON: {loginResult.Cookie}");
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
                            ClearCookiesForBase();
                            cookieContainer.SetCookies(BaseUri, $".VCPCOOKIES={cookieValue}");
                            SessionManager.SaveCookie(cookieValue);
                            Debug.WriteLine($"[VCS] Login cookie from header: {cookieValue}");
                            return cookieValue;
                        }
                    }
                }

                Debug.WriteLine("[VCS] No cookie found in login response.");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VCS] Login error: {ex.Message}");
                return null;
            }
        }

        // Retrieves a list of sessions for a given date, handling unauthorized responses
        public static async Task<List<ProgramSessionBrief>> GetSessionsForDate(string cookie, string date)
        {
            try
            {
                SetCookieFromSession();
                ApplyCookieHeader(cookie);

                string url = $"https://cloud.visualcoaching2.com/Application/Program/?date={date}&current=true&version=2&today=true&format=Tablet&json=true&requireSortFilters=true&client=";

                var response = await client.GetAsync(url);

                if (IsRedirectToLogin(response) || response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    Debug.WriteLine("[VCS] Session expired detected in GetSessionsForDate.");
                    SessionManager.ClearCookie();
                    throw new UnauthorizedAccessException("Session expired, please login again.");
                }

                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"[VCS] Sessions JSON: {json}");

                try
                {
                    var sessions = JsonConvert.DeserializeObject<List<ProgramSessionBrief>>(json);
                    if (sessions != null) return sessions;
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
                Debug.WriteLine($"[VCS] GetSessionsForDate error: {ex.Message}");
                return new List<ProgramSessionBrief>();
            }
        }

        // Retrieves detailed session summary JSON by parsing the session URL to get keys
        public static async Task<ProgramSessionDetail?> GetSessionSummary(string cookie, string sessionUrl)
        {
            try
            {
                SetCookieFromSession();
                ApplyCookieHeader(cookie);

                var match = Regex.Match(sessionUrl, @"/Session/(\d+)\?week=(\d+)&day=(\d+)&session=(\d+)&i=(\d+)");
                if (!match.Success)
                {
                    Debug.WriteLine("[VCS] Could not parse session URL");
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

                if (IsRedirectToLogin(response) || response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    Debug.WriteLine("[VCS] Session expired detected in GetSessionSummary.");
                    SessionManager.ClearCookie();
                    throw new UnauthorizedAccessException("Session expired, please login again.");
                }

                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"[VCS] Session summary JSON: {json}");

                var sessionDetail = JsonConvert.DeserializeObject<ProgramSessionDetail>(json);
                return sessionDetail;
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VCS] GetSessionSummary error: {ex.Message}");
                return null;
            }
        }

        // Retrieves raw HTML content for a given session URL, handling session expiration and silent redirects
        public static async Task<string> GetRawSessionHtml(string cookie, string sessionUrl)
        {
            try
            {
                SetCookieFromSession();
                ApplyCookieHeader(cookie);

                var fullUrl = new Uri(BaseUri, sessionUrl).ToString();

                Debug.WriteLine($"[VCS] Fetching session HTML from: {fullUrl}");
                Debug.WriteLine($"[VCS] Using cookie: {cookie}");

                var response = await client.GetAsync(fullUrl);

                // Detect redirect to login (302) or 401 Unauthorized
                if (IsRedirectToLogin(response) || response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    Debug.WriteLine("[VCS] Unauthorized when fetching session HTML.");
                    // Let caller decide whether to retry/clear cookie.
                    return string.Empty;
                }

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[VCS] GetRawSessionHtml failed with status code: {response.StatusCode}");
                    return string.Empty;
                }

                var content = await response.Content.ReadAsStringAsync();

                // Defensive: if server served the login page with 200
                if (HtmlLooksLikeLogin(content))
                {
                    Debug.WriteLine("[VCS] Login form detected in 200 HTML.");
                    return string.Empty;
                }

                Debug.WriteLine($"[VCS] Response content starts with: {content.Substring(0, Math.Min(200, content.Length))}");
                return content;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VCS] GetRawSessionHtml error: {ex.Message}");
                return string.Empty;
            }
        }
    }
}
