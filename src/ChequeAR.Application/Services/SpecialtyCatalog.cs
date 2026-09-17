using System;
using System.Collections.Generic;
using System.Linq;
using ChequeAR.Domain.Constants;
using ChequeAR.Domain.Entities;

namespace ChequeAR.Application.Services
{
    /// <summary>
    /// Snapshot inmutable y sincrónico del catálogo de especialidades (CHEQUEAR.Specialty).
    /// Reemplaza a SpecialtyMap: en vez de un diccionario hardcodeado en código, refleja lo
    /// que hay en la base en el momento en que se cargó (ver SpecialtyCatalogService).
    /// </summary>
    public sealed class SpecialtyCatalog
    {
        /// <summary>Nombres de zona del tema (chequear-theme.css), sin el prefijo --cq-zone-.</summary>
        public static readonly string[] ZoneColors = { "red", "violet", "green", "yellow", "blue", "orange" };

        private readonly Dictionary<string, Specialty> _byCode;

        public IReadOnlyList<Specialty> Items { get; }
        public string DefaultCode { get; }
        public string DefaultLabel { get; }

        public SpecialtyCatalog(IEnumerable<Specialty> items)
        {
            Items = (items ?? Enumerable.Empty<Specialty>()).ToList();
            _byCode = Items.ToDictionary(s => s.Code, StringComparer.OrdinalIgnoreCase);

            var def = Items.FirstOrDefault(s => s.IsDefault) ?? (Items.Count > 0 ? Items[0] : null);
            DefaultCode = def?.Code ?? SpecialtyDefaults.Code;
            DefaultLabel = def?.Name ?? SpecialtyDefaults.Name;
        }

        /// <summary>Catálogo vacío: se usa cuando la base no responde. Mantiene la app renderizando.</summary>
        public static SpecialtyCatalog Empty { get; } = new SpecialtyCatalog(null);

        /// <param name="display">Nombre que ya venga de la base (ej. Questionnaire.SpecialtyDisplay); tiene prioridad.</param>
        public string Label(string code, string display = null)
        {
            if (!string.IsNullOrWhiteSpace(display)) return display;
            if (string.IsNullOrWhiteSpace(code)) return DefaultLabel;
            return _byCode.TryGetValue(code.Trim(), out var s) ? s.Name : code;
        }

        /// <summary>Código a usar cuando el dato no trae ninguno. NUNCA usar esto para decidir con qué código escribir un recurso nuevo (ver EnsureSpecialtyUsable) — es sólo un fallback de lectura.</summary>
        public string CodeOrDefault(string code) => string.IsNullOrWhiteSpace(code) ? DefaultCode : code.Trim();

        /// <summary>Nombre de zona para el chip: Specialty.Color si está cargado; si no, hash estable del código.</summary>
        public string Color(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return ZoneColors[0];
            if (_byCode.TryGetValue(code.Trim(), out var s) && !string.IsNullOrWhiteSpace(s.Color)) return s.Color;
            return ZoneColors[StableHashIndex(code.Trim())];
        }

        public bool Exists(string code) => !string.IsNullOrWhiteSpace(code) && _byCode.ContainsKey(code.Trim());

        public bool ExistsActive(string code)
            => !string.IsNullOrWhiteSpace(code) && _byCode.TryGetValue(code.Trim(), out var s) && s.IsActive;

        public IReadOnlyList<string> AllCodes() => Items.Select(s => s.Code).ToList();
        public IReadOnlyList<string> ActiveCodes() => Items.Where(s => s.IsActive).Select(s => s.Code).ToList();

        /// <summary>
        /// FNV-1a 32 bits sobre el código en minúsculas. No se usa string.GetHashCode(): en
        /// .NET Framework está randomizado por proceso, y el chip cambiaría de color en cada
        /// reciclo del app pool.
        /// </summary>
        private static int StableHashIndex(string code)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (var ch in code.ToLowerInvariant())
                {
                    h ^= ch;
                    h *= 16777619;
                }
                return (int)(h % (uint)ZoneColors.Length);
            }
        }
    }
}
