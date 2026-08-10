using System.Collections.Generic;

namespace ChequeAR.Application.Services
{
    /// <summary>
    /// Vista previa de "pasar una ficha en progreso a la versión vigente del cuestionario":
    /// qué respuestas se conservan, qué preguntas nuevas aparecen y qué respuestas se
    /// descartarían (preguntas que la versión nueva eliminó). El médico decide con esto a la
    /// vista si actualiza o sigue en la versión con la que venía.
    /// </summary>
    public class RetargetPreview
    {
        public int FromVersion { get; set; }
        public int ToVersion { get; set; }
        /// <summary>Ya está en la versión vigente: no hay nada para actualizar.</summary>
        public bool AlreadyCurrent { get; set; }

        /// <summary>Preguntas presentes en ambas versiones: la respuesta viaja intacta.</summary>
        public List<RetargetItem> Carried { get; set; } = new List<RetargetItem>();
        /// <summary>Preguntas nuevas de la versión vigente: quedan para responder.</summary>
        public List<RetargetItem> Added { get; set; } = new List<RetargetItem>();
        /// <summary>Respuestas que se perderían: preguntas que la versión nueva eliminó y que ya estaban respondidas.</summary>
        public List<RetargetItem> Dropped { get; set; } = new List<RetargetItem>();
    }

    public class RetargetItem
    {
        public string LinkId { get; set; }
        public string Text { get; set; }
        /// <summary>Sólo en "Carried": el enunciado cambió entre versiones (misma pregunta, otro texto).</summary>
        public bool Changed { get; set; }
        /// <summary>Sólo en "Dropped": el valor que se descartaría.</summary>
        public string Answer { get; set; }
    }
}
