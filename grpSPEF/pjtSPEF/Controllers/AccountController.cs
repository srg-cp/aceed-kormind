using System.Linq;
using System.Web.Mvc;
using System.Web.Security;
using pjtSPEF.Data;
using pjtSPEF.Models.Entities;

namespace pjtSPEF.Controllers
{
    // Autenticación de desarrollo: un solo botón que inicia sesión con el usuario dev.
    // Cuando haya credenciales de Google, este controlador se sustituye por el flujo
    // OAuth de OWIN; el resto de la app solo conoce ICurrentUserService.
    [AllowAnonymous]
    public class AccountController : Controller
    {
        private const string EmailDev = "dev@spef.local";

        [HttpGet]
        public ActionResult Login(string returnUrl)
        {
            if (User != null && User.Identity.IsAuthenticated)
                return RedirectToAction("Index", "Cursos");

            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult LoginDev(string returnUrl)
        {
            using (var db = new SpefDbContext())
            {
                // Defensa por si no se ejecutó el seed de database/.
                var usuario = db.Usuarios.FirstOrDefault(u => u.Email == EmailDev);
                if (usuario == null)
                {
                    usuario = new Usuario
                    {
                        Email = EmailDev,
                        Nombre = "Docente (modo desarrollo)",
                        Activo = true,
                        FechaCreacion = System.DateTime.UtcNow
                    };
                    db.Usuarios.Add(usuario);
                    db.SaveChanges();
                }
            }

            FormsAuthentication.SetAuthCookie(EmailDev, createPersistentCookie: false);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);
            return RedirectToAction("Index", "Cursos");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Logout()
        {
            FormsAuthentication.SignOut();
            return RedirectToAction("Login");
        }
    }
}
