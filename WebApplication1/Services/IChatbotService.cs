using WebApplication1.Models;

namespace WebApplication1.Services
{
    public interface IChatbotService
    {
        Task<ChatResponseDto> GetResponseAsync(string userMessage);
    }
}
