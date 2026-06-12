using System;

namespace pjtSPEF.Services
{
    // Se lanza cuando no se puede operar Google Drive en nombre del docente
    // (sin sesión o sin refresh token). El mensaje es apto para mostrar al usuario.
    public class DriveNoAutorizadoException : Exception
    {
        public DriveNoAutorizadoException(string message) : base(message)
        {
        }
    }
}
