using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Hubs;
using WebApplication1.Models;
using WebApplication1.Services; // Cần dùng EmailService
using QRCoder; // [MỚI] Thêm thư viện tạo mã QR

namespace WebApplication1.Controllers
{
    [Authorize] // Bắt buộc phải đăng nhập mới được truy cập Controller này (trừ những hàm có [AllowAnonymous])
    public class BookingsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<NotificationHub> _hubContext; // SignalR Hub
        private readonly EmailService _emailService; // Gửi Email tự động

        // [SỬA LỖI] CHỈ CÓ DUY NHẤT 1 CONSTRUCTOR nhận cả 3 tham số
        public BookingsController(ApplicationDbContext context, IHubContext<NotificationHub> hubContext, EmailService emailService)
        {
            _context = context;
            _hubContext = hubContext;
            _emailService = emailService;
        }

        // 1. Mở giao diện điền giờ đặt phòng
        [HttpGet]
        public async Task<IActionResult> Create(int roomId)
        {
            var room = await _context.Rooms.FindAsync(roomId);
            if (room == null) return NotFound();

            ViewBag.RoomName = room.Name;

            // XỬ LÝ LẤY THIẾT BỊ: Lấy thiết bị thuộc phòng hiện tại HOẶC thiết bị chung (RoomId == null)
            var facilities = await _context.Facilities
                .Where(f => f.RoomId == roomId || f.RoomId == null)
                .ToListAsync();
            ViewBag.Facilities = facilities;

            // XỬ LÝ LÀM SẠCH GIỜ: Lấy giờ hiện tại nhưng ép số giây và mili-giây về 0
            var now = DateTime.Now;
            var cleanStartTime = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);

            // Khởi tạo sẵn một mô hình với giờ đã làm sạch
            var booking = new Booking
            {
                RoomId = roomId,
                StartTime = cleanStartTime,
                EndTime = cleanStartTime.AddHours(2)
            };
            return View(booking);
        }

        // 2. Thuật toán xử lý khi bấm nút "Xác nhận Đặt"
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Booking booking, List<int> selectedFacilities)
        {
            // Lấy ID của người đang đăng nhập
            var username = User.Identity!.Name;
            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (currentUser == null) return Unauthorized();

            booking.UserId = currentUser.Id;
            booking.CreatedAt = DateTime.Now;
            booking.Status = "Pending"; // Mặc định là chờ Admin duyệt

            // Bỏ qua validate các thuộc tính liên kết phức tạp
            ModelState.Remove("Room");
            ModelState.Remove("User");
            ModelState.Remove("BookingFacilities"); // Tránh lỗi ModelState báo thiếu bảng trung gian

            // Kiểm tra tính hợp lệ của thời gian
            if (booking.StartTime >= booking.EndTime)
            {
                ModelState.AddModelError("", "Thời gian kết thúc phải sau thời gian bắt đầu.");
            }

            // ==========================================
            // THUẬT TOÁN CHỐNG TRÙNG LỊCH (QUAN TRỌNG)
            // ==========================================
            bool isConflict = await _context.Bookings.AnyAsync(b =>
                b.RoomId == booking.RoomId &&
                b.Status != "Rejected" && // Không tính những đơn đã bị từ chối
                (booking.StartTime < b.EndTime && booking.EndTime > b.StartTime) // Công thức giao nhau thời gian
            );

            if (isConflict)
            {
                ModelState.AddModelError("", "Phòng này đã có người đặt trong khoảng thời gian bạn chọn. Vui lòng đổi giờ khác.");
            }

            // Nếu không có lỗi gì thì lưu vào Database
            if (ModelState.IsValid)
            {
                // Bước 1: Lưu đơn đặt phòng trước để sinh ra booking.Id
                _context.Bookings.Add(booking);
                await _context.SaveChangesAsync();

                // Bước 2: Lưu các thiết bị VÀ SỐ LƯỢNG được người dùng chọn vào bảng trung gian
                if (selectedFacilities != null && selectedFacilities.Any())
                {
                    foreach (var facilityId in selectedFacilities)
                    {
                        // Lấy số lượng từ ô input tương ứng (có name dạng "quantity_{id}")
                        int qty = 1; // Mặc định là 1
                        if (int.TryParse(Request.Form[$"quantity_{facilityId}"], out int parsedQty))
                        {
                            qty = parsedQty;
                        }

                        var bookingFacility = new BookingFacility
                        {
                            BookingId = booking.Id,
                            FacilityId = facilityId,
                            Quantity = qty // Lưu số lượng thực tế khách nhập
                        };
                        _context.BookingFacilities.Add(bookingFacility);
                    }
                    await _context.SaveChangesAsync(); // Lưu thông tin thiết bị đã mượn
                }

                // ==========================================
                // BẮN THÔNG BÁO SIGNALR LÊN CHỈ CHO ADMIN
                // ==========================================
                await _hubContext.Clients.All.SendAsync("ReceiveNotification", $"Có đơn đặt phòng mới từ user '{username}' vào lúc {booking.StartTime.ToString("HH:mm")}");

                TempData["Success"] = "Gửi yêu cầu đặt phòng kèm thiết bị thành công! Đang chờ duyệt.";

                // Đổi lệnh chuyển hướng về thẳng Trang chủ (HomeController, hàm Index)
                return RedirectToAction("Index", "Home");
            }

            // Nếu có lỗi, trả lại giao diện kèm danh sách thiết bị để người dùng không bị mất dữ liệu form (Lấy cả thiết bị chung)
            var room = await _context.Rooms.FindAsync(booking.RoomId);
            ViewBag.RoomName = room?.Name;
            ViewBag.Facilities = await _context.Facilities.Where(f => f.RoomId == booking.RoomId || f.RoomId == null).ToListAsync();
            return View(booking);
        }

        // 3. Xem lịch sử đặt phòng của tài khoản đang đăng nhập
        [HttpGet]
        public async Task<IActionResult> MyBookings()
        {
            var username = User.Identity!.Name;
            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);

            if (currentUser == null) return Unauthorized();

            var myBookings = await _context.Bookings
                .Include(b => b.Room)
                .Include(b => b.BookingFacilities!)
                    .ThenInclude(bf => bf.Facility) // Load chi tiết tên thiết bị
                .Where(b => b.UserId == currentUser.Id)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();

            return View(myBookings);
        }

        [HttpGet]
        public async Task<IActionResult> Dashboard()
        {
            var username = User.Identity!.Name;
            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);

            if (currentUser == null) return Unauthorized();

            // Lấy luôn danh sách lịch sử đặt phòng để truyền sang giao diện
            var myBookings = await _context.Bookings
                .Include(b => b.Room)
                .Include(b => b.BookingFacilities!)
                    .ThenInclude(bf => bf.Facility) // Đính kèm luôn thiết bị vào trang Dashboard
                .Where(b => b.UserId == currentUser.Id)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();

            ViewBag.User = currentUser; // Truyền thông tin User
            return View(myBookings);    // Truyền danh sách lịch sử
        }

        // ========================================================
        // KHU VỰC DÀNH RIÊNG CHO ADMIN: QUẢN LÝ VÀ DUYỆT ĐƠN
        // ========================================================

        // 4. Mở trang Quản lý toàn bộ đơn đặt phòng (Admin & Manager)
        [HttpGet]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> ManageBookings()
        {
            var allBookings = await _context.Bookings
                .Include(b => b.Room)
                .Include(b => b.User)
                .Include(b => b.BookingFacilities!)
                    .ThenInclude(bf => bf.Facility) // Admin cũng thấy được khách mượn thiết bị nào
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();

            return View(allBookings);
        }

        // 5. Chức năng bấm nút DUYỆT đơn (Admin & Manager)
        [HttpPost]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> Approve(int id)
        {
            // Cần Include User và Room để lấy email và tên phòng
            var booking = await _context.Bookings.Include(b => b.User).Include(b => b.Room).FirstOrDefaultAsync(b => b.Id == id);
            if (booking != null)
            {
                booking.Status = "Approved";
                await _context.SaveChangesAsync();
                TempData["Success"] = "Đã DUYỆT đơn đặt phòng thành công!";

                // ==========================================
                // BẮN EMAIL TỰ ĐỘNG KHI DUYỆT ĐƠN
                // ==========================================
                try
                {
                    string subject = "✅ Yêu cầu đặt phòng đã được duyệt!";
                    string body = $"Chào {booking.User!.Username},<br><br>Đơn đặt phòng <b>{booking.Room!.Name}</b> của bạn vào lúc <b>{booking.StartTime:dd/MM/yyyy HH:mm}</b> đã được Admin phê duyệt.<br>Vui lòng đến đúng giờ để nhận phòng.<br><br>Trân trọng,<br>T-Booking Admin";

                    await _emailService.SendEmailAsync(booking.User.Email, subject, body);
                }
                catch (Exception ex)
                {
                    // Nếu gửi email lỗi, vẫn duyệt đơn thành công nhưng báo lỗi nhẹ
                    TempData["Error"] = $"Duyệt thành công nhưng không thể gửi Email: {ex.Message}";
                }
            }
            return RedirectToAction(nameof(ManageBookings));
        }

        // 6. Chức năng bấm nút TỪ CHỐI đơn (Admin & Manager)
        [HttpPost]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> Reject(int id)
        {
            var booking = await _context.Bookings.Include(b => b.User).Include(b => b.Room).FirstOrDefaultAsync(b => b.Id == id);
            if (booking != null)
            {
                booking.Status = "Rejected";
                await _context.SaveChangesAsync();
                TempData["Error"] = "Đã TỪ CHỐI đơn đặt phòng!";

                // ==========================================
                // BẮN EMAIL TỰ ĐỘNG KHI TỪ CHỐI ĐƠN
                // ==========================================
                try
                {
                    string subject = "❌ Yêu cầu đặt phòng bị từ chối";
                    string body = $"Chào {booking.User!.Username},<br><br>Rất tiếc, đơn đặt phòng <b>{booking.Room!.Name}</b> của bạn vào lúc <b>{booking.StartTime:dd/MM/yyyy HH:mm}</b> đã bị Admin từ chối.<br>Vui lòng liên hệ ban quản lý để biết thêm chi tiết.<br><br>Trân trọng,<br>T-Booking Admin";

                    await _emailService.SendEmailAsync(booking.User.Email, subject, body);
                }
                catch (Exception ex)
                {
                    TempData["Error"] = $"Từ chối thành công nhưng không thể gửi Email: {ex.Message}";
                }
            }
            return RedirectToAction(nameof(ManageBookings));
        }

        // ========================================================
        // LỊCH PHÒNG CHUNG (PUBLIC CALENDAR)
        // ========================================================

        [HttpGet]
        [AllowAnonymous] // Cho phép tất cả mọi người (kể cả chưa đăng nhập) xem lịch
        public IActionResult Calendar()
        {
            return View();
        }

        [HttpGet]
        [AllowAnonymous] // Cho phép tất cả mọi người gọi API lấy dữ liệu sự kiện
        public async Task<IActionResult> GetEvents()
        {
            // CHỈ lấy những đơn đặt phòng đã được Admin duyệt (Approved)
            var bookings = await _context.Bookings
                .Include(b => b.Room)
                .Where(b => b.Status == "Approved")
                .ToListAsync();

            // Chuyển đổi dữ liệu sang định dạng mà FullCalendar đọc được
            var events = bookings.Select(b => new
            {
                id = b.Id,
                title = $"{b.Room.Name} (Đã đặt)", // Hiện tên phòng kèm chữ "Đã đặt"
                start = b.StartTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                end = b.EndTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                backgroundColor = "#f8d7da",
                borderColor = "#f5c2c7",
                textColor = "#842029"
            });

            return Json(events);
        }

        // ========================================================
        // TRANG DASHBOARD THỐNG KÊ DÀNH CHO ADMIN & MANAGER
        // ========================================================
        [HttpGet]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> AdminDashboard()
        {
            // 1. Dữ liệu cho các thẻ Tổng quan
            ViewBag.TotalRooms = await _context.Rooms.CountAsync();
            ViewBag.TotalBookings = await _context.Bookings.CountAsync();
            ViewBag.PendingBookings = await _context.Bookings.CountAsync(b => b.Status == "Pending");
            ViewBag.ApprovedBookings = await _context.Bookings.CountAsync(b => b.Status == "Approved");

            // 2. Dữ liệu cho Biểu đồ tròn (Tỉ lệ trạng thái đơn)
            var statusData = await _context.Bookings
                .GroupBy(b => b.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            ViewBag.StatusLabels = statusData.Select(s => s.Status).ToArray();
            ViewBag.StatusCounts = statusData.Select(s => s.Count).ToArray();

            // 3. Dữ liệu cho Biểu đồ cột (Top phòng được đặt nhiều nhất)
            var roomData = await _context.Bookings
                .Include(b => b.Room)
                .GroupBy(b => b.Room.Name)
                .Select(g => new { RoomName = g.Key, Count = g.Count() })
                .ToListAsync();

            ViewBag.RoomLabels = roomData.Select(r => r.RoomName).ToArray();
            ViewBag.RoomCounts = roomData.Select(r => r.Count).ToArray();

            return View();
        }

        // ========================================================
        // [MỚI] HÀM TẠO MÃ QR CODE
        // ========================================================
        [HttpGet]
        [Authorize]
        public IActionResult GenerateQRCode(string qrText)
        {
            // Tạo mã QR từ chuỗi văn bản truyền vào
            using QRCodeGenerator qrGenerator = new QRCodeGenerator();
            QRCodeData qrCodeData = qrGenerator.CreateQrCode(qrText, QRCodeGenerator.ECCLevel.Q);

            // Xuất ra định dạng PNG dạng mảng byte
            using PngByteQRCode qrCode = new PngByteQRCode(qrCodeData);
            byte[] qrCodeImage = qrCode.GetGraphic(20);

            // Trả về file ảnh
            return File(qrCodeImage, "image/png");
        }
    }
}