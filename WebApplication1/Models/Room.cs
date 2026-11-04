using System.ComponentModel.DataAnnotations;

namespace WebApplication1.Models
{
    public class Room
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Tên phòng không được để trống")]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        public int Capacity { get; set; } // Sức chứa (bao nhiêu người)

        [Required]
        public string RoomType { get; set; } = string.Empty; // Loại phòng: Lab, Meeting, Hall

        public ICollection<Facility>? Facilities { get; set; }

        // Kỹ thuật Optimistic Locking: Ngăn chặn 2 người đặt phòng cùng 1 lúc
        [Timestamp]
        public byte[]? Version { get; set; }

        // Mối quan hệ 1 phòng có nhiều lịch đặt
        public ICollection<Booking>? Bookings { get; set; }
    }
}