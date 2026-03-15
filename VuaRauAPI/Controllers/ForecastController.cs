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
            catch (Exception ex) { return StatusCode(500, ex.Message); }
        }

        [HttpGet("{maHang}/{days}")]
        public async Task<ActionResult> GetPriceForecast(string maHang, int days)
        {
            string apiKey = "7130879d3ff03d4a77fa16c55cac8728"; //
            string city = "Da Lat";
            float currentPrice = 0;
            string tenHang = "";
            float seasonalFactor = 0;

            try
            {
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    conn.Open();

                    // 1. LẤY GIÁ GỐC (Dùng DGXuat từ XUATKHO_CT)
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

                    // 2. PHÂN TÍCH CHU KỲ DỰA TRÊN XUẤT KHO (Vì nhập kho không có data)
                    // Tính lượng xuất trung bình của tháng này trong quá khứ để đoán mùa cao điểm
                    string sqlXuat = @"SELECT AVG(CAST(X.SoLuong AS REAL)) FROM XUATKHO_CT X 
                                       INNER JOIN XUATKHO XK ON X.SoPhieuX = XK.SoPhieuX
                                       WHERE LTRIM(RTRIM(X.MaHang)) = LTRIM(RTRIM(@maHang))
                                       AND MONTH(XK.NgayXuat) = MONTH(GETDATE())";
                    try
                    {
                        SqlCommand cmdXuat = new SqlCommand(sqlXuat, conn);
                        cmdXuat.Parameters.AddWithValue("@maHang", maHang.Trim());
                        object resXuat = cmdXuat.ExecuteScalar();
                        if (resXuat != null && resXuat != DBNull.Value)
                        {
                            float avgQty = Convert.ToSingle(resXuat);
                            // Nếu lượng xuất tháng này cao -> Nhu cầu lớn -> Giá tăng nhẹ
                            if (avgQty > 500) seasonalFactor = 0.10f;
                        }
                    }
                    catch { seasonalFactor = 0; }
                }

                if (currentPrice == 0) return BadRequest("Không có dữ liệu xuất kho.");

                // 3. LẤY THỜI TIẾT ĐÀ LẠT
                using var client = new HttpClient();
                var weatherRes = await client.GetAsync($"https://api.openweathermap.org/data/2.5/forecast?q={city}&appid={apiKey}&units=metric");
                string weatherContent = weatherRes.IsSuccessStatusCode ? await weatherRes.Content.ReadAsStringAsync() : "";

                var finalValues = new List<float>();
                var rnd = new Random();

                for (int i = 0; i < days; i++)
                {
                    DateTime fDate = DateTime.Now.AddDays(i + 1);
                    float totalFactor = 1.0f + seasonalFactor;

                    // Logic Thời tiết
                    if (!string.IsNullOrEmpty(weatherContent))
                    {
                        using var doc = JsonDocument.Parse(weatherContent);
                        var wList = doc.RootElement.GetProperty("list");
                        int idx = Math.Min(i * 8, wList.GetArrayLength() - 1);
                        string mainW = wList[idx].GetProperty("weather")[0].GetProperty("main").GetString();
                        if (mainW == "Rain" || mainW == "Drizzle") totalFactor += 0.15f; // Mưa tăng giá
                        else if (mainW == "Thunderstorm") totalFactor += 0.30f; // Bão tăng mạnh
                    }

                    // Logic Ngày Rằm/Mùng 1
                    if (fDate.Day == 1 || fDate.Day == 14 || fDate.Day == 15 || fDate.Day == 30) totalFactor += 0.20f;

                    float noise = (float)(rnd.NextDouble() * 0.06 - 0.03);
                    finalValues.Add((float)Math.Round(currentPrice * (totalFactor + noise), 0));
                }

                // Trả về cho App Android
                return Ok(new { ma = maHang.Trim(), ten = tenHang.Trim(), duBao = finalValues });

            }
            catch (Exception ex) { return StatusCode(500, "Lỗi AI: " + ex.Message); }
        }
    }
}