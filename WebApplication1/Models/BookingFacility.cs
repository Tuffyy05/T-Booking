using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebApplication1.Models
{
    public class BookingFacility
    {
        [Key]
        public int Id { get; set; }

        public int BookingId { get; set; }
        [ForeignKey("BookingId")]
        public Booking? Booking { get; set; }

        public int FacilityId { get; set; }
        [ForeignKey("FacilityId")]
        public Facility? Facility { get; set; }
        public int Quantity { get; set; } = 1;
    }
}