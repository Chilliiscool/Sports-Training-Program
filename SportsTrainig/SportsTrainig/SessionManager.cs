using Microsoft.Maui.Storage;

namespace SportsTraining.Services
{
    public static class SessionManager
    {
        private const string CookieKey = "VCP_Cookie";

        // Backing field for CurrentCookie
        private static string? currentCookie;

        public static string? CurrentCookie
        {
            get
            {
                if (currentCookie == null)
                {
                    currentCookie = Preferences.Get(CookieKey, null);
                }
                return currentCookie;
            }
            private set
            {
                currentCookie = value;
            }
        }

        public static void SaveCookie(string cookie)
        {
            CurrentCookie = cookie;
            Preferences.Set(CookieKey, cookie);
        }

        public static string? GetCookie()
        {
            return CurrentCookie;
        }

        public static bool IsLoggedIn => !string.IsNullOrEmpty(CurrentCookie);

        public static void ClearCookie()
        {
            CurrentCookie = null;
            Preferences.Remove(CookieKey);
        }
    }
}
