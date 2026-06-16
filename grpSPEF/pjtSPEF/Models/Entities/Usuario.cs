using System;

namespace pjtSPEF.Models.Entities
{
    // Datos de autenticación del docente. NO se guarda en Google Sheets sino en un
    // almacén local (App_Data/usuarios.json): el refresh token es sensible y se cifra
    // con DPAPI antes de persistirlo. Ver LocalUserStore y TokenProtector.
    public class Usuario
    {
        public int Id { get; set; }
        public string GoogleId { get; set; }
        public string Email { get; set; }
        public string Nombre { get; set; }
        public byte[] RefreshToken { get; set; }

        // Id del Spreadsheet de Google donde vive toda la jerarquía/notas de este docente.
        // Se crea (y guarda aquí) la primera vez que el docente usa la app. Ver SpefSheetStore.
        public string SpreadsheetId { get; set; }

        public DateTime FechaCreacion { get; set; }
        public bool Activo { get; set; }
    }
}
