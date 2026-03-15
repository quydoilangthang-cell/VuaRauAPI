using Microsoft.AspNetCore.Mvc;
using System.Data.SqlClient;
using System.Text.Json;

[HttpGet("{maHang}/{days}")]
public async Task<ActionResult> GetPriceForecast(string maHang, int days)
{
    string apiKey = "7130879d3ff03d4a77fa16c55cac8728";
    string city = "Da Lat";
    float currentPrice = 0;
    string tenHang = "";

    try
    {
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

        if (currentPrice == 0) return BadRequest("Không tìm thấy giá.");

        // Gọi API thời tiết
        using var client = new HttpClient();
        var weatherRes = await client.GetAsync($"https://api.openweathermap.org/data/2.5/forecast?q={city}&appid={apiKey}&units=metric");

        var finalForecastValues = new List<float>();
        var rnd = new Random();

        if (weatherRes.IsSuccessStatusCode)
        {
            var weatherData = await weatherRes.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(weatherData);
            var list = doc.RootElement.GetProperty("list");

            for (int i = 0; i < days; i++)
            {
                int weatherIndex = Math.Min(i * 8, list.GetArrayLength() - 1);
                float factor = 1.0f;
                var mainWeather = list[weatherIndex].GetProperty("weather")[0].GetProperty("main").GetString();

                if (mainWeather == "Rain" || mainWeather == "Drizzle") factor = 1.15f;
                else if (mainWeather == "Thunderstorm") factor = 1.35f;

                float price = currentPrice * (factor + (float)(rnd.NextDouble() * 0.04 - 0.02));
                finalForecastValues.Add((float)Math.Round(price, 0));
            }
        }
        else
        {
            // Nếu API thời tiết lỗi, cho giá biến động nhẹ để App vẫn có data hiện lên
            for (int i = 0; i < days; i++)
            {
                float price = currentPrice * (1 + (float)(rnd.NextDouble() * 0.04 - 0.02));
                finalForecastValues.Add((float)Math.Round(price, 0));
            }
        }

        // ĐOẠN QUAN TRỌNG NHẤT: Trả về đúng tên biến cũ để App Android đọc được
        return Ok(new
        {
            ma = maHang.Trim(),
            ten = tenHang.Trim(),
            duBao = finalForecastValues // Trả về danh sách số đơn thuần cho Chart Android
        });

    }
    catch (Exception ex)
    {
        return StatusCode(500, ex.Message);
    }
}