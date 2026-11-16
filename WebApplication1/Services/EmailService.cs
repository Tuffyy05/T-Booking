using System.Net;
using System.Net.Mail;

namespace WebApplication1.Services
{
    public class EmailService
    {
        // Thay bằng Gmail của bạn
        private readonly string _fromEmail = "letai75652@gmail.com";
        // Thay bằng MẬT KHẨU ỨNG DỤNG 16 CHỮ SỐ vừa lấy ở Bước 1
        private readonly string _fromPassword = "wzyr khjs unwt ytcd";

        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            var fromAddress = new MailAddress(_fromEmail, "T-Booking System");
            var toAddress = new MailAddress(toEmail);

            var smtp = new SmtpClient
            {
                Host = "smtp.gmail.com",
                Port = 587,
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(fromAddress.Address, _fromPassword)
            };

            using var message = new MailMessage(fromAddress, toAddress)
            {
                Subject = subject,
                Body = body,
                IsBodyHtml = true // Cho phép gửi email chứa code HTML cho đẹp
            };

            await smtp.SendMailAsync(message);
        }
    }
}