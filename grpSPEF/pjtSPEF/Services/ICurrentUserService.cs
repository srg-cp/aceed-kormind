using pjtSPEF.Models.Entities;

namespace pjtSPEF.Services
{
    // Costura de autenticación: hoy resuelve el usuario desde la cookie de Forms
    // Authentication (modo desarrollo); cuando llegue Google OAuth/OWIN solo cambia
    // la implementación, no los controladores.
    public interface ICurrentUserService
    {
        // Devuelve el usuario autenticado o null si no hay sesión válida.
        Usuario ObtenerUsuarioActual();
    }
}
