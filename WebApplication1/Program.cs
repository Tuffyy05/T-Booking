using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
// Thêm thư mục Hubs của bạn
using WebApplication1.Hubs;
// Thêm thư mục Services
using WebApplication1.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Kết nối Database (Bắt buộc phải có để các Controller gọi được Database)
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// 2. Khai báo các dịch vụ cơ bản của MVC
builder.Services.AddControllersWithViews();

// [SỬA LỖI] Đăng ký EmailService phải nằm ở đây (TRƯỚC khi gọi builder.Build)
builder.Services.AddTransient<EmailService>();

// Đăng ký dịch vụ cho AI Chatbot
builder.Services.AddHttpClient();
builder.Services.AddScoped<IChatbotService, ChatbotService>();

// 3. Cấu hình bảo mật bằng Cookie
builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login"; // Nếu chưa đăng nhập, tự động đẩy về trang này
        options.AccessDeniedPath = "/Account/AccessDenied"; // Nếu không đủ quyền
    });

// 4. Thêm dịch vụ SignalR cho tính năng thông báo Real-time
builder.Services.AddSignalR();
var app = builder.Build();


// Cấu hình HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// 5. QUAN TRỌNG: Phải có Xác thực (Authentication) TRƯỚC Phân quyền (Authorization)
app.UseAuthentication();
app.UseAuthorization();

// 6. Định tuyến (Map) cho SignalR Hub
app.MapHub<NotificationHub>("/notificationHub");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();