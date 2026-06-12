using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using Microsoft.Owin.Security;
using pjtSPEF.Data;
using pjtSPEF.Models.Entities;
using pjtSPEF.Security;
using pjtSPEF.Services;

namespace pjtSPEF.Controllers
{
    // Autenticación real con Google OAuth (OWIN). El resto de la app solo conoce
    // ICurrentUserService; este controlador hace el handshake y firma la cookie de sesión.
    [AllowAnonymous]
    public class AccountController : Controller
    {
        private const string ProveedorGoogle = "Google";

        private IAuthenticationManager Authentication
        {
            get { return HttpContext.GetOwinContext().Authentication; }
        }

        [HttpGet]
        public ActionResult Login(string returnUrl)
        {
            if (User != null && User.Identity.IsAuthenticated)
                return RedirectToAction("Index", "Cursos");

            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        // Inicia el flujo: redirige a Google para que el docente autorice.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ExternalLogin(string returnUrl)
        {
            var redirectUri = Url.Action("ExternalLoginCallback", "Account", new { returnUrl });
            return new ChallengeResult(ProveedorGoogle, redirectUri);
        }

        // Google regresa aquí: se crea/actualiza el docente y se firma la cookie de la app.
        [HttpGet]
        public async Task<ActionResult> ExternalLoginCallback(string returnUrl)
        {
            var result = await Authentication.AuthenticateAsync(AuthClaims.ExternalCookie);
            if (result == null || result.Identity == null)
                return RedirectToAction("Login");

            var identity = result.Identity;
            var email = identity.FindFirst(ClaimTypes.Email)?.Value;
            var googleId = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var nombre = identity.FindFirst(AuthClaims.GoogleName)?.Value;
            var refreshToken = identity.FindFirst(AuthClaims.RefreshToken)?.Value;

            if (string.IsNullOrEmpty(email))
            {
                TempData["Error"] = "Google no devolvió un correo. Intenta de nuevo.";
                return RedirectToAction("Login");
            }

            using (var db = new SpefDbContext())
            {
                var usuario = db.Usuarios.FirstOrDefault(u => u.GoogleId == googleId)
                              ?? db.Usuarios.FirstOrDefault(u => u.Email == email);
                if (usuario == null)
                {
                    usuario = new Usuario { FechaCreacion = DateTime.UtcNow };
                    db.Usuarios.Add(usuario);
                }

                usuario.Email = email;
                usuario.GoogleId = googleId;
                usuario.Nombre = string.IsNullOrWhiteSpace(nombre) ? email : nombre;
                usuario.Activo = true;
                // Solo se sobrescribe si Google mandó uno nuevo (con prompt=consent siempre llega).
                if (!string.IsNullOrEmpty(refreshToken))
                    usuario.RefreshToken = TokenProtector.Proteger(refreshToken);

                db.SaveChanges();
            }

            Authentication.SignOut(AuthClaims.ExternalCookie);

            var appIdentity = new ClaimsIdentity(AuthClaims.ApplicationCookie);
            appIdentity.AddClaim(new Claim(ClaimTypes.Name, email));
            appIdentity.AddClaim(new Claim(ClaimTypes.Email, email));
            if (!string.IsNullOrEmpty(googleId))
                appIdentity.AddClaim(new Claim(ClaimTypes.NameIdentifier, googleId));
            Authentication.SignIn(new AuthenticationProperties { IsPersistent = false }, appIdentity);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction("Index", "Cursos");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Logout()
        {
            Authentication.SignOut(AuthClaims.ApplicationCookie, AuthClaims.ExternalCookie);
            return RedirectToAction("Login");
        }

        // Dispara el reto de autenticación de OWIN hacia el proveedor externo.
        private class ChallengeResult : HttpUnauthorizedResult
        {
            public ChallengeResult(string provider, string redirectUri)
            {
                Provider = provider;
                RedirectUri = redirectUri;
            }

            private string Provider { get; }
            private string RedirectUri { get; }

            public override void ExecuteResult(ControllerContext context)
            {
                var properties = new AuthenticationProperties { RedirectUri = RedirectUri };
                context.HttpContext.GetOwinContext().Authentication.Challenge(properties, Provider);
            }
        }
    }
}
