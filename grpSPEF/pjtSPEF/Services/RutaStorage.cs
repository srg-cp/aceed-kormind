using pjtSPEF.Models.Entities;

namespace pjtSPEF.Services
{
    // Construye la ruta de carpetas (separadas por '/') que se le pasa a DriveStorageService
    // como "categoria", espejando la jerarquía del sistema: Periodo > Curso > Unidad > Tipo > Examen.
    // En Drive cuelga de la carpeta raíz ACEED.
    public static class RutaStorage
    {
        private const string SubcarpetaEntregas = "entregas";
        private const string SubcarpetaCalificados = "calificados";

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

        // Carpeta donde viven las entregas de los estudiantes de un examen base (sin calificar).
        public static string Entregas(ExamenBase examen)
        {
            return Combinar(RutaDelTipo(examen.TipoEvaluacion), Sanitizar(examen.Titulo), SubcarpetaEntregas);
        }

        // Carpeta donde se mueve el PDF de una entrega una vez calificada con éxito.
        public static string Calificados(ExamenBase examen)
        {
            return Combinar(RutaDelTipo(examen.TipoEvaluacion), Sanitizar(examen.Titulo), SubcarpetaCalificados);
        }

        private static string RutaDelTipo(TipoEvaluacion tipo)
        {
            var unidad = tipo.Unidad;
            var curso = unidad.Curso;
            // El periodo es ahora la raíz de la jerarquía, así que encabeza la ruta en Drive.
            return Combinar(NombrePeriodo(curso.Periodo), Sanitizar(curso.Nombre), NombreUnidad(unidad), Sanitizar(tipo.Nombre));
        }

        private static string NombrePeriodo(Periodo periodo)
        {
            return Sanitizar(periodo != null ? periodo.Nombre : "sin-periodo");
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
