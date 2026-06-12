using System.Linq;
using System.Web;
using pjtSPEF.Data;
using pjtSPEF.Models.Entities;

namespace pjtSPEF.Services
{
    // Resuelve el docente autenticado a partir de la cookie de sesión (OWIN),
    // cuyo Name es el email obtenido del login con Google.
    public class GoogleCurrentUserService
    {
        private readonly SpefDbContext _db;

        public GoogleCurrentUserService(SpefDbContext db)
        {
            _db = db;
        }

        public Usuario ObtenerUsuarioActual()
        {
            var context = HttpContext.Current;
            if (context == null || context.User == null || !context.User.Identity.IsAuthenticated)
                return null;

            // El Name de la identidad es el email (lo setea AccountController al firmar la cookie tras el login con Google).
            var email = context.User.Identity.Name;
            if (string.IsNullOrEmpty(email))
                return null;

            return _db.Usuarios.FirstOrDefault(u => u.Email == email && u.Activo);
        }
    }
}
