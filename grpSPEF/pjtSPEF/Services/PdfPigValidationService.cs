using System;
using System.IO;
using UglyToad.PdfPig;

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

    public class PdfPigValidationService
    {
        // Regla de negocio: un examen (base o de alumno) tiene como máximo 10 hojas.
        public const int MaximoPaginas = 10;

        public ResultadoValidacionPdf Validar(Stream contenido)
        {
            if (contenido == null || contenido.Length == 0)
                return ResultadoValidacionPdf.Invalido("El archivo está vacío.");

            // Verificación rápida de los magic bytes antes de parsear completo.
            var cabecera = new byte[4];
            contenido.Position = 0;
            var leidos = contenido.Read(cabecera, 0, 4);
            contenido.Position = 0;
            if (leidos < 4 || cabecera[0] != '%' || cabecera[1] != 'P' || cabecera[2] != 'D' || cabecera[3] != 'F')
                return ResultadoValidacionPdf.Invalido("El archivo no es un PDF válido.");

            try
            {
                using (var documento = PdfDocument.Open(contenido))
                {
                    var paginas = documento.NumberOfPages;
                    if (paginas < 1)
                        return ResultadoValidacionPdf.Invalido("El PDF no tiene páginas.");
                    if (paginas > MaximoPaginas)
                        return ResultadoValidacionPdf.Invalido(
                            string.Format("El PDF tiene {0} páginas; el máximo permitido es {1}.", paginas, MaximoPaginas));

                    return ResultadoValidacionPdf.Valido(paginas);
                }
            }
            catch (Exception)
            {
                // PDF corrupto, cifrado o ilegible para PdfPig.
                return ResultadoValidacionPdf.Invalido("No se pudo leer el PDF (¿archivo dañado o protegido con contraseña?).");
            }
            finally
            {
                if (contenido.CanSeek)
                    contenido.Position = 0;
            }
        }
    }
}
