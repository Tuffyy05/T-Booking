namespace WebApplication1.Models
{
    public class ChatRequestDto
    {
        public string Message { get; set; } = string.Empty;
        public string? SessionId { get; set; }
    }

    public class ChatResponseDto
    {
        public bool Success { get; set; } = true;
        public string Reply { get; set; } = string.Empty;
        public List<RoomSuggestionDto>? SuggestedRooms { get; set; }
        public List<string>? QuickSuggestions { get; set; }
    }

    public class RoomSuggestionDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Capacity { get; set; }
        public string RoomType { get; set; } = string.Empty;
        public List<string> Facilities { get; set; } = new();
        public string BookingUrl => $"/Bookings/Create?roomId={Id}";
    }
}
