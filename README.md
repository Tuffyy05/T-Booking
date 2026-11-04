# 🏫 T-Booking System

![ASP.NET Core](https://img.shields.io/badge/ASP.NET_Core-MVC-512BD4?style=for-the-badge&logo=dotnet)
![Entity Framework](https://img.shields.io/badge/Entity_Framework-Core-3da639?style=for-the-badge&logo=nuget)
![Bootstrap](https://img.shields.io/badge/Bootstrap-5-7952B3?style=for-the-badge&logo=bootstrap)
![Status](https://img.shields.io/badge/Status-Active-success?style=for-the-badge)

**T-Booking** là hệ thống quản lý đặt phòng họp, phòng Lab và mượn trang thiết bị (chung/riêng) được phát triển nhằm tự động hóa và tối ưu quy trình mượn phòng tại các trường học và cơ quan. 

Dự án được xây dựng phục vụ cho báo cáo môn học **Công nghệ phần mềm nâng cao**.

## ✨ Tính năng nổi bật

### 👨‍💻 Dành cho Khách hàng (User)
* **Xác thực & Bảo mật:** Đăng ký, đăng nhập an toàn (hỗ trợ Username/Email).
* **Tra cứu trực quan:** Giao diện thẻ (Card) hiện đại, tìm kiếm và lọc phòng theo sức chứa, loại phòng.
* **Đặt phòng thông minh:** 
  * Chọn giờ bắt đầu/kết thúc.
  * Tự động hiển thị và cho phép mượn trang thiết bị (cả thiết bị cố định của phòng lẫn thiết bị từ kho chung).
  * 🛡️ **Thuật toán chống trùng lịch:** Ngăn chặn việc đặt phòng nếu khung giờ đã có người khác giữ chỗ.
* **Lịch chung (Public Calendar):** Tích hợp FullCalendar hiển thị sinh động toàn bộ các khung giờ đã được đặt trên hệ thống.
* **Quản lý cá nhân:** Theo dõi lịch sử đơn đặt phòng và trạng thái phê duyệt.

### 👑 Dành cho Quản trị viên (Admin)
* **Kiểm duyệt đơn:** Xem chi tiết, Phê duyệt (Approve) hoặc Từ chối (Reject) các yêu cầu đặt phòng kèm thiết bị.
* **Dashboard Thống kê:** Theo dõi trực quan tổng số phòng, số đơn, tỉ lệ trạng thái duyệt và top các phòng được sử dụng nhiều nhất qua biểu đồ.
* **Quản lý dữ liệu lõi:** Thêm, sửa, xóa thông tin Phòng và Trang thiết bị.

## 🛠️ Công nghệ sử dụng

* **Backend:** C#, ASP.NET Core MVC
* **Database:** SQL Server, Entity Framework Core (Code-First)
* **Frontend:** HTML5, CSS3, JavaScript, Bootstrap 5
* **Thư viện tích hợp:** FontAwesome (Icons), FullCalendar (Giao diện lịch)
* **Quản lý mã nguồn:** Git & GitHub

## 🚀 Hướng dẫn cài đặt

1. **Clone mã nguồn về máy:**
   ```bash
   git clone [https://github.com/Tuffyy05/T-Booking.git](https://github.com/Tuffyy05/T-Booking.git)
