using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models;

namespace WebApplication1.Services
{
    public class ChatbotService : IChatbotService
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ChatbotService> _logger;

        public ChatbotService(
            ApplicationDbContext context,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILogger<ChatbotService> logger)
        {
            _context = context;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<ChatResponseDto> GetResponseAsync(string userMessage, string? currentUsername = null)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
            {
                return new ChatResponseDto
                {
                    Success = true,
                    Reply = "Xin chào! Tôi có thể giúp gì cho bạn về đặt phòng, thiết bị hoặc kiểm tra lịch trống hôm nay?",
                    QuickSuggestions = GetDefaultQuickSuggestions()
                };
            }

            var trimmedMessage = userMessage.Trim();

            // 1. Kiểm tra phạm vi (Scope validation): Nếu là câu hỏi hoàn toàn ngoài lề -> Từ chối lịch sự
            if (IsExplicitlyOutOfScope(trimmedMessage))
            {
                return BuildOutOfScopeResponse();
            }

            // 2. Thử gọi Gemini AI nếu có cấu hình API Key
            var geminiApiKey = _configuration["Gemini:ApiKey"];
            if (!string.IsNullOrWhiteSpace(geminiApiKey) && !geminiApiKey.Contains("YOUR_GEMINI_API_KEY", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var geminiResponse = await CallGeminiAsync(trimmedMessage, geminiApiKey);
                    if (geminiResponse != null && !string.IsNullOrWhiteSpace(geminiResponse.Reply))
                    {
                        return geminiResponse;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Gọi Gemini AI thất bại, tự động chuyển về Local NLP Engine.");
                }
            }

            // 3. Sử dụng Smart Local NLP & Live DB Query Engine
            return await ProcessWithLocalEngineAsync(trimmedMessage, currentUsername);
        }

        #region Local Smart NLP & DB Query Engine

        private async Task<ChatResponseDto> ProcessWithLocalEngineAsync(string rawMessage, string? currentUsername = null)
        {
            var lower = rawMessage.ToLowerInvariant();
            var unaccented = RemoveDiacritics(lower);

            // A. Kiểm tra chào hỏi
            if (IsGreeting(lower, unaccented))
            {
                return new ChatResponseDto
                {
                    Success = true,
                    Reply = "👋 **Xin chào bạn! Tôi là AI Trợ lý ảo của hệ thống T-Booking.**\n\nTôi có thể hỗ trợ bạn:\n" +
                            "• 🔍 **Kiểm tra phòng trống** theo khung giờ & ngày bạn muốn\n" +
                            "• 👥 **Tìm phòng phù hợp** theo số lượng người (sức chứa)\n" +
                            "• 💻 **Tra cứu thông tin phòng** (Phòng Lab, Phòng họp, Hội trường)\n" +
                            "• 📽️ **Tra cứu trang thiết bị** có sẵn hoặc mượn kèm\n" +
                            "• 📝 **Hướng dẫn quy trình đặt phòng** nhanh chóng\n\n" +
                            "*Bạn cần tôi hỗ trợ thông tin gì ạ?*",
                    QuickSuggestions = GetDefaultQuickSuggestions()
                };
            }

            // B. Kiểm tra nếu không chứa bất kỳ từ khóa nào liên quan đến hệ thống -> Từ chối
            if (!HasDomainKeyword(lower, unaccented))
            {
                return BuildOutOfScopeResponse();
            }

            // C. Nhận diện các yếu tố: Khung giờ, Ngày, Sức chứa, Loại phòng
            var (hasTime, startTime, endTime, dateDesc, timeDesc) = ParseDateTimeRange(rawMessage);
            var (hasCapacity, requestedCapacity) = ParseCapacity(rawMessage);
            var roomTypeFilter = ParseRoomType(lower, unaccented);

            // D0. Khách hỏi xem Lịch sử đặt phòng / Đơn đặt phòng của tôi
            if (IsBookingHistoryQuery(lower, unaccented))
            {
                return await HandleBookingHistoryQueryAsync(currentUsername);
            }

            // D. Khách hỏi kiểm tra phòng trống (hoặc có khung giờ cụ thể)
            bool isAskingAvailability = lower.Contains("trống") || lower.Contains("rảnh") || lower.Contains("con phong") ||
                                       lower.Contains("còn phòng") || lower.Contains("khung giờ") || lower.Contains("lich trong") ||
                                       lower.Contains("lịch trống") || hasTime;

            if (isAskingAvailability)
            {
                return await HandleAvailabilityQueryAsync(hasTime, startTime, endTime, dateDesc, timeDesc, hasCapacity, requestedCapacity, roomTypeFilter);
            }

            // E. Khách hỏi tìm phòng theo số lượng người / sức chứa
            if (hasCapacity)
            {
                return await HandleCapacityQueryAsync(requestedCapacity, roomTypeFilter);
            }

            // F. Khách hỏi về Nhận phòng / Mã QR Check-in / Sau khi đơn được duyệt (TC_AI_04)
            if (IsCheckInOrQrQuery(lower, unaccented))
            {
                return HandleCheckInQuery();
            }

            // G. Khách hỏi về Thiết bị (Facility)
            if (IsFacilityQuery(lower, unaccented))
            {
                return await HandleFacilityQueryAsync(lower, unaccented);
            }

            // H. Khách hỏi về Hướng dẫn / Quy trình đặt phòng
            if (IsBookingGuideQuery(lower, unaccented))
            {
                return HandleBookingGuideQuery();
            }

            // I. Khách hỏi danh sách / thông tin phòng
            if (IsRoomInfoQuery(lower, unaccented))
            {
                return await HandleRoomInfoQueryAsync(roomTypeFilter);
            }

            // Mặc định hỗ trợ chung
            return new ChatResponseDto
            {
                Success = true,
                Reply = "Tôi đã ghi nhận câu hỏi của bạn về hệ thống T-Booking. Bạn có thể cho tôi biết rõ hơn:\n" +
                        "1. Bạn muốn đặt phòng vào **ngày nào và khung giờ nào** (ví dụ: *từ 8h đến 10h ngày mai*)?\n" +
                        "2. Bạn cần phòng cho **bao nhiêu người** (ví dụ: *phòng cho 30 người*)?\n" +
                        "3. Hoặc bạn cần tìm **loại phòng** (Lab, Họp, Hội trường) hay **thiết bị** nào?",
                QuickSuggestions = GetDefaultQuickSuggestions()
            };
        }

        private async Task<ChatResponseDto> HandleAvailabilityQueryAsync(
            bool hasTime,
            DateTime? startTime,
            DateTime? endTime,
            string dateDesc,
            string timeDesc,
            bool hasCapacity,
            int requestedCapacity,
            string? roomTypeFilter)
        {
            try
            {
                var roomsQuery = _context.Rooms.Include(r => r.Facilities).AsQueryable();

                if (!string.IsNullOrEmpty(roomTypeFilter))
                {
                    roomsQuery = roomsQuery.Where(r => r.RoomType.Contains(roomTypeFilter));
                }

                if (hasCapacity && requestedCapacity > 0)
                {
                    roomsQuery = roomsQuery.Where(r => r.Capacity >= requestedCapacity);
                }

                var allRooms = await roomsQuery.ToListAsync();

                if (!allRooms.Any())
                {
                    var msg = "Rất tiếc, hiện không có phòng nào";
                    if (!string.IsNullOrEmpty(roomTypeFilter)) msg += $" thuộc loại **{roomTypeFilter}**";
                    if (hasCapacity) msg += $" có sức chứa từ **{requestedCapacity} người**";
                    msg += " trong hệ thống.\n\nBạn có thể thử tìm với số lượng người ít hơn hoặc liên hệ ban quản trị để được hỗ trợ.";

                    return new ChatResponseDto
                    {
                        Success = true,
                        Reply = msg,
                        QuickSuggestions = new List<string> { "Danh sách tất cả phòng", "Xem lịch phòng chung" }
                    };
                }

                // Nếu khách chưa đưa khung giờ cụ thể
                if (!hasTime || !startTime.HasValue || !endTime.HasValue)
                {
                    var replyBuilder = new StringBuilder();
                    replyBuilder.AppendLine($"📋 Tìm thấy **{allRooms.Count} phòng** phù hợp với yêu cầu của bạn:");
                    if (hasCapacity) replyBuilder.AppendLine($"• Sức chứa: từ **{requestedCapacity} người** trở lên");
                    if (!string.IsNullOrEmpty(roomTypeFilter)) replyBuilder.AppendLine($"• Loại phòng: **{roomTypeFilter}**");
                    replyBuilder.AppendLine();

                    foreach (var r in allRooms.Take(5))
                    {
                        replyBuilder.AppendLine($"🏢 **{r.Name}** ({r.RoomType}) - Sức chứa: **{r.Capacity} người**");
                        if (r.Facilities != null && r.Facilities.Any())
                        {
                            var facNames = string.Join(", ", r.Facilities.Select(f => f.Name));
                            replyBuilder.AppendLine($"  └ Thiết bị sẵn có: *{facNames}*");
                        }
                    }

                    replyBuilder.AppendLine();
                    replyBuilder.AppendLine("👉 **Để tôi kiểm tra chính xác phòng nào còn trống**, bạn vui lòng cho biết **khung giờ cụ thể** nhé (ví dụ: *'từ 8h đến 10h ngày mai'* hoặc *'chiều nay 14h - 16h'*).");

                    return new ChatResponseDto
                    {
                        Success = true,
                        Reply = replyBuilder.ToString(),
                        SuggestedRooms = allRooms.Select(ToRoomDto).ToList(),
                        QuickSuggestions = new List<string> { "Trống từ 8h đến 10h sáng mai?", "Trống từ 14h đến 16h chiều nay?", "Xem lịch phòng chung" }
                    };
                }

                // Khách đã đưa giờ cụ thể -> Kiểm tra xung đột với Bookings
                var conflicts = await _context.Bookings
                    .Where(b => b.Status != "Rejected" &&
                               (startTime < b.EndTime && endTime > b.StartTime))
                    .Select(b => b.RoomId)
                    .Distinct()
                    .ToListAsync();

                var availableRooms = allRooms.Where(r => !conflicts.Contains(r.Id)).ToList();
                var occupiedRooms = allRooms.Where(r => conflicts.Contains(r.Id)).ToList();

                var sb = new StringBuilder();
                sb.AppendLine($"🕒 **Kết quả kiểm tra lịch trống**");
                sb.AppendLine($"• Khung giờ: **{startTime:HH:mm} - {endTime:HH:mm}** ({dateDesc})");
                if (hasCapacity) sb.AppendLine($"• Sức chứa yêu cầu: từ **{requestedCapacity} người**");
                if (!string.IsNullOrEmpty(roomTypeFilter)) sb.AppendLine($"• Loại phòng: **{roomTypeFilter}**");
                sb.AppendLine();

                if (availableRooms.Any())
                {
                    sb.AppendLine($"✅ Hiện có **{availableRooms.Count} phòng còn TRỐNG** có thể đặt ngay:");
                    sb.AppendLine();

                    foreach (var room in availableRooms)
                    {
                        sb.AppendLine($"📍 **{room.Name}** ({room.RoomType})");
                        sb.AppendLine($"   • Sức chứa: **{room.Capacity} người**");
                        if (room.Facilities != null && room.Facilities.Any())
                        {
                            var facList = string.Join(", ", room.Facilities.Select(f => f.Name));
                            sb.AppendLine($"   • Thiết bị: *{facList}*");
                        }
                        sb.AppendLine($"   👉 <a href=\"/Bookings/Create?roomId={room.Id}\" class=\"btn btn-sm btn-primary py-0 px-2 fw-bold text-white text-decoration-none rounded-pill my-1\">Đặt phòng này</a>");
                        sb.AppendLine();
                    }

                    if (occupiedRooms.Any())
                    {
                        sb.AppendLine($"*(Đã có {occupiedRooms.Count} phòng khác bị kín lịch trong khung giờ này)*");
                    }
                }
                else
                {
                    sb.AppendLine("❌ **Rất tiếc! Tất cả các phòng phù hợp đều đã có người đặt trong khung giờ này.**");
                    sb.AppendLine();
                    sb.AppendLine("💡 **Gợi ý cho bạn:**");
                    sb.AppendLine("• Thử lùi hoặc tiến khung giờ khác (ví dụ: sớm hơn 1-2 tiếng hoặc ca tiếp theo).");
                    sb.AppendLine("• Truy cập trang **[Lịch Phòng](/Bookings/Calendar)** để quan sát toàn bộ lịch trống trực quan theo tuần/tháng.");
                }

                return new ChatResponseDto
                {
                    Success = true,
                    Reply = sb.ToString(),
                    SuggestedRooms = availableRooms.Select(ToRoomDto).ToList(),
                    QuickSuggestions = new List<string> { "Xem lịch phòng chung", "Đổi khung giờ khác", "Có thiết bị gì mượn thêm?" }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi truy vấn lịch trống");
                return new ChatResponseDto
                {
                    Success = false,
                    Reply = "Hệ thống đang bận cập nhật dữ liệu lịch phòng. Bạn có thể xem trực tiếp tại trang [Lịch Phòng](/Bookings/Calendar) hoặc thử lại sau ít phút nhé!"
                };
            }
        }

        private async Task<ChatResponseDto> HandleCapacityQueryAsync(int requestedCapacity, string? roomTypeFilter)
        {
            try
            {
                var query = _context.Rooms.Include(r => r.Facilities).Where(r => r.Capacity >= requestedCapacity);
                if (!string.IsNullOrEmpty(roomTypeFilter))
                {
                    query = query.Where(r => r.RoomType.Contains(roomTypeFilter));
                }

                var rooms = await query.OrderBy(r => r.Capacity).ToListAsync();

                if (!rooms.Any())
                {
                    return new ChatResponseDto
                    {
                        Success = true,
                        Reply = $"Hiện tại hệ thống không có phòng nào có sức chứa từ **{requestedCapacity} người** trở lên. Phòng lớn nhất của hệ thống có sức chứa thấp hơn mức này.",
                        QuickSuggestions = new List<string> { "Danh sách tất cả phòng", "Phòng nào lớn nhất?" }
                    };
                }

                var sb = new StringBuilder();
                sb.AppendLine($"👥 Tìm thấy **{rooms.Count} phòng** có sức chứa từ **{requestedCapacity} người** trở lên:");
                sb.AppendLine();

                foreach (var r in rooms)
                {
                    sb.AppendLine($"🏢 **{r.Name}** ({r.RoomType})");
                    sb.AppendLine($"   • Sức chứa: **{r.Capacity} người**");
                    if (r.Facilities != null && r.Facilities.Any())
                    {
                        sb.AppendLine($"   • Thiết bị: {string.Join(", ", r.Facilities.Select(f => f.Name))}");
                    }
                    sb.AppendLine($"   👉 <a href=\"/Bookings/Create?roomId={r.Id}\" class=\"btn btn-sm btn-outline-primary py-0 px-2 fw-bold text-decoration-none rounded-pill my-1\">Chọn đặt phòng này</a>");
                    sb.AppendLine();
                }

                sb.AppendLine("💡 *Để biết phòng có trống vào thời điểm bạn cần hay không, hãy cho tôi biết thêm ngày giờ bạn muốn dùng nhé!*");

                return new ChatResponseDto
                {
                    Success = true,
                    Reply = sb.ToString(),
                    SuggestedRooms = rooms.Select(ToRoomDto).ToList(),
                    QuickSuggestions = new List<string> { "Kiểm tra phòng trống ngày mai", "Trống từ 14h đến 16h hôm nay?", "Xem lịch phòng chung" }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi truy vấn phòng theo sức chứa");
                return new ChatResponseDto
                {
                    Success = false,
                    Reply = "Hệ thống đang bận cập nhật dữ liệu. Bạn có thể kiểm tra danh sách phòng tại trang chủ hoặc xem trực tiếp tại trang [Lịch Phòng](/Bookings/Calendar) nhé!"
                };
            }
        }

        private async Task<ChatResponseDto> HandleFacilityQueryAsync(string lower, string unaccented)
        {
            try
            {
                var facilities = await _context.Facilities.Include(f => f.Room).ToListAsync();

                if (!facilities.Any())
                {
                    return new ChatResponseDto
                    {
                        Success = true,
                        Reply = "Hiện tại hệ thống chưa cập nhật danh sách thiết bị. Quý khách vui lòng liên hệ trực tiếp ban quản trị để được hỗ trợ.",
                        QuickSuggestions = GetDefaultQuickSuggestions()
                    };
                }

                var sb = new StringBuilder();
                sb.AppendLine("📽️ **Danh sách trang thiết bị trong hệ thống T-Booking:**");
                sb.AppendLine();

                // Thiết bị dùng chung (RoomId == null)
                var sharedFacilities = facilities.Where(f => f.RoomId == null).ToList();
                if (sharedFacilities.Any())
                {
                    sb.AppendLine("🔹 **Thiết bị dùng chung (có thể mượn kèm khi đặt bất kỳ phòng nào):**");
                    foreach (var f in sharedFacilities)
                    {
                        sb.AppendLine($"• **{f.Name}** (Số lượng: {f.Quantity})");
                        if (!string.IsNullOrEmpty(f.Description))
                        {
                            sb.AppendLine($"  └ *Mô tả: {f.Description}*");
                        }
                    }
                    sb.AppendLine();
                }

                // Thiết bị cố định theo từng phòng
                var roomFacilities = facilities.Where(f => f.RoomId != null).GroupBy(f => f.Room?.Name ?? "Phòng").ToList();
                if (roomFacilities.Any())
                {
                    sb.AppendLine("🔹 **Thiết bị cố định theo phòng:**");
                    foreach (var group in roomFacilities)
                    {
                        sb.AppendLine($"📍 **{group.Key}:**");
                        foreach (var f in group)
                        {
                            sb.AppendLine($"  • {f.Name} (SL: {f.Quantity}) {(string.IsNullOrEmpty(f.Description) ? "" : $"- {f.Description}")}");
                        }
                    }
                    sb.AppendLine();
                }

                sb.AppendLine("📌 **Cách mượn thiết bị:** Khi bạn vào trang **Đặt phòng**, ở bước 2 hệ thống sẽ cho bạn tích chọn các thiết bị cần mượn và nhập số lượng tương ứng.");

                return new ChatResponseDto
                {
                    Success = true,
                    Reply = sb.ToString(),
                    QuickSuggestions = new List<string> { "Hướng dẫn đặt phòng", "Kiểm tra phòng trống hôm nay", "Danh sách phòng" }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi truy vấn thiết bị");
                return new ChatResponseDto
                {
                    Success = false,
                    Reply = "Hệ thống đang bận cập nhật dữ liệu thiết bị. Bạn có thể xem thiết bị có sẵn trực tiếp khi bấm vào từng phòng nhé!"
                };
            }
        }

        private async Task<ChatResponseDto> HandleRoomInfoQueryAsync(string? roomTypeFilter)
        {
            try
            {
                var query = _context.Rooms.Include(r => r.Facilities).AsQueryable();
                if (!string.IsNullOrEmpty(roomTypeFilter))
                {
                    query = query.Where(r => r.RoomType.Contains(roomTypeFilter));
                }

                var rooms = await query.ToListAsync();

                if (!rooms.Any())
                {
                    return new ChatResponseDto
                    {
                        Success = true,
                        Reply = "Hiện tại hệ thống chưa có dữ liệu phòng nào phù hợp.",
                        QuickSuggestions = GetDefaultQuickSuggestions()
                    };
                }

                var sb = new StringBuilder();
                sb.AppendLine($"🏢 **Danh sách các phòng trong hệ thống T-Booking** ({rooms.Count} phòng):");
                sb.AppendLine();

                var groups = rooms.GroupBy(r => r.RoomType).ToList();
                foreach (var g in groups)
                {
                    sb.AppendLine($"📁 **Nhóm {g.Key}:**");
                    foreach (var r in g)
                    {
                        sb.AppendLine($"• **{r.Name}** - Sức chứa: **{r.Capacity} người**");
                        if (r.Facilities != null && r.Facilities.Any())
                        {
                            var facs = string.Join(", ", r.Facilities.Select(f => f.Name));
                            sb.AppendLine($"  └ Thiết bị: *{facs}*");
                        }
                        sb.AppendLine($"  └ 👉 <a href=\"/Bookings/Create?roomId={r.Id}\">Đặt phòng này</a>");
                    }
                    sb.AppendLine();
                }

                return new ChatResponseDto
                {
                    Success = true,
                    Reply = sb.ToString(),
                    SuggestedRooms = rooms.Select(ToRoomDto).ToList(),
                    QuickSuggestions = new List<string> { "Kiểm tra phòng trống hôm nay", "Tìm phòng cho 30 người", "Thiết bị có sẵn gồm những gì?" }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi truy vấn thông tin phòng");
                return new ChatResponseDto
                {
                    Success = false,
                    Reply = "Hệ thống đang bận cập nhật thông tin phòng. Bạn có thể xem toàn bộ phòng trên trang chủ nhé!"
                };
            }
        }

        private ChatResponseDto HandleBookingGuideQuery()
        {
            var reply = "📝 **Quy trình đặt phòng trên T-Booking vô cùng đơn giản với 3 bước:**\n\n" +
                        "1️⃣ **Bước 1: Chọn phòng**\n" +
                        "   • Vào **Trang chủ** hoặc xem **[Lịch Phòng](/Bookings/Calendar)** để chọn phòng ưng ý.\n" +
                        "   • Bấm nút **'Đặt phòng'** ở phòng bạn muốn mượn.\n\n" +
                        "2️⃣ **Bước 2: Điền thông tin & Mượn thiết bị**\n" +
                        "   • Chọn **Thời gian bắt đầu** và **Thời gian kết thúc**.\n" +
                        "   • Tích chọn các **thiết bị** bạn muốn mượn kèm và số lượng.\n" +
                        "   • Hệ thống tự động kiểm tra chống trùng lịch.\n\n" +
                        "3️⃣ **Bước 3: Gửi yêu cầu & Nhận kết quả**\n" +
                        "   • Đơn của bạn sẽ ở trạng thái **'Pending' (Chờ duyệt)**.\n" +
                        "   • Quản trị viên sẽ phê duyệt và hệ thống tự động **gửi Email thông báo** cho bạn kèm mã QR vào phòng.\n" +
                        "   • Bạn có thể theo dõi đơn tại mục **Lịch sử đặt phòng**.";

            return new ChatResponseDto
            {
                Success = true,
                Reply = reply,
                QuickSuggestions = new List<string> { "Kiểm tra phòng trống hôm nay", "Xem lịch phòng chung", "Thiết bị có sẵn gồm những gì?" }
            };
        }

        private ChatResponseDto HandleCheckInQuery()
        {
            var reply = "🎫 **Hướng dẫn quy trình Nhận phòng (Check-in bằng mã QR):**\n\n" +
                        "Sau khi đơn đặt phòng của bạn được Quản trị viên duyệt (**Approved**), bạn thực hiện nhận phòng theo các bước sau:\n\n" +
                        "1️⃣ **Bước 1: Lấy Mã QR Nhận Phòng**\n" +
                        "   • Đăng nhập vào hệ thống và bấm vào tên tài khoản của bạn để vào **Trang cá nhân (Dashboard)** hoặc mục **[Đơn của tôi](/Bookings/MyBookings)**.\n" +
                        "   • Tại dòng đơn đặt phòng đã duyệt, bạn sẽ thấy ảnh **Mã QR nhận phòng** tương ứng.\n\n" +
                        "2️⃣ **Bước 2: Phóng to & Xuất trình Mã QR**\n" +
                        "   • Nhấp vào ảnh mã QR nhỏ để phóng to rõ nét trên màn hình điện thoại hoặc máy tính.\n\n" +
                        "3️⃣ **Bước 3: Quét mã xác nhận nhận phòng**\n" +
                        "   • Đưa mã QR cho Quản trị viên / Cán bộ quản lý phòng quét bằng **Camera Zalo** hoặc ứng dụng **Camera điện thoại**.\n" +
                        "   • Hệ thống sẽ tự động đối chiếu thông tin Mã đơn, Tên phòng và bàn giao phòng học/hội trường cho bạn ngay lập tức!\n\n" +
                        "💡 *Lưu ý: Chỉ những đơn đã được duyệt (Approved) mới hiển thị mã QR. Đơn ở trạng thái Chờ duyệt (Pending) sẽ không có mã QR.*";

            return new ChatResponseDto
            {
                Success = true,
                Reply = reply,
                QuickSuggestions = new List<string> { "Xem lịch sử đặt phòng", "Hướng dẫn đặt phòng", "Kiểm tra phòng trống hôm nay" }
            };
        }

        private async Task<ChatResponseDto> HandleBookingHistoryQueryAsync(string? currentUsername)
        {
            if (!string.IsNullOrEmpty(currentUsername))
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == currentUsername);
                if (user != null)
                {
                    var myBookings = await _context.Bookings
                        .Include(b => b.Room)
                        .Include(b => b.BookingFacilities!)
                            .ThenInclude(bf => bf.Facility)
                        .Where(b => b.UserId == user.Id)
                        .OrderByDescending(b => b.CreatedAt)
                        .Take(5)
                        .ToListAsync();

                    if (myBookings.Any())
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine($"📋 **Lịch sử đặt phòng của bạn ({user.Username}):**\n");
                        sb.AppendLine($"Dưới đây là **{myBookings.Count} đơn đặt phòng gần nhất** của bạn:\n");

                        foreach (var b in myBookings)
                        {
                            string statusBadge = b.Status switch
                            {
                                "Approved" => "🟢 **Đã duyệt (Approved)** *(Có mã QR nhận phòng)*",
                                "Pending" => "🟡 **Chờ duyệt (Pending)**",
                                "Rejected" => "🔴 **Bị từ chối (Rejected)**",
                                "Cancelled" => "⚫ **Đã hủy (Cancelled)**",
                                _ => $"⚪ **{b.Status}**"
                            };

                            sb.AppendLine($"• **Phòng:** {b.Room.Name} ({b.Room.RoomType})");
                            sb.AppendLine($"  ⏰ **Thời gian:** {b.StartTime:HH:mm dd/MM/yyyy} ➔ {b.EndTime:HH:mm dd/MM/yyyy}");
                            sb.AppendLine($"  📌 **Trạng thái:** {statusBadge}");

                            if (b.BookingFacilities != null && b.BookingFacilities.Any())
                            {
                                var facList = string.Join(", ", b.BookingFacilities.Select(bf => $"{bf.Facility?.Name} (x{bf.Quantity})"));
                                sb.AppendLine($"  🔌 **Thiết bị mượn kèm:** {facList}");
                            }
                            sb.AppendLine();
                        }

                        sb.AppendLine("🔗 Bạn có thể xem toàn bộ lịch sử chi tiết và mở ảnh mã QR nhận phòng tại mục **[Đơn của tôi](/Bookings/MyBookings)** hoặc **[Trang cá nhân (Dashboard)](/Bookings/Dashboard)**.");

                        return new ChatResponseDto
                        {
                            Success = true,
                            Reply = sb.ToString().Trim(),
                            QuickSuggestions = new List<string> { "Đơn của tôi", "Quy trình nhận phòng mã QR", "Kiểm tra phòng trống hôm nay" }
                        };
                    }
                    else
                    {
                        return new ChatResponseDto
                        {
                            Success = true,
                            Reply = $"📋 **Lịch sử đặt phòng ({user.Username}):**\n\n" +
                                    "Hiện tại bạn **chưa có đơn đặt phòng nào** trong hệ thống T-Booking.\n\n" +
                                    "👉 Bạn có thể xem lịch phòng trống tại **[Lịch Đặt Phòng](/Bookings/Calendar)** hoặc cho tôi biết ngày/giờ bạn cần để tôi gợi ý phòng phù hợp ngay nhé!",
                            QuickSuggestions = new List<string> { "Kiểm tra phòng trống hôm nay", "Phòng họp cho 20 người", "Hướng dẫn đặt phòng" }
                        };
                    }
                }
            }

            // Trường hợp khách chưa đăng nhập hoặc không xác định được tài khoản
            return new ChatResponseDto
            {
                Success = true,
                Reply = "📋 **Hướng dẫn xem Lịch sử đặt phòng & Đơn của bạn:**\n\n" +
                        "Để xem danh sách các đơn đã đặt, theo dõi trạng thái duyệt và xuất trình mã QR nhận phòng:\n\n" +
                        "1️⃣ **Bước 1:** Đăng nhập vào tài khoản của bạn tại trang **[Đăng nhập](/Account/Login)**.\n" +
                        "2️⃣ **Bước 2:** Bấm vào tên tài khoản ở thanh menu trên cùng và chọn mục **[Đơn của tôi](/Bookings/MyBookings)** hoặc **[Trang cá nhân (Dashboard)](/Bookings/Dashboard)**.\n\n" +
                        "💡 **Ý nghĩa các trạng thái đơn:**\n" +
                        "• 🟡 **Chờ duyệt (Pending):** Đơn đang chờ Quản trị viên phê duyệt.\n" +
                        "• 🟢 **Đã duyệt (Approved):** Đơn đã thành công! Hệ thống hiển thị ảnh **Mã QR nhận phòng**, bạn chỉ cần xuất trình cho quản trị viên quét check-in.\n" +
                        "• 🔴 **Từ chối (Rejected):** Đơn không được phê duyệt.\n\n" +
                        "👉 Nếu bạn muốn đặt phòng mới, hãy cho tôi biết thời gian hoặc số người bạn cần nhé!",
                QuickSuggestions = new List<string> { "Xem Đơn của tôi", "Quy trình nhận phòng mã QR", "Kiểm tra phòng trống hôm nay" }
            };
        }

        #endregion

        #region Out-of-Scope & Domain Check

        private bool IsExplicitlyOutOfScope(string message)
        {
            var lower = " " + message.ToLowerInvariant().Trim() + " ";
            var unaccented = " " + RemoveDiacritics(lower).Trim() + " ";

            // BẢO VỆ CỐT LÕI: Nếu câu hỏi chứa bất kỳ từ khóa nghiệp vụ nào của hệ thống -> KHÔNG BAO GIỜ bị chặn
            string[] coreDomainWords = new[]
            {
                "nhan phong", "nhận phòng", "duyet don", "duyệt đơn", "dat phong", "đặt phòng",
                "muon phong", "mượn phòng", "thiet bi", "thiết bị", "ma qr", "mã qr", "qrcode", "check in", "checkin",
                "lich phong", "lịch phòng", "phong hop", "phòng họp", "hoi truong", "hội trường", "phong lab", "phòng lab",
                "duyet xong", "duyệt xong", "lay phong", "lấy phòng",
                "lich su", "lịch sử", "don cua toi", "đơn của tôi", "don dat phong", "đơn đặt phòng", "da dat", "đã đặt", "trang thai don", "trạng thái đơn"
            };

            foreach (var core in coreDomainWords)
            {
                if (lower.Contains(core) || unaccented.Contains(RemoveDiacritics(core)))
                {
                    return false;
                }
            }

            string[] outOfScopePhrases = new[]
            {
                "thời tiết", "thoi tiet", "trời mưa", "troi mua", "trời nắng", "troi nang", "nhiệt độ", "nhiet do", "dự báo thời tiết",
                "nấu ăn", "nau an", "công thức nấu", "món ăn", "mon an", "quán ăn", "quan an", "bún bò", "bun bo", "cơm tấm", "com tam", "ăn phở", "nau pho",
                "viết code", "viet code", "lập trình", "lap trinh", "python", "javascript", "c++", "java", "sql query",
                "giải toán", "giai toan", "bài tập", "bai tap", "phương trình", "phuong trinh", "tích phân", "tich phan", "hóa học", "hoa hoc",
                "dịch tiếng", "dich tieng", "dịch câu", "dich cau", "translate",
                "viết thơ", "viet tho", "bài thơ", "bai tho", "kể chuyện", "ke truyen", "chuyện cười", "chuyen cuoi", "hát cho", "hat cho", "bài hát", "bai hat", "ca hát", "ca hat",
                "tổng thống", "tong thong", "chính trị", "chinh tri", "chiến tranh", "chien tranh", "bầu cử", "bau cu",
                "bóng đá", "bong da", "ronaldo", "messi", "ngoại hạng anh", "ngoai hang anh", "world cup", "euro",
                "chữa bệnh", "chua benh", "uống thuốc", "uong thuoc", "bác sĩ", "bac si", "đau đầu", "dau dau", "covid",
                "tử vi", "tu vi", "bói toán", "boi toan", "cung hoàng đạo", "cung hoang dao", "phong thủy", "phong thuy",
                "chứng khoán", "chung khoan", "bitcoin", "crypto", "tiền ảo", "tien ao", "giá vàng", "gia vang",
                "showbiz", "ca sĩ", "ca si", "diễn viên", "dien vien"
            };

            foreach (var phrase in outOfScopePhrases)
            {
                var pPadded = " " + phrase.Trim() + " ";
                var pUnaccented = " " + RemoveDiacritics(phrase.Trim()) + " ";

                if (lower.Contains(pPadded) || unaccented.Contains(pUnaccented))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasDomainKeyword(string lower, string unaccented)
        {
            string[] domainKeywords = new[]
            {
                "phòng", "phong", "room", "đặt", "dat", "book", "mượn", "muon", "thuê", "thue",
                "nhận", "nhan", "lấy", "lay", "check in", "checkin", "check-in", "qr", "qrcode", "mã", "ma",
                "lịch", "lich", "trống", "trong", "rảnh", "ranh", "kín", "kin", "chỗ", "cho",
                "người", "nguoi", "sức chứa", "suc chua", "chứa", "chua", "giờ", "gio",
                "hôm nay", "hom nay", "ngày mai", "ngay mai", "mốt", "mot", "kia",
                "sáng", "sang", "chiều", "chieu", "tối", "toi", "slot", "ca",
                "lab", "họp", "hop", "meeting", "hội trường", "hoi truong", "hall",
                "thiết bị", "thiet bi", "facility", "máy chiếu", "may chieu", "projector",
                "micro", "mic", "loa", "âm thanh", "am thanh", "máy tính", "may tinh", "laptop", "pc",
                "bảng", "bang", "bút", "but", "hdmi", "wifi", "mạng", "mang", "điều hòa", "dieu hoa", "máy lạnh", "may lanh",
                "duyệt", "duyet", "hủy", "huy", "pending", "approved", "rejected",
                "t-booking", "tbooking", "tài khoản", "tai khoan", "đăng ký", "dang ky", "đăng nhập", "dang nhap",
                "dashboard", "trang chủ", "trang chu", "hướng dẫn", "huong dan", "quy trình", "quy trinh",
                "lịch sử", "lich su", "đơn", "don", "của tôi", "cua toi", "đã đặt", "da dat"
            };

            foreach (var kw in domainKeywords)
            {
                if (lower.Contains(kw) || unaccented.Contains(RemoveDiacritics(kw)))
                {
                    return true;
                }
            }

            return false;
        }

        private ChatResponseDto BuildOutOfScopeResponse()
        {
            return new ChatResponseDto
            {
                Success = true,
                Reply = "⚠️ **Xin lỗi bạn, tôi là AI Trợ lý của hệ thống T-Booking.**\n\n" +
                        "Tôi chỉ được thiết kế để hỗ trợ các câu hỏi liên quan đến **đặt phòng, tra cứu thiết bị, kiểm tra lịch phòng trống và quy định sử dụng phòng** của hệ thống.\n\n" +
                        "Rất tiếc tôi không thể giải đáp các câu hỏi ngoài phạm vi này. Bạn có muốn tôi giúp tra cứu phòng hoặc kiểm tra thiết bị nào không ạ?",
                QuickSuggestions = GetDefaultQuickSuggestions()
            };
        }

        #endregion

        #region Parsers (NLP Helpers)

        private bool IsGreeting(string lower, string unaccented)
        {
            string[] greetings = { "xin chao", "chao", "hello", "hi", "hey", "halo", "alo", "ban la ai", "ai day", "ai do", "giup", "help" };
            return greetings.Any(g => lower.StartsWith(g) || unaccented.StartsWith(g) || lower == g || unaccented == g);
        }

        private bool IsBookingHistoryQuery(string lower, string unaccented)
        {
            string[] historyWords = {
                "lich su dat phong", "lịch sử đặt phòng",
                "lich su", "lịch sử",
                "don cua toi", "đơn của tôi",
                "don dat phong", "đơn đặt phòng",
                "don dat", "đơn đặt",
                "cac phong toi da dat", "các phòng tôi đã đặt",
                "phong da dat", "phòng đã đặt",
                "da dat nhung phong nao", "đã đặt những phòng nào",
                "toi da dat", "tôi đã đặt",
                "kiem tra don dat phong", "kiểm tra đơn đặt phòng",
                "kiem tra don cua toi", "kiểm tra đơn của tôi",
                "tra cuu don", "tra cứu đơn",
                "danh sach don", "danh sách đơn",
                "xem don", "xem đơn",
                "don cua minh", "đơn của mình",
                "trang thai don", "trạng thái đơn",
                "don da duyet chua", "đơn đã duyệt chưa"
            };
            return historyWords.Any(w => lower.Contains(w) || unaccented.Contains(w));
        }

        private bool IsCheckInOrQrQuery(string lower, string unaccented)
        {
            string[] checkInWords = {
                "nhan phong", "nhận phòng", "lay phong", "lấy phòng", "nhan lai phong",
                "check in", "checkin", "check-in",
                "ma qr", "mã qr", "qrcode", "quet qr", "quét qr", "hien thi qr", "hiển thị qr",
                "duyet don xong", "duyệt đơn xong", "duyet xong", "duyệt xong",
                "lam sao de vao phong", "làm sao để vào phòng", "vao phong nhu the nao", "vào phòng như thế nào",
                "cach nhan phong", "cách nhận phòng", "nhan chia khoa", "nhận chìa khóa"
            };
            return checkInWords.Any(w => lower.Contains(w) || unaccented.Contains(w));
        }

        private bool IsFacilityQuery(string lower, string unaccented)
        {
            string[] fWords = { "thiet bi", "thiết bị", "may chieu", "máy chiếu", "micro", "mic", "loa", "may tinh", "máy tính", "muon thiet bi", "mượn thiết bị" };
            return fWords.Any(w => lower.Contains(w) || unaccented.Contains(w));
        }

        private bool IsBookingGuideQuery(string lower, string unaccented)
        {
            string[] gWords = {
                "quy trinh", "quy trình", "cach dat", "cách đặt", "huong dan", "hướng dẫn",
                "lam sao de", "làm sao để", "duyet don", "duyệt đơn", "tôi muốn đặt", "toi muon dat",
                "muon dat phong", "mượn phòng", "dat phong nhu the nao", "đặt phòng như thế nào",
                "huy phong", "hủy phòng", "cho duyet", "chờ duyệt", "phe duyet", "phê duyệt"
            };
            return gWords.Any(w => lower.Contains(w) || unaccented.Contains(w));
        }

        private bool IsRoomInfoQuery(string lower, string unaccented)
        {
            string[] rWords = {
                "danh sach phong", "danh sách phòng", "co nhung phong nao", "có những phòng nào",
                "phong hop", "phòng họp", "phong lab", "phòng lab", "hoi truong", "hội trường",
                "thong tin phong", "thông tin phòng", "phong nao", "phòng nào", "chi tiet phong", "chi tiết phòng"
            };
            return rWords.Any(w => lower.Contains(w) || unaccented.Contains(w));
        }

        private string? ParseRoomType(string lower, string unaccented)
        {
            if (lower.Contains("lab") || unaccented.Contains("lab") || lower.Contains("máy tính") || unaccented.Contains("may tinh"))
                return "Lab";
            if (lower.Contains("hội trường") || unaccented.Contains("hoi truong") || lower.Contains("hall"))
                return "Hall";
            if (lower.Contains("họp") || unaccented.Contains("hop") || lower.Contains("meeting"))
                return "Meeting";

            return null;
        }

        private (bool HasCapacity, int Capacity) ParseCapacity(string message)
        {
            var match = Regex.Match(message, @"(\d+)\s*(?:người|nguoi|chỗ|cho|bạn|ban|thành viên|khách|ghế)", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int cap))
            {
                return (true, cap);
            }

            var match2 = Regex.Match(message, @"sức chứa\s*(\d+)", RegexOptions.IgnoreCase);
            if (match2.Success && int.TryParse(match2.Groups[1].Value, out int cap2))
            {
                return (true, cap2);
            }

            return (false, 0);
        }

        private (bool HasTime, DateTime? StartTime, DateTime? EndTime, string DateDesc, string TimeDesc) ParseDateTimeRange(string message)
        {
            var lower = message.ToLowerInvariant();
            var unaccented = RemoveDiacritics(lower);

            DateTime targetDate = DateTime.Today;
            string dateDesc = "Hôm nay";

            if (lower.Contains("ngày mai") || lower.Contains("mai") || unaccented.Contains("ngay mai"))
            {
                targetDate = DateTime.Today.AddDays(1);
                dateDesc = $"Ngày mai ({targetDate:dd/MM/yyyy})";
            }
            else if (lower.Contains("ngày mốt") || lower.Contains("mốt") || unaccented.Contains("ngay mot") || lower.Contains("ngày kia"))
            {
                targetDate = DateTime.Today.AddDays(2);
                dateDesc = $"Ngày mốt ({targetDate:dd/MM/yyyy})";
            }
            else
            {
                // Kiểm tra format ngày cụ thể: dd/MM hoặc dd/MM/yyyy
                var dateMatch = Regex.Match(message, @"(\d{1,2})[/.-](\d{1,2})(?:[/.-](\d{4}))?");
                if (dateMatch.Success)
                {
                    int day = int.Parse(dateMatch.Groups[1].Value);
                    int month = int.Parse(dateMatch.Groups[2].Value);
                    int year = dateMatch.Groups[3].Success ? int.Parse(dateMatch.Groups[3].Value) : DateTime.Today.Year;

                    if (month >= 1 && month <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month))
                    {
                        targetDate = new DateTime(year, month, day);
                        dateDesc = $"Ngày {targetDate:dd/MM/yyyy}";
                    }
                }
            }

            // Kiểm tra khung giờ
            // Mẫu 1: từ 8h đến 10h, 8:00 - 10:00, 14h - 16h30
            var rangeMatch = Regex.Match(message, @"(?:từ\s*)?(\d{1,2})(?:h|:)?(\d{2})?\s*(?:đến|-|tới|–|den)\s*(\d{1,2})(?:h|:)?(\d{2})?", RegexOptions.IgnoreCase);
            if (rangeMatch.Success)
            {
                int startHour = int.Parse(rangeMatch.Groups[1].Value);
                int startMinute = rangeMatch.Groups[2].Success && !string.IsNullOrEmpty(rangeMatch.Groups[2].Value) ? int.Parse(rangeMatch.Groups[2].Value) : 0;
                int endHour = int.Parse(rangeMatch.Groups[3].Value);
                int endMinute = rangeMatch.Groups[4].Success && !string.IsNullOrEmpty(rangeMatch.Groups[4].Value) ? int.Parse(rangeMatch.Groups[4].Value) : 0;

                // Nếu nhập 2h chiều hoặc 4h chiều
                if (lower.Contains("chiều") && startHour < 12) startHour += 12;
                if (lower.Contains("chiều") && endHour < 12) endHour += 12;

                var start = targetDate.AddHours(startHour).AddMinutes(startMinute);
                var end = targetDate.AddHours(endHour).AddMinutes(endMinute);

                if (end > start)
                {
                    return (true, start, end, dateDesc, $"{startHour:D2}:{startMinute:D2} - {endHour:D2}:{endMinute:D2}");
                }
            }

            // Mẫu 2: lúc 8h, vào 14h
            var singleMatch = Regex.Match(message, @"(?:lúc|vào|tu|từ)\s*(\d{1,2})(?:h|:)?(\d{2})?", RegexOptions.IgnoreCase);
            if (singleMatch.Success)
            {
                int startHour = int.Parse(singleMatch.Groups[1].Value);
                int startMinute = singleMatch.Groups[2].Success && !string.IsNullOrEmpty(singleMatch.Groups[2].Value) ? int.Parse(singleMatch.Groups[2].Value) : 0;
                if (lower.Contains("chiều") && startHour < 12) startHour += 12;

                var start = targetDate.AddHours(startHour).AddMinutes(startMinute);
                var end = start.AddHours(2); // Mặc định 2 tiếng

                return (true, start, end, dateDesc, $"{startHour:D2}:{startMinute:D2} - {end.Hour:D2}:{end.Minute:D2}");
            }

            // Mẫu 3: Buổi sáng / Buổi chiều / Buổi tối
            if (lower.Contains("sáng") || unaccented.Contains("sang"))
            {
                var start = targetDate.AddHours(8);
                var end = targetDate.AddHours(11).AddMinutes(30);
                return (true, start, end, dateDesc, "Buổi sáng (08:00 - 11:30)");
            }
            if (lower.Contains("chiều") || unaccented.Contains("chieu"))
            {
                var start = targetDate.AddHours(13).AddMinutes(30);
                var end = targetDate.AddHours(17);
                return (true, start, end, dateDesc, "Buổi chiều (13:30 - 17:00)");
            }

            return (false, null, null, dateDesc, string.Empty);
        }

        private static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder();

            foreach (var c in normalizedString)
            {
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            return stringBuilder.ToString().Normalize(NormalizationForm.FormC).Replace('đ', 'd').Replace('Đ', 'D');
        }

        private static RoomSuggestionDto ToRoomDto(Room r)
        {
            return new RoomSuggestionDto
            {
                Id = r.Id,
                Name = r.Name,
                Capacity = r.Capacity,
                RoomType = r.RoomType,
                Facilities = r.Facilities?.Select(f => f.Name).ToList() ?? new List<string>()
            };
        }

        private static List<string> GetDefaultQuickSuggestions()
        {
            return new List<string>
            {
                "🔍 Kiểm tra phòng trống hôm nay",
                "👥 Tìm phòng cho 30 người",
                "💻 Danh sách phòng Lab",
                "📽️ Thiết bị có sẵn gồm những gì?",
                "📝 Hướng dẫn đặt phòng"
            };
        }

        #endregion

        #region Gemini Integration

        private async Task<ChatResponseDto?> CallGeminiAsync(string userMessage, string apiKey)
        {
            var model = _configuration["Gemini:Model"] ?? "gemini-1.5-flash";
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(12);

            // Thu thập dữ liệu thời gian thực từ database làm ngữ cảnh hệ thống
            var rooms = await _context.Rooms.Include(r => r.Facilities).ToListAsync();
            var facilities = await _context.Facilities.ToListAsync();
            var upcomingBookings = await _context.Bookings
                .Include(b => b.Room)
                .Where(b => b.Status != "Rejected" && b.EndTime >= DateTime.Now)
                .Take(20)
                .ToListAsync();

            var dbContextInfo = new StringBuilder();
            dbContextInfo.AppendLine("DỮ LIỆU THỜI GIAN THỰC TỪ HỆ THỐNG T-BOOKING:");
            dbContextInfo.AppendLine($"Thời gian hiện tại: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            dbContextInfo.AppendLine("1. DANH SÁCH PHÒNG:");
            foreach (var r in rooms)
            {
                var facs = r.Facilities != null && r.Facilities.Any() ? string.Join(", ", r.Facilities.Select(f => f.Name)) : "Không có";
                dbContextInfo.AppendLine($"- ID: {r.Id}, Tên: {r.Name}, Loại: {r.RoomType}, Sức chứa: {r.Capacity} người, Thiết bị: {facs}");
            }
            dbContextInfo.AppendLine("2. THIẾT BỊ HỆ THỐNG:");
            foreach (var f in facilities)
            {
                dbContextInfo.AppendLine($"- Tên: {f.Name}, SL: {f.Quantity}, Mô tả: {f.Description}, Thuộc phòng: {(f.RoomId.HasValue ? $"Room ID {f.RoomId}" : "Dùng chung")}");
            }
            dbContextInfo.AppendLine("3. LỊCH ĐÃ ĐẶT (ĐANG BẬN/CHỜ DUYỆT):");
            foreach (var b in upcomingBookings)
            {
                dbContextInfo.AppendLine($"- Phòng: {b.Room?.Name} (ID: {b.RoomId}), Từ: {b.StartTime:yyyy-MM-dd HH:mm} Đến: {b.EndTime:yyyy-MM-dd HH:mm}, Trạng thái: {b.Status}");
            }

            string systemPrompt =
                "Bạn là 'AI Trợ lý T-Booking' - trợ lý thông minh hỗ trợ khách hàng của hệ thống quản lý đặt phòng và thiết bị T-Booking.\n" +
                "QUY TẮC CỐT LÕI BẮT BUỘC:\n" +
                "1. BẠN CHỈ ĐƯỢC PHÉP TRẢ LỜI các câu hỏi liên quan đến: thông tin phòng, sức chứa, danh sách thiết bị, kiểm tra lịch phòng trống, và quy trình đặt phòng của hệ thống T-Booking dựa trên dữ liệu thời gian thực được cung cấp.\n" +
                "2. TUYỆT ĐỐI TỪ CHỐI mọi câu hỏi nằm ngoài phạm vi trang web (như thời tiết, nấu ăn, lập trình, giải toán, chính trị, thể thao, thơ ca, tin tức...) bằng câu lịch sự: 'Xin lỗi bạn, tôi là AI Trợ lý của hệ thống T-Booking. Tôi chỉ có thể hỗ trợ các thông tin về đặt phòng, tra cứu thiết bị, kiểm tra lịch phòng trống và quy định sử dụng phòng. Rất tiếc tôi không thể giải đáp các câu hỏi ngoài phạm vi này. Bạn có muốn tôi giúp kiểm tra phòng hoặc thiết bị nào không ạ?'.\n" +
                "3. Khi người dùng hỏi về phòng trống trong khung giờ, kiểm tra xung đột với danh sách 'LỊCH ĐÃ ĐẶT'. Nếu phòng không có lịch trùng thì là TRỐNG. Khi giới thiệu phòng, hãy đính kèm link HTML dạng: <a href=\"/Bookings/Create?roomId={Id}\" class=\"btn btn-sm btn-primary py-0 px-2 fw-bold text-white text-decoration-none rounded-pill my-1\">Đặt phòng này</a>.\n" +
                "4. Giọng điệu thân thiện, chuyên nghiệp, súc tích, định dạng Markdown đẹp mắt.\n\n" +
                dbContextInfo.ToString();

            var requestBody = new
            {
                system_instruction = new
                {
                    parts = new[] { new { text = systemPrompt } }
                },
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = userMessage } }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.2,
                    maxOutputTokens = 800
                }
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

            var response = await client.PostAsync(url, jsonContent);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Gemini API returned status code: {StatusCode}", response.StatusCode);
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
            {
                var firstCandidate = candidates[0];
                if (firstCandidate.TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var parts) &&
                    parts.GetArrayLength() > 0)
                {
                    var text = parts[0].GetProperty("text").GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return new ChatResponseDto
                        {
                            Success = true,
                            Reply = text,
                            QuickSuggestions = GetDefaultQuickSuggestions()
                        };
                    }
                }
            }

            return null;
        }

        #endregion
    }
}
