using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

class Program
{
    static void Main()
    {
        // 1. Mở Socket: Tương ứng với system call gọi OS cấp phát tài nguyên mạng
        Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        // 2. Bind và Listen: Gắn server vào Port 8080 và bắt đầu lắng nghe
        IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, 8080);
        serverSocket.Bind(endPoint);
        serverSocket.Listen(10); // Tham số 10 là hàng đợi tối đa cho các kết nối đang chờ

        Console.WriteLine("Server dang lang nghe o port 8080...");

        // 3. Vòng lặp vô tận (Infinite Loop): Server là một tiến trình chạy liên tục
        while (true)
        {
            // Accept: Đợi và chấp nhận kết nối từ trình duyệt (Client)
            // Lệnh này sẽ "block" (chặn) chương trình cho đến khi có khách truy cập
            Socket clientSocket = serverSocket.Accept();
            Console.WriteLine("Da co client ket noi!");

            // Đọc dữ liệu thô (HTTP Request) từ client
            byte[] buffer = new byte[3];
            int bytesRead = clientSocket.Receive(buffer);
            string request = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            Console.WriteLine("Request nhan duoc:\n" + request);

            // 4. Gửi dữ liệu thô: Trả về một HTTP Response hợp lệ để trình duyệt hiểu được
            string httpResponse = "HTTP/1.1 200 OK\r\n" +
                  "Content-Type: text/plain; charset=UTF-8\r\n" +
                  "Content-Length: 12\r\n" +
                  "Connection: close\r\n" +
                  "\r\n" + // CỰC KỲ QUAN TRỌNG: Dòng trống ngăn cách
                  "Hello World!";

            // Chuyển chuỗi thành mảng byte và gửi về client qua Socket
            byte[] responseData = Encoding.UTF8.GetBytes(httpResponse);
            clientSocket.Send(responseData);

            // Đóng cổng giao tiếp với client hiện tại
            clientSocket.Close();
        }
    }
}