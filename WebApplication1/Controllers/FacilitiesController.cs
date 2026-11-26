using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;

namespace WebApplication1.Controllers
{
    [Authorize(Roles = "Admin,Manager")] // Admin và Manager quản lý thiết bị
    public class FacilitiesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public FacilitiesController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Facilities
        public async Task<IActionResult> Index()
        {
            var facilities = await _context.Facilities
                .Include(f => f.Room)
                .OrderBy(f => f.RoomId.HasValue ? 0 : 1)
                .ThenBy(f => f.Room != null ? f.Room.Name : "")
                .ThenBy(f => f.Name)
                .ToListAsync();

            return View(facilities);
        }

        // GET: Facilities/Create
        public async Task<IActionResult> Create()
        {
            ViewBag.Rooms = await _context.Rooms.OrderBy(r => r.RoomType).ThenBy(r => r.Name).ToListAsync();
            return View();
        }

        // POST: Facilities/Create
        // Hỗ trợ thêm cùng một thiết bị cho 2, 3 phòng (hoặc nhiều phòng) cùng lúc (ví dụ: 2 Hội trường, 1 Hội trường + 1 Phòng họp...)
        // Tự động kiểm tra và ngăn chặn thêm trùng thiết bị vào cùng một phòng
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Name,Description,Quantity")] Facility facility, List<int>? selectedRoomIds, string allocationType = "rooms")
        {
            var cleanName = facility.Name?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(cleanName))
            {
                ModelState.AddModelError("Name", "Tên thiết bị không được để trống!");
            }

            if (facility.Quantity <= 0)
            {
                ModelState.AddModelError("Quantity", "Số lượng thiết bị phải lớn hơn 0!");
            }

            // Trường hợp 1: Phân bổ vào Kho dùng chung (Không gán cố định cho phòng nào)
            if (allocationType == "shared")
            {
                var existsInShared = await _context.Facilities.AnyAsync(f =>
                    f.RoomId == null && f.Name.ToLower() == cleanName.ToLower());

                if (existsInShared)
                {
                    ModelState.AddModelError("Name", $"Thiết bị '{cleanName}' đã tồn tại trong Kho dùng chung! Không thể thêm trùng lặp.");
                }

                if (ModelState.IsValid)
                {
                    facility.Name = cleanName;
                    facility.Description = facility.Description ?? "";
                    facility.RoomId = null;
                    _context.Facilities.Add(facility);
                    await _context.SaveChangesAsync();

                    TempData["Success"] = $"Đã thêm thiết bị '{cleanName}' vào Kho dùng chung thành công!";
                    return RedirectToAction(nameof(Index));
                }
            }
            // Trường hợp 2: Gán cố định cho 1 hoặc nhiều phòng được chọn cùng lúc
            else
            {
                if (selectedRoomIds == null || selectedRoomIds.Count == 0)
                {
                    ModelState.AddModelError("", "Vui lòng chọn ít nhất một phòng trực thuộc hoặc chuyển sang chế độ 'Kho dùng chung'!");
                }

                if (ModelState.IsValid && selectedRoomIds != null && selectedRoomIds.Count > 0)
                {
                    var addedRooms = new List<string>();
                    var duplicateRooms = new List<string>();

                    foreach (var roomId in selectedRoomIds)
                    {
                        var room = await _context.Rooms.FindAsync(roomId);
                        if (room == null) continue;

                        // Chống thêm trùng thiết bị vào cùng một phòng
                        var isDuplicate = await _context.Facilities.AnyAsync(f =>
                            f.RoomId == roomId && f.Name.ToLower() == cleanName.ToLower());

                        if (isDuplicate)
                        {
                            duplicateRooms.Add(room.Name);
                        }
                        else
                        {
                            var newFacility = new Facility
                            {
                                Name = cleanName,
                                Description = facility.Description ?? "",
                                Quantity = facility.Quantity,
                                RoomId = roomId
                            };
                            _context.Facilities.Add(newFacility);
                            addedRooms.Add(room.Name);
                        }
                    }

                    // Nếu tất cả các phòng được chọn đều đã có thiết bị này
                    if (addedRooms.Count == 0 && duplicateRooms.Count > 0)
                    {
                        ModelState.AddModelError("", $"Thiết bị '{cleanName}' đã tồn tại ở tất cả các phòng bạn chọn ({string.Join(", ", duplicateRooms)})! Không thể thêm trùng lặp.");
                        ViewBag.Rooms = await _context.Rooms.OrderBy(r => r.RoomType).ThenBy(r => r.Name).ToListAsync();
                        ViewBag.SelectedRoomIds = selectedRoomIds;
                        return View(facility);
                    }

                    await _context.SaveChangesAsync();

                    var message = $"Thêm thiết bị '{cleanName}' (SL: {facility.Quantity}) thành công cho {addedRooms.Count} phòng: {string.Join(", ", addedRooms)}!";
                    if (duplicateRooms.Count > 0)
                    {
                        message += $" Lưu ý: Đã bỏ qua các phòng đã có sẵn thiết bị này ({string.Join(", ", duplicateRooms)}) để tránh trùng lặp dữ liệu.";
                    }

                    TempData["Success"] = message;
                    return RedirectToAction(nameof(Index));
                }
            }

            ViewBag.Rooms = await _context.Rooms.OrderBy(r => r.RoomType).ThenBy(r => r.Name).ToListAsync();
            ViewBag.SelectedRoomIds = selectedRoomIds;
            return View(facility);
        }

        // GET: Facilities/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var facility = await _context.Facilities.FindAsync(id);
            if (facility == null) return NotFound();

            ViewBag.Rooms = await _context.Rooms.OrderBy(r => r.RoomType).ThenBy(r => r.Name).ToListAsync();
            ViewBag.SelectedRoomIds = facility.RoomId.HasValue ? new List<int> { facility.RoomId.Value } : new List<int>();
            return View(facility);
        }

        // POST: Facilities/Edit/5
        // Hỗ trợ cập nhật thiết bị hiện tại và có thể gán / sao chép cho nhiều phòng cùng lúc (ví dụ chọn 2 hội trường hoặc 1 hội trường, 1 phòng họp...)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Name,Description,Quantity")] Facility facility, List<int>? selectedRoomIds, string allocationType = "rooms")
        {
            if (id != facility.Id) return NotFound();

            var cleanName = facility.Name?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(cleanName))
            {
                ModelState.AddModelError("Name", "Tên thiết bị không được để trống!");
            }

            if (facility.Quantity <= 0)
            {
                ModelState.AddModelError("Quantity", "Số lượng thiết bị phải lớn hơn 0!");
            }

            var existingFacility = await _context.Facilities.FindAsync(id);
            if (existingFacility == null) return NotFound();

            if (allocationType == "shared")
            {
                var isDuplicateInShared = await _context.Facilities.AnyAsync(f =>
                    f.Id != id &&
                    f.RoomId == null &&
                    f.Name.ToLower() == cleanName.ToLower());

                if (isDuplicateInShared)
                {
                    ModelState.AddModelError("Name", $"Thiết bị '{cleanName}' đã tồn tại trong Kho dùng chung! Không thể đặt trùng tên.");
                }

                if (ModelState.IsValid)
                {
                    existingFacility.Name = cleanName;
                    existingFacility.Description = facility.Description ?? "";
                    existingFacility.Quantity = facility.Quantity;
                    existingFacility.RoomId = null;

                    await _context.SaveChangesAsync();
                    TempData["Success"] = $"Đã cập nhật thiết bị '{cleanName}' thành Kho dùng chung thành công!";
                    return RedirectToAction(nameof(Index));
                }
            }
            else // allocationType == "rooms"
            {
                if (selectedRoomIds == null || selectedRoomIds.Count == 0)
                {
                    ModelState.AddModelError("", "Vui lòng chọn ít nhất một phòng trực thuộc hoặc chuyển sang chế độ 'Kho dùng chung'!");
                }

                if (ModelState.IsValid && selectedRoomIds != null && selectedRoomIds.Count > 0)
                {
                    // Phòng đầu tiên được chọn sẽ cập nhật cho bản ghi hiện tại
                    var primaryRoomId = selectedRoomIds[0];
                    var primaryRoom = await _context.Rooms.FindAsync(primaryRoomId);

                    // Kiểm tra trùng trong phòng đầu tiên với thiết bị KHÁC
                    var isDupInPrimary = await _context.Facilities.AnyAsync(f =>
                        f.Id != id &&
                        f.RoomId == primaryRoomId &&
                        f.Name.ToLower() == cleanName.ToLower());

                    if (isDupInPrimary)
                    {
                        ModelState.AddModelError("", $"Thiết bị '{cleanName}' đã có trong {primaryRoom?.Name}! Vui lòng không đặt trùng tên.");
                        ViewBag.Rooms = await _context.Rooms.OrderBy(r => r.RoomType).ThenBy(r => r.Name).ToListAsync();
                        ViewBag.SelectedRoomIds = selectedRoomIds;
                        return View(facility);
                    }

                    existingFacility.Name = cleanName;
                    existingFacility.Description = facility.Description ?? "";
                    existingFacility.Quantity = facility.Quantity;
                    existingFacility.RoomId = primaryRoomId;

                    var addedRooms = new List<string> { primaryRoom?.Name ?? $"Phòng #{primaryRoomId}" };
                    var skippedRooms = new List<string>();

                    // Nếu người dùng chọn thêm các phòng khác (phòng thứ 2, thứ 3...) -> Nhân bản thiết bị sang các phòng đó
                    for (int i = 1; i < selectedRoomIds.Count; i++)
                    {
                        var extraRoomId = selectedRoomIds[i];
                        var extraRoom = await _context.Rooms.FindAsync(extraRoomId);
                        if (extraRoom == null) continue;

                        var extraDup = await _context.Facilities.AnyAsync(f =>
                            f.RoomId == extraRoomId &&
                            f.Name.ToLower() == cleanName.ToLower());

                        if (extraDup)
                        {
                            skippedRooms.Add(extraRoom.Name);
                        }
                        else
                        {
                            var extraFacility = new Facility
                            {
                                Name = cleanName,
                                Description = facility.Description ?? "",
                                Quantity = facility.Quantity,
                                RoomId = extraRoomId
                            };
                            _context.Facilities.Add(extraFacility);
                            addedRooms.Add(extraRoom.Name);
                        }
                    }

                    await _context.SaveChangesAsync();

                    var msg = $"Cập nhật thiết bị '{cleanName}' thành công cho {addedRooms.Count} phòng: {string.Join(", ", addedRooms)}!";
                    if (skippedRooms.Count > 0)
                    {
                        msg += $" (Bỏ qua các phòng đã có sẵn: {string.Join(", ", skippedRooms)})";
                    }

                    TempData["Success"] = msg;
                    return RedirectToAction(nameof(Index));
                }
            }

            ViewBag.Rooms = await _context.Rooms.OrderBy(r => r.RoomType).ThenBy(r => r.Name).ToListAsync();
            ViewBag.SelectedRoomIds = selectedRoomIds;
            return View(facility);
        }

        // GET: Facilities/Delete/5
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var facility = await _context.Facilities
                .Include(f => f.Room)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (facility == null) return NotFound();

            return View(facility);
        }

        // POST: Facilities/Delete/5
        [HttpPost, ActionName("Delete")]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var facility = await _context.Facilities.FindAsync(id);
            if (facility != null)
            {
                var name = facility.Name;
                _context.Facilities.Remove(facility);
                await _context.SaveChangesAsync();
                TempData["Success"] = $"Đã xóa thiết bị '{name}' thành công!";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}
