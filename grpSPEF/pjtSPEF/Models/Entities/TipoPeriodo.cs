using System;

namespace pjtSPEF.Models.Entities
{
    // Tipo de periodo académico. Solo existen estos tres por año; el usuario
    // elige el año y combina con el tipo para formar el periodo (p. ej. 2026-I).
    public enum TipoPeriodo : byte
    {
        I = 0,
        II = 1,
        REC = 2
    }

    public static class TipoPeriodoExtensions
    {
        // Sufijo legible que se concatena al año para formar el nombre del periodo.
        public static string Sufijo(this TipoPeriodo tipo)
        {
            switch (tipo)
            {
                case TipoPeriodo.I: return "I";
                case TipoPeriodo.II: return "II";
                default: return "REC";
            }
        }

        // Convierte el valor del formulario ("I" / "II" / "REC") al enum.
        public static TipoPeriodo Parsear(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
                return TipoPeriodo.I;

            switch (valor.Trim().ToUpperInvariant())
            {
                case "II": return TipoPeriodo.II;
                case "REC": return TipoPeriodo.REC;
                default: return TipoPeriodo.I;
            }
        }
    }
}
