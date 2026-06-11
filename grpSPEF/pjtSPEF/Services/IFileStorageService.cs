using System.IO;

namespace pjtSPEF.Services
{
    // Costura de almacenamiento: hoy guarda en App_Data/storage (LocalFileStorageService);
    // cuando llegue Google Drive, DriveStorageService devolverá un DriveFileId en el mismo
    // string de referencia y el resto de la app no cambia.
    public interface IFileStorageService
    {
        // Guarda el contenido y devuelve la referencia del archivo (fileRef).
        string Guardar(Stream contenido, string categoria, string extension);

        // Abre el archivo para lectura. Lanza FileNotFoundException si no existe.
        Stream Abrir(string fileRef);

        // Elimina el archivo si existe; no falla si ya no está.
        void Eliminar(string fileRef);
    }
}
