using System.Collections.Generic;
using System.IO;
using System.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace pjtSPEF.Services
{
    // Visto que se dibuja sobre la respuesta del alumno según el puntaje obtenido.
    public enum VistoEstado { Correcto, Incorrecto, Parcial }

    // Una marca (✔/✗) a dibujar sobre el PDF en una posición aproximada (fracción 0..1 desde
    // la esquina superior izquierda de la página indicada, 1-based).
    public class MarcaVisto
    {
        public int Pagina { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public VistoEstado Estado { get; set; }
    }

    // Estampa la nota del alumno (obtenido / máximo) sobre el PDF de su entrega, en un recuadro
    // en la esquina superior derecha de la primera página (estilo casilla "NOTA" del examen).
    // Devuelve un PDF nuevo (bytes); no modifica el original recibido.
    //
    // El recuadro es de relleno blanco opaco, así que re-sellar (tras re-calificar) sobre el mismo
    // punto tapa por completo el sello anterior: no se acumulan.
    public class PdfSelloService
    {
        // Tamaño del recuadro (en puntos PDF).
        private const double AnchoCaja = 96;
        private const double AltoCaja = 48;
        // Separación respecto a los bordes superior y derecho: "arriba a la derecha pero no en la esquina".
        private const double MargenDerecho = 54;
        private const double MargenSuperior = 70;

        public byte[] Estampar(Stream pdfOriginal, string textoNota, IList<MarcaVisto> marcas = null)
        {
            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                if (pdfOriginal.CanSeek)
                    pdfOriginal.Position = 0;
                pdfOriginal.CopyTo(ms);
                bytes = ms.ToArray();
            }

            marcas = marcas ?? new List<MarcaVisto>();

            using (var doc = PdfDocument.Open(bytes))
            {
                var builder = new PdfDocumentBuilder();
                var fuente = builder.AddStandard14Font(Standard14Font.HelveticaBold);

                var total = doc.NumberOfPages;
                for (var i = 1; i <= total; i++)
                {
                    var pageBuilder = builder.AddPage(doc, i);
                    var pageOriginal = doc.GetPage(i);

                    // El sello de la nota solo va en la portada (donde está la casilla de nota del examen).
                    if (i == 1)
                        DibujarSello(pageBuilder, pageOriginal, fuente, textoNota);

                    // Los ✔/✗ van sobre la respuesta del alumno, en su página correspondiente.
                    foreach (var marca in marcas.Where(m => m.Pagina == i))
                        DibujarVisto(pageBuilder, pageOriginal, marca);
                }

                return builder.Build();
            }
        }

        // Dibuja un ✔ (verde/ámbar) o ✗ (rojo) centrado en la posición aproximada de la marca del alumno.
        private static void DibujarVisto(PdfPageBuilder page, Page original, MarcaVisto marca)
        {
            var x = Clamp(marca.X, 0, 1) * original.Width;
            // La fracción Y viene desde arriba; en PDF el origen está abajo, así que se invierte.
            var y = original.Height - Clamp(marca.Y, 0, 1) * original.Height;

            const double s = 12;       // tamaño del símbolo
            const double grosor = 2.4; // ancho de trazo

            if (marca.Estado == VistoEstado.Incorrecto)
            {
                page.SetStrokeColor(200, 0, 0); // rojo
                page.DrawLine(new PdfPoint(x - s * 0.5, y - s * 0.5), new PdfPoint(x + s * 0.5, y + s * 0.5), grosor);
                page.DrawLine(new PdfPoint(x - s * 0.5, y + s * 0.5), new PdfPoint(x + s * 0.5, y - s * 0.5), grosor);
            }
            else
            {
                // Correcto = verde; parcial = ámbar (acierto parcial).
                if (marca.Estado == VistoEstado.Parcial)
                    page.SetStrokeColor(230, 150, 0);
                else
                    page.SetStrokeColor(0, 150, 0);

                // ✔: trazo corto bajando a la izquierda + trazo largo subiendo a la derecha.
                var vertice = new PdfPoint(x - s * 0.1, y - s * 0.4);
                page.DrawLine(new PdfPoint(x - s * 0.55, y), vertice, grosor);
                page.DrawLine(vertice, new PdfPoint(x + s * 0.6, y + s * 0.6), grosor);
            }

            page.ResetColor();
        }

        private static double Clamp(double v, double min, double max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private static void DibujarSello(PdfPageBuilder page, Page original,
            PdfDocumentBuilder.AddedFont fuente, string textoNota)
        {
            double ancho = original.Width;
            double alto = original.Height;

            // Coordenadas PDF: origen abajo-izquierda. La caja se ancla arriba a la derecha.
            var xDerecha = ancho - MargenDerecho;
            var xIzquierda = xDerecha - AnchoCaja;
            var yArriba = alto - MargenSuperior;
            var yAbajo = yArriba - AltoCaja;

            // Recuadro: relleno blanco opaco con borde negro.
            page.SetStrokeColor(0, 0, 0);
            page.SetTextAndFillColor(255, 255, 255);
            page.DrawRectangle(new PdfPoint(xIzquierda, yAbajo), AnchoCaja, AltoCaja, 1.5, true);

            // A partir de aquí el texto va en negro.
            page.SetTextAndFillColor(0, 0, 0);

            // Rótulo "NOTA" pequeño en la parte superior de la caja.
            const double tamRotulo = 9;
            var anchoRotulo = AnchoTexto(page, "NOTA", tamRotulo, fuente);
            var xRotulo = xIzquierda + (AnchoCaja - anchoRotulo) / 2;
            page.AddText("NOTA", tamRotulo, new PdfPoint(xRotulo, yArriba - 14), fuente);

            // Nota (obtenido / máximo) grande y centrada en la parte baja de la caja.
            const double tamNota = 20;
            var anchoNota = AnchoTexto(page, textoNota, tamNota, fuente);
            var xNota = xIzquierda + (AnchoCaja - anchoNota) / 2;
            page.AddText(textoNota, tamNota, new PdfPoint(xNota, yAbajo + 9), fuente);

            page.ResetColor();
        }

        // Mide el ancho del texto midiéndolo desde x = 0 y tomando el borde derecho del último glifo.
        private static double AnchoTexto(PdfPageBuilder page, string texto, double tam,
            PdfDocumentBuilder.AddedFont fuente)
        {
            var letras = page.MeasureText(texto, tam, new PdfPoint(0, 0), fuente);
            return letras.Count == 0 ? 0 : letras.Max(l => l.BoundingBox.Right);
        }
    }
}
