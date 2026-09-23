using System;
using System.Collections.Generic;

namespace ChequeAR.Api.Models
{
    /// <summary>Listado del catálogo. Espejo de UserListViewModel.</summary>
    public class VariableListViewModel
    {
        public string SearchTerm { get; set; }
        public string TypeFilter { get; set; }
        public string StatusFilter { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; }
        public int Total { get; set; }
        public List<VariableListItem> Variables { get; } = new List<VariableListItem>();
    }

    public class VariableListItem
    {
        public Guid Id { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }

        /// <summary>Etiqueta legible del tipo ("Opción única"), no el código interno.</summary>
        public string TypeLabel { get; set; }

        public string Unit { get; set; }
        public string Range { get; set; }
        public string SnomedCode { get; set; }
        public string SnomedDisplay { get; set; }
        public bool IsActive { get; set; }
        public string CreatedAt { get; set; }
    }

    /// <summary>
    /// El formulario NO tiene un campo por propiedad: lo renderiza la isla `variable-form`,
    /// que postea el modelo completo serializado en PayloadJson. Las opciones de respuesta son
    /// una lista dinámica y los campos visibles dependen del tipo, así que un binder indexado
    /// (Options[0].Display, …) obligaría a reindexar a mano al borrar una del medio.
    ///
    /// Sigue siendo un POST MVC con antiforgery y no un fetch: así conserva el
    /// TempData["Toast"] + redirect del resto de las pantallas de administración.
    /// </summary>
    public class VariableFormViewModel
    {
        public Guid? Id { get; set; }
        public string PayloadJson { get; set; }
    }
}
