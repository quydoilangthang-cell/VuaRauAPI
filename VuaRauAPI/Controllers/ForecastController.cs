using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data.SqlClient;
using System.Net.Http;
using System.Text.Json;

namespace VuaRauAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ForecastController : ControllerBase
    {
        private readonly string _connectionString = "Server=sql.bsite.net\\MSSQL2016;Database=dangdinhlap_VuaRau;User Id=dangdinhlap_VuaRau;Password=YourPassword;TrustServerCertificate=True;";

        [HttpGet("{maHang}/{days}")]
        public async Task<ActionResult> GetPriceForecast(string maHang, int days)
        {
            // API Key chính chủ của anh Lập
            string apiKey = "7130879d3ff03d4a77fa16c55cac8728";
            string city = "Da Lat"; // Khu vực nguồn hàng

            try
            {
                // 1. LẤY GIÁ MỚI NHẤT TỪ SQL
                float currentPrice = 0;
                string tenHang = "";
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    string sql = @"SELECT TOP 1 H.TenHang, CAST(X.DGXuat AS REAL) 
                                   FROM XUATKHO_CT X INNER JOIN XUATKHO XK ON X.SoPhieuX = XK.SoPhieuX
                                   INNER JOIN HANGHOA H ON LTRIM(RTRIM(X.MaHang)) = LTRIM(RTRIM(H.MaHang))
                                   WHERE LTRIM(RTRIM(X.MaHang)) = LTRIM(RTRIM(@maHang)) AND X.DGXuat > 0
                                   ORDER BY XK.NgayXuat DESC";
                    SqlCommand cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@maHang", maHang.Trim());
                    conn.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            tenHang = reader.GetString(0);
                            currentPrice = reader.GetFloat(1);
                        }
                    }
                }

                if (currentPrice == 0) return BadRequest("Không tìm thấy dữ liệu giá cho mã này.");

                // 2. GỌI API THỜI TIẾT THẬT TỪ OPENWEATHER
                using var client = new HttpClient();
                var weatherRes = await client.GetAsync($"https://api.openweathermap.org/data/2.5/forecast?q={city}&appid={apiKey}&units=metric");

                if (!weatherRes.IsSuccessStatusCode)
                    return Ok(new { tb = "Đang chờ kích hoạt API Key (30p), giá tạm tính đi ngang", ma = maHang, duBao = Enumerable.Repeat(currentPrice, days) });

                var weatherData = await weatherRes.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(weatherData);
                var list = doc.RootElement.GetProperty("list");

                // 3. LOGIC DỰ BÁO: GIÁ = GIÁ GỐC * HỆ SỐ THỜI TIẾT
                var forecastResult = new List<object>();
                var rnd = new Random();

                for (int i = 0; i < days; i++)
                {
                    // Lấy dữ liệu thời tiết mỗi ngày (cách nhau 8 slot thời gian)
                    int weatherIndex = Math.Min(i * 8, list.GetArrayLength() - 1);
                    float weatherFactor = 1.0f;
                    string weatherDesc = "Nắng đẹp/Mây";

                    var mainWeather = list[weatherIndex].GetProperty("weather")[0].GetProperty("main").GetString();

                    // Nếu mưa tăng 15%, nếu bão tăng 30%
                    if (mainWeather == "Rain" || mainWeather == "Drizzle")
                    {
                        weatherFactor = 1.15f;
                        weatherDesc = "Có mưa (Giá tăng)";
                    }
                    else if (mainWeather == "Thunderstorm") {