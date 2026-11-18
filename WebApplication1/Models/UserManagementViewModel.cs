using System.Collections.Generic;

namespace WebApplication1.Models
{
    public class UserManagementViewModel
    {
        public List<User> Users { get; set; } = new List<User>();
        public int TotalUsers { get; set; }
        public int AdminCount { get; set; }
        public int CustomerCount { get; set; }
        public int ManagerCount { get; set; }
        public string? SearchString { get; set; }
        public string RoleFilter { get; set; } = "All";
    }
}
