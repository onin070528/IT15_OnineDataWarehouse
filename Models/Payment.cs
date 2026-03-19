using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace it15_webproject_mvc.Models
{
    public class Payment
    {
        [Key]
        public int PaymentID { get; set; }

        public int UserID { get; set; }

        public int OrganizationID { get; set; }

        [Required, MaxLength(50)]
        public string PlanName { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [Required, MaxLength(10)]
        public string Currency { get; set; } = "PHP";

        [Required, MaxLength(30)]
        public string Status { get; set; } = "Pending";

        [MaxLength(255)]
        public string? CheckoutSessionId { get; set; }

        [MaxLength(255)]
        public string? PayMongoPaymentId { get; set; }

        [MaxLength(500)]
        public string? CheckoutUrl { get; set; }

        public DateTime Created_at { get; set; } = DateTime.UtcNow;

        public DateTime? Paid_at { get; set; }

        [ForeignKey(nameof(UserID))]
        public User? User { get; set; }

        [ForeignKey(nameof(OrganizationID))]
        public Organization? Organization { get; set; }
    }
}
