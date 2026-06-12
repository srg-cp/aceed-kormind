namespace pjtSPEF.Security
{
    // Nombres de los tipos de cookie y de los claims que comparten Startup y AccountController.
    public static class AuthClaims
    {
        public const string ApplicationCookie = "ApplicationCookie";
        public const string ExternalCookie = "ExternalCookie";

        public const string GoogleName = "urn:google:name";
        public const string RefreshToken = "urn:tokens:refresh";
    }
}
