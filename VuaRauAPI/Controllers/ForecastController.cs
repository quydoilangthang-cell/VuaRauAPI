using Microsoft.AspNetCore.Mvc;
using Microsoft.ML;
using Microsoft.ML.Data;
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
        // Chuỗi kết nối giữ nguyên của anh
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
        public ActionResult GetPriceForecast(string maHang, int days)
        {
            var rawData = new List<PriceDataForAI>();
            string tenHang = "";

            try
            {
                using (SqlConnection conn = new SqlConnection(_connectionString))
                {
                    // Lấy 200 dòng mới nhất để AI học xu hướng giá hiện tại
                    string sql = @"SELECT TOP 200 
                                        H.TenHang, 
                                        CAST(X.DGXuat AS REAL), 
                                        XK.NgayXuat
                                   FROM XUATKHO_CT X 
                                   INNER JOIN XUATKHO XK ON X.SoPhieuX = XK.SoPhieuX
                                   INNER JOIN HANGHOA H ON LTRIM(RTRIM(X.MaHang)) = LTRIM(RTRIM(H.MaHang))
                                   WHERE LTRIM(RTRIM(X.MaHang)) = LTRIM(RTRIM(@maHang))
                                   AND X.DGXuat > 0
                                   ORDER BY XK.NgayXuat DESC";

                    SqlCommand cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@maHang", maHang.Trim());

                    conn.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            tenHang = reader.GetString(0);
                            rawData.Add(new PriceDataForAI { Price = reader.GetFloat(1) });
                        }
                    }
                }

                if (rawData.Count < 5)
                    return BadRequest($"Dữ liệu quá ít ({rawData.Count} dòng). Team Hằng Vương cần ít nhất 5 ngày dữ liệu.");

                // Đảo lại để đúng thứ tự thời gian: cũ -> mới
                rawData.Reverse();

                // Đánh số thứ tự (Index) cho dữ liệu để AI học theo thời gian
                for (int i = 0; i < rawData.Count; i++)
                {
                    rawData[i].TimeIndex = (float)i;
                }

                var mlContext = new MLContext();
                var trainingData = mlContext.Data.LoadFromEnumerable(rawData);

                // Sử dụng thuật toán Regression (Sdca) - Cực nhẹ, KHÔNG CẦN THƯ VIỆN NGOÀI
                var pipeline = mlContext.Transforms.CopyColumns(outputColumnName: "Label", inputColumnName: "Price")
                    .Append(mlContext.Transforms.Concatenate("Features", "TimeIndex"))
                    .Append(mlContext.Regression.Trainers.Sdca(labelColumnName: "Label", maximumNumberOfIterations: 100));

                var model = pipeline.Fit(trainingData);
                var predictionEngine = mlContext.Model.CreatePredictionEngine<PriceDataForAI, PricePrediction>(model);

                // Tiến hành dự báo cho số ngày tiếp theo
                var forecastResults = new List<float>();
                float lastIndex = rawData.Count - 1;

                for (int i = 1; i <= days; i++)
                {
                    var prediction = predictionEngine.Predict(new PriceDataForAI { TimeIndex = lastIndex + i });
                    // Đảm bảo giá dự báo không bị âm do biến động dữ liệu
                    forecastResults.Add(prediction.Score > 0 ? (float)Math.Round(prediction.Score, 2) : rawData.Last().Price);
                }

                return Ok(new { Ma = maHang, Ten = tenHang, DuBao = forecastResults });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Lỗi AI vựa rau: {ex.Message}");
            }
        }
    }

    // Các class hỗ trợ AI
    public class PriceDataForAI
    {
        public float TimeIndex { get; set; } // Trục thời gian
        public float Price { get; set; }     // Giá thực tế
    }

    public class PricePrediction
    {
        [ColumnName("Score")]
        public float Score { get; set; }     // Giá dự báo
    }
}