using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using WebApplication1.Data;
using WebApplication1.Models;

namespace WebApplication1.Controllers
{
    [Authorize(Roles = "Admin")]
    public class UsersController : Controller
    {
        private readonly ApplicationDbContext _context;

        public UsersController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: /Users
        public async Task<IActionResult> Index(string? searchString, string? roleFilter)
        {
            var allUsers = await _context.Users.ToListAsync();
            var totalUsers = allUsers.Count;
            var adminCount = allUsers.Count(u => u.Role == "Admin");
            var customerCount = allUsers.Count(u => u.Role == "Customer");
            var managerCount = allUsers.Count(u => u.Role == "Manager");

            var query = _context.Users.AsQueryable();

            // Tìm kiếm theo Tên người dùng hoặc Email
            if (!string.IsNullOrWhiteSpace(searchString))
            {
                var search = searchString.Trim().ToLower();
                query = query.Where(u => u.Username.ToLower().Contains(search) || u.Email.ToLower().Contains(search));
            }

            // Lọc theo Vai trò
            if (!string.IsNullOrWhiteSpace(roleFilter) && roleFilter != "All")
            {
                query = query.Where(u => u.Role == roleFilter);
            }

            var filteredUsers = await query.OrderByDescending(u => u.Id).ToListAsync();

            var viewModel = new UserManagementViewModel
            {
                Users = filteredUsers,
                TotalUsers = totalUsers,
                AdminCount = adminCount,
                CustomerCount = customerCount,
                ManagerCount = managerCount,
                SearchString = searchString,
                RoleFilter = string.IsNullOrWhiteSpace(roleFilter) ? "All" : roleFilter
            };

            return View(viewModel);
        }

        // POST: /Users/CreateUser
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateUser(string username, string email, string password, string role)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                TempData["Error"] = "Vui lòng điền đầy đủ Tên đăng nhập, Email và Mật khẩu!";
                return RedirectToAction(nameof(Index));
            }

            if (password.Length < 6)
            {
                TempData["Error"] = "Mật khẩu phải có độ dài từ 6 ký tự trở lên!";
                return RedirectToAction(nameof(Index));
            }

            var cleanUsername = username.Trim();
            var cleanEmail = email.Trim();

            if (await _context.Users.AnyAsync(u => u.Username.ToLower() == cleanUsername.ToLower()))
            {
                TempData["Error"] = $"Tên đăng nhập '{cleanUsername}' đã được sử dụng!";
                return RedirectToAction(nameof(Index));
            }

            if (await _context.Users.AnyAsync(u => u.Email.ToLower() == cleanEmail.ToLower()))
            {
                TempData["Error"] = $"Email '{cleanEmail}' đã được đăng ký trong hệ thống!";
                return RedirectToAction(nameof(Index));
            }

            var validRoles = new[] { "Admin", "Customer", "Manager" };
            if (string.IsNullOrWhiteSpace(role) || !validRoles.Contains(role))
            {
                role = "Customer";
            }

            var newUser = new User
            {
                Username = cleanUsername,
                Email = cleanEmail,
                Password = HashPassword(password),
                Role = role
            };

            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Thêm tài khoản '{cleanUsername}' với vai trò '{role}' thành công!";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Users/UpdateRole
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateRole(int id, string newRole)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                TempData["Error"] = "Không tìm thấy người dùng cần phân quyền!";
                return RedirectToAction(nameof(Index));
            }

            var validRoles = new[] { "Admin", "Customer", "Manager" };
            if (!validRoles.Contains(newRole))
            {
                TempData["Error"] = "Vai trò được chọn không hợp lệ!";
                return RedirectToAction(nameof(Index));
            }

            var currentAdmin = User.Identity?.Name;

            // Kiểm tra: Không cho admin tự hạ quyền chính mình
            if (user.Username.Equals(currentAdmin, StringComparison.OrdinalIgnoreCase) && newRole != "Admin")
            {
                TempData["Error"] = "Bạn không thể tự hạ quyền Quản trị viên (Admin) của chính mình!";
                return RedirectToAction(nameof(Index));
            }

            // Kiểm tra: Không cho hạ quyền nếu là Admin duy nhất trong hệ thống
            if (user.Role == "Admin" && newRole != "Admin")
            {
                var adminCount = await _context.Users.CountAsync(u => u.Role == "Admin");
                if (adminCount <= 1)
                {
                    TempData["Error"] = "Không thể hạ quyền Quản trị viên này vì đây là Admin duy nhất của hệ thống!";
                    return RedirectToAction(nameof(Index));
                }
            }

            var oldRole = user.Role;
            user.Role = newRole;
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Đã cập nhật quyền cho tài khoản '{user.Username}' từ '{oldRole}' sang '{newRole}' thành công!";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Users/ResetPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(int id, string newPassword)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                TempData["Error"] = "Không tìm thấy người dùng!";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            {
                TempData["Error"] = "Mật khẩu mới phải có tối thiểu 6 ký tự!";
                return RedirectToAction(nameof(Index));
            }

            user.Password = HashPassword(newPassword);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Đã đặt lại mật khẩu cho tài khoản '{user.Username}' thành công!";
            return RedirectToAction(nameof(Index));
        }

        // POST: /Users/Delete
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                TempData["Error"] = "Không tìm thấy người dùng cần xóa!";
                return RedirectToAction(nameof(Index));
            }

            var currentAdmin = User.Identity?.Name;

            // Không cho xóa tài khoản đang đăng nhập
            if (user.Username.Equals(currentAdmin, StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "Bạn không thể tự xóa tài khoản Quản trị viên đang đăng nhập!";
                return RedirectToAction(nameof(Index));
            }

            // Không cho xóa Admin cuối cùng
            if (user.Role == "Admin")
            {
                var adminCount = await _context.Users.CountAsync(u => u.Role == "Admin");
                if (adminCount <= 1)
                {
                    TempData["Error"] = "Không thể xóa Quản trị viên duy nhất còn lại trong hệ thống!";
                    return RedirectToAction(nameof(Index));
                }
            }

            // Kiểm tra ràng buộc đơn đặt phòng
            var hasBookings = await _context.Bookings.AnyAsync(b => b.UserId == id);
            if (hasBookings)
            {
                TempData["Error"] = $"Không thể xóa tài khoản '{user.Username}' vì người dùng này đã có lịch sử đặt phòng trong hệ thống!";
                return RedirectToAction(nameof(Index));
            }

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Đã xóa tài khoản '{user.Username}' thành công!";
            return RedirectToAction(nameof(Index));
        }

        // Hashing chuẩn SHA256 tương thích hoàn toàn với AccountController
        private string HashPassword(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(password);
                var hash = sha256.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
        }
    }
}
