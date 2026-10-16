using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ChequeAR.Application.Interfaces;

namespace ChequeAR.Infrastructure.Oracle
{
    /// Sirve las vistas VW_* del lado hospital desde memoria, sin Oracle.
    ///
    /// El mock de spec 008 (infra/mock-hospital) es Oracle XE en Docker: sirve en la
    /// maquina de un dev, pero Azure App Service no corre contenedores, asi que alla
    /// el buscador de pacientes queda sin datos. Este cliente cubre ese hueco: se
    /// registra cuando Oracle:ConnectionString esta vacio (ver ServiceComposition),
    /// igual que MockHospitalAuthClient con HospitalAuth:LoginUrl.
    ///
    /// Respeta el mismo contrato que OracleViewClient —allowlist de vistas, columnas
    /// validadas, filtros por igualdad, busqueda por substring— para que el codigo de
    /// arriba no distinga cual de los dos tiene enchufado. Lo que NO reproduce es la
    /// paridad de driver y dialecto que spec 008 justifica: no hay SQL, ni tipos
    /// Oracle, ni grants. Un bug de dialecto no aparece aca.
    ///
    /// El dataset usa los mismos catalogos que 03-seed.sql (nombres es-AR, CIE-10 y
    /// LOINC pediatricos, ATC, especialidades) y los mismos conteos objetivo, pero el
    /// RNG es System.Random y no DBMS_RANDOM: los valores NO son fila-a-fila iguales a
    /// los del contenedor. Es deterministico entre corridas, no entre implementaciones.
    public class MockOracleViewClient : IOracleViewClient
    {
        private const int Seed = 20260625;
        private static readonly DateTime Anclaje = new DateTime(2026, 6, 25);
        private const int TotalPacientes = 52;

        // Mismo patron que OracleViewClient: identificador Oracle, opcionalmente calificado.
        private static readonly Regex IdentifierPattern =
            new Regex(@"^[A-Za-z][A-Za-z0-9_$#]{0,29}(\.[A-Za-z][A-Za-z0-9_$#]{0,29})?$", RegexOptions.Compiled);

        private static readonly Lazy<Dictionary<string, List<IDictionary<string, object>>>> Dataset =
            new Lazy<Dictionary<string, List<IDictionary<string, object>>>>(Build);

        private readonly HashSet<string> _allowedViews;

        public MockOracleViewClient(IEnumerable<string> allowedViews = null)
        {
            _allowedViews = allowedViews != null
                ? new HashSet<string>(allowedViews, StringComparer.OrdinalIgnoreCase)
                : null;
        }

        public Task PingAsync() => Task.CompletedTask;

        public Task<IReadOnlyList<IDictionary<string, object>>> QueryAsync(
            string viewName, IReadOnlyDictionary<string, object> filters)
        {
            var rows = Rows(ValidateViewName(viewName));

            if (filters != null && filters.Count > 0)
            {
                foreach (var kv in filters)
                {
                    var column = ValidateColumnName(kv.Key);
                    rows = rows.Where(r => Matches(r, column, kv.Value)).ToList();
                }
            }

            return Task.FromResult<IReadOnlyList<IDictionary<string, object>>>(Copy(rows));
        }

        public Task<IReadOnlyList<IDictionary<string, object>>> SearchAsync(
            string viewName, IEnumerable<string> columns, string term, int maxRows = 50)
        {
            var rows = Rows(ValidateViewName(viewName));

            var safeColumns = (columns ?? Enumerable.Empty<string>()).Select(ValidateColumnName).ToList();
            if (safeColumns.Count == 0)
                throw new ArgumentException("Se requiere al menos una columna de busqueda", nameof(columns));

            if (string.IsNullOrWhiteSpace(term))
                return Task.FromResult<IReadOnlyList<IDictionary<string, object>>>(new List<IDictionary<string, object>>());

            var needle = term.Trim();
            var hits = rows
                .Where(r => safeColumns.Any(c => Contains(r, c, needle)))
                .Take(maxRows)
                .ToList();

            return Task.FromResult<IReadOnlyList<IDictionary<string, object>>>(Copy(hits));
        }

        // ---- Contrato de acceso (espeja OracleViewClient) ----

        private string ValidateViewName(string viewName)
        {
            if (string.IsNullOrWhiteSpace(viewName) || !IdentifierPattern.IsMatch(viewName))
                throw new ArgumentException("Nombre de vista inválido: " + viewName, nameof(viewName));

            if (_allowedViews != null)
            {
                if (!_allowedViews.Contains(viewName))
                    throw new ArgumentException("Vista no autorizada: " + viewName, nameof(viewName));
            }
            else if (!Unqualified(viewName).StartsWith("VW_", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Vista no autorizada: " + viewName, nameof(viewName));
            }

            return viewName;
        }

        private static string ValidateColumnName(string column)
        {
            if (string.IsNullOrWhiteSpace(column) || !IdentifierPattern.IsMatch(column))
                throw new ArgumentException("Nombre de columna inválido: " + column, nameof(column));
            return column;
        }

        private static string Unqualified(string viewName)
        {
            var dot = viewName.LastIndexOf('.');
            return dot >= 0 ? viewName.Substring(dot + 1) : viewName;
        }

        private static List<IDictionary<string, object>> Rows(string viewName)
        {
            // El schema calificado (HOSPITAL_MOCK.VW_PACIENTE) resuelve a la misma tabla:
            // el consumidor puede venir configurado con o sin prefijo.
            return Dataset.Value.TryGetValue(Unqualified(viewName).ToUpperInvariant(), out var rows)
                ? rows
                : new List<IDictionary<string, object>>();
        }

        /// Copia defensiva: los diccionarios del dataset son estaticos y compartidos
        /// entre requests. Sin esto, quien mutase una fila envenenaria el mock entero.
        private static List<IDictionary<string, object>> Copy(IEnumerable<IDictionary<string, object>> rows)
            => rows.Select(r => (IDictionary<string, object>)new Dictionary<string, object>(r, StringComparer.OrdinalIgnoreCase)).ToList();

        private static bool Matches(IDictionary<string, object> row, string column, object expected)
        {
            if (!row.TryGetValue(column, out var actual)) return false;
            if (actual == null || expected == null) return actual == null && expected == null;

            // Comparacion por texto: los filtros llegan como string desde la UI aunque
            // la columna sea numerica o fecha, igual que en el bind de Oracle.
            return string.Equals(ToText(actual), ToText(expected), StringComparison.OrdinalIgnoreCase);
        }

        private static bool Contains(IDictionary<string, object> row, string column, string needle)
        {
            if (!row.TryGetValue(column, out var actual) || actual == null) return false;
            return ToText(actual).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ToText(object value)
            => value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value.ToString();

        // =================================================================
        // Dataset. Catalogos tomados de infra/mock-hospital/init/03-seed.sql.
        // =================================================================

        private static readonly string[] Nombres =
        {
            "Juan","Mariano","Lucia","Sofia","Mateo","Valentina","Bruno","Camila",
            "Tomas","Martina","Joaquin","Catalina","Benjamin","Emma","Felipe","Mia",
            "Agustin","Olivia","Lautaro","Renata","Bautista","Julieta","Santino","Isabella",
            "Ciro","Delfina","Nahuel","Antonella","Iara","Pilar"
        };

        private static readonly string[] Apellidos =
        {
            "Gonzalez","Rodriguez","Lopez","Fernandez","Martinez","Diaz","Perez","Garcia",
            "Sanchez","Romero","Torres","Alvarez","Ramirez","Flores","Suarez","Castro",
            "Russo","Bianchi","Rossi","Esposito","Romano","Greco","Marchetti",
            "Quiroga","Aguirre","Sosa","Carballo",
            "Calfunao","Curiqueo","Nahuelpan"
        };

        private static readonly string[] Coberturas =
        { "OSDE","Swiss Medical","Galeno","PAMI","IOMA","Particular","OSDE","Swiss Medical","Galeno",null };

        private static readonly string[] TiposDoc = { "DNI","DNI","DNI","DNI","DNI","DNI","DNI","PAS","CI",null };
        private static readonly string[] Sexos = { "M","F","F","M","O" };

        private static readonly string[] Cie10Cod =
        {
            "J06.9","A09","J45.9","K21.9","R50.9","H66.9","J20.9","B01.9","L20.9","J18.9",
            "K59.0","R10.4","J03.9","H10.9","J00","R11","R51","L50.9","R05","N39.0",
            "J32.9","K35.80","R06.0","S00.9","T78.4"
        };

        private static readonly string[] Cie10Dsp =
        {
            "Infeccion aguda de las vias respiratorias superiores, no especificada",
            "Diarrea y gastroenteritis de presunto origen infeccioso",
            "Asma, no especificada",
            "Enfermedad por reflujo gastroesofagico sin esofagitis",
            "Fiebre, no especificada",
            "Otitis media, no especificada",
            "Bronquitis aguda, no especificada",
            "Varicela sin complicacion",
            "Dermatitis atopica, no especificada",
            "Neumonia, no especificada",
            "Constipacion",
            "Dolor abdominal, no especificado",
            "Faringitis aguda, no especificada",
            "Conjuntivitis, no especificada",
            "Rinofaringitis aguda (resfriado comun)",
            "Nauseas y vomitos",
            "Cefalea",
            "Urticaria, no especificada",
            "Tos",
            "Infeccion de vias urinarias, sitio no especificado",
            "Sinusitis cronica, no especificada",
            "Apendicitis aguda, no especificada",
            "Disnea",
            "Traumatismo superficial de la cabeza, parte no especificada",
            "Alergia, no especificada"
        };

        private static readonly string[] LoincCod =
        {
            "718-7","4544-3","2345-7","2951-2","2823-3","2160-0","6690-2","789-8",
            "777-3","1751-7","1742-6","2093-3","17861-6","2085-9","2571-8"
        };

        private static readonly string[] LoincDsp =
        {
            "Hemoglobina en sangre","Hematocrito","Glucosa en sangre","Sodio en suero",
            "Potasio en suero","Creatinina en suero","Leucocitos en sangre","Eritrocitos en sangre",
            "Plaquetas en sangre","Albumina en suero","Alanina aminotransferasa",
            "Colesterol total","Calcio en suero","HDL colesterol","Trigliceridos"
        };

        private static readonly string[] LoincUni =
        { "g/dL","%","mg/dL","mmol/L","mmol/L","mg/dL","10^3/uL","10^6/uL","10^3/uL","g/dL","U/L","mg/dL","mg/dL","mg/dL","mg/dL" };

        private static readonly double[] LoincMin = { 10, 30, 60, 135, 3.5, 0.3, 4.0, 3.8, 150, 3.5, 7, 120, 8.5, 35, 50 };
        private static readonly double[] LoincMax = { 16, 50, 110, 145, 5.5, 1.0, 12.0, 5.5, 450, 5.0, 40, 200, 10.5, 75, 150 };

        private static readonly string[] AtcCod =
        {
            "N02BE01","J01CA04","R03AC02","R06AE07","A02BC01","R05CB06","J01CR02","M01AE01",
            "A03FA01","B05XA03","J01XD01","D07AB01","S01AA01","H02AB06","R03BA02","A07EC01",
            "C09AA02","J01EE01","N05CD08","J05AB01"
        };

        private static readonly string[] AtcNom =
        {
            "Paracetamol jarabe","Amoxicilina suspension","Salbutamol inhalador","Cetirizina gotas",
            "Omeprazol capsulas","Ambroxol jarabe","Amoxi-clavulanico susp","Ibuprofeno jarabe",
            "Metoclopramida gotas","Suero fisiologico","Metronidazol comprimidos","Hidrocortisona crema",
            "Cloranfenicol colirio","Prednisolona jarabe","Budesonide inhalador","Mesalazina granulado",
            "Enalapril comprimidos","Sulfametoxazol+TMP susp","Midazolam gotas","Aciclovir comprimidos"
        };

        private static readonly string[] AtcVia =
        { "ORAL","ORAL","INH","ORAL","ORAL","ORAL","ORAL","ORAL","ORAL","IV","ORAL","TOPICA","TOPICA","ORAL","INH","ORAL","ORAL","ORAL","ORAL","ORAL" };

        private static readonly string[] Alergenos =
        { "Penicilina","AINEs","Frutos secos","Lacteos","Polen","Acaros","Mariscos","Latex","Huevo","Polvo" };

        private static readonly string[] Motivos =
        {
            "Control pediatrico de rutina","Cuadro febril","Tos persistente","Diarrea aguda",
            "Erupcion cutanea","Dolor abdominal","Dificultad respiratoria","Otalgia",
            "Vomitos","Dolor de garganta","Control de crecimiento","Seguimiento"
        };

        private static readonly string[] TiposEpi = { "AMBULATORIO","AMBULATORIO","AMBULATORIO","URGENCIA","INTERNACION" };
        private static readonly string[] EstadosTurno = { "PROGRAMADO","ATENDIDO","AUSENTE","CANCELADO" };
        private static readonly string[] EstadosAlergia = { "CONFIRMADA","SOSPECHADA","DESCONOCIDAS","DESCARTADA" };
        private static readonly string[] Severidades = { "LEVE","MODERADA","SEVERA" };
        private static readonly string[] ReaccionesAlergicas = { "Erupcion cutanea", "Broncoespasmo", "Angioedema", "Vomitos", "Prurito" };

        private static readonly string[] EspCod = { "PED","NEO","CARP","NEUM","GAST","INFE","NEFRO","ENDO" };

        private static readonly string[] EspNom =
        {
            "Pediatria General","Neonatologia","Cardiologia Pediatrica","Neumonologia Pediatrica",
            "Gastroenterologia Pediatrica","Infectologia Pediatrica","Nefrologia Pediatrica","Endocrinologia Pediatrica"
        };

        private static Dictionary<string, List<IDictionary<string, object>>> Build()
        {
            var rnd = new Random(Seed);

            var pacientes = new List<IDictionary<string, object>>();
            var episodios = new List<IDictionary<string, object>>();
            var diagnosticos = new List<IDictionary<string, object>>();
            var observaciones = new List<IDictionary<string, object>>();
            var signos = new List<IDictionary<string, object>>();
            var medicaciones = new List<IDictionary<string, object>>();
            var alergias = new List<IDictionary<string, object>>();
            var turnos = new List<IDictionary<string, object>>();
            var especialidades = new List<IDictionary<string, object>>();
            var profesionales = new List<IDictionary<string, object>>();

            for (var i = 0; i < EspCod.Length; i++)
                especialidades.Add(Row(("COD_ESPECIALIDAD", EspCod[i]), ("NOMBRE", EspNom[i])));

            for (var i = 1; i <= 12; i++)
            {
                var esp = i % EspCod.Length;
                profesionales.Add(Row(
                    ("ID_PROFESIONAL", "PRO-" + i.ToString("D6", CultureInfo.InvariantCulture)),
                    ("MATRICULA", "MN-" + (10000 + i * 137)),
                    ("NOMBRE", Pick(rnd, Nombres)),
                    ("APELLIDO", Pick(rnd, Apellidos)),
                    ("COD_ESPECIALIDAD", EspCod[esp]),
                    ("ESPECIALIDAD", EspNom[esp])));
            }

            var seqDx = 0; var seqObs = 0; var seqSig = 0; var seqMed = 0; var seqAle = 0; var seqTur = 0;

            for (var p = 1; p <= TotalPacientes; p++)
            {
                var idPaciente = "PAC-" + p.ToString("D6", CultureInfo.InvariantCulture);

                // 0 a 17 anios: poblacion pediatrica (FR del spec 008).
                var edadMeses = rnd.Next(0, 216);
                var fechaNac = Anclaje.AddMonths(-edadMeses).AddDays(-rnd.Next(0, 28));

                pacientes.Add(Row(
                    ("ID_PACIENTE", idPaciente),
                    ("DOCUMENTO", (45000000 + p * 1731).ToString(CultureInfo.InvariantCulture)),
                    ("TIPO_DOCUMENTO", Pick(rnd, TiposDoc)),
                    ("NOMBRE", Pick(rnd, Nombres)),
                    ("APELLIDO", Pick(rnd, Apellidos)),
                    ("FECHA_NACIMIENTO", fechaNac),
                    ("SEXO", Pick(rnd, Sexos)),
                    ("COBERTURA", Pick(rnd, Coberturas))));

                var nEpisodios = rnd.Next(1, 6);
                for (var k = 0; k < nEpisodios; k++)
                {
                    var idEpisodio = "EPI-" + episodios.Count.ToString("D6", CultureInfo.InvariantCulture);
                    var espIdx = rnd.Next(EspCod.Length);
                    var inicio = Anclaje.AddDays(-rnd.Next(1, 730)).AddHours(rnd.Next(7, 20));
                    var tipo = Pick(rnd, TiposEpi);
                    var fin = tipo == "INTERNACION" ? (object)inicio.AddDays(rnd.Next(1, 12)) : inicio.AddHours(1);

                    episodios.Add(Row(
                        ("ID_EPISODIO", idEpisodio),
                        ("ID_PACIENTE", idPaciente),
                        ("TIPO", tipo),
                        ("COD_ESPECIALIDAD", EspCod[espIdx]),
                        ("ESPECIALIDAD", EspNom[espIdx]),
                        ("FECHA_INICIO", inicio),
                        ("FECHA_FIN", fin),
                        ("MOTIVO_CONSULTA", Pick(rnd, Motivos))));

                    var dx = rnd.Next(Cie10Cod.Length);
                    diagnosticos.Add(Row(
                        ("ID_DIAGNOSTICO", "DX-" + (++seqDx).ToString("D6", CultureInfo.InvariantCulture)),
                        ("ID_EPISODIO", idEpisodio),
                        ("ID_PACIENTE", idPaciente),
                        ("COD_CIE10", Cie10Cod[dx]),
                        ("DISPLAY", Cie10Dsp[dx]),
                        ("ES_PRINCIPAL", "S"),
                        ("FECHA_DIAGNOSTICO", inicio.Date)));

                    var nObs = rnd.Next(1, 7);
                    for (var o = 0; o < nObs; o++)
                    {
                        var l = rnd.Next(LoincCod.Length);
                        var valor = Math.Round(LoincMin[l] + rnd.NextDouble() * (LoincMax[l] - LoincMin[l]), 2);
                        observaciones.Add(Row(
                            ("ID_OBSERVACION", "OBS-" + (++seqObs).ToString("D6", CultureInfo.InvariantCulture)),
                            ("ID_PACIENTE", idPaciente),
                            ("ID_EPISODIO", idEpisodio),
                            ("COD_LOINC", LoincCod[l]),
                            ("DISPLAY", LoincDsp[l]),
                            ("VALOR_NUMERICO", (decimal)valor),
                            ("VALOR_TEXTO", null),
                            ("UNIDAD", LoincUni[l]),
                            ("FECHA_TOMA", inicio.AddHours(rnd.Next(1, 24)))));
                    }

                    var anios = edadMeses / 12.0;
                    signos.Add(Row(
                        ("ID_SIGNO", "SIG-" + (++seqSig).ToString("D6", CultureInfo.InvariantCulture)),
                        ("ID_EPISODIO", idEpisodio),
                        ("ID_PACIENTE", idPaciente),
                        ("FECHA_TOMA", inicio.AddMinutes(rnd.Next(5, 90))),
                        ("PESO_KG", (decimal)Math.Round(3.3 + anios * 2.6 + rnd.NextDouble() * 3, 1)),
                        ("TALLA_CM", (decimal)Math.Round(50 + anios * 6.2 + rnd.NextDouble() * 8, 1)),
                        ("FC_LPM", 70 + rnd.Next(0, 60)),
                        ("FR_RPM", 16 + rnd.Next(0, 20)),
                        ("TA_SISTOLICA", 85 + rnd.Next(0, 35)),
                        ("TA_DIASTOLICA", 50 + rnd.Next(0, 25)),
                        ("TEMPERATURA_C", (decimal)Math.Round(36.2 + rnd.NextDouble() * 2.4, 1))));

                    var nMed = rnd.Next(0, 3);
                    for (var m = 0; m < nMed; m++)
                    {
                        var a = rnd.Next(AtcCod.Length);
                        medicaciones.Add(Row(
                            ("ID_MEDICACION", "MED-" + (++seqMed).ToString("D6", CultureInfo.InvariantCulture)),
                            ("ID_EPISODIO", idEpisodio),
                            ("ID_PACIENTE", idPaciente),
                            ("COD_ATC", AtcCod[a]),
                            ("NOMBRE_COMERCIAL", AtcNom[a]),
                            ("DOSIS", rnd.Next(1, 4) + " x " + (rnd.Next(1, 5) * 125) + " mg"),
                            ("VIA", AtcVia[a]),
                            ("FECHA_INICIO", inicio.Date),
                            ("FECHA_FIN", inicio.Date.AddDays(rnd.Next(3, 15)))));
                    }
                }

                var nAle = rnd.Next(0, 4);
                for (var a = 0; a < nAle; a++)
                {
                    alergias.Add(Row(
                        ("ID_ALERGIA", "ALE-" + (++seqAle).ToString("D6", CultureInfo.InvariantCulture)),
                        ("ID_PACIENTE", idPaciente),
                        ("SUSTANCIA", Pick(rnd, Alergenos)),
                        ("REACCION", Pick(rnd, ReaccionesAlergicas)),
                        ("SEVERIDAD", Pick(rnd, Severidades)),
                        ("ESTADO", Pick(rnd, EstadosAlergia)),
                        ("FECHA_REGISTRO", Anclaje.AddDays(-rnd.Next(30, 1500)))));
                }

                var nTur = rnd.Next(0, 3);
                for (var t = 0; t < nTur; t++)
                {
                    var espIdx = rnd.Next(EspCod.Length);
                    turnos.Add(Row(
                        ("ID_TURNO", "TUR-" + (++seqTur).ToString("D6", CultureInfo.InvariantCulture)),
                        ("ID_PACIENTE", idPaciente),
                        ("ID_PROFESIONAL", "PRO-" + rnd.Next(1, 13).ToString("D6", CultureInfo.InvariantCulture)),
                        ("COD_ESPECIALIDAD", EspCod[espIdx]),
                        ("ESPECIALIDAD", EspNom[espIdx]),
                        ("FECHA_HORA", Anclaje.AddDays(rnd.Next(-60, 60)).AddHours(rnd.Next(8, 18))),
                        ("ESTADO", Pick(rnd, EstadosTurno))));
                }
            }

            return new Dictionary<string, List<IDictionary<string, object>>>(StringComparer.OrdinalIgnoreCase)
            {
                { "VW_PACIENTE", pacientes },
                { "VW_EPISODIO", episodios },
                { "VW_DIAGNOSTICO", diagnosticos },
                { "VW_OBSERVACION_LAB", observaciones },
                { "VW_SIGNOS_VITALES", signos },
                { "VW_MEDICACION", medicaciones },
                { "VW_ALERGIA", alergias },
                { "VW_PROFESIONAL", profesionales },
                { "VW_ESPECIALIDAD", especialidades },
                { "VW_TURNO", turnos }
            };
        }

        private static string Pick(Random rnd, string[] options) => options[rnd.Next(options.Length)];

        private static Dictionary<string, object> Row(params (string Column, object Value)[] cells)
        {
            var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in cells) row[c.Column] = c.Value;
            return row;
        }
    }
}
