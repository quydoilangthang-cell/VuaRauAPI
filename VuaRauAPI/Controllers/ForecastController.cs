using Microsoft.AspNetCore.Mvc;
using Microsoft.ML;
using Microsoft.ML.Transforms.TimeSeries;
using System.Data.SqlClient;
using System.Collections.Generic;
using System.Linq;
using System;

namespace VuaRauAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ForecastController : ControllerBase
    {
        // Chuỗi kết nối giữ nguyên
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
                            list.Add(new { Ma = reader.GetString(0), Ten = reader.GetString(1) });
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
        public ActionResult GetPriceForecast(string maHang, int days)
        {
            var data = new List<PriceData>();
            string tenHang = "";

            try
            {
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    // LẤY 200 DÒNG MỚI NHẤT để nhẹ server Render
                    string sql = @"SELECT TOP 200 
                                        H.TenHang, 
                                        CAST(X.DGXuat AS REAL), 
                                        XK.NgayXuat
                                   FROM XUATKHO_CT X 
                                   INNER JOIN XUATKHO XK ON X.SoPhieuX = XK.SoPhieuX
                                   INNER JOIN HANGHOA H ON LTRIM(RTRIM(X.MaHang)) = LTRIM(RTRIM(H.MaHang))
                                   WHERE LTRIM(RTRIM(X.MaHang)) = LTRIM(RTRIM(@maHang))
                                   AND X.DGXuat > 0
                                   ORDER BY XK.NgayXuat DESC"; // Lấy ngày mới nhất

                    SqlCommand cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@maHang", maHang.Trim());

                    conn.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            tenHang = reader.GetString(0);
                            data.Add(new PriceData { Gia = reader.GetFloat(1) });
                        }
                    }
                }

                // Đảo lại danh sách cho đúng trình tự thời gian (từ cũ đến mới) để AI học
                data.Reverse();

                if (data.Count < 5)
                    return BadRequest($"Tìm thấy {data.Count} dòng. Cần tối thiểu 5 dòng để dự báo!");

                var mlContext = new MLContext();
                var idataView = mlContext.Data.LoadFromEnumerable(data);

                // windowSize = 2: Cấu hình cực nhẹ để không lỗi 500 trên Render Free
                var pipeline = mlContext.Forecasting.ForecastBySsa(
    outputColumnName: "Forecast",
    inputColumnName: "Gia",
    windowSize: 2,
    seriesLength: data.Count,
    trainSize: data.Count,
    horizon: days,
    confidenceLevel: 0.95f);
                var model = pipeline.Fit(idataView);
                var forecastingEngine = model.CreateTimeSeriesEngine<PriceData, PriceForecast>(mlContext);
                var predictions = forecastingEngine.Predict();

                return Ok(new { Ma = maHang, Ten = tenHang, DuBao = predictions.Forecast });

            }
            catch (Exception ex)
            {
                // Trả về lỗi chi tiết để dễ sửa
                return StatusCode(500, $"Lỗi dự báo: {ex.Message} -> {ex.InnerException?.Message}");
            }
        }
    }

    public class PriceData
    {
        public float Gia { get; set; }
    }

    public class PriceForecast
    {
        public float[] Forecast { get; set; }
    }
}