using System.Configuration;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Owin;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.Cookies;
using Microsoft.Owin.Security.Google;
using Owin;
using pjtSPEF.Security;

[assembly: OwinStartup(typeof(pjtSPEF.Startup))]

namespace pjtSPEF
{
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            // Cookie de sesión de la app. La identidad firmada aquí es la que ven los controladores
            // (HttpContext.User); su Name es el email del docente, igual que antes.
            app.UseCookieAuthentication(new CookieAuthenticationOptions
            {
                AuthenticationType = AuthClaims.ApplicationCookie,
                LoginPath = new PathString("/Account/Login"),
                ExpireTimeSpan = System.TimeSpan.FromHours(8),
                SlidingExpiration = true
            });

            // Cookie temporal para el handshake con Google (la consume el callback y se descarta).
            app.UseCookieAuthentication(new CookieAuthenticationOptions
            {
                AuthenticationType = AuthClaims.ExternalCookie,
                AuthenticationMode = AuthenticationMode.Passive,
                CookieName = ".AspNet.ExternalCookie",
                ExpireTimeSpan = System.TimeSpan.FromMinutes(5)
            });
            app.SetDefaultSignInAsAuthenticationType(AuthClaims.ExternalCookie);

            var google = new GoogleOAuth2AuthenticationOptions
            {
                ClientId = ConfigurationManager.AppSettings["GoogleAuth:ClientId"],
                ClientSecret = ConfigurationManager.AppSettings["GoogleAuth:ClientSecret"],
                SignInAsAuthenticationType = AuthClaims.ExternalCookie,
                Provider = new GoogleOAuth2AuthenticationProvider
                {
                    // access_type=offline + prompt=consent garantizan un refresh token en cada login,
                    // necesario para que la app use Drive en nombre del docente (incluso en background).
                    OnApplyRedirect = context =>
                    {
                        context.Response.Redirect(context.RedirectUri + "&access_type=offline&prompt=consent");
                    },
                    OnAuthenticated = context =>
                    {
                        context.Identity.AddClaim(new Claim(AuthClaims.GoogleName, context.Name ?? string.Empty));
                        if (!string.IsNullOrEmpty(context.Email))
                            context.Identity.AddClaim(new Claim(ClaimTypes.Email, context.Email));
                        if (!string.IsNullOrEmpty(context.RefreshToken))
                            context.Identity.AddClaim(new Claim(AuthClaims.RefreshToken, context.RefreshToken));
                        return Task.CompletedTask;
                    }
                }
            };
            google.Scope.Add("email");
            google.Scope.Add("profile");
            google.Scope.Add("https://www.googleapis.com/auth/drive.file");
            // Sheets: la jerarquía y las notas viven en un Spreadsheet por docente (SpefSheetStore).
            google.Scope.Add("https://www.googleapis.com/auth/spreadsheets");

            app.UseGoogleAuthentication(google);
        }
    }
}
