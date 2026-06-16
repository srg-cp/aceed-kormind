using System.Web;
using pjtSPEF.Data;
using pjtSPEF.Models.Entities;

namespace pjtSPEF.Services
{
    // Resuelve el docente autenticado a partir de la cookie de sesión (OWIN),
    // cuyo Name es el email obtenido del login con Google. Los datos del docente
    // (incluido el refresh token cifrado) viven en el almacén local, no en Sheets.
    public class GoogleCurrentUserService
    {
        private readonly LocalUserStore _store;

        public GoogleCurrentUserService() : this(new LocalUserStore())
        {
        }

        public GoogleCurrentUserService(LocalUserStore store)
        {
            _store = store;
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

            var usuario = _store.PorEmail(email);
            return usuario != null && usuario.Activo ? usuario : null;
        }
    }
}
