using pjtSPEF.Models.Entities;

namespace pjtSPEF.Services
{
    // Construye la ruta de carpetas (separadas por '/') que se le pasa a DriveStorageService
    // como "categoria", espejando la jerarquía del sistema: Curso > Unidad > Tipo > Examen.
    // En Drive cuelga de la carpeta raíz ACEED; en local, de App_Data/storage.
    public static class RutaStorage
    {
        private const string SubcarpetaEntregas = "entregas";

        // Carpeta donde vive el PDF del examen base.
        public static string ExamenBase(TipoEvaluacion tipo, string tituloExamen)
        {
            return Combinar(RutaDelTipo(tipo), Sanitizar(tituloExamen));
        }

        // Nombre de la carpeta (hoja) que representa un examen, para renombrarla cuando cambia el título.
        public static string NombreCarpetaExamen(string tituloExamen)
        {
            return Sanitizar(tituloExamen);
        }

        // Carpeta donde viven las entregas de los estudiantes de un examen base.
        public static string Entregas(ExamenBase examen)
        {
            return Combinar(RutaDelTipo(examen.TipoEvaluacion), Sanitizar(examen.Titulo), SubcarpetaEntregas);
        }

        private static string RutaDelTipo(TipoEvaluacion tipo)
        {
            var unidad = tipo.Unidad;
            var curso = unidad.Curso;
            return Combinar(NombreCurso(curso), NombreUnidad(unidad), Sanitizar(tipo.Nombre));
        }

        private static string NombreCurso(Curso curso)
        {
            var nombre = Sanitizar(curso.Nombre);
            return string.IsNullOrWhiteSpace(curso.Periodo)
                ? nombre
                : nombre + " (" + Sanitizar(curso.Periodo) + ")";
        }

        private static string NombreUnidad(Unidad unidad)
        {
            return Sanitizar("U" + unidad.Numero + " " + unidad.Nombre);
        }

        private static string Combinar(params string[] segmentos)
        {
            return string.Join("/", segmentos);
        }

        // Los nombres son de carpeta, así que se quita el separador '/' (y '\') para no romper la ruta.
        private static string Sanitizar(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
                return "sin-nombre";
            return valor.Trim().Replace('/', '-').Replace('\\', '-');
        }
    }
}
