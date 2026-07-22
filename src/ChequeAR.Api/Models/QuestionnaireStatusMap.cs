using ChequeAR.Domain.Enums;

namespace ChequeAR.Api.Models
{
    /// <summary>
    /// Traduce entre el estado de dominio (FHIR: draft/active/retired) y el vocabulario
    /// de la UI (borrador/publicado/archivado). Sin esto, un cuestionario publicado
    /// (Active) no coincide con "published" y el listado lo muestra como archivado.
    /// </summary>
    public static class QuestionnaireStatusMap
    {
        public static string ToUi(QuestionnaireStatus status)
        {
            switch (status)
            {
                case QuestionnaireStatus.Active: return "published";
                case QuestionnaireStatus.Retired: return "archived";
                default: return "draft";
            }
        }

        public static QuestionnaireStatus? FromUi(string ui)
        {
            if (string.IsNullOrWhiteSpace(ui)) return null;
            switch (ui.Trim().ToLowerInvariant())
            {
                case "published":
                case "active": return QuestionnaireStatus.Active;
                case "archived":
                case "retired": return QuestionnaireStatus.Retired;
                case "draft": return QuestionnaireStatus.Draft;
                default: return null;
            }
        }
    }
}
