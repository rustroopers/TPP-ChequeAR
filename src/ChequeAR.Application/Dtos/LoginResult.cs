using System.Collections.Generic;

namespace ChequeAR.Application.Dtos
{
    public class LoginResult
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public int ExpiresIn { get; set; }
        public UserInfo User { get; set; }
    }

    public class UserInfo
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public List<string> Roles { get; set; }
        public List<string> Specialties { get; set; }
    }
}
