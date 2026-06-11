using System.Linq;
using System.Web;
using pjtSPEF.Data;
using pjtSPEF.Models.Entities;

namespace pjtSPEF.Services
{
    public class FormsCurrentUserService : ICurrentUserService
    {
        private readonly SpefDbContext _db;

        public FormsCurrentUserService(SpefDbContext db)
        {
            _db = db;
        }

        public Usuario ObtenerUsuarioActual()
        {
            var context = HttpContext.Current;
            if (context == null || context.User == null || !context.User.Identity.IsAuthenticated)
                return null;

            // El name de la identidad es el email (lo setea AccountController al hacer login).
            var email = context.User.Identity.Name;
            if (string.IsNullOrEmpty(email))
                return null;

            return _db.Usuarios.FirstOrDefault(u => u.Email == email && u.Activo);
        }
    }
}
