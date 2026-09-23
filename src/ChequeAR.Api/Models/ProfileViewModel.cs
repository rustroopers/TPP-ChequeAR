using System.Collections.Generic;

namespace ChequeAR.Api.Models
{
    public class ProfileViewModel
    {
        public string UserName { get; set; }
        public string DisplayName { get; set; }
        public string Email { get; set; }
        public List<string> Roles { get; set; } = new List<string>();
        public List<SpecialtyChipView> Specialties { get; set; } = new List<SpecialtyChipView>();
    }

    public class SpecialtyChipView
    {
        public string Code { get; set; }
        public string Label { get; set; }
        public string Color { get; set; }
    }
}
