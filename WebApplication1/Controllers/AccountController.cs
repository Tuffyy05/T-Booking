using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace WebApplication1.Controllers
{
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AccountController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Mở giao diện Đăng ký
        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        // POST: Xử lý Đăng ký
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(string username, string email, string password, string confirmPassword)
        {
            if (password != confirmPassword)
            {
                ViewBag.Error = "Mật khẩu nhập lại không khớp!";
                return View();
            }

            if (await _context.Users.AnyAsync(u => u.Username == username))
            {
                ViewBag.Error = "Tên đăng nhập đã tồn tại!";
                return View();
            }

            var user = new User
            {
                Username = username,
                Email = email,
                Password = HashPassword(password), // Mã hóa trước khi lưu
                Role = "Customer"
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Đăng ký thành công! Vui lòng đăng nhập.";
            return RedirectToAction("Login");
        }

        // GET: Mở giao diện Đăng nhập
        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        // POST: Xử lý Đăng nhập
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string username, string password)
        {
            var hashedPassword = HashPassword(password);
            var user = await _context.Users.FirstOrDefaultAsync(u =>
                (u.Username == username || u.Email == username) &&
                 u.Password == hashedPassword);

            if (user == null)
            {
                ViewBag.Error = "Tên đăng nhập hoặc mật khẩu không đúng!";
                return View();
            }

            // Tạo "Thẻ bài" (Claims) lưu thông tin người dùng
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role)
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

            // Cấp Cookie đăng nhập cho trình duyệt
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity));

            // ========================================================
            // PHÂN LUỒNG CHUYỂN HƯỚNG TỰ ĐỘNG SAU KHI ĐĂNG NHẬP
            // ========================================================
            if (user.Role == "Admin")
            {
                // Nếu là Admin, vào thẳng trang Thống kê (Dashboard)
                return RedirectToAction("AdminDashboard", "Bookings");
            }
            else
            {
                // Nếu là User thường, vào Trang chủ để xem danh sách phòng
                return RedirectToAction("Index", "Home");
            }
        }

        // Xử lý Đăng xuất
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home");
        }

        // ==========================================
        // TÍNH NĂNG ĐỔI MẬT KHẨU TỪ DASHBOARD
        // ==========================================
        [HttpPost]
        [Authorize]
        public async Task<IActionResult> ChangePassword(string newPassword)
        {
            // Lấy tên người dùng đang đăng nhập
            var username = User.Identity.Name;
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);

            if (user == null) return Unauthorized();

            // Kiểm tra mật khẩu (đề phòng ai đó cố tình vượt qua lớp check HTML)
            if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 6)
            {
                TempData["Error"] = "Mật khẩu mới phải có ít nhất 6 ký tự.";
                return RedirectToAction("Dashboard", "Bookings");
            }

            // Gọi hàm HashPassword để mã hóa mật khẩu mới trước khi lưu
            user.Password = HashPassword(newPassword);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Tuyệt vời! Bạn đã cập nhật mật khẩu thành công.";
            return RedirectToAction("Dashboard", "Bookings");
        }

        // Hàm hỗ trợ mã hóa mật khẩu
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