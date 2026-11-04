using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;
using System.Threading.Tasks;

namespace WebApplication1.Controllers
{
    // Bắt buộc đăng nhập VÀ phải có quyền Admin
    [Authorize(Roles = "Admin")]
    public class RoomsController : Controller
    {
        private readonly ApplicationDbContext _context;

        // Tiêm ApplicationDbContext vào thông qua Constructor
        public RoomsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: /Rooms/Index
        public async Task<IActionResult> Index()
        {
            var rooms = await _context.Rooms.ToListAsync();
            return View(rooms);
        }

        // GET: /Rooms/Create
        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        // POST: /Rooms/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Id,Name,Capacity,RoomType")] Room room)
        {
            ModelState.Remove("Version");
            ModelState.Remove("Bookings");

            if (ModelState.IsValid)
            {
                _context.Add(room);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(room);
        }

        // GET: /Rooms/Edit/5
        [HttpGet]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var room = await _context.Rooms.FindAsync(id);
            if (room == null) return NotFound();

            return View(room);
        }

        // POST: /Rooms/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        // Bắt buộc thêm "Version" vào Bind để nhận phiên bản hiện tại
        public async Task<IActionResult> Edit(int id, [Bind("Id,Name,Capacity,RoomType,Version")] Room room)
        {
            if (id != room.Id) return NotFound();

            // Chỉ remove Bookings, giữ lại Version để hệ thống tự động kiểm tra
            ModelState.Remove("Bookings");

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(room);
                    await _context.SaveChangesAsync();
                    return RedirectToAction(nameof(Index));
                }
                catch (DbUpdateConcurrencyException) // Bắt lỗi khóa lạc quan
                {
                    ModelState.AddModelError("", "Dữ liệu này vừa được cập nhật bởi một người khác. Vui lòng quay lại danh sách và thử lại để tránh ghi đè dữ liệu.");
                    return View(room);
                }
            }
            return View(room);
        }

        // GET: /Rooms/Delete/5
        [HttpGet]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var room = await _context.Rooms.FirstOrDefaultAsync(m => m.Id == id);
            if (room == null) return NotFound();

            return View(room);
        }

        // POST: /Rooms/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            // 1. Kiểm tra xem phòng này có bất kỳ đơn đặt nào (Pending, Approved...) hay không
            bool hasBookings = await _context.Bookings.AnyAsync(b => b.RoomId == id);

            if (hasBookings)
            {
                // Nếu có, chặn việc xóa và gửi thông báo lỗi về trang Index
                TempData["Error"] = "Không thể xóa! Phòng này đang có lịch đặt. Vui lòng hủy/xóa các đơn đặt của phòng này trước.";
                return RedirectToAction(nameof(Index));
            }

            // 2. Nếu không có đơn đặt nào, tiến hành xóa bình thường
            var room = await _context.Rooms.FindAsync(id);
            if (room != null)
            {
                _context.Rooms.Remove(room);
                await _context.SaveChangesAsync();

                // Thêm thông báo thành công cho xịn
                TempData["Success"] = "Đã xóa phòng thành công!";
            }
            return RedirectToAction(nameof(Index));
        }
        [HttpGet]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            // THÊM .Include(r => r.Facilities) ĐỂ KÉO KÈM DỮ LIỆU THIẾT BỊ
            var room = await _context.Rooms
                .Include(r => r.Facilities)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (room == null) return NotFound();

            return View(room);
        }
    }
}