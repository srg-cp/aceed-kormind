using System.IO;

namespace pjtSPEF.Services
{
    public class ResultadoValidacionPdf
    {
        public bool EsValido { get; set; }
        public int TotalPaginas { get; set; }
        public string Error { get; set; }

        public static ResultadoValidacionPdf Valido(int totalPaginas)
        {
            return new ResultadoValidacionPdf { EsValido = true, TotalPaginas = totalPaginas };
        }

        public static ResultadoValidacionPdf Invalido(string error)
        {
            return new ResultadoValidacionPdf { EsValido = false, Error = error };
        }
    }

    public interface IPdfValidationService
    {
        // Valida que el stream sea un PDF real y que no exceda el máximo de páginas.
        ResultadoValidacionPdf Validar(Stream contenido);
    }
}
