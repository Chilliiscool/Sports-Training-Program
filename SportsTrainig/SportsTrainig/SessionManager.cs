using Microsoft.Maui.Storage;

namespace SportsTraining.Services
{
    public static class SessionManager
    {
        private const string CookieKey = "VCP_Cookie";

        public static void SaveCookie(string cookie)
        {
            Preferences.Set(CookieKey, cookie);
        }

        public static string? GetCookie()
        {
            return Preferences.Get(CookieKey, null);
        }

        public static bool IsLoggedIn => !string.IsNullOrEmpty(GetCookie());

        public static void ClearCookie()
        {
            Preferences.Remove(CookieKey);
        }
    }
}
