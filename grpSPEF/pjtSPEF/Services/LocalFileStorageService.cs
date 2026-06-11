using System;
using System.IO;
using System.Web.Hosting;

namespace pjtSPEF.Services
{
    public class LocalFileStorageService : IFileStorageService
    {
        private readonly string _raiz;

        public LocalFileStorageService()
            : this(HostingEnvironment.MapPath("~/App_Data/storage"))
        {
        }

        public LocalFileStorageService(string raiz)
        {
            _raiz = raiz;
        }

        public string Guardar(Stream contenido, string categoria, string extension)
        {
            var fileRef = string.Format("{0}/{1}{2}", categoria, Guid.NewGuid().ToString("N"), extension);
            var rutaFisica = RutaFisica(fileRef);
            Directory.CreateDirectory(Path.GetDirectoryName(rutaFisica));

            using (var destino = File.Create(rutaFisica))
            {
                contenido.CopyTo(destino);
            }
            return fileRef;
        }

        public Stream Abrir(string fileRef)
        {
            return File.OpenRead(RutaFisica(fileRef));
        }

        public void Eliminar(string fileRef)
        {
            if (string.IsNullOrEmpty(fileRef))
                return;

            var rutaFisica = RutaFisica(fileRef);
            if (File.Exists(rutaFisica))
                File.Delete(rutaFisica);
        }

        private string RutaFisica(string fileRef)
        {
            // fileRef usa '/' como separador; se valida que no escape de la raíz.
            var ruta = Path.GetFullPath(Path.Combine(_raiz, fileRef.Replace('/', Path.DirectorySeparatorChar)));
            if (!ruta.StartsWith(Path.GetFullPath(_raiz), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Referencia de archivo inválida.", "fileRef");
            return ruta;
        }
    }
}
