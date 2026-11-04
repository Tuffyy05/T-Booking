using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;
using System.Diagnostics;

namespace WebApplication1.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;

        public HomeController(ApplicationDbContext context)
        {
            _context = context;
        }

        // Bổ sung tham số để nhận dữ liệu từ form tìm kiếm
        public async Task<IActionResult> Index(string searchString, string roomType, int? minCapacity)
        {
            // 1. Khởi tạo Query lấy danh sách phòng
            var roomsQuery = _context.Rooms.AsQueryable();

            // 2. Truy vấn danh sách "Loại phòng" loại bỏ các giá trị rỗng/null để đưa lên Dropdown list
            var roomTypes = await _context.Rooms
                                        .Where(r => !string.IsNullOrEmpty(r.RoomType))
                                        .Select(r => r.RoomType)
                                        .Distinct()
                                        .ToListAsync();

            ViewBag.RoomTypes = new SelectList(roomTypes);

            // 3. Áp dụng các bộ lọc (nếu người dùng có nhập)
            if (!string.IsNullOrEmpty(searchString))
            {
                roomsQuery = roomsQuery.Where(r => r.Name.Contains(searchString));
            }

            if (!string.IsNullOrEmpty(roomType))
            {
                roomsQuery = roomsQuery.Where(r => r.RoomType == roomType);
            }

            if (minCapacity.HasValue && minCapacity > 0)
            {
                roomsQuery = roomsQuery.Where(r => r.Capacity >= minCapacity);
            }

            // Lưu lại các từ khóa người dùng vừa nhập để hiển thị lại trên thanh tìm kiếm
            ViewBag.CurrentFilter = searchString;
            ViewBag.CurrentRoomType = roomType;
            ViewBag.CurrentMinCapacity = minCapacity;

            // 4. Chạy Query và trả kết quả ra giao diện
            var rooms = await roomsQuery.ToListAsync();
            return View(rooms);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}