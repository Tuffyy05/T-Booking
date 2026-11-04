using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebApplication1.Models
{
    public class Booking
    {
        [Key]
        public int Id { get; set; }

        // Liên kết với bảng Room (Phòng nào được đặt?)
        [Required]
        public int RoomId { get; set; }
        [ForeignKey("RoomId")]
        public Room Room { get; set; }

        // Liên kết với bảng User (Ai là người đặt?)
        [Required]
        public int UserId { get; set; }
        [ForeignKey("UserId")]
        public User User { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn thời gian bắt đầu")]
        public DateTime StartTime { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn thời gian kết thúc")]
        public DateTime EndTime { get; set; }

        // Trạng thái: Pending (Chờ duyệt), Approved (Đã duyệt), Rejected (Từ chối)
        public string Status { get; set; } = "Pending";

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public ICollection<BookingFacility>? BookingFacilities { get; set; }

    }
}