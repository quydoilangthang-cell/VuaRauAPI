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
        // Chuỗi kết nối đến database Somee của bạn
        private readonly string _connectionString = "workstation id=MyhangGa.mssql.somee.com;packet size=4096;user id=dangduclap_SQLLogin_1;pwd=qm662zq6o1;data source=MyhangGa.mssql.somee.com;persist security info=False;initial catalog=MyhangGa;TrustServerCertificate=True";

        // 1. API Lấy danh sách hàng hóa (Để Android hiện danh sách chọn)
        [HttpGet("danh-sach-hang")]
        public ActionResult GetProducts()
        {
            var list = new List<object>();
            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                string sql = "SELECT MaHang, TenHang FROM HANGHOA WHERE TenHang IS NOT NULL";
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

        // 2. API Dự báo giá (Kết nối 3 bảng để lấy thời gian chính xác)
        [HttpGet("{maHang}/{days}")]
        public ActionResult GetPriceForecast(string maHang, int days)
        {
            var data = new List<PriceData>();
            string tenHang = "";

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                // SỬA ĐỔI QUAN TRỌNG: Join thêm bảng XUATKHO để lấy NgayXuat
                // Lấy TOP 2000 dòng gần nhất để AI học nhanh và chính xác
                string sql = @"SELECT TOP 2000 H.TenHang, CAST(X.DGXuat AS REAL), XK.NgayXuat 
                               FROM XUATKHO_CT X 
                               INNER JOIN HANGHOA H ON X.MaHang = H.MaHang 
                               INNER JOIN XUATKHO XK ON X.SoPhieuX = XK.SoPhieuX 
                               WHERE X.MaHang = @maHang AND X.DGXuat > 0 
                               ORDER BY XK.NgayXuat ASC";

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

            // Kiểm tra số lượng dữ liệu sau khi Join
            if (data.Count < 5)
                return BadRequest("Dữ liệu trong bảng XUATKHO_CT liên kết với XUATKHO quá ít.");

            // Bắt đầu xử lý AI với ML.NET
            var mlContext = new MLContext();
            var idataView = mlContext.Data.LoadFromEnumerable(data);

            // Cấu hình thuật toán SSA (Single Spectrum Analysis)
            var pipeline = mlContext.Forecasting.ForecastBySsa(
                outputColumnName: "Forecast",
                inputColumnName: "Gia",
                windowSize: data.Count >= 14 ? 7 : 3, // Nếu dữ liệu nhiều thì học theo tuần (7 ngày)
                seriesLength: data.Count,
                trainSize: data.Count,
                horizon: days, // Số ngày Android yêu cầu
                confidenceLevel: 0.95f);

            var model = pipeline.Fit(idataView);
            var forecastingEngine = model.CreateTimeSeriesEngine<PriceData, PriceForecast>(mlContext);
            var predictions = forecastingEngine.Predict();

            // Trả về kết quả JSON chuẩn cho Android nhận diện
            return Ok(new
            {
                Ma = maHang,
                Ten = tenHang,
                DuBao = predictions.Forecast
            });
        }
    }

    // Các lớp hỗ trợ dữ liệu ML.NET
    public class PriceData
    {
        public float Gia { get; set; }
    }

    public class PriceForecast
    {
        public float[] Forecast { get; set; }
    }
}