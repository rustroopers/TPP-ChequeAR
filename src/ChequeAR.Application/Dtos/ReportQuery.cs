using System;
using System.Collections.Generic;

namespace ChequeAR.Application.Dtos
{
    public class ReportQuery
    {
        public Guid QuestionnaireId { get; set; }
        public List<int> QuestionnaireVersions { get; set; }
        public FilterNode Filters { get; set; }
        public DateRange DateRange { get; set; }
        /// <summary>
        /// Usuarios que firmaron la ficha. Reemplaza al viejo `Specialties`, que no se podía
        /// honrar: la especialidad del paciente vive en las vistas del hospital, no en el
        /// esquema de ChequeAR, y la del cuestionario es redundante porque toda consulta ya
        /// está acotada a un QuestionnaireId. Un campo que nadie leía daba la impresión de
        /// filtrar sin filtrar nada.
        /// </summary>
        public List<string> Authors { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        public string Format { get; set; } // xlsx | csv
    }

    public class FilterNode
    {
        public string Logic { get; set; } // AND | OR
        public List<FilterPredicate> Predicates { get; set; }
    }

    public class FilterPredicate
    {
        public string LinkId { get; set; }
        public string Op { get; set; }
        public object Value { get; set; }
        public FilterNode Group { get; set; }
    }

    public class DateRange
    {
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
    }
}
