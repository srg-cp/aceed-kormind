using System.Security.Cryptography;
using System.Text;

namespace pjtSPEF.Services
{
    // Cifra/descifra el refresh token de Google antes de guardarlo en la columna
    // refresh_token (VARBINARY). Usa DPAPI con alcance de máquina: solo este servidor
    // puede descifrarlo. En despliegue multi-servidor habría que cambiar a una clave compartida.
    public static class TokenProtector
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ACEED.refresh_token.v1");

        public static byte[] Proteger(string token)
        {
            if (string.IsNullOrEmpty(token))
                return null;

            var datos = Encoding.UTF8.GetBytes(token);
            return ProtectedData.Protect(datos, Entropy, DataProtectionScope.LocalMachine);
        }

        public static string Desproteger(byte[] cifrado)
        {
            if (cifrado == null || cifrado.Length == 0)
                return null;

            var datos = ProtectedData.Unprotect(cifrado, Entropy, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(datos);
        }
    }
}
