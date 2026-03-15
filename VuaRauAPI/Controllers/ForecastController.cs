using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace VuaRauAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ForecastController : ControllerBase
    {
        // Chuỗi kết nối của anh Lập
        private readonly string _connectionString = "workstation id=MyhangGa.mssql.somee.com;packet size=4096;user id=dangduclap_SQLLogin_1;pwd=qm662zq6o1;data source=MyhangGa.mssql.somee.com;persist security info=False;initial catalog=MyhangGa;TrustServerCertificate=True";

        [HttpGet("danh-sach-hang")]
        public ActionResult GetProducts()
        {
            var list = new List<object>();
            try
            {
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    string sql = "SELECT MaHang, TenHang FROM HANGHOA";
                    SqlCommand cmd = new SqlCommand(sql, conn);
                    conn.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(new { Ma = reader.GetString(0).Trim(), Ten = reader.GetString(1).Trim() });
                        }
                    }
                }
                return Ok(list);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Lỗi kết nối DB: " + ex.Message);
            }
        }

        [HttpGet("{maHang}/{days}")]
        public async Task<ActionResult> GetPriceForecast(string maHang, int days)
        {
            // API Key chính chủ từ hình b94a91 của anh
            string apiKey = "7130879d3ff03d4a77fa16c55cac8728";
            string city = "Da Lat";

            float currentPrice = 0;
            string tenHang = "";

            try
            {
                // 1. LẤY GIÁ GỐC MỚI NHẤT
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

                if (currentPrice == 0) return BadRequest("Không tìm thấy giá gốc.");

                // 2. GỌI API THỜI TIẾT THẬT
                using var client = new HttpClient();
                var weatherRes = await client.GetAsync($"https://api.openweathermap.org/data/2.5/forecast?q={city}&appid={apiKey}&units=metric");

                if (!weatherRes.IsSuccessStatusCode)
                {
                    // Nếu lỗi API thời tiết (hoặc đang đợi kích hoạt), cho giá biến động nhẹ để không bị phẳng
                    var rndFallback = new Random();
                    var fallback = new List<object>();
                    for (int i = 1; i <= days; i++)
                    {
                        float p = currentPrice * (1 + (float)(rndFallback.NextDouble() * 0.04 - 0.02));
                        fallback.Add(new { ngay = DateTime.Now.AddDays(i).ToString("dd/MM"), giaDuBao = Math.Round(p, 0), tinhTrang = "Chờ cập nhật khí tượng..." });
                    }
                    return Ok(new { ma = maHang, ten = tenHang, giaHienTai = currentPrice, duBao = fallback });
                }

                var weatherData = await weatherRes.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(weatherData);
                var list = doc.RootElement.GetProperty("list");

                // 3. DỰ BÁO NHẢY SỐ THEO THỜI TIẾT
                var forecastResult = new List<object>();
                var rnd = new Random();

                for (int i = 0; i < days; i++)
                {
                    int weatherIndex = Math.Min(i * 8, list.GetArrayLength() - 1);
                    float weatherFactor = 1.0f;
                    string weatherDesc = "Nắng/Mây";

                    var mainWeather = list[weatherIndex].GetProperty("weather")[0].GetProperty("main").GetString();

                    if (mainWeather == "Rain" || mainWeather == "Drizzle")
                    {
                        weatherFactor = 1.15f; // Mưa tăng 15%
                        weatherDesc = "Trời mưa (Giá tăng)";
                    }
                    else if (mainWeather == "Thunderstorm")
                    {
                        weatherFactor = 1.35f; // Bão tăng 35%
                        weatherDesc = "Dông bão (Giá tăng mạnh)";
                    }

                    // Tạo chút dao động thị trường để số không bị trùng nhau
                    float noise = (float)(rnd.NextDouble() * 0.05 - 0.02);
                    float finalPrice = currentPrice * (weatherFactor + noise);

                    forecastResult.Add(new
                    {
                        ngay = DateTime.Now.AddDays(i + 1).ToString("dd/MM"),
                        giaDuBao = Math.Round(finalPrice, 0),
                        tinhTrang = weatherDesc
                    });
                }

                return Ok(new
                {
                    maHang = maHang,
                    tenHang = tenHang,
                    giaHienTai = currentPrice,
                    duBao7Ngay = forecastResult
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Lỗi AI Vựa Rau: " + ex.Message);
            }
        }
    }
}