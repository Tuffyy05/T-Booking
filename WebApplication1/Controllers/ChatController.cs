using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebApplication1.Models;
using WebApplication1.Services;

namespace WebApplication1.Controllers
{
    [AllowAnonymous]
    public class ChatController : Controller
    {
        private readonly IChatbotService _chatbotService;
        private readonly ILogger<ChatController> _logger;

        public ChatController(IChatbotService chatbotService, ILogger<ChatController> logger)
        {
            _chatbotService = chatbotService;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> Ask([FromBody] ChatRequestDto request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Message))
            {
                return Json(new ChatResponseDto
                {
                    Success = false,
                    Reply = "Vui lòng nhập nội dung câu hỏi để tôi có thể hỗ trợ bạn nhé!"
                });
            }

            try
            {
                var response = await _chatbotService.GetResponseAsync(request.Message);
                return Json(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý tin nhắn chat AI");
                return Json(new ChatResponseDto
                {
                    Success = false,
                    Reply = "Đã xảy ra lỗi nhẹ khi xử lý yêu cầu. Vui lòng thử lại sau ít giây hoặc xem trực tiếp tại trang [Lịch Phòng](/Bookings/Calendar) nhé!"
                });
            }
        }
    }
}
