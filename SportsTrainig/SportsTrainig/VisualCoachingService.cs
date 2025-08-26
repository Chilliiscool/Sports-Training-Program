// Module Name: VisualCoachingService
// Author: Kye Franken 
// Date Created: 23 / 07 / 2025
// Date Modified: 15 / 08 / 2025
// Description: Provides methods to interact with the Visual Coaching API including login, 
// fetching sessions for a date, session summaries, and raw session HTML content. 
// Manages cookies and handles session expiration, with explicit cookie headers and redirect detection to avoid false sign-outs.
// Update (15/08): GetSessionSummary now parses query params by name (order-independent).

using Microsoft.Maui.Storage;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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
            [JsonProperty("ClientName")]
            public string ClientName { get; set; }
            [JsonProperty("ClientGroup")]
            public string ClientGroup { get; set; }
            [JsonProperty("Week")]
            public int Week { get; set; }
            [JsonProperty("Day")]
            public int Day { get; set; }
            [JsonProperty("DateStart")]
            public string DateStart { get; set; }

            [JsonProperty("DateEnd")]
            public string DateEnd { get; set; }
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

        // Retrieves detailed session summary JSON — now robust to ANY query param order.
        public static async Task<ProgramSessionDetail?> GetSessionSummary(string cookie, string sessionUrl)
        {
            try
            {
                SetCookieFromSession();
                ApplyCookieHeader(cookie);

                // Build absolute URL first (handles relative "/Application/Program/Session/...")
                var full = new Uri(BaseUri, sessionUrl);

                // 1) Get the numeric SessionId from the path: /Application/Program/Session/{id}
                //    Be lenient: just grab the last number in the path.
                string path = full.AbsolutePath; // e.g. /Application/Program/Session/1474814
                var idMatch = Regex.Match(path, @"(\d+)$");
                if (!idMatch.Success)
                {
                    Debug.WriteLine("[VCS] Could not parse SessionId from path: " + path);
                    return null;
                }
                string sessionId = idMatch.Value;

                // 2) Parse the query into a case-insensitive map (order independent)
                var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var query = full.Query; // includes leading '?'
                if (!string.IsNullOrEmpty(query))
                {
                    foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        int eq = part.IndexOf('=');
                        if (eq <= 0) continue;
                        var key = part[..eq];
                        var val = part[(eq + 1)..];
                        kv[key] = val;
                    }
                }

                // 3) Pull required values; bail if any are missing
                if (!kv.TryGetValue("week", out var week) ||
                    !kv.TryGetValue("day", out var day) ||
                    !kv.TryGetValue("session", out var sess) ||
                    !kv.TryGetValue("i", out var i))
                {
                    Debug.WriteLine("[VCS] Missing one of week/day/session/i in query: " + full.Query);
                    return null;
                }

                // 4) Build Summary2 URL
                string keyStr = $"{week}:{day}:{sess}:{i}";
                string apiUrl = $"https://cloud.visualcoaching2.com/api/2/Program/Summary2/{sessionId}?key={keyStr}";

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
        // Models for diary functionality
        public class UserInfo
        {
            public string DisplayName { get; set; }
            public int PerformanceDiaryId { get; set; }
            public int UserId { get; set; }
            public int WellnessDiaryId { get; set; }
        }

        public class DiaryEntry
        {
            // The structure will depend on your diary setup
            // Add properties as needed based on your diary form structure
            public string Date { get; set; }
            public Dictionary<string, object> Fields { get; set; } = new Dictionary<string, object>();
        }

        // Add these methods to your VisualCoachingService class:

        /// <summary>
        /// Gets user information including diary IDs for the specified email address
        /// </summary>
        /// <param name="cookie">Authentication cookie</param>
        /// <param name="email">User's email address</param>
        /// <returns>UserInfo object with diary IDs, or null if not found</returns>
        public static async Task<UserInfo?> GetUserInfo(string cookie, string email)
        {
            try
            {
                SetCookieFromSession();
                ApplyCookieHeader(cookie);

                string url = $"https://cloud.visualcoaching2.com/Application/Client/GetUserInfo?email={Uri.EscapeDataString(email)}";

                Debug.WriteLine($"[VCS] Getting user info for: {email}");
                Debug.WriteLine($"[VCS] URL: {url}");

                var response = await client.GetAsync(url);

                if (IsRedirectToLogin(response) || response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    Debug.WriteLine("[VCS] Session expired detected in GetUserInfo.");
                    SessionManager.ClearCookie();
                    throw new UnauthorizedAccessException("Session expired, please login again.");
                }

                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"[VCS] User info JSON: {json}");

                if (string.IsNullOrWhiteSpace(json))
                {
                    Debug.WriteLine("[VCS] Empty response from GetUserInfo");
                    return null;
                }

                var userInfo = JsonConvert.DeserializeObject<UserInfo>(json);
                return userInfo;
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VCS] GetUserInfo error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets diary data for a specific user, date, and program
        /// </summary>
        /// <param name="cookie">Authentication cookie</param>
        /// <param name="diaryId">Diary ID (PerformanceDiaryId or WellnessDiaryId)</param>
        /// <param name="date">Date in yyyy-MM-dd format</param>
        /// <param name="userId">User ID (not email)</param>
        /// <param name="programId">Program ID</param>
        /// <param name="week">Week number (0-based)</param>
        /// <param name="day">Day number (1-7)</param>
        /// <param name="session">Session number (usually 0)</param>
        /// <param name="i">Session index (usually 0)</param>
        /// <returns>Raw diary JSON data</returns>
        public static async Task<string?> GetDiaryData(string cookie, int diaryId, string date, int userId,
    int programId, int week, int day, int session = 0, int i = 0)
        {
            try
            {
                SetCookieFromSession();
                ApplyCookieHeader(cookie);

                // ✅ FIXED: Use 6-part program key format (add extra :000 at the end)
                string programKey = $"{programId}:{week:000}:{day:000}:{session:000}:{i:000}:000";

                string url = $"https://cloud.visualcoaching2.com/api/2/Form/GetForm/{diaryId}" +
                            $"?date={date}" +
                            $"&userId={userId}" +
                            $"&programKey={Uri.EscapeDataString(programKey)}" +
                            $"&matchTemplateId=false" +
                            $"&createNew=false";

                Debug.WriteLine($"[VCS] Getting diary data:");
                Debug.WriteLine($"[VCS] Diary ID: {diaryId}");
                Debug.WriteLine($"[VCS] Date: {date}");
                Debug.WriteLine($"[VCS] User ID: {userId}");
                Debug.WriteLine($"[VCS] Program Key: {programKey}"); // Now shows 6 parts
                Debug.WriteLine($"[VCS] URL: {url}");

                var response = await client.GetAsync(url);

                if (IsRedirectToLogin(response) || response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    Debug.WriteLine("[VCS] Session expired detected in GetDiaryData.");
                    SessionManager.ClearCookie();
                    throw new UnauthorizedAccessException("Session expired, please login again.");
                }

                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"[VCS] Diary data JSON length: {json?.Length ?? 0}");

                return json;
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VCS] GetDiaryData error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Convenience method to get diary data by email address
        /// </summary>
        /// <param name="cookie">Authentication cookie</param>
        /// <param name="email">User's email address</param>
        /// <param name="date">Date in yyyy-MM-dd format</param>
        /// <param name="programId">Program ID</param>
        /// <param name="week">Week number (0-based)</param>
        /// <param name="day">Day number (1-7)</param>
        /// <param name="diaryType">Type of diary: "performance" or "wellness"</param>
        /// <param name="session">Session number (usually 0)</param>
        /// <param name="i">Session index (usually 0)</param>
        /// <returns>Raw diary JSON data</returns>
        public static async Task<string?> GetDiaryDataByEmail(string cookie, string email, string date,
            int programId, int week, int day, string diaryType = "performance", int session = 0, int i = 0)
        {
            try
            {
                // First, get user info to retrieve user ID and diary IDs
                var userInfo = await GetUserInfo(cookie, email);
                if (userInfo == null)
                {
                    Debug.WriteLine($"[VCS] Could not find user info for email: {email}");
                    return null;
                }

                // Select the appropriate diary ID based on type
                int diaryId = diaryType.ToLower() switch
                {
                    "wellness" => userInfo.WellnessDiaryId,
                    "performance" => userInfo.PerformanceDiaryId,
                    _ => userInfo.PerformanceDiaryId // Default to performance diary
                };

                if (diaryId <= 0)
                {
                    Debug.WriteLine($"[VCS] No {diaryType} diary found for user {email}");
                    return null;
                }

                Debug.WriteLine($"[VCS] Found {diaryType} diary ID {diaryId} for user {userInfo.DisplayName} ({userInfo.UserId})");

                // Get the diary data
                return await GetDiaryData(cookie, diaryId, date, userInfo.UserId, programId, week, day, session, i);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VCS] GetDiaryDataByEmail error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Parse diary JSON into a more usable format
        /// </summary>
        /// <param name="diaryJson">Raw JSON from GetDiaryData</param>
        /// <returns>Parsed diary entry or null</returns>
        public static DiaryEntry? ParseDiaryData(string? diaryJson)
        {
            if (string.IsNullOrWhiteSpace(diaryJson))
                return null;

            try
            {
                // Parse the JSON - structure will depend on your diary setup
                var jsonObject = Newtonsoft.Json.Linq.JObject.Parse(diaryJson);

                var entry = new DiaryEntry();

                // Extract common fields - adjust based on your diary structure
                if (jsonObject["date"]?.ToString() is string dateStr)
                    entry.Date = dateStr;

                // Extract all other fields into the Fields dictionary
                foreach (var prop in jsonObject.Properties())
                {
                    if (prop.Name != "date") // Skip date as it's already extracted
                    {
                        entry.Fields[prop.Name] = prop.Value?.ToObject<object>();
                    }
                }

                return entry;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VCS] ParseDiaryData error: {ex.Message}");
                return null;
            }
        }
        // Replace your SubmitDiaryData method in VisualCoachingService.cs with this corrected version:

        public static async Task<bool> SubmitDiaryData(string cookie, int diaryId, int userId, string date,
    int programId, int week, int day, Dictionary<string, object> diaryData, int session = 0, int i = 0)
        {
            try
            {
                SetCookieFromSession();
                ApplyCookieHeader(cookie);

                // ✅ FIXED: Use 6-part program key format
                string programKey = $"{programId}:{week:000}:{day:000}:{session:000}:{i:000}:000";

                var formData = new Dictionary<string, object>
                {
                    ["date"] = date,
                    ["userId"] = userId,
                    ["programKey"] = programKey, // Now 6 parts
                    ["diaryId"] = diaryId
                };

                // Add all diary fields
                foreach (var item in diaryData)
                {
                    formData[item.Key] = item.Value;
                }

                var jsonContent = JsonConvert.SerializeObject(formData);
                var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

                string submitUrl = $"https://cloud.visualcoaching2.com/api/2/Form/SubmitForm";

                Debug.WriteLine($"[VCS] Submitting diary data to: {submitUrl}");
                Debug.WriteLine($"[VCS] Program Key: {programKey}"); // Now 6 parts
                Debug.WriteLine($"[VCS] Data: {jsonContent}");

                var response = await client.PostAsync(submitUrl, content);

                if (IsRedirectToLogin(response) || response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    Debug.WriteLine("[VCS] Session expired detected in SubmitDiaryData.");
                    SessionManager.ClearCookie();
                    throw new UnauthorizedAccessException("Session expired, please login again.");
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"[VCS] Diary submit response: {responseContent}");
                Debug.WriteLine($"[VCS] Status code: {response.StatusCode}");

                return response.IsSuccessStatusCode;
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VCS] SubmitDiaryData error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get diary form template/structure for a specific diary
        /// </summary>
        /// <param name="cookie">Authentication cookie</param>
        /// <param name="diaryId">Diary ID</param>
        /// <returns>Form template JSON</returns>
        public static async Task<string?> GetDiaryTemplate(string cookie, int diaryId)
        {
            try
            {
                SetCookieFromSession();
                ApplyCookieHeader(cookie);

                string url = $"https://cloud.visualcoaching2.com/api/2/Form/GetTemplate/{diaryId}";

                var response = await client.GetAsync(url);

                if (IsRedirectToLogin(response) || response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    Debug.WriteLine("[VCS] Session expired detected in GetDiaryTemplate.");
                    SessionManager.ClearCookie();
                    throw new UnauthorizedAccessException("Session expired, please login again.");
                }

                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();

                Debug.WriteLine($"[VCS] Diary template: {json}");
                return json;
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VCS] GetDiaryTemplate error: {ex.Message}");
                return null;
            }
        }

        // Models for diary form fields
        public class DiaryField
        {
            public string Name { get; set; } = "";
            public string Label { get; set; } = "";
            public string Type { get; set; } = ""; // text, number, dropdown, rating, etc.
            public List<string> Options { get; set; } = new List<string>();
            public bool Required { get; set; }
            public object? Value { get; set; }
            public string Placeholder { get; set; } = "";
            public int? MinValue { get; set; }
            public int? MaxValue { get; set; }
        }

        public class DiaryForm
        {
            public int DiaryId { get; set; }
            public string Title { get; set; } = "";
            public string Type { get; set; } = ""; // performance, wellness
            public List<DiaryField> Fields { get; set; } = new List<DiaryField>();
            public string Date { get; set; } = "";
            public int UserId { get; set; }
            public string ProgramKey { get; set; } = "";
        }

        /// <summary>
        /// Parse diary template into a structured form
        /// </summary>
        /// <param name="templateJson">Template JSON from GetDiaryTemplate</param>
        /// <returns>Structured diary form</returns>
        public static DiaryForm? ParseDiaryTemplate(string? templateJson)
        {
            if (string.IsNullOrWhiteSpace(templateJson))
                return null;

            try
            {
                var jsonObject = Newtonsoft.Json.Linq.JObject.Parse(templateJson);

                var form = new DiaryForm
                {
                    DiaryId = jsonObject["diaryId"]?.Value<int>() ?? 0,
                    Title = jsonObject["title"]?.Value<string>() ?? "Diary",
                    Type = jsonObject["type"]?.Value<string>() ?? "unknown"
                };

                // Parse fields array
                var fieldsArray = jsonObject["fields"] as Newtonsoft.Json.Linq.JArray;
                if (fieldsArray != null)
                {
                    foreach (var fieldToken in fieldsArray)
                    {
                        var field = new DiaryField
                        {
                            Name = fieldToken["name"]?.Value<string>() ?? "",
                            Label = fieldToken["label"]?.Value<string>() ?? "",
                            Type = fieldToken["type"]?.Value<string>() ?? "text",
                            Required = fieldToken["required"]?.Value<bool>() ?? false,
                            Placeholder = fieldToken["placeholder"]?.Value<string>() ?? "",
                            MinValue = fieldToken["minValue"]?.Value<int?>(),
                            MaxValue = fieldToken["maxValue"]?.Value<int?>()
                        };

                        // Parse options for dropdown/radio fields
                        var optionsArray = fieldToken["options"] as Newtonsoft.Json.Linq.JArray;
                        if (optionsArray != null)
                        {
                            field.Options = optionsArray.Select(o => o.Value<string>() ?? "").ToList();
                        }

                        form.Fields.Add(field);
                    }
                }

                return form;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VCS] ParseDiaryTemplate error: {ex.Message}");
                return null;
            }
        }
        public static async Task<string> GetRawApiCall(string cookie, string url)
        {
            try
            {
                SetCookieFromSession();
                ApplyCookieHeader(cookie);

                var response = await client.GetAsync(url);

                if (IsRedirectToLogin(response) || response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    Debug.WriteLine("[VCS] Session expired detected in GetRawApiCall.");
                    SessionManager.ClearCookie();
                    throw new UnauthorizedAccessException("Session expired, please login again.");
                }

                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();

                Debug.WriteLine($"[VCS] GetRawApiCall response length: {content?.Length ?? 0}");
                return content ?? "";
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VCS] GetRawApiCall error: {ex.Message}");
                return "";
            }
        }
    }
}
