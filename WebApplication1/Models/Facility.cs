using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebApplication1.Models
{
    public class Facility
    {
        [Key]
        public int Id { get; set; }

        [Required(ErrorMessage = "Tên thiết bị không được để trống")]
        [Display(Name = "Tên thiết bị")]
        public string Name { get; set; }

        [Display(Name = "Mô tả / Thông số")]
        public string Description { get; set; }

        [Display(Name = "Số lượng")]
        public int Quantity { get; set; } = 1;

        // Khóa ngoại liên kết tới bảng Room (nếu thiết bị thuộc cố định một phòng)
        [Display(Name = "Phòng trực thuộc")]
        public int? RoomId { get; set; }
        
        [ForeignKey("RoomId")]
        public Room? Room { get; set; }
    }
}