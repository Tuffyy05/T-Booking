using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Hubs;
using WebApplication1.Services;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;

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
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login"; // Nếu chưa đăng nhập, tự động đẩy về trang này
        options.AccessDeniedPath = "/Account/AccessDenied"; // Nếu không đủ quyền
        options.Events = new CookieAuthenticationEvents
        {
            // Tự động đồng bộ quyền hạn (Role) từ Database vào Cookie theo thời gian thực
            OnValidatePrincipal = async context =>
            {
                var username = context.Principal?.Identity?.Name;
                if (!string.IsNullOrEmpty(username))
                {
                    var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
                    var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username);
                    if (user != null)
                    {
                        var roleClaim = context.Principal?.FindFirst(ClaimTypes.Role);
                        if (roleClaim == null || roleClaim.Value != user.Role)
                        {
                            var identity = (ClaimsIdentity)context.Principal!.Identity!;
                            if (roleClaim != null)
                            {
                                identity.RemoveClaim(roleClaim);
                            }
                            identity.AddClaim(new Claim(ClaimTypes.Role, user.Role));
                            context.ShouldRenew = true;
                        }
                    }
                }
            }
        };
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