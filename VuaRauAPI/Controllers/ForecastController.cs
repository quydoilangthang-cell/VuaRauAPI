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
    float seasonalFactor = 0; // Biến lưu trữ xu hướng từ kho

    try
    {
        using (SqlConnection conn = new SqlConnection(_connectionString))
        {
            conn.Open();

            // 1. LẤY GIÁ GỐC HIỆN TẠI
            string sqlGia = @"SELECT TOP 1 H.TenHang, CAST(X.DGXuat AS REAL) 
                               FROM XUATKHO_CT X INNER JOIN XUATKHO XK ON X.SoPhieuX = XK.SoPhieuX
                               INNER JOIN HANGHOA H ON LTRIM(RTRIM(X.MaHang)) = LTRIM(RTRIM(H.MaHang))
                               WHERE LTRIM(RTRIM(X.MaHang)) = LTRIM(RTRIM(@maHang)) AND X.DGXuat > 0
                               ORDER BY XK.NgayXuat DESC";
            SqlCommand cmdGia = new SqlCommand(sqlGia, conn);
            cmdGia.Parameters.AddWithValue("@maHang", maHang.Trim());
            using (var reader = cmdGia.ExecuteReader())
            {
                if (reader.Read())
                {
                    tenHang = reader.GetString(0);
                    currentPrice = reader.GetFloat(1);
                }
            }

            // 2. PHÂN TÍCH LỊCH SỬ KHO (Tính chu kỳ mùa)
            // Kiểm tra lượng nhập hàng trung bình của tháng này trong quá khứ
            string sqlKho = @"SELECT AVG(SoLuong) FROM NHAPKHO_CT N 
                              INNER JOIN NHAPKHO NK ON N.SoPhieuN = NK.SoPhieuN
                              WHERE LTRIM(RTRIM(N.MaHang)) = LTRIM(RTRIM(@maHang))
                              AND MONTH(NK.NgayNhap) = MONTH(GETDATE())";
            SqlCommand cmdKho = new SqlCommand(sqlKho, conn);
            cmdKho.Parameters.AddWithValue("@maHang", maHang.Trim());
            var resultKho = cmdKho.ExecuteScalar();
            if (resultKho != DBNull.Value && resultKho != null)
            {
                float avgQty = Convert.ToSingle(resultKho);
                // Nếu lượng nhập trung bình tháng này > 1000 (ví dụ), coi là mùa rộ -> giảm giá 10%
                if (avgQty > 1000) seasonalFactor = -0.10f;
                // Nếu lượng nhập thấp < 200 -> hàng hiếm -> tăng giá 15%
                else if (avgQty < 200) seasonalFactor = 0.15f;
            }
        }

        if (currentPrice == 0) return BadRequest("Không có giá.");

        // 3. LẤY THỜI TIẾT & TỔNG HỢP DỰ BÁO
        using var client = new HttpClient();
        var weatherRes = await client.GetAsync($"https://api.openweathermap.org/data/2.5/forecast?q={city}&appid={apiKey}&units=metric");

        var finalValues = new List<float>();
        var rnd = new Random();
        JsonElement? weatherList = null;
        if (weatherRes.IsSuccessStatusCode)
        {
            weatherList = JsonDocument.Parse(await weatherRes.Content.ReadAsStringAsync()).RootElement.GetProperty("list");
        }

        for (int i = 0; i < days; i++)
        {
            DateTime forecastDate = DateTime.Now.AddDays(i + 1);
            float totalFactor = 1.0f + seasonalFactor; // Áp dụng chu kỳ kho

            // Cộng thêm logic thời tiết
            if (weatherList.HasValue)
            {
                int idx = Math.Min(i * 8, weatherList.Value.GetArrayLength() - 1);
                var main = weatherList.Value[idx].GetProperty("weather")[0].GetProperty("main").GetString();
                if (main == "Rain") totalFactor += 0.15f;
            }

            // Cộng thêm logic Ngày Rằm/Mùng 1 (Lịch dương xấp xỉ)
            int d = forecastDate.Day;
            if (d == 1 || d == 15 || d == 30) totalFactor += 0.20f;

            float noise = (float)(rnd.NextDouble() * 0.04 - 0.02);
            finalValues.Add((float)Math.Round(currentPrice * (totalFactor + noise), 0));
        }

        return Ok(new { ma = maHang.Trim(), ten = tenHang.Trim(), duBao = finalValues });

    }
    catch (Exception ex) { return StatusCode(500, ex.Message); }
}