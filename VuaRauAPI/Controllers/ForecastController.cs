using Microsoft.AspNetCore.Mvc;
using Microsoft.ML;
using Microsoft.ML.Transforms.TimeSeries;
using System.Data.SqlClient;
using System.Collections.Generic;
using System.Linq;

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

        [HttpGet("{maHang}/{days}")]
        public ActionResult GetPriceForecast(string maHang, int days)
        {
            var data = new List<PriceData>();
            string tenHang = "";

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                // Câu lệnh SQL đã tối ưu để khớp 2830 dòng của anh
                string sql = @"SELECT TOP 2000 
                                    H.TenHang, 
                                    CAST(X.DGXuat AS REAL), 
                                    XK.NgayXuat
                               FROM XUATKHO_CT X 
                               INNER JOIN XUATKHO XK ON X.SoPhieuX = XK.SoPhieuX
                               INNER JOIN HANGHOA H ON LTRIM(RTRIM(X.MaHang)) = LTRIM(RTRIM(H.MaHang))
                               WHERE LTRIM(RTRIM(X.MaHang)) = LTRIM(RTRIM(@maHang))
                               AND X.DGXuat > 0
                               ORDER BY XK.NgayXuat ASC";

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

            // Kiểm tra dữ liệu sau khi truy vấn
            if (data.Count < 5)
                return BadRequest($"Tìm thấy {data.Count} dòng. Kiểm tra lại liên kết SoPhieuX!");

            var mlContext = new MLContext();
            var idataView = mlContext.Data.LoadFromEnumerable(data);

            var pipeline = mlContext.Forecasting.ForecastBySsa(
                outputColumnName: "Forecast",
                inputColumnName: "Gia",
                windowSize: 4,
                seriesLength: data.Count,
                trainSize: data.Count,
                horizon: days,
                confidenceLevel: 0.95f);

            var model = pipeline.Fit(idataView);
            var forecastingEngine = model.CreateTimeSeriesEngine<PriceData, PriceForecast>(mlContext);
            var predictions = forecastingEngine.Predict();

            return Ok(new { Ma = maHang, Ten = tenHang, DuBao = predictions.Forecast });
        }
    }

    // Định nghĩa Class dữ liệu nằm ngoài class Controller nhưng trong Namespace
    public class PriceData
    {
        public float Gia { get; set; }
    }

    public class PriceForecast
    {
        public float[] Forecast { get; set; }
    }
}