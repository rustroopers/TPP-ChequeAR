using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using ChequeAR.Application.Reports;

namespace ChequeAR.Application.Exporters
{
    /// Vuelca el resumen estadístico (FR-008) a XLSX: una hoja con el resumen numérico/
    /// completitud por pregunta, otra con el detalle de categorías, otra con los bins de los
    /// histogramas y una cuarta con gráficos nativos de Excel (torta por categórica, barra
    /// por histograma) que leen esas mismas hojas de datos. Mismo enfoque sin dependencias
    /// que XlsxExporter, reutilizando sus helpers de bajo nivel.
    public class StatsXlsxExporter
    {
        private static readonly CultureInfo Ar = CultureInfo.GetCultureInfo("es-AR");

        /// <param name="linkIds">Preguntas a incluir; null o vacío exporta todas.</param>
        /// <param name="chartTypes">
        /// LinkId de pregunta categórica -> "bar" | "pie". La persona ya eligió esto en
        /// pantalla para leerlo; el Excel que se lleva tiene que coincidir con lo que vio, no
        /// con un tipo fijo elegido de antemano. Sin entrada para una pregunta, o el
        /// diccionario en null, esa pregunta sale en barra.
        /// </param>
        public Stream Export(ReportStats stats, IReadOnlyCollection<string> linkIds = null,
            IDictionary<string, string> chartTypes = null)
        {
            var questions = Filtrar(stats.Questions, linkIds);

            List<QuestionBlock> categoriaBlocks;
            var categoriasSheet = CategoriasSheet(questions, out categoriaBlocks);
            List<QuestionBlock> histogramaBlocks;
            var histogramasSheet = HistogramasSheet(questions, out histogramaBlocks);
            var charts = BuildCharts(categoriaBlocks, histogramaBlocks, chartTypes);

            var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                XlsxExporter.AddEntry(zip, "[Content_Types].xml", ContentTypes(charts.Count));
                XlsxExporter.AddEntry(zip, "_rels/.rels", RootRels());
                XlsxExporter.AddEntry(zip, "xl/workbook.xml", Workbook(charts.Count > 0));
                XlsxExporter.AddEntry(zip, "xl/_rels/workbook.xml.rels", WorkbookRels(charts.Count > 0));
                XlsxExporter.AddEntry(zip, "xl/worksheets/sheet1.xml", ResumenSheet(questions));
                XlsxExporter.AddEntry(zip, "xl/worksheets/sheet2.xml", categoriasSheet);
                XlsxExporter.AddEntry(zip, "xl/worksheets/sheet3.xml", histogramasSheet);

                if (charts.Count > 0)
                {
                    XlsxExporter.AddEntry(zip, "xl/worksheets/sheet4.xml", GraficosSheet());
                    XlsxExporter.AddEntry(zip, "xl/worksheets/_rels/sheet4.xml.rels", Sheet4Rels());
                    XlsxExporter.AddEntry(zip, "xl/drawings/drawing1.xml", Drawing(charts));
                    XlsxExporter.AddEntry(zip, "xl/drawings/_rels/drawing1.xml.rels", DrawingRels(charts));
                    for (int i = 0; i < charts.Count; i++)
                        XlsxExporter.AddEntry(zip, "xl/charts/chart" + (i + 1) + ".xml", charts[i].Xml);
                }
            }
            ms.Position = 0;
            return ms;
        }

        private static List<QuestionStats> Filtrar(List<QuestionStats> questions, IReadOnlyCollection<string> linkIds)
        {
            if (linkIds == null || linkIds.Count == 0) return questions;
            var set = new HashSet<string>(linkIds, System.StringComparer.OrdinalIgnoreCase);
            return questions.Where(q => set.Contains(q.LinkId)).ToList();
        }

        // ---- packaging (Content Types / rels / workbook) --------------------------------

        private static string ContentTypes(int chartCount)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
            sb.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
            sb.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
            sb.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
            sb.Append("<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            sb.Append("<Override PartName=\"/xl/worksheets/sheet2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            sb.Append("<Override PartName=\"/xl/worksheets/sheet3.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            if (chartCount > 0)
            {
                sb.Append("<Override PartName=\"/xl/worksheets/sheet4.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
                sb.Append("<Override PartName=\"/xl/drawings/drawing1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawing+xml\"/>");
                for (int i = 1; i <= chartCount; i++)
                    sb.Append("<Override PartName=\"/xl/charts/chart").Append(i)
                      .Append(".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawingml.chart+xml\"/>");
            }
            sb.Append("</Types>");
            return sb.ToString();
        }

        private static string RootRels() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>";

        private static string Workbook(bool conGraficos)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" ")
              .Append("xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
            // Categorias/Histogramas solo existen para alimentar los graficos por formula;
            // si hay graficos se ocultan para no duplicar en pantalla lo que el grafico ya
            // muestra -siguen ahi por si alguien quiere los numeros crudos ("Mostrar hoja")-.
            var datosOcultos = conGraficos ? " state=\"hidden\"" : "";
            sb.Append("<sheets>");
            sb.Append("<sheet name=\"Resumen\" sheetId=\"1\" r:id=\"rId1\"/>");
            sb.Append("<sheet name=\"Categorias\" sheetId=\"2\" r:id=\"rId2\"").Append(datosOcultos).Append("/>");
            sb.Append("<sheet name=\"Histogramas\" sheetId=\"3\" r:id=\"rId3\"").Append(datosOcultos).Append("/>");
            if (conGraficos) sb.Append("<sheet name=\"Graficos\" sheetId=\"4\" r:id=\"rId4\"/>");
            sb.Append("</sheets>");
            // Sin esto, algunos Excel dejan los graficos en blanco hasta un recalculo manual
            // porque el archivo no fue generado por Excel y no trae marcado que hace falta.
            sb.Append("<calcPr calcId=\"0\" fullCalcOnLoad=\"1\"/>");
            sb.Append("</workbook>");
            return sb.ToString();
        }

        private static string WorkbookRels(bool conGraficos)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            sb.Append("<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>");
            sb.Append("<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet2.xml\"/>");
            sb.Append("<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet3.xml\"/>");
            if (conGraficos) sb.Append("<Relationship Id=\"rId4\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet4.xml\"/>");
            sb.Append("</Relationships>");
            return sb.ToString();
        }

        // ---- hoja Resumen -----------------------------------------------------------------

        private static readonly string[] ComunesHeaders =
            { "Pregunta", "Tipo", "Respondidas", "Faltantes", "Completitud %" };
        private static readonly string[] NumericasHeaders =
            { "n", "Media", "Mediana", "Desvio", "Minimo", "Maximo", "P25", "P75" };
        private static readonly string[] CategoricasHeaders =
            { "Categoria mas frecuente", "% de esa categoria", "Categorias distintas" };

        /// <summary>
        /// Tres bloques separados -numéricas, categóricas, resto- en vez de una tabla ancha
        /// con columnas vacías según el tipo de cada fila: mezcladas se leían como
        /// "incompletas" (una fila usaba 8 columnas de golpe, la de al lado 3 distintas).
        /// Separadas, cada bloque es una tabla compacta y completa en sus propias columnas.
        /// Las columnas se anchan al contenido: el ancho por defecto de Excel corta textos
        /// como "Categoria mas frecuente" o preguntas largas.
        /// </summary>
        private static string ResumenSheet(List<QuestionStats> questions)
        {
            var filas = new List<string[]>();

            AgregarBloque(filas, "Preguntas numericas",
                questions.Where(q => q.Kind == "numeric").ToList(),
                ComunesHeaders.Concat(NumericasHeaders).ToArray(),
                q => Comunes(q).Concat(new[]
                {
                    q.N.ToString(Ar), Num(q.Mean), Num(q.Median), Num(q.StdDev), Num(q.Min), Num(q.Max), Num(q.P25), Num(q.P75)
                }).ToArray());

            AgregarBloque(filas, "Preguntas categoricas",
                questions.Where(q => q.Kind == "categorical").ToList(),
                ComunesHeaders.Concat(CategoricasHeaders).ToArray(),
                q =>
                {
                    var moda = q.Categories != null && q.Categories.Count > 0 ? q.Categories[0] : null; // ya viene ordenada desc
                    return Comunes(q).Concat(new[]
                    {
                        moda?.Label ?? "", moda != null ? moda.Pct.ToString(Ar) : "",
                        q.Categories != null ? q.Categories.Count.ToString(Ar) : ""
                    }).ToArray();
                });

            AgregarBloque(filas, "Otras preguntas (texto/fecha)",
                questions.Where(q => q.Kind != "numeric" && q.Kind != "categorical").ToList(),
                ComunesHeaders, Comunes);

            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            sb.Append(ColsXml(filas));
            sb.Append("<sheetData>");
            for (int i = 0; i < filas.Count; i++) AppendRow(sb, i + 1, filas[i]);
            sb.Append("</sheetData></worksheet>");
            return sb.ToString();
        }

        private static string[] Comunes(QuestionStats q) => new[]
        {
            q.Text, q.Type, q.Answered.ToString(Ar), q.Missing.ToString(Ar), q.CompletenessPct.ToString(Ar)
        };

        /// <summary>Titulo + encabezado + filas, con una fila en blanco antes si no es el primer bloque.</summary>
        private static void AgregarBloque(List<string[]> filas, string titulo, List<QuestionStats> qs,
            string[] headers, System.Func<QuestionStats, string[]> fila)
        {
            if (qs.Count == 0) return;
            if (filas.Count > 0) filas.Add(new string[0]); // separador en blanco entre bloques

            filas.Add(new[] { titulo });
            filas.Add(headers);
            foreach (var q in qs) filas.Add(fila(q));
        }

        /// <summary>
        /// Ancho por columna = el texto más largo que le tocó, con un margen. Sin esto, Excel
        /// abre con el ancho por defecto (~8.43) y corta cualquier encabezado o pregunta larga.
        /// </summary>
        private static string ColsXml(List<string[]> filas)
        {
            var anchos = new List<int>();
            foreach (var fila in filas)
            {
                for (int c = 0; c < fila.Length; c++)
                {
                    var largo = (fila[c] ?? "").Length;
                    while (anchos.Count <= c) anchos.Add(0);
                    if (largo > anchos[c]) anchos[c] = largo;
                }
            }

            var sb = new StringBuilder("<cols>");
            for (int c = 0; c < anchos.Count; c++)
            {
                var ancho = System.Math.Min(60, System.Math.Max(8, anchos[c] + 2));
                sb.Append("<col min=\"").Append(c + 1).Append("\" max=\"").Append(c + 1)
                  .Append("\" width=\"").Append(ancho).Append("\" customWidth=\"1\"/>");
            }
            sb.Append("</cols>");
            return sb.ToString();
        }

        // ---- hojas de datos para los graficos (Categorias / Histogramas) ------------------

        /// <summary>
        /// Un bloque de filas contiguas de una pregunta dentro de una hoja de datos, con las
        /// etiquetas/valores ya resueltos para poder cachearlos en el grafico (ver PieChartXml/
        /// BarChartXml): sin cache, algunos Excel no dibujan el grafico hasta forzar un
        /// recalculo manual, y para quien lo recibe por mail eso es peor que no tener grafico.
        /// </summary>
        private class QuestionBlock
        {
            public string LinkId;
            public string Text;
            public int FirstRow; // 1-based, primera fila de datos de la pregunta
            public int LastRow;  // 1-based, ultima fila de datos de la pregunta
            public List<string> Labels;
            /// <summary>Solo en Categorias: "Etiqueta (n=Cantidad)", para la leyenda de la torta.</summary>
            public List<string> LabelsConN;
            public List<decimal> Values;
        }

        private static string CategoriasSheet(List<QuestionStats> questions, out List<QuestionBlock> blocks)
        {
            blocks = new List<QuestionBlock>();
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");

            // "Categoria (n)" es una columna aparte, no una alternativa a "Categoria": la
            // torta la usa para la leyenda -"Si (n=7)"- sin ensuciar la columna con la
            // categoria "pura" que el resto (filtros, la barra) sigue leyendo.
            AppendRow(sb, 1, new[] { "Pregunta", "Categoria", "Cantidad", "%", "Categoria (n)" });
            int r = 2;
            foreach (var q in questions.Where(q => q.Kind == "categorical" && q.Categories != null && q.Categories.Count > 0))
            {
                int first = r;
                foreach (var c in q.Categories)
                {
                    sb.Append("<row r=\"").Append(r).Append("\">");
                    sb.Append(XlsxExporter.TextCell(XlsxExporter.CellRef(0, r), q.Text));
                    sb.Append(XlsxExporter.TextCell(XlsxExporter.CellRef(1, r), c.Label));
                    sb.Append(NumCell(XlsxExporter.CellRef(2, r), c.Count));
                    sb.Append(NumCell(XlsxExporter.CellRef(3, r), (decimal)c.Pct));
                    sb.Append(XlsxExporter.TextCell(XlsxExporter.CellRef(4, r), c.Label + " (n=" + c.Count + ")"));
                    sb.Append("</row>");
                    r++;
                }
                blocks.Add(new QuestionBlock
                {
                    LinkId = q.LinkId, Text = q.Text, FirstRow = first, LastRow = r - 1,
                    Labels = q.Categories.Select(c => c.Label).ToList(),
                    LabelsConN = q.Categories.Select(c => c.Label + " (n=" + c.Count + ")").ToList(),
                    Values = q.Categories.Select(c => (decimal)c.Count).ToList()
                });
            }

            sb.Append("</sheetData></worksheet>");
            return sb.ToString();
        }

        private static string HistogramasSheet(List<QuestionStats> questions, out List<QuestionBlock> blocks)
        {
            blocks = new List<QuestionBlock>();
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");

            AppendRow(sb, 1, new[] { "Pregunta", "Rango", "Cantidad" });
            int r = 2;
            foreach (var q in questions.Where(q => q.Kind == "numeric" && q.Histogram != null && q.Histogram.Count > 0))
            {
                int first = r;
                foreach (var b in q.Histogram)
                {
                    sb.Append("<row r=\"").Append(r).Append("\">");
                    sb.Append(XlsxExporter.TextCell(XlsxExporter.CellRef(0, r), q.Text));
                    sb.Append(XlsxExporter.TextCell(XlsxExporter.CellRef(1, r), Rango(b)));
                    sb.Append(NumCell(XlsxExporter.CellRef(2, r), b.Count));
                    sb.Append("</row>");
                    r++;
                }
                blocks.Add(new QuestionBlock
                {
                    LinkId = q.LinkId, Text = q.Text, FirstRow = first, LastRow = r - 1,
                    Labels = q.Histogram.Select(Rango).ToList(),
                    Values = q.Histogram.Select(b => (decimal)b.Count).ToList()
                });
            }

            sb.Append("</sheetData></worksheet>");
            return sb.ToString();
        }

        private static string Rango(HistogramBin b) =>
            b.From == b.To ? b.From.ToString(Ar) : b.From.ToString(Ar) + "-" + b.To.ToString(Ar);

        private static void AppendRow(StringBuilder sb, int rowIdx, IReadOnlyList<string> values)
        {
            sb.Append("<row r=\"").Append(rowIdx).Append("\">");
            for (int c = 0; c < values.Count; c++)
                sb.Append(XlsxExporter.TextCell(XlsxExporter.CellRef(c, rowIdx), values[c] ?? ""));
            sb.Append("</row>");
        }

        private static string Num(decimal? v) => v.HasValue ? v.Value.ToString(Ar) : "";

        /// <summary>
        /// Celda numérica real (sin t="inlineStr"): las columnas Cantidad/% de Categorias e
        /// Histogramas las lee un gráfico via numRef, y si quedan como texto Excel las
        /// interpreta como 0 -el bug que dejaba los gráficos con las categorías bien pero
        /// todos los valores en cero-. El valor va en InvariantCulture porque así lo exige
        /// el formato de celda OOXML (punto decimal), independiente de la cultura de display.
        /// </summary>
        private static string NumCell(string reference, decimal value) =>
            "<c r=\"" + reference + "\"><v>" + value.ToString(CultureInfo.InvariantCulture) + "</v></c>";

        // ---- hoja Graficos + drawing + charts ----------------------------------------------

        private class ChartPart
        {
            public string Xml;
        }

        /// <summary>
        /// Categóricas: torta o barra según lo que la persona haya elegido en pantalla para
        /// cada pregunta (barra si no hay entrada). Histogramas: siempre barra, como en
        /// pantalla -no hay toggle de tipo para las numéricas-.
        /// </summary>
        private static List<ChartPart> BuildCharts(List<QuestionBlock> categoriaBlocks, List<QuestionBlock> histogramaBlocks,
            IDictionary<string, string> chartTypes)
        {
            var charts = new List<ChartPart>();
            foreach (var b in categoriaBlocks)
            {
                string tipo;
                var esTorta = chartTypes != null && b.LinkId != null &&
                    chartTypes.TryGetValue(b.LinkId, out tipo) && tipo == "pie";
                charts.Add(new ChartPart { Xml = esTorta ? PieChartXml(b, "Categorias") : BarChartXml(b, "Categorias") });
            }
            foreach (var b in histogramaBlocks)
                charts.Add(new ChartPart { Xml = BarChartXml(b, "Histogramas") });
            return charts;
        }

        private static string GraficosSheet() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
            "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            "<sheetData/><drawing r:id=\"rId1\"/></worksheet>";

        private static string Sheet4Rels() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing\" Target=\"../drawings/drawing1.xml\"/>" +
            "</Relationships>";

        /// <summary>Un chart apilado cada 16 filas, ancho de 8 columnas.</summary>
        private static string Drawing(List<ChartPart> charts)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<xdr:wsDr xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" ")
              .Append("xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">");

            for (int i = 0; i < charts.Count; i++)
            {
                int rowFrom = i * 16;
                int rowTo = rowFrom + 15;
                sb.Append("<xdr:twoCellAnchor>");
                sb.Append("<xdr:from><xdr:col>0</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>").Append(rowFrom).Append("</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>");
                sb.Append("<xdr:to><xdr:col>8</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>").Append(rowTo).Append("</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>");
                sb.Append("<xdr:graphicFrame macro=\"\">");
                sb.Append("<xdr:nvGraphicFramePr><xdr:cNvPr id=\"").Append(i + 2).Append("\" name=\"Chart ").Append(i + 1).Append("\"/><xdr:cNvGraphicFramePr/></xdr:nvGraphicFramePr>");
                sb.Append("<xdr:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"4800600\" cy=\"2857500\"/></xdr:xfrm>");
                sb.Append("<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/chart\">");
                sb.Append("<c:chart xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" ")
                  .Append("xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" r:id=\"rId").Append(i + 1).Append("\"/>");
                sb.Append("</a:graphicData></a:graphic>");
                sb.Append("</xdr:graphicFrame><xdr:clientData/>");
                sb.Append("</xdr:twoCellAnchor>");
            }

            sb.Append("</xdr:wsDr>");
            return sb.ToString();
        }

        private static string DrawingRels(List<ChartPart> charts)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            for (int i = 0; i < charts.Count; i++)
                sb.Append("<Relationship Id=\"rId").Append(i + 1).Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart\" ")
                  .Append("Target=\"../charts/chart").Append(i + 1).Append(".xml\"/>");
            sb.Append("</Relationships>");
            return sb.ToString();
        }

        /// <summary>
        /// La torta muestra solo el % en cada porción -"n; %" en un sector chico de torta se
        /// lee mal, es más ruido que dato-. La cantidad no desaparece, se muda a la leyenda:
        /// "Sí (n=7)" en vez de "Sí", así se sigue viendo sin ensuciar la porción.
        /// </summary>
        private static string PieChartXml(QuestionBlock b, string sheetName)
        {
            // Ojo: la formula manda por sobre el cache -Excel recalcula al abrir (ver
            // fullCalcOnLoad en Workbook()) y descarta lo que el cache diga-, asi que para que
            // la leyenda muestre "Si (n=7)" la formula tiene que apuntar a una celda que
            // REALMENTE diga eso. Por eso la columna E ("Categoria (n)") en vez de cachear un
            // texto que la celda B no respalda.
            var catRange = "'" + sheetName + "'!$E$" + b.FirstRow + ":$E$" + b.LastRow;
            var valRange = "'" + sheetName + "'!$C$" + b.FirstRow + ":$C$" + b.LastRow;

            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<c:chartSpace ").Append(ChartNamespaces()).Append(">");
            sb.Append("<c:chart>");
            AppendTitle(sb, b.Text);
            sb.Append("<c:plotArea><c:layout/>");
            sb.Append("<c:pieChart><c:varyColors val=\"1\"/>");
            sb.Append("<c:ser><c:idx val=\"0\"/><c:order val=\"0\"/>");
            AppendDLbls(sb, showVal: false, showPercent: true);
            AppendCat(sb, catRange, b.LabelsConN);
            AppendVal(sb, valRange, b.Values);
            sb.Append("</c:ser><c:firstSliceAng val=\"0\"/></c:pieChart>");
            sb.Append("</c:plotArea>");
            sb.Append("<c:legend><c:legendPos val=\"r\"/><c:overlay val=\"0\"/></c:legend>");
            sb.Append("<c:plotVisOnly val=\"1\"/>");
            sb.Append("</c:chart></c:chartSpace>");
            return sb.ToString();
        }

        private static string BarChartXml(QuestionBlock b, string sheetName)
        {
            var catRange = "'" + sheetName + "'!$B$" + b.FirstRow + ":$B$" + b.LastRow;
            var valRange = "'" + sheetName + "'!$C$" + b.FirstRow + ":$C$" + b.LastRow;

            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<c:chartSpace ").Append(ChartNamespaces()).Append(">");
            sb.Append("<c:chart>");
            AppendTitle(sb, b.Text);
            sb.Append("<c:plotArea><c:layout/>");
            sb.Append("<c:barChart><c:barDir val=\"col\"/><c:grouping val=\"clustered\"/><c:varyColors val=\"0\"/>");
            sb.Append("<c:ser><c:idx val=\"0\"/><c:order val=\"0\"/>");
            AppendDLbls(sb, showVal: true, showPercent: true);
            AppendCat(sb, catRange, b.Labels);
            AppendVal(sb, valRange, b.Values);
            sb.Append("</c:ser>");
            sb.Append("<c:axId val=\"111111111\"/><c:axId val=\"222222222\"/>");
            sb.Append("</c:barChart>");
            sb.Append("<c:catAx><c:axId val=\"111111111\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling>")
              .Append("<c:delete val=\"0\"/><c:axPos val=\"b\"/><c:crossAx val=\"222222222\"/></c:catAx>");
            sb.Append("<c:valAx><c:axId val=\"222222222\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling>")
              .Append("<c:delete val=\"0\"/><c:axPos val=\"l\"/><c:crossAx val=\"111111111\"/></c:valAx>");
            sb.Append("</c:plotArea>");
            sb.Append("<c:plotVisOnly val=\"1\"/>");
            sb.Append("</c:chart></c:chartSpace>");
            return sb.ToString();
        }

        private static string ChartNamespaces() =>
            "xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" " +
            "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
            "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"";

        private static void AppendTitle(StringBuilder sb, string title)
        {
            sb.Append("<c:title><c:tx><c:rich><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>")
              .Append(XlsxExporter.Escape(title)).Append("</a:t></a:r></a:p></c:rich></c:tx><c:overlay val=\"0\"/></c:title>");
            sb.Append("<c:autoTitleDeleted val=\"0\"/>");
        }

        /// <summary>
        /// Cantidad y porcentaje sobre cada porción/barra: pedido explícito -sin esto, el
        /// gráfico se ve pero hay que ir a mirar la hoja de datos para saber cuánto es cada
        /// parte-. Mismos números que ya tiene la hoja de datos, no un cálculo nuevo.
        /// </summary>
        private static void AppendDLbls(StringBuilder sb, bool showVal, bool showPercent)
        {
            sb.Append("<c:dLbls>");
            sb.Append("<c:showLegendKey val=\"0\"/><c:showVal val=\"").Append(showVal ? "1" : "0").Append("\"/><c:showCatName val=\"0\"/>");
            sb.Append("<c:showSerName val=\"0\"/><c:showPercent val=\"").Append(showPercent ? "1" : "0").Append("\"/><c:showBubbleSize val=\"0\"/>");
            sb.Append("</c:dLbls>");
        }

        /// <summary>
        /// Categorias con cache de strings: sin el cache, algunos Excel dejan el grafico en
        /// blanco hasta que se fuerza un recalculo manual -inaceptable para un archivo que se
        /// va a compartir y mirar tal cual llega-.
        /// </summary>
        private static void AppendCat(StringBuilder sb, string range, List<string> labels)
        {
            sb.Append("<c:cat><c:strRef><c:f>").Append(Xml(range)).Append("</c:f><c:strCache>");
            sb.Append("<c:ptCount val=\"").Append(labels.Count).Append("\"/>");
            for (int i = 0; i < labels.Count; i++)
                sb.Append("<c:pt idx=\"").Append(i).Append("\"><c:v>").Append(XlsxExporter.Escape(labels[i])).Append("</c:v></c:pt>");
            sb.Append("</c:strCache></c:strRef></c:cat>");
        }

        private static void AppendVal(StringBuilder sb, string range, List<decimal> values)
        {
            sb.Append("<c:val><c:numRef><c:f>").Append(Xml(range)).Append("</c:f><c:numCache>");
            sb.Append("<c:formatCode>General</c:formatCode>");
            sb.Append("<c:ptCount val=\"").Append(values.Count).Append("\"/>");
            for (int i = 0; i < values.Count; i++)
                sb.Append("<c:pt idx=\"").Append(i).Append("\"><c:v>").Append(values[i].ToString(CultureInfo.InvariantCulture)).Append("</c:v></c:pt>");
            sb.Append("</c:numCache></c:numRef></c:val>");
        }

        private static string Xml(string s) => XlsxExporter.Escape(s);
    }
}
