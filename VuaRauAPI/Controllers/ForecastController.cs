using Microsoft.AspNetCore.Mvc;
using Microsoft.ML;
using Microsoft.ML.Transforms.TimeSeries;
using System.Data.SqlClient;

namespace VuaRauAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ForecastController : ControllerBase
    {
        private readonly string _connectionString = "workstation id=MyhangGa.mssql.somee.com;packet size=4096;user id=dangduclap_SQLLogin_1;pwd=qm662zq6o1;data source=MyhangGa.mssql.somee.com;persist security info=False;initial catalog=MyhangGa;TrustServerCertificate=True";

        // 1. API Lấy danh sách hàng hóa (Để Android hiện danh sách chọn)
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

        // 2. API Dự báo giá (Đã kết nối 2 bảng)
        [HttpGet("{maHang}/{days}")]
        public ActionResult GetPriceForecast(string maHang, int days)
        {
            var data = new List<PriceData>();
            string tenHang = "";

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                // Kết nối bảng XUATKHO_CT với HANGHOA để lấy Tên và Giá
                string sql = @"SELECT H.TenHang, CAST(X.DGXuat AS REAL) 
                               FROM XUATKHO_CT X 
                               INNER JOIN HANGHOA H ON X.MaHang = H.MaHang 
                               WHERE X.MaHang = @maHang 
                               ORDER BY X.SoPhieuX ASC";

                SqlCommand cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@maHang", maHang);
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

            if (data.Count < 5) return BadRequest("Dữ liệu quá ít.");

            var mlContext = new MLContext();
            var idataView = mlContext.Data.LoadFromEnumerable(data);

            var pipeline = mlContext.Forecasting.ForecastBySsa(
                outputColumnName: "Forecast",
                inputColumnName: "Gia",
                windowSize: 3,
                seriesLength: data.Count,
                trainSize: data.Count,
                horizon: days,
                confidenceLevel: 0.95f);

            var model = pipeline.Fit(idataView);
            var forecastingEngine = model.CreateTimeSeriesEngine<PriceData, PriceForecast>(mlContext);
            var predictions = forecastingEngine.Predict();

            return Ok(new
            {
                Ma = maHang,
                Ten = tenHang,
                DuBao = predictions.Forecast
            });
        }
    }
}