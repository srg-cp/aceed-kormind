using System;

namespace pjtSPEF.Services
{
    // Motor de IA con el que se califica una entrega. Se elige por entrega
    // (cada botón "Calificar con ..." manda el valor correspondiente).
    public enum ProveedorIa
    {
        Gemini,
        Claude
    }

    public static class ProveedorIaExtensions
    {
        // Convierte el valor que llega del formulario ("gemini" / "claude") al enum.
        // Cualquier valor desconocido o vacío cae en Gemini (el proveedor por defecto).
        public static ProveedorIa Parsear(string valor)
        {
            if (!string.IsNullOrWhiteSpace(valor) &&
                valor.Trim().Equals("claude", StringComparison.OrdinalIgnoreCase))
                return ProveedorIa.Claude;

            return ProveedorIa.Gemini;
        }

        // Nombre legible para mostrar en mensajes al usuario.
        public static string Nombre(this ProveedorIa proveedor)
        {
            return proveedor == ProveedorIa.Claude ? "Claude" : "Gemini";
        }
    }
}
