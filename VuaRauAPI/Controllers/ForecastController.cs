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

                if (currentPrice == 0) return BadRequest("Không có giá.");

                using var client = new HttpClient();
                var weatherRes = await client.GetAsync($"https://api.openweathermap.org/data/2.5/forecast?q={city}&appid={apiKey}&units=metric");

                var finalValues = new List<float>();
                var rnd = new Random();

                if (weatherRes.IsSuccessStatusCode)
                {
                    var weatherData = await weatherRes.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(weatherData);
                    var list = doc.RootElement.GetProperty("list");
                    for (int i = 0; i < days; i++)
                    {
                        int idx = Math.Min(i * 8, list.GetArrayLength() - 1);
                        float factor = 1.0f;
                        var main = list[idx].GetProperty("weather")[0].GetProperty("main").GetString();
                        if (main == "Rain" || main == "Drizzle") factor = 1.15f;
                        else if (main == "Thunderstorm") factor = 1.35f;
                        float p = currentPrice * (factor + (float)(rnd.NextDouble() * 0.04 - 0.02));
                        finalValues.Add((float)Math.Round(p, 0));
                    }
                }
                else
                {
                    for (int i = 0; i < days; i++)
                    {
                        float p = currentPrice * (1 + (float)(rnd.NextDouble() * 0.04 - 0.02));
                        finalValues.Add((float)Math.Round(p, 0));
                    }
                }

                // Trả về đúng tên biến cũ (ma, ten, duBao) để App Android không bị lỗi chart
                return Ok(new
                {
                    ma = maHang.Trim(),
                    ten = tenHang.Trim(),
                    duBao = finalValues
                });

            }
            catch (Exception ex) { return StatusCode(500, ex.Message); }
        }
    }
}