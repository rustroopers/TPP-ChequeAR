using System.Collections.Generic;

namespace ChequeAR.Application.Dtos
{
    public class AdminUserEdit
    {
        public string UserId { get; set; }
        public string DisplayName { get; set; }
        public bool IsActive { get; set; }
        public string Role { get; set; }

        public IReadOnlyList<string> Specialties { get; set; } = new List<string>();

        public int? ExpectedRevision { get; set; }
    }
}
